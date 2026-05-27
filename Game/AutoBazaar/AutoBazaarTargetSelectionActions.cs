#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarTargetSelectionActions
{
    /// <summary>Lightweight value snapshot of an owned card, with everything the
    /// emitter needs to build a SelectItem decision option targeting it. Decoupled
    /// from game types so the helper stays unit-testable.</summary>
    internal readonly record struct OwnedCardRef(
        string InstanceId,
        string TemplateId,
        AutoBazaarTargetSection Section,
        string LeftSocketId,
        int Size
    );

    /// <summary>Emit one SelectItem AutoBazaarDecisionOption per owned card whose
    /// TemplateId is in <paramref name="filter"/>. When <paramref name="filter"/>
    /// is empty, returns empty. Skill-section cards get no TargetSockets list.</summary>
    public static IReadOnlyList<AutoBazaarDecisionOption> Emit(
        ISet<string> filter,
        IEnumerable<OwnedCardRef> ownedCards
    )
    {
        if (filter.Count == 0)
            return System.Array.Empty<AutoBazaarDecisionOption>();
        var result = new List<AutoBazaarDecisionOption>();
        var seen = new HashSet<string>();
        foreach (var c in ownedCards)
        {
            if (string.IsNullOrEmpty(c.TemplateId))
                continue;
            if (!filter.Contains(c.TemplateId))
                continue;
            if (!seen.Add(c.InstanceId))
                continue;
            if (
                c.Section is not AutoBazaarTargetSection.Hand
                && c.Section is not AutoBazaarTargetSection.Stash
            )
            {
                continue;
            }

            IReadOnlyList<string>? sockets = null;
            if (
                !string.IsNullOrEmpty(c.LeftSocketId)
                && c.Size > 0
                && TryParseSocketIndex(c.LeftSocketId, out var start)
            )
            {
                var list = new string[c.Size];
                for (var i = 0; i < c.Size; i++)
                    list[i] = "Socket_" + (start + i);
                sockets = list;
            }

            var socketSeg = sockets is null ? "" : ":" + string.Join(",", sockets);
            result.Add(
                new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.SelectItem,
                    Group = AutoBazaarActionGroup.Offer,
                    DisplayKey = "SelectItem:" + c.InstanceId + ":" + c.Section + socketSeg,
                    CardInstanceId = c.InstanceId,
                    TargetSection = c.Section,
                    TargetSockets = sockets,
                }
            );
        }
        return result;
    }

    private static bool TryParseSocketIndex(string socketId, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(socketId))
            return false;
        const string prefix = "Socket_";
        if (!socketId.StartsWith(prefix, System.StringComparison.Ordinal))
            return false;
        return int.TryParse(socketId.Substring(prefix.Length), out index);
    }

    /// <summary>Target-selection mode (filter non-empty): preserve only the
    /// <c>SelectItem</c> options whose underlying templateId is in the filter (these
    /// are the only clicks the game accepts), then append owned-card SelectItem
    /// options for any owned card whose templateId is in the filter — covers the
    /// BuySpecificCardCondition._canInteractWithOwnedCards=true variant. Existing
    /// non-SelectItem options pass through untouched.</summary>
    public static IReadOnlyList<AutoBazaarDecisionOption> ApplyTargetSelectionFilter(
        IReadOnlyList<AutoBazaarDecisionOption> actions,
        ISet<string> filter,
        IReadOnlyList<AutoBazaarCardSnapshot> boardItems,
        IReadOnlyList<AutoBazaarCardSnapshot> chestItems,
        IReadOnlyList<AutoBazaarCardSnapshot> playerSkills,
        IReadOnlyList<AutoBazaarCardSnapshot> selectionOptionsCards
    )
    {
        var templateByInstance = new Dictionary<string, string>();
        AddTemplates(templateByInstance, boardItems);
        AddTemplates(templateByInstance, chestItems);
        AddTemplates(templateByInstance, playerSkills);
        AddTemplates(templateByInstance, selectionOptionsCards);

        var kept = new List<AutoBazaarDecisionOption>(actions.Count);
        var keptInstanceIds = new HashSet<string>();
        foreach (var a in actions)
        {
            if (a.ActionKind != AutoBazaarActionKind.SelectItem)
            {
                kept.Add(a);
                continue;
            }
            if (a.CardInstanceId is null)
                continue;
            if (!templateByInstance.TryGetValue(a.CardInstanceId, out var tid))
                continue;
            if (!filter.Contains(tid))
                continue;
            kept.Add(a);
            keptInstanceIds.Add(a.CardInstanceId);
        }

        var owned = new List<OwnedCardRef>();
        AddOwnedRefs(owned, boardItems, AutoBazaarTargetSection.Hand);
        AddOwnedRefs(owned, chestItems, AutoBazaarTargetSection.Stash);

        var targetOpts = Emit(filter, owned);
        foreach (var opt in targetOpts)
        {
            if (opt.CardInstanceId is null)
                continue;
            if (!keptInstanceIds.Add(opt.CardInstanceId))
                continue;
            kept.Add(opt);
        }
        return kept;
    }

    private static void AddTemplates(
        Dictionary<string, string> sink,
        IReadOnlyList<AutoBazaarCardSnapshot> cards
    )
    {
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.InstanceId) || string.IsNullOrEmpty(c.TemplateId))
                continue;
            if (!sink.ContainsKey(c.InstanceId))
                sink[c.InstanceId] = c.TemplateId!;
        }
    }

    private static void AddOwnedRefs(
        List<OwnedCardRef> sink,
        IReadOnlyList<AutoBazaarCardSnapshot> cards,
        AutoBazaarTargetSection section
    )
    {
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.TemplateId))
                continue;
            int size = ParseCardSize(c.Size);
            sink.Add(
                new OwnedCardRef(
                    InstanceId: c.InstanceId,
                    TemplateId: c.TemplateId!,
                    Section: section,
                    LeftSocketId: c.SocketId ?? "",
                    Size: size
                )
            );
        }
    }

    private static int ParseCardSize(string? size) =>
        size switch
        {
            "Small" => 1,
            "Medium" => 2,
            "Large" => 3,
            _ => 1,
        };
}
