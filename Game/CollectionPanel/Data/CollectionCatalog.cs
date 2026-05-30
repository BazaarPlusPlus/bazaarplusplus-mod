#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Enumerates the game's runtime card map into a cached list of CollectionCardVms. The map
// itself is built once during JsonGameDataManager.Create() and is immutable thereafter
// (decompiled/TheBazaar.DataManagement.Json/JsonGameDataManager.cs:64-66), so iterating it
// from any frame after Data.IsManagerCreated() returns true is safe.
//
// Scope (design C7 + section 3.1):
//   - Only ECardType.Item and ECardType.Skill (~1644 templates; ~1571 after the art filter).
//   - Cards with missing or "Invalid" ArtKey are dropped (no placeholder rendering).
//
// Cache invalidation: CollectionPanelMount calls InvalidateCache() when the user changes
// the BPP Chinese locale mode so DisplayName regenerates on next build.
internal sealed class CollectionCatalog
{
    private IReadOnlyList<CollectionCardVm>? _cache;

    public bool TryBuild(out IReadOnlyList<CollectionCardVm> cards)
    {
        if (_cache != null)
        {
            cards = _cache;
            return true;
        }

        cards = Array.Empty<CollectionCardVm>();

        var managerObject = BppStaticDataAccess.TryGet();
        if (managerObject is not JsonGameDataManager manager)
        {
            BppLog.Debug(
                "CollectionCatalog",
                "Static data manager not yet ready; catalog build deferred."
            );
            return false;
        }

        Dictionary<Guid, ITCard>? map;
        try
        {
            map = manager.GetCardMap();
        }
        catch (Exception ex)
        {
            BppLog.Error("CollectionCatalog", "GetCardMap() threw", ex);
            return false;
        }

        if (map == null)
        {
            BppLog.Warn("CollectionCatalog", "GetCardMap() returned null.");
            return false;
        }

        var list = new List<CollectionCardVm>(map.Count);
        foreach (var entry in map)
        {
            if (entry.Value is not TCardBase template)
                continue;
            if (template.Type != ECardType.Item && template.Type != ECardType.Skill)
                continue;
            if (!HasValidArt(template))
                continue;
            list.Add(CollectionCardVm.From(template));
        }

        _cache = list;
        cards = list;
        BppLog.Info(
            "CollectionCatalog",
            $"Catalog built: {list.Count} cards from {map.Count} templates."
        );
        return true;
    }

    public void InvalidateCache() => _cache = null;

    // Mirrors CardPreviewBase.HasValidArtKey() (decompiled/TheBazaar.UI/CardPreviewBase.cs:166-173).
    private static bool HasValidArt(TCardBase template) =>
        !string.IsNullOrEmpty(template.ArtKey)
        && !string.Equals(template.ArtKey, "Invalid", StringComparison.Ordinal);
}
