#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomCollectiblePool.Selected";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        var readIds = TheDragonsHeroIdentity.PersistenceReadIds(hero);
        var accountScope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(
            RandomPoolKind.Collectible
        );
        var canonicalKey = BuildScopedPrefsKey(readIds[0], collectionType, accountScope);
        if (PlayerPrefs.HasKey(canonicalKey))
        {
            return RandomPoolPrefsHelpers.LoadIdCollection(
                canonicalKey,
                RandomPoolKind.Collectible
            );
        }

        for (var index = 1; index < readIds.Count; index++)
        {
            var legacyKey = BuildScopedPrefsKey(readIds[index], collectionType, accountScope);
            if (!PlayerPrefs.HasKey(legacyKey))
                continue;

            var selectedIds = RandomPoolPrefsHelpers.LoadIdCollection(
                legacyKey,
                RandomPoolKind.Collectible
            );
            if (selectedIds == null)
                return null;

            RandomPoolPrefsHelpers.SaveIdCollection(canonicalKey, selectedIds);
            PlayerPrefs.DeleteKey(legacyKey);
            PlayerPrefs.Save();
            return selectedIds;
        }

        return null;
    }

    public static void SaveSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        IEnumerable<string> ids
    )
    {
        var accountScope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(
            RandomPoolKind.Collectible
        );
        RandomPoolPrefsHelpers.SaveIdCollection(
            BuildScopedPrefsKey(
                TheDragonsHeroIdentity.ToCanonicalId(hero),
                collectionType,
                accountScope
            ),
            ids
        );
    }

    private static string BuildScopedPrefsKey(
        string heroId,
        BazaarInventoryTypes.ECollectionType collectionType,
        string accountScope
    )
    {
        return $"{SelectedPoolPrefsKeyPrefix}.{Uri.EscapeDataString(collectionType.ToString())}.{Uri.EscapeDataString(heroId)}.{accountScope}";
    }
}
