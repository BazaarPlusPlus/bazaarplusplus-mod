#nullable enable
using System;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class HeroPortraitSpriteProvider
{
    private const string LogComponent = "HeroPortrait";

    private static readonly AsyncLoadCache<EHero, Sprite> Portraits = new(LoadPortraitAsync);

    internal static bool IsRenderableHero(EHero hero) =>
        hero != EHero.Common && !string.Equals(hero.ToString(), "Hero8", StringComparison.Ordinal);

    internal static bool TryGetCached(EHero hero, out Sprite? sprite)
    {
        sprite = null;
        return IsRenderableHero(hero) && Portraits.TryGetCached(hero, out sprite);
    }

    internal static Task<Sprite?> LoadDefaultPortraitAsync(EHero hero)
    {
        if (!IsRenderableHero(hero))
            return Task.FromResult<Sprite?>(null);

        return Portraits.GetOrLoadAsync(hero);
    }

    private static async Task<AsyncLoadResult<Sprite>> LoadPortraitAsync(EHero hero)
    {
        Sprite? result = null;
        var shouldCache = false;

        try
        {
            Services.TryGet<CollectionManager>(out var collectionManager);
            if (collectionManager == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"CollectionManager unavailable for hero={hero}; using text fallback."
                );
                return new AsyncLoadResult<Sprite>(null, shouldCache);
            }

            SkinAssetDataSO? skin = collectionManager.GetDefaultHeroSkin(hero);
            shouldCache = true;
            if (skin == null)
            {
                BppLog.Warn(
                    LogComponent,
                    $"No default hero skin for hero={hero}; using text fallback."
                );
                return new AsyncLoadResult<Sprite>(null, shouldCache);
            }

            result = await skin.LoadPortraitSpriteAsync();
            if (result == null)
                BppLog.Debug(
                    LogComponent,
                    $"No static portrait sprite for hero={hero}; using text fallback."
                );
            return new AsyncLoadResult<Sprite>(result, shouldCache);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogComponent,
                $"Failed to load hero portrait for hero={hero}: {ex.Message}"
            );
            return new AsyncLoadResult<Sprite>(null, shouldCache);
        }
    }
}
