#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.Localization;
using TMPro;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BazaarPlusPlus.GameInterop.Localization;

/// <summary>
/// Loads the game's own zh-CN Noto fallback assets without switching the active locale or
/// modifying the game's global primary-font chains.
/// </summary>
internal static class NativeChineseFontFallback
{
    private const string Component = "BilingualNames";
    private const string ChineseLocale = "zh-CN";
    private static readonly List<AsyncOperationHandle<TMP_FontAsset>> Handles = new();
    private static readonly List<InstalledFallback> InstalledFallbacks = new();
    private static TMP_FontAsset[]? _serifFallbacks;
    private static TMP_FontAsset[]? _sansFallbacks;
    private static bool _configurationWarningLogged;
    private static bool _loadWarningLogged;

    internal static bool TryInstall(TMP_Text? text, string? sampleText)
    {
        if (text?.font == null || string.IsNullOrWhiteSpace(sampleText))
            return false;

        var preferSerif = text.font.name.IndexOf("Serif", StringComparison.OrdinalIgnoreCase) >= 0;
        var fallbacks = ResolveFallbacks(preferSerif);
        if (fallbacks.Length == 0 && preferSerif)
            fallbacks = ResolveFallbacks(preferSerif: false);
        if (fallbacks.Length == 0)
            return false;

        var table = text.font.fallbackFontAssetTable;
        if (table == null)
            return false;

        var installed = false;
        foreach (var fallback in fallbacks)
        {
            if (fallback == null || table.Contains(fallback))
                continue;

            table.Add(fallback);
            InstalledFallbacks.Add(new InstalledFallback(text.font, fallback));
            installed = true;
        }

        if (installed)
            text.ForceMeshUpdate(ignoreActiveState: true);
        return installed || fallbacks.Length > 0;
    }

    internal static void Reset()
    {
        foreach (var installed in InstalledFallbacks)
            installed.Primary?.fallbackFontAssetTable?.Remove(installed.Fallback);
        InstalledFallbacks.Clear();

        foreach (var handle in Handles)
            if (handle.IsValid())
                Addressables.Release(handle);
        Handles.Clear();

        _serifFallbacks = null;
        _sansFallbacks = null;
        _configurationWarningLogged = false;
        _loadWarningLogged = false;
    }

    private static TMP_FontAsset[] ResolveFallbacks(bool preferSerif)
    {
        var cached = preferSerif ? _serifFallbacks : _sansFallbacks;
        if (cached != null)
            return cached;

        var configuration = NotoFontFallbackRuntime._configuration;
        if (
            configuration == null
            || !configuration.TryGetRuleForLocale(ChineseLocale, out var rule)
        )
        {
            if (!_configurationWarningLogged)
            {
                _configurationWarningLogged = true;
                BppLog.Warn(
                    Component,
                    "The game's zh-CN font fallback configuration is not ready."
                );
            }
            return Array.Empty<TMP_FontAsset>();
        }

        var references = preferSerif
            ? rule.NotoSerifFallbacksOrdered
            : rule.NotoSansFallbacksOrdered;
        var loaded = LoadReferences(references);
        if (loaded.Length > 0)
        {
            if (preferSerif)
                _serifFallbacks = loaded;
            else
                _sansFallbacks = loaded;
        }
        return loaded;
    }

    private static TMP_FontAsset[] LoadReferences(AssetReferenceT<TMP_FontAsset>[]? references)
    {
        if (references == null || references.Length == 0)
            return Array.Empty<TMP_FontAsset>();

        var loaded = new List<TMP_FontAsset>(references.Length);
        foreach (var reference in references)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
                continue;

            try
            {
                var handle = Addressables.LoadAssetAsync<TMP_FontAsset>(reference.RuntimeKey);
                var font = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded || font == null)
                {
                    if (handle.IsValid())
                        Addressables.Release(handle);
                    continue;
                }

                Handles.Add(handle);
                font.ReadFontAssetDefinition();
                loaded.Add(font);
            }
            catch (Exception ex)
            {
                if (!_loadWarningLogged)
                {
                    _loadWarningLogged = true;
                    BppLog.Warn(Component, $"Failed to load the game's zh-CN font: {ex.Message}");
                }
            }
        }

        if (loaded.Count > 0)
            BppLog.Info(
                Component,
                $"Loaded the game's native zh-CN font fallback: {string.Join(", ", loaded.ConvertAll(font => font.name))}."
            );
        return loaded.ToArray();
    }

    private sealed class InstalledFallback
    {
        internal InstalledFallback(TMP_FontAsset primary, TMP_FontAsset fallback)
        {
            Primary = primary;
            Fallback = fallback;
        }

        internal TMP_FontAsset Primary { get; }

        internal TMP_FontAsset Fallback { get; }
    }
}
