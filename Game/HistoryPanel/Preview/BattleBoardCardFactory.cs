#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

// A spawned preview card plus the async SetUp task the renderer must await before Show.
internal readonly struct BattleBoardSpawn
{
    public BattleBoardSpawn(Component card, Task setUpTask)
    {
        Card = card;
        SetUpTask = setUpTask;
    }

    public Component Card { get; }
    public Task SetUpTask { get; }
}

// Owns the CardPreviewBase pool and the reflection-driven SetUp/Show/Resize lifecycle, turning a
// HistoryItemSpec into a live native card parented under a socket. Concentrates the entire game
// reflection surface for the battle-board preview in one place.
internal sealed class BattleBoardCardFactory
{
    private readonly int _layer;
    private HistoryPanelPreviewCardPool? _pool;

    public BattleBoardCardFactory(int layer)
    {
        _layer = layer;
    }

    public bool ReflectionReady => HistoryPanelCardPreviewReflection.SetUpMethod != null;

    // Ensure the pool exists and its prefab refs are resolved. Returns false when the native
    // MonsterBoardTooltip prefabs are not yet available.
    public bool EnsureReady()
    {
        _pool ??= new HistoryPanelPreviewCardPool(_layer);
        return _pool.TryEnsurePrefabRefs();
    }

    public bool TryEnsurePrefabRefs() => _pool != null && _pool.TryEnsurePrefabRefs();

    // Spawn one card for the spec, parented under the socket the resolver selects from `sockets`.
    // Returns null when the template is unknown, no socket fits, or the pool cannot vend a card.
    public BattleBoardSpawn? TrySpawn(HistoryItemSpec? spec, int index, RectTransform[] sockets)
    {
        if (_pool == null)
            return null;
        if (spec == null || spec.TemplateId == Guid.Empty)
            return null;

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
            return null;

        var template = HistoryPanelPreviewTemplateLookup.GetCardTemplate(
            staticData,
            spec.TemplateId
        );
        if (template == null)
            return null;

        var size = ResolveCardSize(template);
        var socketIndex = BattleBoardSocketResolver.ResolveIndex(
            sockets.Length,
            spec.SocketId.HasValue ? (int)spec.SocketId.Value : (int?)null,
            index,
            SpanForSize(size)
        );
        if (socketIndex < 0)
            return null;

        var card = _pool.Take(size, sockets[socketIndex]);
        if (card == null)
            return null;

        var instance = BuildSyntheticInstance(spec, index);
        var setUpTask = InvokeSetUpSafe(card, template, instance);
        return new BattleBoardSpawn(card, setUpTask);
    }

    public void Show(IReadOnlyList<Component> cards)
    {
        var show = HistoryPanelCardPreviewReflection.ShowMethod;
        if (show == null)
            return;

        var args = new object[] { true };
        foreach (var card in cards)
        {
            if (card == null)
                continue;
            try
            {
                show.Invoke(card, args);
            }
            catch (Exception ex)
            {
                BppLog.Warn("BattleBoardPreview", $"CardPreviewBase.Show threw: {ex.Message}");
            }
        }
    }

    public void Return(Component? card) => _pool?.Return(card);

    public void DestroyAll()
    {
        _pool?.DestroyAll();
        _pool = null;
    }

    private static int SpanForSize(ECardSize size) =>
        size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };

    private static ECardSize ResolveCardSize(TCardBase template) =>
        template.Size switch
        {
            ECardSize.Small => ECardSize.Small,
            ECardSize.Medium => ECardSize.Medium,
            ECardSize.Large => ECardSize.Large,
            _ => ECardSize.Small,
        };

    private static TCardInstanceItem BuildSyntheticInstance(HistoryItemSpec spec, int index)
    {
        return new TCardInstanceItem
        {
            TemplateId = spec.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = $"bpp-battleboard-{index}",
            Tier = spec.Tier,
            SocketId = spec.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = spec.EnchantmentType,
            Attributes =
                spec.Attributes != null
                    ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                    : new Dictionary<ECardAttributeType, int>(),
        };
    }

    private static async Task InvokeSetUpSafe(
        Component card,
        TCardBase template,
        TCardInstanceItem instance
    )
    {
        var method = HistoryPanelCardPreviewReflection.SetUpMethod;
        if (method == null)
            return;

        try
        {
            var raw = method.Invoke(card, new object[] { template, false, instance });
            if (raw is Task task)
                await task;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "BattleBoardPreview",
                $"CardPreviewBase.SetUp threw for template={template?.Id}: {ex.InnerException?.Message ?? ex.Message}"
            );
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BattleBoardPreview",
                $"CardPreviewBase.SetUp invocation failed for template={template?.Id}: {ex.Message}"
            );
            throw;
        }
    }
}

internal static class HistoryPanelPreviewTemplateLookup
{
    private static MethodInfo? _getCardByIdMethod;
    private static Type? _lastStaticDataType;

    public static TCardBase? GetCardTemplate(object? staticData, Guid templateId)
    {
        if (staticData == null || templateId == Guid.Empty)
            return null;

        var staticType = staticData.GetType();
        if (!ReferenceEquals(_lastStaticDataType, staticType))
        {
            _lastStaticDataType = staticType;
            _getCardByIdMethod = staticType.GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        }

        return _getCardByIdMethod?.Invoke(staticData, new object[] { templateId }) as TCardBase;
    }
}
