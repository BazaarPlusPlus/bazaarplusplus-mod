#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewItemCardFactory : IPreviewCardFactory
{
    private MethodInfo _instantiateCardMethod;
    private object _spawnSection;
    private object _staticData;

    public async Task<GameObject> CreateCardAsync(PreviewCardSpec spec, Transform parent)
    {
        Services.TryGet<AssetLoader>(out var loader);
        if (loader == null || !EnsureApi(loader))
        {
            BppLog.Warn(
                "MonsterPreviewItemCardFactory",
                $"API unavailable for template={spec?.TemplateId ?? "null"} loader={(loader == null ? "null" : "ok")}"
            );
            return null;
        }

        if (_staticData == null)
        {
            _staticData = await Data.GetStatic();
            BppLog.Debug("MonsterPreviewItemCardFactory", $"Static data loaded={(_staticData != null)}");
        }

        if (_staticData == null)
        {
            BppLog.Warn(
                "MonsterPreviewItemCardFactory",
                $"Static data unavailable for template={spec?.TemplateId ?? "null"}"
            );
            return null;
        }

        var card = BuildCard(spec, _staticData);
        if (card == null)
        {
            BppLog.Warn("MonsterPreviewItemCardFactory", $"BuildCard failed for template={spec?.TemplateId ?? "null"}");
            return null;
        }

        var cardObject = await InstantiateAsync(loader, card, parent.gameObject);
        if (cardObject == null)
        {
            BppLog.Warn(
                "MonsterPreviewItemCardFactory",
                $"Instantiate returned null for template={spec?.TemplateId ?? "null"}"
            );
            return null;
        }

        cardObject.AddComponent<ShowcaseCardMarker>();
        ConfigureSpawned(cardObject);
        BppLog.Debug(
            "MonsterPreviewItemCardFactory",
            $"Created card template={spec?.TemplateId ?? "null"} object={cardObject.name}"
        );
        return cardObject;
    }

    public Task UpdateCardAsync(GameObject cardObject, PreviewCardSpec spec)
    {
        return Task.CompletedTask;
    }

    public void DestroyCard(GameObject cardObject)
    {
        if (cardObject == null)
            return;

        var marker = cardObject.GetComponent<ShowcaseCardMarker>();
        if (marker != null)
            UnityEngine.Object.Destroy(marker);

        if (cardObject.TryGetComponent<ItemController>(out var itemController))
            itemController.EnableMovement(true);
        else if (cardObject.TryGetComponent<CardController>(out var cardController))
            cardController.EnableMovement(true);

        cardObject.transform.SetParent(null);
        cardObject.transform.localScale = Vector3.one;
        cardObject.PoolObject();
    }

    private static ItemCard BuildCard(PreviewCardSpec entry, object staticData)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.TemplateId))
        {
            BppLog.Warn("MonsterPreviewItemCardFactory", "Empty preview card spec");
            return null;
        }

        if (!Guid.TryParse(entry.TemplateId, out var templateId))
        {
            BppLog.Warn("MonsterPreviewItemCardFactory", $"Invalid template id: {entry.TemplateId}");
            return null;
        }

        var template = GetTemplate(staticData, templateId) as ITCard;
        if (template == null)
        {
            BppLog.Warn("MonsterPreviewItemCardFactory", $"Template not found: {entry.TemplateId}");
            return null;
        }

        var card = new ItemCard
        {
            InstanceId = InstanceId.New("ppmon"),
            TemplateId = templateId,
            Template = template,
            Tier = (ETier)Mathf.Clamp(entry.Tier, 0, 5),
            Size = ParseSize(entry.Size, template.Size),
            Type = ECardType.Item,
            Attributes = new Dictionary<ECardAttributeType, int>(),
            Tags = new HashSet<ECardTag>(),
            HiddenTags = new HashSet<EHiddenTag>(),
            Heroes = new HashSet<EHero>(),
            Owner = null,
            Section = null,
            LeftSocketId = null,
        };

        if (
            !string.IsNullOrWhiteSpace(entry.Enchant)
            && !string.Equals(entry.Enchant, "None", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(entry.Enchant, out EEnchantmentType enchantType)
        )
        {
            card.Enchantment = enchantType;
        }

        if (entry.Attributes != null)
        {
            foreach (var kv in entry.Attributes)
            {
                if (Enum.IsDefined(typeof(ECardAttributeType), kv.Key))
                    card.Attributes[(ECardAttributeType)kv.Key] = kv.Value;
            }
        }

        BppLog.Debug(
            "MonsterPreviewItemCardFactory",
            $"BuildCard result template={entry.TemplateId} tier={card.Tier} size={card.Size} type={card.Type} enchant={card.Enchantment} attrs={card.Attributes.Count} templateName={template.InternalName}"
        );
        return card;
    }


    private static ECardSize ParseSize(int size, ECardSize fallback)
    {
        switch (size)
        {
            case 1:
                return ECardSize.Small;
            case 2:
                return ECardSize.Medium;
            case 3:
                return ECardSize.Large;
            default:
                return fallback;
        }
    }    private static object GetTemplate(object staticData, Guid templateId)
    {
        if (staticData == null)
            return null;

        var method = staticData
            .GetType()
            .GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        return method?.Invoke(staticData, new object[] { templateId });
    }

    private void ConfigureSpawned(GameObject cardObject)
    {
        if (cardObject.TryGetComponent<ItemController>(out var itemController))
        {
            itemController.ShowCard(true);
            itemController.EnableMovement(false);
            return;
        }

        if (cardObject.TryGetComponent<CardController>(out var cardController))
        {
            cardController.EnableMovement(false);
            cardController.ShowCard(true);
        }
    }

    private bool EnsureApi(AssetLoader loader)
    {
        if (_instantiateCardMethod != null && _spawnSection != null)
            return true;

        _instantiateCardMethod = loader
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "InstantiateCardAsync", StringComparison.Ordinal))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(Card)
                    && parameters[1].ParameterType == typeof(GameObject)
                    && parameters[2].ParameterType.IsEnum;
            });

        if (_instantiateCardMethod == null)
        {
            BppLog.Warn("MonsterPreviewItemCardFactory", "InstantiateCardAsync API not found");
            return false;
        }

        var sectionType = _instantiateCardMethod.GetParameters()[2].ParameterType;
        if (!sectionType.IsEnum)
        {
            BppLog.Warn("MonsterPreviewItemCardFactory", "Spawn section parameter is not enum");
            return false;
        }

        var sectionNames = Enum.GetNames(sectionType);
        if (sectionNames.Contains("Opponent"))
            _spawnSection = Enum.Parse(sectionType, "Opponent");
        else if (sectionNames.Contains("Board"))
            _spawnSection = Enum.Parse(sectionType, "Board");
        else if (sectionNames.Contains("Storage"))
            _spawnSection = Enum.Parse(sectionType, "Storage");
        else
            _spawnSection = Enum.ToObject(sectionType, 0);
        BppLog.Debug(
            "MonsterPreviewItemCardFactory",
            $"Resolved instantiate API with spawnSection={_spawnSection}"
        );
        return _spawnSection != null;
    }

    private async Task<GameObject> InstantiateAsync(
        AssetLoader loader,
        Card card,
        GameObject parent
    )
    {
        if (_instantiateCardMethod == null || _spawnSection == null)
            return null;

        var taskObject = _instantiateCardMethod.Invoke(
            loader,
            new object[] { card, parent, _spawnSection }
        );
        if (taskObject is Task<GameObject> typedTask)
            return await typedTask;

        if (taskObject is Task task)
        {
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task) as GameObject;
        }

        return null;
    }
}
