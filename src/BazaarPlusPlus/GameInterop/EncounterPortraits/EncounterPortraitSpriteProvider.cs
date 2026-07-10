#nullable enable
using System;
using System.Threading.Tasks;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.EncounterPortraits;

internal static class EncounterPortraitSpriteProvider
{
    private const string LogComponent = "EncounterPortrait";

    private static readonly AsyncLoadCache<Guid, Sprite> Portraits = new(LoadPortraitCoreAsync);

    internal static bool TryGetCached(Guid sourceTemplateId, out Sprite? sprite)
    {
        sprite = null;
        return sourceTemplateId != Guid.Empty
            && Portraits.TryGetCached(sourceTemplateId, out sprite);
    }

    internal static Task<Sprite?> LoadPortraitAsync(Guid sourceTemplateId)
    {
        if (sourceTemplateId == Guid.Empty)
            return Task.FromResult<Sprite?>(null);

        return Portraits.GetOrLoadAsync(sourceTemplateId);
    }

    private static async Task<AsyncLoadResult<Sprite>> LoadPortraitCoreAsync(Guid sourceTemplateId)
    {
        Sprite? result = null;
        var shouldCache = false;
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, sourceTemplateId);
            if (
                template == null
                || string.IsNullOrWhiteSpace(template.ArtKey)
                || template.ArtKey == "Invalid"
            )
            {
                shouldCache = staticData != null;
                BppLog.Warn(
                    LogComponent,
                    $"No encounter art key sourceTemplateId={sourceTemplateId}; using text fallback."
                );
                return new AsyncLoadResult<Sprite>(null, shouldCache);
            }

            if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"AssetLoader unavailable sourceTemplateId={sourceTemplateId}; using text fallback."
                );
                return new AsyncLoadResult<Sprite>(null, shouldCache);
            }

            shouldCache = true;
            var encounterData = await assetLoader.LoadAssetAsyncByAddress<EncounterAssetDataSO>(
                template.ArtKey
            );
            if (encounterData == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"Encounter asset unavailable artKey={template.ArtKey}; using text fallback."
                );
                return new AsyncLoadResult<Sprite>(null, shouldCache);
            }

            result = await encounterData.LoadPortraitSpriteAsync();
            return new AsyncLoadResult<Sprite>(result, shouldCache);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogComponent,
                $"Failed to load encounter portrait sourceTemplateId={sourceTemplateId}: {ex.Message}"
            );
            return new AsyncLoadResult<Sprite>(null, shouldCache);
        }
    }
}
