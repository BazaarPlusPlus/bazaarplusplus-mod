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
        int Size);

    /// <summary>Emit one SelectItem AutoBazaarDecisionOption per owned card whose
    /// TemplateId is in <paramref name="filter"/>. When <paramref name="filter"/>
    /// is empty, returns empty. Skill-section cards get no TargetSockets list.</summary>
    public static IReadOnlyList<AutoBazaarDecisionOption> Emit(
        ISet<string> filter,
        IEnumerable<OwnedCardRef> ownedCards)
    {
        if (filter.Count == 0) return System.Array.Empty<AutoBazaarDecisionOption>();
        var result = new List<AutoBazaarDecisionOption>();
        var seen = new HashSet<string>();
        foreach (var c in ownedCards)
        {
            if (string.IsNullOrEmpty(c.TemplateId)) continue;
            if (!filter.Contains(c.TemplateId)) continue;
            if (!seen.Add(c.InstanceId)) continue;

            IReadOnlyList<string>? sockets = null;
            if ((c.Section is AutoBazaarTargetSection.Hand || c.Section is AutoBazaarTargetSection.Stash)
                && !string.IsNullOrEmpty(c.LeftSocketId)
                && c.Size > 0
                && TryParseSocketIndex(c.LeftSocketId, out var start))
            {
                var list = new string[c.Size];
                for (var i = 0; i < c.Size; i++) list[i] = "Socket_" + (start + i);
                sockets = list;
            }

            var socketSeg = sockets is null ? "" : ":" + string.Join(",", sockets);
            result.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.SelectItem,
                Group = AutoBazaarActionGroup.Offer,
                DisplayKey = "SelectItem:" + c.InstanceId + ":" + c.Section + socketSeg,
                CardInstanceId = c.InstanceId,
                TargetSection = c.Section,
                TargetSockets = sockets,
            });
        }
        return result;
    }

    private static bool TryParseSocketIndex(string socketId, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(socketId)) return false;
        const string prefix = "Socket_";
        if (!socketId.StartsWith(prefix, System.StringComparison.Ordinal)) return false;
        return int.TryParse(socketId.Substring(prefix.Length), out index);
    }
}
