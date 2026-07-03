#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class FontDiagnostics
{
    private const string ChineseSample = "中文字体测试商人英雄价格";
    private static readonly string[] MacChineseFontNames =
    {
        "Hiragino Sans GB W6",
        "PingFang SC Heavy",
        "PingFang SC Semibold",
        "PingFang SC Medium",
        "PingFang SC",
        "Hiragino Sans GB",
        "STHeiti",
        "Heiti SC",
        "Arial Unicode MS",
    };

    private static readonly string[] WindowsChineseFontNames =
    {
        "Microsoft YaHei UI Bold",
        "Microsoft YaHei Bold",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimHei",
        "SimSun",
    };

    private static readonly string[] FallbackChineseFontNames =
    {
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans SC",
        "Arial Unicode MS",
        "PingFang SC",
        "Microsoft YaHei",
    };

    private static Font? _systemChineseUiFont;
    private static bool _systemChineseUiFontAttempted;
    private static bool _logged;

    public static bool HasChineseCoverage(TMP_FontAsset? font)
    {
        return HasFullChineseCoverage(font);
    }

    public static Font? ResolveSystemChineseUiFont()
    {
        if (_systemChineseUiFontAttempted)
            return _systemChineseUiFont;

        _systemChineseUiFontAttempted = true;

        try
        {
            var candidates = GetSystemChineseFontNames();
            _systemChineseUiFont = Font.CreateDynamicFontFromOSFont(candidates, 24);
            if (_systemChineseUiFont == null)
            {
                VoiceSubtitlesLog.Warn("Failed to create UI system font for Chinese subtitles");
                return null;
            }

            var resolvedFontNames =
                _systemChineseUiFont.fontNames == null
                    ? "<none>"
                    : string.Join(", ", _systemChineseUiFont.fontNames);
            VoiceSubtitlesLog.Info(
                "Created UI system font for Chinese subtitles "
                    + $"font='{_systemChineseUiFont.name}' "
                    + $"resolvedNames={VoiceSubtitlesLog.Field(resolvedFontNames)} "
                    + $"candidates={VoiceSubtitlesLog.Field(string.Join(", ", candidates))}"
            );
            return _systemChineseUiFont;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to create UI system font for Chinese subtitles: {ex.GetType().Name}: {ex.Message}\n{ex}"
            );
            return null;
        }
    }

    public static void LogOnce(TextMeshProUGUI sourceLabel, string reason)
    {
        if (_logged)
            return;

        _logged = true;

        try
        {
            var sourceFont = sourceLabel.font;
            VoiceSubtitlesLog.Info(
                "TMP font diagnostics "
                    + $"reason={reason} "
                    + $"sourceLabel='{BuildPath(sourceLabel.transform)}' "
                    + $"sourceFont={DescribeFont(sourceFont)} "
                    + $"sourceCoverage={DescribeCoverage(sourceFont)} "
                    + $"globalDefault={DescribeFont(TMP_Settings.defaultFontAsset)} "
                    + $"globalFallbacks={DescribeFontList(TMP_Settings.fallbackFontAssets)}"
            );

            var loadedFonts = Resources
                .FindObjectsOfTypeAll<TMP_FontAsset>()
                .Where(font => font != null)
                .GroupBy(font => font.GetInstanceID())
                .Select(group => group.First())
                .OrderByDescending(ChineseCoverageCount)
                .ThenBy(font => font.name, StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToArray();

            VoiceSubtitlesLog.Info(
                "Loaded TMP fonts with best Chinese coverage "
                    + $"sample={VoiceSubtitlesLog.Field(ChineseSample)} "
                    + $"count={loadedFonts.Length} "
                    + $"fonts={DescribeScoredFonts(loadedFonts)}"
            );
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to log TMP font diagnostics: {ex.GetType().Name}: {ex.Message}\n{ex}"
            );
        }
    }

    private static string DescribeScoredFonts(IReadOnlyList<TMP_FontAsset> fonts)
    {
        if (fonts.Count == 0)
            return "<none>";

        return string.Join(
            "; ",
            fonts.Select(font => $"{DescribeFont(font)} coverage={DescribeCoverage(font)}")
        );
    }

    private static string DescribeFontList(IReadOnlyList<TMP_FontAsset>? fonts)
    {
        if (fonts == null || fonts.Count == 0)
            return "<none>";

        return string.Join(", ", fonts.Where(font => font != null).Select(DescribeFont));
    }

    public static string DescribeFont(TMP_FontAsset? font)
    {
        if (font == null)
            return "<null>";

        return $"'{font.name}'#{font.GetInstanceID()}";
    }

    private static string DescribeCoverage(TMP_FontAsset? font)
    {
        if (font == null)
            return "0/0";

        return $"{ChineseCoverageCount(font)}/{ChineseSample.Length}";
    }

    private static int ChineseCoverageCount(TMP_FontAsset? font)
    {
        if (font == null)
            return 0;

        var count = 0;
        foreach (var character in ChineseSample)
        {
            if (font.HasCharacter(character, searchFallbacks: true, tryAddCharacter: false))
                count++;
        }

        return count;
    }

    private static bool HasFullChineseCoverage(TMP_FontAsset? font)
    {
        return font != null && ChineseCoverageCount(font) == ChineseSample.Length;
    }

    private static string[] GetSystemChineseFontNames()
    {
        return SystemInfo.operatingSystemFamily switch
        {
            OperatingSystemFamily.MacOSX => MacChineseFontNames
                .Concat(FallbackChineseFontNames)
                .ToArray(),
            OperatingSystemFamily.Windows => WindowsChineseFontNames
                .Concat(FallbackChineseFontNames)
                .ToArray(),
            _ => FallbackChineseFontNames
                .Concat(MacChineseFontNames)
                .Concat(WindowsChineseFontNames)
                .ToArray(),
        };
    }

    private static string BuildPath(Transform transform)
    {
        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
