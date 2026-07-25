#nullable enable
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.GameInterop.StaticCards;

/// <summary>
/// Keeps card titles consistent across live snapshots, replay reports, and native asset exports.
/// Stable template IDs remain the identity; this helper resolves presentation text only.
/// </summary>
internal static class BppCardDisplayName
{
    public static string Resolve(ITCard? template, string? fallback = null) =>
        ResolveText(template?.Localization?.Title?.Text, fallback, template?.InternalName);

    internal static string ResolveText(
        string? localizedTitle,
        string? fallback,
        string? internalName
    )
    {
        if (!string.IsNullOrWhiteSpace(localizedTitle))
            return localizedTitle.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();
        return string.IsNullOrWhiteSpace(internalName) ? string.Empty : internalName.Trim();
    }
}
