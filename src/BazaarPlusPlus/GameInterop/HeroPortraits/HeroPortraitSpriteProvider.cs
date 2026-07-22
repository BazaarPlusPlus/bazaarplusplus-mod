#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class HeroPortraitSpriteProvider
{
    private static readonly AsyncLoadCache<EHero, HeroPortraitLoadOutcome> Portraits = new(
        LoadPortraitCoreAsync
    );

    internal static bool IsRenderableHero(EHero hero) =>
        hero != EHero.Common && !string.Equals(hero.ToString(), "Hero8", StringComparison.Ordinal);

    internal static bool TryGetCached(EHero hero, out HeroPortraitLoadOutcome? outcome)
    {
        outcome = null;
        return IsRenderableHero(hero) && Portraits.TryGetCached(hero, out outcome);
    }

    internal static Task<HeroPortraitLoadOutcome?> LoadDefaultPortraitAsync(EHero hero)
    {
        if (!IsRenderableHero(hero))
            return Task.FromResult<HeroPortraitLoadOutcome?>(null);

        return Portraits.GetOrLoadAsync(hero);
    }

    private static async Task<AsyncLoadResult<HeroPortraitLoadOutcome>> LoadPortraitCoreAsync(
        EHero hero
    )
    {
        var shouldCache = false;

        try
        {
            Services.TryGet<CollectionManager>(out var collectionManager);
            if (collectionManager == null)
            {
                return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                    HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.CollectionManagerUnavailable
                    ),
                    shouldCache
                );
            }

            SkinAssetDataSO? skin = collectionManager.GetDefaultHeroSkin(hero);
            shouldCache = true;
            if (skin == null)
            {
                return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                    HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.DefaultSkinUnavailable
                    ),
                    shouldCache
                );
            }

            var identity = HeroPortraitAssetIdentity.From(hero, skin);
            var result = await skin.LoadPortraitSpriteAsync();
            return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                result == null
                    ? HeroPortraitLoadOutcome.Degraded(
                        HeroPortraitFailureReason.PortraitUnavailable,
                        identity: identity
                    )
                    : HeroPortraitLoadOutcome.Ready(result, identity),
                shouldCache
            );
        }
        catch (Exception ex)
        {
            return new AsyncLoadResult<HeroPortraitLoadOutcome>(
                HeroPortraitLoadOutcome.Degraded(HeroPortraitFailureReason.LoadException, ex),
                shouldCache
            );
        }
    }
}

internal sealed record HeroPortraitAssetIdentity(
    EHero Hero,
    string SkinAssetName,
    string PortraitAssetGuid,
    string PortraitSubObjectName
)
{
    internal string StableKey =>
        $"hero:{(int)Hero}:{Hero}|skin:{SkinAssetName}|portrait:{PortraitAssetGuid}:{PortraitSubObjectName}";

    internal static HeroPortraitAssetIdentity From(EHero hero, SkinAssetDataSO skin)
    {
        var portrait = skin.portraitTextureReference;
        return new HeroPortraitAssetIdentity(
            hero,
            string.IsNullOrWhiteSpace(skin.name) ? "unnamed-default-skin" : skin.name.Trim(),
            string.IsNullOrWhiteSpace(portrait?.AssetGUID)
                ? "missing-portrait-guid"
                : portrait.AssetGUID.Trim(),
            string.IsNullOrWhiteSpace(portrait?.SubObjectName)
                ? "default-subobject"
                : portrait.SubObjectName.Trim()
        );
    }
}

internal enum HeroPortraitFailureReason
{
    None,
    CollectionManagerUnavailable,
    DefaultSkinUnavailable,
    PortraitUnavailable,
    LoadException,
}

internal sealed class HeroPortraitLoadOutcome
{
    private HeroPortraitLoadOutcome(
        Sprite? sprite,
        HeroPortraitFailureReason reason,
        Exception? exception,
        HeroPortraitAssetIdentity? identity
    )
    {
        Sprite = sprite;
        Reason = reason;
        Exception = exception;
        Identity = identity;
    }

    internal Sprite? Sprite { get; }
    internal HeroPortraitFailureReason Reason { get; }
    internal Exception? Exception { get; }
    internal HeroPortraitAssetIdentity? Identity { get; }
    internal bool IsDegraded => Reason != HeroPortraitFailureReason.None;

    internal static HeroPortraitLoadOutcome Ready(
        Sprite sprite,
        HeroPortraitAssetIdentity identity
    ) => new(sprite, HeroPortraitFailureReason.None, null, identity);

    internal static HeroPortraitLoadOutcome Degraded(
        HeroPortraitFailureReason reason,
        Exception? exception = null,
        HeroPortraitAssetIdentity? identity = null
    ) => new(null, reason, exception, identity);
}
