#nullable enable
using System;
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

    private static TMP_FontAsset? _default;
    private static bool _loadFailureLogged;

    public static bool TryApply(TextMeshProUGUI? text, string? sampleText)
    {
        if (text == null || !BppTmpFontPolicy.ShouldUseEmbeddedCjkFont(sampleText))
            return false;

        var fontAsset = ResolveDefault();
        if (fontAsset == null)
            return false;

        text.font = fontAsset;
        if (fontAsset.material != null)
            text.fontSharedMaterial = fontAsset.material;

        WarmCharacters(fontAsset, sampleText);
        return true;
    }

    private static TMP_FontAsset? ResolveDefault()
    {
        if (_default != null)
            return _default;

        try
        {
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                BppUiFont.Default,
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

            fontAsset.name = "BPP SourceHanSansCN TMP";
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

    private static void LogLoadFailure(string reason)
    {
        if (_loadFailureLogged)
            return;

        _loadFailureLogged = true;
        BppLog.Warn(Component, $"Failed to load embedded TMP UI font. {reason}");
    }
}
