#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Localization;
using TMPro;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Localization;

/// <summary>
/// Loads the game's own zh-CN Noto fallback assets without switching the active locale or
/// modifying the game's global primary-font chains.
/// </summary>
internal static class NativeChineseFontFallback
{
    private const string ChineseLocale = "zh-CN";
    private const int HealthKey = 0;
    private static readonly List<AsyncOperationHandle<TMP_FontAsset>> Handles = new();
    private static readonly List<FontBinding> Bindings = new();
    private static TMP_FontAsset[]? _serifFallbacks;
    private static TMP_FontAsset[]? _sansFallbacks;
    private static readonly OperationalHealthTracker<int, BilingualLogReasonCode> Health = new();
    private static bool _readyReported;

    internal static bool TryInstall(TMP_Text? text, string? sampleText)
    {
        if (text?.font == null || string.IsNullOrWhiteSpace(sampleText))
            return false;

        var preferSerif = text.font.name.IndexOf("Serif", StringComparison.OrdinalIgnoreCase) >= 0;
        var attempt = ResolveFallbacks(preferSerif);
        if (attempt.Fonts.Length == 0 && preferSerif)
        {
            var sansAttempt = ResolveFallbacks(preferSerif: false);
            attempt =
                sansAttempt.Fonts.Length > 0 ? sansAttempt : attempt.MergeFailure(sansAttempt);
        }
        var fallbacks = attempt.Fonts;
        if (attempt.WasAttempted)
        {
            if (fallbacks.Length > 0)
                ReportSuccess(fallbacks);
            else
                ReportFailure(attempt.Stage, attempt.ReasonCode, attempt.Exception);
        }
        if (fallbacks.Length == 0)
            return false;

        var binding = FindOrCreateBinding(text);
        if (binding == null || binding.Clone == null)
            return false;

        var table = binding.Clone.fallbackFontAssetTable;
        if (table == null)
            return false;

        var installed = false;
        foreach (var fallback in fallbacks)
        {
            if (fallback == null || table.Contains(fallback))
                continue;

            table.Add(fallback);
            installed = true;
        }

        if (installed)
            text.ForceMeshUpdate(ignoreActiveState: true);
        return installed || fallbacks.Length > 0;
    }

    internal static void Reset()
    {
        try
        {
            foreach (var binding in Bindings)
            {
                try
                {
                    if (
                        binding.Text != null
                        && binding.Clone != null
                        && binding.Text.font == binding.Clone
                        && binding.Original != null
                    )
                        binding.Text.font = binding.Original;
                }
                catch (Exception ex)
                {
                    BppLog.DebugEvent(
                        BilingualItemNamesLogEvents.FontFallbackCleanupFailed,
                        ex,
                        () =>
                            [
                                BilingualItemNamesLogEvents.FontFallbackCleanupFailedStage.Bind(
                                    BilingualFontStage.RestoreBinding
                                ),
                            ]
                    );
                }
                finally
                {
                    if (binding.Clone != null)
                        Object.DestroyImmediate(binding.Clone);
                }
            }
            Bindings.Clear();
        }
        finally
        {
            foreach (var handle in Handles)
                TryRelease(handle);
            Handles.Clear();
        }

        _serifFallbacks = null;
        _sansFallbacks = null;
        Health.Reset();
        _readyReported = false;
    }

    private static FontBinding? FindOrCreateBinding(TMP_Text text)
    {
        foreach (var binding in Bindings)
            if (binding.Text == text)
                return binding;

        var primary = text.font;
        if (primary == null)
            return null;

        var clone = Object.Instantiate(primary);
        clone.name = $"{primary.name} (BPP Bilingual Tooltip)";
        clone.fallbackFontAssetTable = new List<TMP_FontAsset>(
            primary.fallbackFontAssetTable ?? new List<TMP_FontAsset>()
        );
        text.font = clone;

        var created = new FontBinding(text, primary, clone);
        Bindings.Add(created);
        return created;
    }

    private static FontLoadAttempt ResolveFallbacks(bool preferSerif)
    {
        var cached = preferSerif ? _serifFallbacks : _sansFallbacks;
        if (cached != null)
            return FontLoadAttempt.Cached(cached);

        var configuration = NotoFontFallbackRuntime._configuration;
        if (
            configuration == null
            || !configuration.TryGetRuleForLocale(ChineseLocale, out var rule)
        )
        {
            return FontLoadAttempt.Failed(
                BilingualFontStage.ResolveConfiguration,
                BilingualLogReasonCode.ConfigurationUnavailable
            );
        }

        var references = preferSerif
            ? rule.NotoSerifFallbacksOrdered
            : rule.NotoSansFallbacksOrdered;
        var attempt = LoadReferences(references);
        if (attempt.Fonts.Length > 0)
        {
            if (preferSerif)
                _serifFallbacks = attempt.Fonts;
            else
                _sansFallbacks = attempt.Fonts;
        }
        return attempt;
    }

    private static FontLoadAttempt LoadReferences(AssetReferenceT<TMP_FontAsset>[]? references)
    {
        if (references == null || references.Length == 0)
            return FontLoadAttempt.Failed(
                BilingualFontStage.LoadFonts,
                BilingualLogReasonCode.FontReferencesUnavailable
            );

        var loaded = new List<TMP_FontAsset>(references.Length);
        Exception? firstException = null;
        var sawLoadFailure = false;
        var sawValidReference = false;
        foreach (var reference in references)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
                continue;
            sawValidReference = true;

            AsyncOperationHandle<TMP_FontAsset> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<TMP_FontAsset>(reference.RuntimeKey);
                var font = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded || font == null)
                {
                    sawLoadFailure = true;
                    continue;
                }

                font.ReadFontAssetDefinition();
                loaded.Add(font);
                Handles.Add(handle);
                handle = default;
            }
            catch (Exception ex)
            {
                sawLoadFailure = true;
                firstException ??= ex;
            }
            finally
            {
                TryRelease(handle);
            }
        }

        if (loaded.Count > 0)
            return FontLoadAttempt.Succeeded(loaded.ToArray());
        return FontLoadAttempt.Failed(
            BilingualFontStage.LoadFonts,
            sawLoadFailure
                ? BilingualLogReasonCode.FontLoadFailed
                : BilingualLogReasonCode.FontReferencesUnavailable,
            firstException,
            wasAttempted: sawValidReference || references.Length > 0
        );
    }

    private static void TryRelease(AsyncOperationHandle<TMP_FontAsset> handle)
    {
        try
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                BilingualItemNamesLogEvents.FontFallbackCleanupFailed,
                ex,
                () =>
                    [
                        BilingualItemNamesLogEvents.FontFallbackCleanupFailedStage.Bind(
                            BilingualFontStage.ReleaseHandle
                        ),
                    ]
            );
        }
    }

    private static void ReportFailure(
        BilingualFontStage stage,
        BilingualLogReasonCode reasonCode,
        Exception? exception
    )
    {
        _readyReported = false;
        if (!Health.ObserveFailure(HealthKey, reasonCode))
            return;
        var fields = new[]
        {
            BilingualItemNamesLogEvents.FontFallbackDegradedStage.Bind(stage),
            BilingualItemNamesLogEvents.FontFallbackDegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(BilingualItemNamesLogEvents.FontFallbackDegraded, fields);
        else
            BppLog.WarnEvent(BilingualItemNamesLogEvents.FontFallbackDegraded, exception, fields);
    }

    private static void ReportSuccess(TMP_FontAsset[] fonts)
    {
        if (Health.ObserveSuccess(HealthKey, out _))
        {
            _readyReported = true;
            BppLog.RecoverStorm(BilingualItemNamesLogEvents.FontFallbackDegraded);
            BppLog.InfoEvent(
                BilingualItemNamesLogEvents.FontFallbackRecovered,
                BilingualItemNamesLogEvents.FontFallbackRecoveredFontCount.Bind(fonts.Length),
                BilingualItemNamesLogEvents.FontFallbackRecoveredFontNames.Bind(
                    string.Join(",", Array.ConvertAll(fonts, font => font.name))
                )
            );
            return;
        }

        if (_readyReported)
            return;
        _readyReported = true;
        BppLog.DebugEvent(
            BilingualItemNamesLogEvents.FontFallbackLoaded,
            () =>
                [
                    BilingualItemNamesLogEvents.FontFallbackLoadedFontCount.Bind(fonts.Length),
                    BilingualItemNamesLogEvents.FontFallbackLoadedFontNames.Bind(
                        string.Join(",", Array.ConvertAll(fonts, font => font.name))
                    ),
                ]
        );
    }

    private readonly struct FontLoadAttempt
    {
        private FontLoadAttempt(
            TMP_FontAsset[] fonts,
            bool wasAttempted,
            BilingualFontStage stage,
            BilingualLogReasonCode reasonCode,
            Exception? exception
        )
        {
            Fonts = fonts;
            WasAttempted = wasAttempted;
            Stage = stage;
            ReasonCode = reasonCode;
            Exception = exception;
        }

        internal TMP_FontAsset[] Fonts { get; }
        internal bool WasAttempted { get; }
        internal BilingualFontStage Stage { get; }
        internal BilingualLogReasonCode ReasonCode { get; }
        internal Exception? Exception { get; }

        internal static FontLoadAttempt Cached(TMP_FontAsset[] fonts) =>
            new(fonts, false, default, default, null);

        internal static FontLoadAttempt Succeeded(TMP_FontAsset[] fonts) =>
            new(fonts, true, default, default, null);

        internal static FontLoadAttempt Failed(
            BilingualFontStage stage,
            BilingualLogReasonCode reasonCode,
            Exception? exception = null,
            bool wasAttempted = true
        ) => new(Array.Empty<TMP_FontAsset>(), wasAttempted, stage, reasonCode, exception);

        internal FontLoadAttempt MergeFailure(FontLoadAttempt later) =>
            later.WasAttempted ? later : this;
    }

    private sealed class FontBinding
    {
        internal FontBinding(TMP_Text text, TMP_FontAsset original, TMP_FontAsset clone)
        {
            Text = text;
            Original = original;
            Clone = clone;
        }

        internal TMP_Text Text { get; }

        internal TMP_FontAsset Original { get; }

        internal TMP_FontAsset Clone { get; }
    }
}
