#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>
/// Resolves tag display data from the game's native tooltip typography: the keyword
/// configuration table first (official localized text, official color, uppercase rule), the
/// game string table second. Every failure point degrades to the native APIs' own behavior
/// (string-table miss returns the enum name); there is no mod-side tag dictionary.
/// Main thread only. The core lookup is string-keyed so the v2 keywords facet (EHiddenTag)
/// can reuse the same adapter.
/// </summary>
internal static class NativeTagTypography
{
    // GetConfiguration is private and overloaded (string / ECardAttributeType); resolve the
    // string overload explicitly. The MethodInfo is cached, but the TooltipTypography instance
    // is never cached: locale changes rebuild it as a fresh instance, so a stored reference
    // would silently go stale.
    private static readonly MethodInfo? GetConfigurationMethod = AccessTools.Method(
        typeof(TooltipTypography),
        "GetConfiguration",
        new[] { typeof(string) }
    );

    // Cache key: the typography instance reference (a new instance per locale change makes the
    // reference a natural invalidation key) plus the mod-side language code. Results resolved
    // while typography is null are NOT cached, so the table self-heals once the game's async
    // typography registration completes. The BPP Chinese script mode is deliberately NOT part
    // of the key: tag labels show the game's native zh-CN text as-is in Taiwan/HongKong modes
    // (per-character conversion of game vocabulary was judged worse than the script mismatch).
    private static readonly Dictionary<string, NativeTagDisplay> Cache = new(
        StringComparer.Ordinal
    );
    private static TooltipTypography? _cachedTypography;
    private static string _cachedLanguageCode = string.Empty;

    // One-time fail-closed switch for the reflection path (game update renamed the member or
    // changed its shape): labels keep flowing through the game string table, colors are lost.
    private static bool _configurationPathBroken;

    /// <summary>True once the game's async typography registration has completed (or after a
    /// locale change rebuilt the instance). While false, <see cref="Resolve(string)"/> degrades
    /// to the string-table path; consumers that rendered in that window can poll this to know
    /// when a re-render will pick up native labels and colors.</summary>
    public static bool IsNativeTypographyAvailable => Data.TooltipTypography != null;

    public static NativeTagDisplay Resolve(ECardTag tag) => Resolve(tag.ToString());

    public static NativeTagDisplay Resolve(EHiddenTag tag) => Resolve(tag.ToString());

    public static NativeTagDisplay Resolve(string key)
    {
        var typography = Data.TooltipTypography;
        var languageCode = L.CurrentLanguageCode;

        // Startup window (async registration pending) or tooltip host destroyed: resolve
        // through the string table only and skip the cache so the next call retries.
        if (typography == null)
            return ResolveUncached(null, key);

        if (
            !ReferenceEquals(typography, _cachedTypography)
            || !string.Equals(languageCode, _cachedLanguageCode, StringComparison.Ordinal)
        )
        {
            Cache.Clear();
            _cachedTypography = typography;
            _cachedLanguageCode = languageCode;
        }

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var display = ResolveUncached(typography, key);
        Cache[key] = display;
        return display;
    }

    private static NativeTagDisplay ResolveUncached(TooltipTypography? typography, string key)
    {
        string label;
        Color? accentColor = null;

        var configuration = GetConfigurationOrNull(typography, key);
        if (configuration != null)
        {
            label = LocalizeConfiguredText(configuration, key);
            if (configuration.MakeAllUppercase)
                label = label.ToUpperInvariant();
            accentColor = configuration.Color;
        }
        else
        {
            // No keyword configuration (legal state: the native tooltip hides such tags, but a
            // filter option must stay visible) — fall back to the game string table.
            label = LocalizeThroughStringTable(key);
        }

        return new NativeTagDisplay(label, accentColor);
    }

    private static KeywordIconColorConfiguration? GetConfigurationOrNull(
        TooltipTypography? typography,
        string key
    )
    {
        if (typography == null || _configurationPathBroken)
            return null;

        if (GetConfigurationMethod == null)
        {
            ReportConfigurationPathBroken(
                "TooltipTypography.GetConfiguration(string) was not found via reflection."
            );
            return null;
        }

        try
        {
            return GetConfigurationMethod.Invoke(typography, new object[] { key })
                as KeywordIconColorConfiguration;
        }
        catch (Exception ex)
        {
            ReportConfigurationPathBroken(
                $"TooltipTypography.GetConfiguration(string) invocation failed: {ex.Message}"
            );
            return null;
        }
    }

    // Only reachable while the broken flag is still unset, so this warns exactly once.
    private static void ReportConfigurationPathBroken(string reason)
    {
        _configurationPathBroken = true;
        BppLog.Warn(
            "NativeTagTypography",
            $"{reason} Tag labels fall back to the game string table without keyword colors."
        );
    }

    private static string LocalizeConfiguredText(
        KeywordIconColorConfiguration configuration,
        string key
    )
    {
        try
        {
            var text = configuration.Text.GetLocalizedText();
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch
        {
            return key;
        }
    }

    // The game string table keyed by the English source text; a miss returns the key itself
    // (LocalizableText's own fallback), matching the game's behavior for untranslated entries.
    private static string LocalizeThroughStringTable(string key)
    {
        try
        {
            var text = new LocalizableText(key).GetLocalizedText();
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch
        {
            return key;
        }
    }
}
