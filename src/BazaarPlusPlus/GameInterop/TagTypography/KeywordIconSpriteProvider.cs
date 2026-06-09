#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>
/// Resolves a keyword icon name to a UITK-usable sprite. Native tooltips render keyword icons as
/// TMP sprite markup, which UI Toolkit chips do not parse, so this builds a Sprite over the TMP
/// atlas glyph rect instead. Main thread only; icons are locale-invariant and do not clear with
/// NativeTagTypography's locale cache.
/// </summary>
internal static class KeywordIconSpriteProvider
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);
    private static TMP_SpriteAsset? _spriteAsset;
    private static bool _scannedThisPass;

    public static void BeginResolvePass() => _scannedThisPass = false;

    public static Sprite? Resolve(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;
        if (Cache.TryGetValue(iconName, out var cached))
            return cached;

        try
        {
            var asset = ResolveSpriteAsset(iconName);
            if (asset == null)
                return null;

            var sprite = ExtractSprite(asset, iconName);
            if (sprite != null)
                Cache[iconName] = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "KeywordIconSpriteProvider",
                $"Icon '{iconName}' failed to resolve: {ex.Message}"
            );
            return null;
        }
    }

    private static TMP_SpriteAsset? ResolveSpriteAsset(string iconName)
    {
        if (_spriteAsset != null && _spriteAsset.GetSpriteIndexFromName(iconName) >= 0)
            return _spriteAsset;

        if (_scannedThisPass)
            return null;
        _scannedThisPass = true;

        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            var asset = text != null ? text.spriteAsset : null;
            if (asset != null && asset.GetSpriteIndexFromName(iconName) >= 0)
                return _spriteAsset = asset;
        }

        foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
        {
            if (asset != null && asset.GetSpriteIndexFromName(iconName) >= 0)
                return _spriteAsset = asset;
        }

        return null;
    }

    private static Sprite? ExtractSprite(TMP_SpriteAsset asset, string iconName)
    {
        var index = asset.GetSpriteIndexFromName(iconName);
        var table = asset.spriteCharacterTable;
        if (index < 0 || table == null || index >= table.Count)
            return null;

        if (table[index]?.glyph is not TMP_SpriteGlyph glyph)
            return null;

        if (glyph.sprite != null)
            return glyph.sprite;

        if (asset.spriteSheet is not Texture2D atlas)
            return null;

        var rect = glyph.glyphRect;
        if (rect.width <= 0 || rect.height <= 0)
            return null;

        return Sprite.Create(
            atlas,
            new Rect(rect.x, rect.y, rect.width, rect.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect
        );
    }
}
