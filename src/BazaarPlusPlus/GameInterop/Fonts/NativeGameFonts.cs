#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace BazaarPlusPlus.GameInterop.Fonts;

/// <summary>
/// Owns the game's zh-CN font references for the full plugin session. Native TMP labels keep
/// their donor primary/material and receive only the game's fallback chain; BPP-created UI uses
/// the Dynamic asset's packaged UnityEngine.Font.
/// </summary>
internal static class NativeGameFonts
{
    private const string ChineseLocale = "zh-CN";
    private const string PanelTextSettingsName = "BPP Native Game Font Text Settings";
    private const int HealthKey = 0;

    private static readonly FieldInfo? OsFallbackFontAssetsField = typeof(TextSettings).GetField(
        "m_FallbackOSFontAssets",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly FieldInfo? EmojiSupportField = typeof(TextSettings).GetField(
        "m_EnableEmojiSupport",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly FieldInfo? EmojiFallbackTextAssetsField = typeof(TextSettings).GetField(
        "m_EmojiFallbackTextAssets",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly List<AsyncOperationHandle<TMP_FontAsset>> Handles = new();
    private static readonly Dictionary<object, TMP_FontAsset> LoadedByRuntimeKey = new();
    private static readonly List<FontBinding> Bindings = new();
    private static readonly List<PanelTextSettings> PanelTextSettingsInstances = new();
    private static readonly OperationalHealthTracker<int, NativeGameFontReasonCode> Health = new();

    private static TMP_FontAsset[]? _serifFallbacks;
    private static TMP_FontAsset[]? _sansFallbacks;
    private static Font? _serifSourceFont;
    private static Font? _sansSourceFont;
    private static bool _readyReported;

    internal static bool IsConfigurationReady => NotoFontFallbackRuntime.HasConfiguration;

    internal static bool TryInstallFallback(TMP_Text? text, string? sampleText)
    {
        if (
            text?.font == null
            || string.IsNullOrWhiteSpace(sampleText)
            || !UnicodeFontCoverage.ContainsCjk(sampleText)
        )
            return false;

        var preferSerif = text.font.name.IndexOf("Serif", StringComparison.OrdinalIgnoreCase) >= 0;
        var attempt = ResolveFallbacks(preferSerif);
        if (attempt.Fonts.Length == 0 && preferSerif)
        {
            var sansAttempt = ResolveFallbacks(preferSerif: false);
            attempt =
                sansAttempt.Fonts.Length > 0 ? sansAttempt : attempt.MergeFailure(sansAttempt);
        }
        Observe(attempt);
        if (attempt.Fonts.Length == 0)
            return false;

        var binding = FindOrCreateBinding(text);
        if (binding?.Clone?.fallbackFontAssetTable == null)
            return false;

        var installed = false;
        foreach (var fallback in attempt.Fonts)
        {
            if (fallback == null || binding.Clone.fallbackFontAssetTable.Contains(fallback))
                continue;

            binding.Clone.fallbackFontAssetTable.Add(fallback);
            installed = true;
        }

        if (installed)
            text.ForceMeshUpdate(ignoreActiveState: true);
        return true;
    }

    internal static bool TryGetSansSourceFont(out Font? sourceFont) =>
        TryGetSourceFont(preferSerif: false, ref _sansSourceFont, out sourceFont);

    internal static bool TryGetSerifSourceFont(out Font? sourceFont) =>
        TryGetSourceFont(preferSerif: true, ref _serifSourceFont, out sourceFont);

    private static bool TryGetSourceFont(
        bool preferSerif,
        ref Font? cachedSourceFont,
        out Font? sourceFont
    )
    {
        if (cachedSourceFont != null)
        {
            sourceFont = cachedSourceFont;
            return true;
        }

        var attempt = ResolveFallbacks(preferSerif);
        if (attempt.Fonts.Length == 0)
        {
            Observe(attempt);
            if (attempt.WasAttempted)
                ReportFailure(
                    NativeGameFontStage.ResolveSourceFont,
                    NativeGameFontReasonCode.SourceFontUnavailable,
                    null
                );
            sourceFont = null;
            return false;
        }
        var sourceIndex = NativeGameFontSelection.FindLastIndexWithSource(
            attempt.Fonts,
            candidate => candidate?.sourceFontFile != null
        );
        if (sourceIndex >= 0)
        {
            var candidate = attempt.Fonts[sourceIndex].sourceFontFile;
            if (!candidate.dynamic)
            {
                ReportFailure(
                    NativeGameFontStage.ResolveSourceFont,
                    NativeGameFontReasonCode.SourceFontNotDynamic,
                    null
                );
                sourceFont = null;
                return false;
            }

            cachedSourceFont = candidate;
            sourceFont = candidate;
            ReportSuccess(attempt.Fonts, candidate);
            return true;
        }

        ReportFailure(
            NativeGameFontStage.ResolveSourceFont,
            NativeGameFontReasonCode.SourceFontUnavailable,
            null
        );
        sourceFont = null;
        return false;
    }

    internal static bool TryFindMissingCodePoint(Font? font, string? text, out int missingCodePoint)
    {
        if (font == null)
        {
            missingCodePoint = 0;
            return !string.IsNullOrEmpty(text);
        }

        return UnicodeFontCoverage.TryFindMissingCodePoint(
            text,
            font.HasCharacter,
            out missingCodePoint
        );
    }

    internal static bool IsTextSupported(Font? font, string? text, string surface)
    {
        if (!TryFindMissingCodePoint(font, text, out var missingCodePoint))
            return true;

        BppLog.WarnEvent(
            NativeGameFontsLogEvents.TextRejected,
            NativeGameFontsLogEvents.TextRejectedSurface.Bind(surface),
            NativeGameFontsLogEvents.TextRejectedCodePoint.Bind($"U+{missingCodePoint:X}")
        );
        return false;
    }

    internal static bool TryConfigurePanel(PanelSettings? panelSettings, out Font? sourceFont)
    {
        sourceFont = null;
        if (panelSettings == null || panelSettings.textSettings != null)
            return false;
        if (!TryGetSansSourceFont(out sourceFont) || sourceFont == null)
            return false;

        PanelTextSettings? textSettings = null;
        try
        {
            RequireField(OsFallbackFontAssetsField, "m_FallbackOSFontAssets");
            RequireField(EmojiSupportField, "m_EnableEmojiSupport");
            RequireField(EmojiFallbackTextAssetsField, "m_EmojiFallbackTextAssets");

            textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            textSettings.name = PanelTextSettingsName;
            textSettings.defaultFontAsset = null;
            textSettings.fallbackFontAssets = new List<TextCoreFontAsset>();
            textSettings.missingCharacterUnicode = 0x25A1;
            EmojiSupportField!.SetValue(textSettings, false);
            SetEmptyCollection(textSettings, EmojiFallbackTextAssetsField!);
            SetEmptyCollection(textSettings, OsFallbackFontAssetsField!);

            if (!IsIsolated(textSettings))
                throw new InvalidOperationException(
                    "Panel text settings retain a fallback font path."
                );

            panelSettings.textSettings = textSettings;
            PanelTextSettingsInstances.Add(textSettings);
            return true;
        }
        catch (Exception ex)
        {
            if (textSettings != null)
                Object.DestroyImmediate(textSettings);
            sourceFont = null;
            ReportFailure(
                NativeGameFontStage.ConfigurePanelTextSettings,
                NativeGameFontReasonCode.PanelTextSettingsUnavailable,
                ex
            );
            return false;
        }
    }

    internal static bool IsIsolated(PanelTextSettings? textSettings)
    {
        if (
            textSettings == null
            || OsFallbackFontAssetsField == null
            || EmojiSupportField == null
            || EmojiFallbackTextAssetsField == null
        )
            return false;

        var osFallbacks = OsFallbackFontAssetsField.GetValue(textSettings) as ICollection;
        var emojiFallbacks = EmojiFallbackTextAssetsField.GetValue(textSettings) as ICollection;
        var emojiSupport = EmojiSupportField.GetValue(textSettings) as bool?;
        return textSettings.defaultFontAsset == null
            && (textSettings.fallbackFontAssets?.Count ?? 0) == 0
            && emojiSupport == false
            && emojiFallbacks?.Count == 0
            && osFallbacks?.Count == 0;
    }

    private static void RequireField(FieldInfo? field, string name)
    {
        if (field == null)
            throw new MissingFieldException(typeof(TextSettings).FullName, name);
    }

    private static void SetEmptyCollection(TextSettings target, FieldInfo field)
    {
        var empty = Activator.CreateInstance(field.FieldType);
        if (empty is not ICollection collection || collection.Count != 0)
            throw new InvalidOperationException($"{field.Name} is not an empty collection.");

        field.SetValue(target, empty);
        if (field.GetValue(target) is not ICollection installed || installed.Count != 0)
            throw new InvalidOperationException(
                $"{field.Name} did not retain an empty collection."
            );
    }

    internal static void ReleasePanelTextSettings(PanelSettings? panelSettings)
    {
        if (panelSettings?.textSettings is not { } textSettings)
            return;
        if (!string.Equals(textSettings.name, PanelTextSettingsName, StringComparison.Ordinal))
            return;

        panelSettings.textSettings = null;
        PanelTextSettingsInstances.Remove(textSettings);
        Object.Destroy(textSettings);
    }

    internal static void Reset()
    {
        try
        {
            RestoreBindings();
            foreach (var textSettings in PanelTextSettingsInstances)
                if (textSettings != null)
                    Object.DestroyImmediate(textSettings);
            PanelTextSettingsInstances.Clear();
        }
        finally
        {
            foreach (var handle in Handles)
                TryRelease(handle);
            Handles.Clear();
            LoadedByRuntimeKey.Clear();
        }

        _serifFallbacks = null;
        _sansFallbacks = null;
        _serifSourceFont = null;
        _sansSourceFont = null;
        Health.Reset();
        _readyReported = false;
    }

    private static void RestoreBindings()
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
                    NativeGameFontsLogEvents.CleanupFailed,
                    ex,
                    () =>
                        [
                            NativeGameFontsLogEvents.CleanupFailedStage.Bind(
                                NativeGameFontStage.RestoreBinding
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

    private static FontBinding? FindOrCreateBinding(TMP_Text text)
    {
        foreach (var binding in Bindings)
            if (binding.Text == text)
                return binding;

        var primary = text.font;
        if (primary == null)
            return null;

        var clone = Object.Instantiate(primary);
        clone.name = $"{primary.name} (BPP Game Font Fallback)";
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

        if (!IsConfigurationReady)
            return FontLoadAttempt.NotReady();

        var configuration = NotoFontFallbackRuntime._configuration;
        if (
            configuration == null
            || !configuration.TryGetRuleForLocale(ChineseLocale, out var rule)
        )
        {
            return FontLoadAttempt.Failed(
                NativeGameFontStage.ResolveConfiguration,
                NativeGameFontReasonCode.ConfigurationUnavailable
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
                NativeGameFontStage.LoadFonts,
                NativeGameFontReasonCode.FontReferencesUnavailable
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
            var runtimeKey = reference.RuntimeKey;
            if (LoadedByRuntimeKey.TryGetValue(runtimeKey, out var cachedFont))
            {
                loaded.Add(cachedFont);
                continue;
            }

            AsyncOperationHandle<TMP_FontAsset> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<TMP_FontAsset>(runtimeKey);
                var font = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded || font == null)
                {
                    sawLoadFailure = true;
                    continue;
                }

                font.ReadFontAssetDefinition();
                loaded.Add(font);
                LoadedByRuntimeKey.Add(runtimeKey, font);
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

        if (NativeGameFontSelection.HasCompleteChain(references.Length, loaded.Count))
            return FontLoadAttempt.Succeeded(loaded.ToArray());
        return FontLoadAttempt.Failed(
            NativeGameFontStage.LoadFonts,
            sawLoadFailure
                ? NativeGameFontReasonCode.FontLoadFailed
                : NativeGameFontReasonCode.FontReferencesUnavailable,
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
                NativeGameFontsLogEvents.CleanupFailed,
                ex,
                () =>
                    [
                        NativeGameFontsLogEvents.CleanupFailedStage.Bind(
                            NativeGameFontStage.ReleaseHandle
                        ),
                    ]
            );
        }
    }

    private static void Observe(FontLoadAttempt attempt)
    {
        if (!attempt.WasAttempted)
            return;
        if (attempt.Fonts.Length > 0)
            ReportSuccess(attempt.Fonts, null);
        else
            ReportFailure(attempt.Stage, attempt.ReasonCode, attempt.Exception);
    }

    private static void ReportFailure(
        NativeGameFontStage stage,
        NativeGameFontReasonCode reasonCode,
        Exception? exception
    )
    {
        _readyReported = false;
        if (!Health.ObserveFailure(HealthKey, reasonCode))
            return;
        var fields = new[]
        {
            NativeGameFontsLogEvents.DegradedStage.Bind(stage),
            NativeGameFontsLogEvents.DegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(NativeGameFontsLogEvents.Degraded, fields);
        else
            BppLog.WarnEvent(NativeGameFontsLogEvents.Degraded, exception, fields);
    }

    private static void ReportSuccess(TMP_FontAsset[] fonts, Font? sourceFont)
    {
        if (Health.ObserveSuccess(HealthKey, out _))
        {
            _readyReported = true;
            BppLog.RecoverStorm(NativeGameFontsLogEvents.Degraded);
            BppLog.InfoEvent(
                NativeGameFontsLogEvents.Recovered,
                NativeGameFontsLogEvents.RecoveredFontCount.Bind(fonts.Length)
            );
            return;
        }

        if (_readyReported)
            return;
        _readyReported = true;
        BppLog.DebugEvent(
            NativeGameFontsLogEvents.Loaded,
            () =>
                [
                    NativeGameFontsLogEvents.LoadedFontCount.Bind(fonts.Length),
                    NativeGameFontsLogEvents.LoadedFontNames.Bind(
                        string.Join(",", Array.ConvertAll(fonts, font => font.name))
                    ),
                    NativeGameFontsLogEvents.LoadedSourceFont.Bind(sourceFont?.name),
                ]
        );
    }

    private readonly struct FontLoadAttempt
    {
        private FontLoadAttempt(
            TMP_FontAsset[] fonts,
            bool wasAttempted,
            NativeGameFontStage stage,
            NativeGameFontReasonCode reasonCode,
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
        internal NativeGameFontStage Stage { get; }
        internal NativeGameFontReasonCode ReasonCode { get; }
        internal Exception? Exception { get; }

        internal static FontLoadAttempt Cached(TMP_FontAsset[] fonts) =>
            new(fonts, false, default, default, null);

        internal static FontLoadAttempt Succeeded(TMP_FontAsset[] fonts) =>
            new(fonts, true, default, default, null);

        internal static FontLoadAttempt NotReady() =>
            new(
                Array.Empty<TMP_FontAsset>(),
                false,
                NativeGameFontStage.ResolveConfiguration,
                NativeGameFontReasonCode.ConfigurationUnavailable,
                null
            );

        internal static FontLoadAttempt Failed(
            NativeGameFontStage stage,
            NativeGameFontReasonCode reasonCode,
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
