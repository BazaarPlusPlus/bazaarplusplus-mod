#nullable enable
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

/// <summary>
/// Pure cache-readiness policy used before startup recovery hands an immutable report to the
/// normal publication state machine. It deliberately performs no Unity or filesystem work.
/// Missing bindings are diagnostics only; startup recovery publishes every cache hit and leaves
/// unresolved entities to the Viewer's deterministic placeholder.
/// </summary>
internal static class CombatReplayReportRecoveryAssetGate
{
    internal static IReadOnlyList<string> FindMissingBindings(
        CombatReportDocumentV1 document,
        IReadOnlyList<PostCombatReportAssetFile> availableAssets,
        bool requireNativeBindings
    )
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));
        if (availableAssets == null)
            throw new ArgumentNullException(nameof(availableAssets));

        var entityBindings = availableAssets
            .Where(asset => asset.BindingKind == PostCombatReportAssetBindingKind.Entity)
            .Select(asset => asset.BindingKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var semanticBindings = availableAssets
            .Where(asset => asset.BindingKind == PostCombatReportAssetBindingKind.EventSemantic)
            .Select(asset => asset.BindingKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in document.Entities ?? [])
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.EntityId))
                continue;
            var type = entity.Type?.Trim().ToLowerInvariant();
            var requiresAsset =
                type is "item" or "skill" || (requireNativeBindings && type == "hero");
            if (requiresAsset && !entityBindings.Contains(entity.EntityId))
                missing.Add("entity:" + entity.EntityId);
        }

        if (requireNativeBindings)
        {
            foreach (var reportEvent in document.Events ?? [])
            {
                var semanticKey = reportEvent?.IconSemanticKey;
                if (
                    !string.IsNullOrWhiteSpace(semanticKey)
                    && !semanticBindings.Contains(semanticKey)
                )
                {
                    missing.Add("event-semantic:" + semanticKey);
                }
            }
        }

        return missing.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
