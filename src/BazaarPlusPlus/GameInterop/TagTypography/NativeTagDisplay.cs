#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>Resolved native display data for one tag: the locale-correct label and the game's
/// official keyword accent color (null when the tag has no keyword configuration).</summary>
internal readonly struct NativeTagDisplay(string label, Color? accentColor)
{
    public string Label { get; } = label;

    public Color? AccentColor { get; } = accentColor;
}
