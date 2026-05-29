#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.CardSetPreview;

// Pure text / CJK / transform-path helpers extracted from ItemBoardOverlay.
// All members are stateless and side-effect free.
internal static class ItemBoardTextHelpers
{
    public static string WrapWithColor(string text, Color color)
    {
        var colorHex = ColorUtility.ToHtmlStringRGB(color);
        return $"<color=#{colorHex}>{text}</color>";
    }

    public static string ReplaceFirst(string source, string target, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return source;

        var index = source.IndexOf(target, StringComparison.Ordinal);
        if (index < 0)
            return source;

        return source[..index] + replacement + source[(index + target.Length)..];
    }

    public static string ReplaceLast(string source, string target, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return source;

        var index = source.LastIndexOf(target, StringComparison.Ordinal);
        if (index < 0)
            return source;

        return source[..index] + replacement + source[(index + target.Length)..];
    }

    public static bool FontLooksCjkCapable(TMP_FontAsset font)
    {
        var name = font.name ?? string.Empty;
        return name.IndexOf("Han", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Noto", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CJK", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool ContainsCjk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var ch in text)
        {
            if (
                (ch >= 0x4E00 && ch <= 0x9FFF)
                || (ch >= 0x3400 && ch <= 0x4DBF)
                || (ch >= 0xF900 && ch <= 0xFAFF)
            )
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildTransformPath(Transform? transform)
    {
        if (transform == null)
            return "<unknown>";

        var segments = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            segments.Push(current.name);

        return string.Join("/", segments);
    }
}
