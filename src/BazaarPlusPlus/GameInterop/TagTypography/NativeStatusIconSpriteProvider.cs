#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>
/// Resolves a report status semantic through the game's own tooltip keyword configuration and TMP
/// sprite atlas. No mod-authored semantic-to-filename table exists here: if either native stage is
/// unavailable, callers must omit the icon and retain the report's textual semantic.
/// </summary>
internal static class NativeStatusIconSpriteProvider
{
    internal static NativeStatusIconResolveOutcome Resolve(string nativeAttributeKey)
    {
        var display = NativeTagTypography.Resolve(nativeAttributeKey);
        if (string.IsNullOrWhiteSpace(display.IconName))
        {
            return NativeStatusIconResolveOutcome.Unavailable(
                nativeAttributeKey,
                NativeStatusIconFailureReason.NativeConfigurationHasNoIcon
            );
        }

        var spriteOutcome = KeywordIconSpriteProvider.Resolve(display.IconName);
        if (spriteOutcome.IsDegraded)
        {
            return NativeStatusIconResolveOutcome.Unavailable(
                nativeAttributeKey,
                NativeStatusIconFailureReason.SpriteProviderFailed,
                display.IconName
            );
        }
        if (spriteOutcome.Sprite == null)
        {
            return NativeStatusIconResolveOutcome.Unavailable(
                nativeAttributeKey,
                NativeStatusIconFailureReason.SpriteUnavailable,
                display.IconName
            );
        }

        return NativeStatusIconResolveOutcome.Ready(
            nativeAttributeKey,
            display.IconName,
            spriteOutcome.Sprite
        );
    }
}

internal enum NativeStatusIconFailureReason
{
    None,
    NativeConfigurationHasNoIcon,
    SpriteUnavailable,
    SpriteProviderFailed,
}

internal sealed class NativeStatusIconResolveOutcome
{
    private NativeStatusIconResolveOutcome(
        string nativeAttributeKey,
        string iconName,
        Sprite? sprite,
        NativeStatusIconFailureReason reason
    )
    {
        NativeAttributeKey = nativeAttributeKey;
        IconName = iconName;
        Sprite = sprite;
        Reason = reason;
    }

    internal string NativeAttributeKey { get; }
    internal string IconName { get; }
    internal Sprite? Sprite { get; }
    internal NativeStatusIconFailureReason Reason { get; }
    internal bool IsReady => Reason == NativeStatusIconFailureReason.None && Sprite != null;

    internal string StableNativeIdentity => $"attribute:{NativeAttributeKey}|icon:{IconName}";

    internal static NativeStatusIconResolveOutcome Ready(
        string nativeAttributeKey,
        string iconName,
        Sprite sprite
    ) => new(nativeAttributeKey, iconName, sprite, NativeStatusIconFailureReason.None);

    internal static NativeStatusIconResolveOutcome Unavailable(
        string nativeAttributeKey,
        NativeStatusIconFailureReason reason,
        string iconName = ""
    ) => new(nativeAttributeKey, iconName, null, reason);
}
