#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class BppTmpFont
{
    private const string Component = "TmpFont";
    private const int SamplingPointSize = 90;
    private const int AtlasPadding = 9;
    private const int AtlasSize = 2048;
    private static readonly string[] SystemCjkFontNames =
    {
        "PingFang SC",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Arial Unicode MS",
    };

    private static TMP_FontAsset? _default;
    private static TMP_FontAsset? _systemCjk;
    private static bool _loadFailureLogged;
    private static bool _systemCjkLoadFailureLogged;
    private static readonly ConditionalWeakTable<TMP_Text, FontSnapshot> OriginalFonts = new();

    public static bool TryApply(TMP_Text? text, string? sampleText)
    {
        if (text == null)
            return false;

        if (!BppTmpFontPolicy.ShouldUseEmbeddedCjkFont(sampleText))
        {
            RestoreOriginal(text);
            return false;
        }

        var fontAsset = ResolveDefault();
        if (fontAsset == null)
            return false;

        CaptureOriginal(text);
        text.font = fontAsset;
        if (fontAsset.material != null)
            text.fontSharedMaterial = fontAsset.material;

        WarmCharacters(fontAsset, sampleText);
        return true;
    }

    public static bool TryInstallSystemCjkFallback(TMP_Text? text, string? sampleText)
    {
        if (text?.font == null || !BppTmpFontPolicy.ShouldUseEmbeddedCjkFont(sampleText))
            return false;

        var fontAsset = ResolveSystemCjk();
        if (fontAsset == null)
            return false;

        var fallbacks = text.font.fallbackFontAssetTable;
        if (fallbacks == null)
        {
            fallbacks = new List<TMP_FontAsset>();
            text.font.fallbackFontAssetTable = fallbacks;
        }
        if (!fallbacks.Contains(fontAsset))
            fallbacks.Add(fontAsset);

        WarmCharacters(fontAsset, sampleText);
        return true;
    }

    private static TMP_FontAsset? ResolveSystemCjk()
    {
        if (_systemCjk != null)
            return _systemCjk;

        try
        {
            var systemFont = Font.CreateDynamicFontFromOSFont(
                SystemCjkFontNames,
                SamplingPointSize
            );
            if (systemFont == null)
            {
                LogSystemCjkLoadFailure("CreateDynamicFontFromOSFont returned null.");
                return null;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                systemFont,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true
            );
            if (fontAsset == null)
            {
                LogSystemCjkLoadFailure("CreateFontAsset returned null.");
                return null;
            }

            fontAsset.name = $"BPP System CJK TMP ({systemFont.name})";
            _systemCjk = fontAsset;
            BppLog.Info(Component, $"Loaded system CJK TMP font '{fontAsset.name}'.");
            return _systemCjk;
        }
        catch (Exception ex)
        {
            LogSystemCjkLoadFailure(ex.Message);
            return null;
        }
    }

    private static TMP_FontAsset? ResolveDefault()
    {
        if (_default != null)
            return _default;

        try
        {
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                BppUiFont.LxgwWenKai,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true
            );
            if (fontAsset == null)
            {
                LogLoadFailure("CreateFontAsset returned null.");
                return null;
            }

            fontAsset.name = "BPP LXGWWenKai TMP";
            _default = fontAsset;
            BppLog.Info(Component, $"Loaded TMP UI font '{fontAsset.name}'.");
            return _default;
        }
        catch (Exception ex)
        {
            LogLoadFailure(ex.Message);
            return null;
        }
    }

    private static void WarmCharacters(TMP_FontAsset fontAsset, string? sampleText)
    {
        if (string.IsNullOrEmpty(sampleText))
            return;

        try
        {
            fontAsset.TryAddCharacters(sampleText, out _);
        }
        catch (Exception ex)
        {
            BppLog.Debug(Component, $"Failed to warm TMP glyphs: {ex.Message}");
        }
    }

    private static void CaptureOriginal(TMP_Text text)
    {
        if (OriginalFonts.TryGetValue(text, out _))
            return;

        OriginalFonts.Add(
            text,
            new FontSnapshot { Font = text.font, SharedMaterial = text.fontSharedMaterial }
        );
    }

    private static void RestoreOriginal(TMP_Text text)
    {
        if (!OriginalFonts.TryGetValue(text, out var snapshot))
            return;

        if (snapshot.Font != null)
            text.font = snapshot.Font;
        if (snapshot.SharedMaterial != null)
            text.fontSharedMaterial = snapshot.SharedMaterial;
    }

    private static void LogLoadFailure(string reason)
    {
        if (_loadFailureLogged)
            return;

        _loadFailureLogged = true;
        BppLog.Warn(Component, $"Failed to load embedded TMP UI font. {reason}");
    }

    private static void LogSystemCjkLoadFailure(string reason)
    {
        if (_systemCjkLoadFailureLogged)
            return;

        _systemCjkLoadFailureLogged = true;
        BppLog.Warn(Component, $"Failed to load a system CJK TMP font. {reason}");
    }

    private sealed class FontSnapshot
    {
        public TMP_FontAsset? Font { get; init; }
        public Material? SharedMaterial { get; init; }
    }
}
