#nullable enable

using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.VoiceSubtitles.Settings;
using BazaarPlusPlus.Infrastructure.Fonts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceLineDisplay
{
    private static GameObject? _labelRoot;
    private static TextMeshProUGUI? _englishLabel;
    private static Text? _chineseUiLabel;
    private static TMP_FontAsset? _subtitleFont;
    private static VoiceLineOverlayLifetime? _lifetime;
    private static bool _mountedFromVersionLabel;
    private static int _nextDisplayId;
    private static float _secondLineOffset = -28f;
    private static RectTransform? _sourceRect;
    private static TextMeshProUGUI? _sourceLabel;
    private static readonly object QueueSync = new();
    private static readonly Queue<VoiceSubtitleCue> QueuedShows = new();

    public static bool IsMountedFromVersionLabel =>
        _mountedFromVersionLabel && CurrentLabelObject != null;

    public static void MountFromVersionLabel(TextMeshProUGUI versionLabel)
    {
        if (versionLabel == null)
            return;

        if (IsMountedFromVersionLabel)
            return;

        var stage = "destroy-existing";

        try
        {
            DestroyCurrentLabel();

            stage = "create-game-object";
            var labelObject = new GameObject("BazaarLine_SubtitleLabel", typeof(RectTransform));
            labelObject.SetActive(false);

            stage = "set-parent";
            labelObject.transform.SetParent(
                versionLabel.transform.parent,
                worldPositionStays: false
            );

            stage = "add-canvas-renderer";
            labelObject.AddComponent<CanvasRenderer>();

            stage = "add-layout-element";
            var layoutElement = labelObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            stage = "store-source-label";
            _sourceRect = versionLabel.rectTransform;
            _sourceLabel = versionLabel;
            FontDiagnostics.LogOnce(versionLabel, "mount");

            // Use the game's own font. The mod swaps the main-menu version label to LXGW (per-label),
            // so if that label happens to be the mount source we must recover its pre-swap (original
            // game) font rather than render in LXGW; in-run labels are never swapped, so their current
            // font already is the game font. This keeps the subtitle in the game's font consistently
            // across scenes (position/parent still come from the scanned version label).
            stage = "resolve-font";
            _subtitleFont = ResolveSubtitleFont(versionLabel);

            // Always use the split renderer: English in the game's own font, Chinese in a SEPARATE
            // system CJK font. Never route Chinese through the game's font asset — the subtitle shares
            // that asset with the game's own UI (shop text, etc.), so rendering new CJK glyphs into its
            // shared dynamic atlas corrupts the game's own text into tofu. English is Latin and already
            // present in that atlas, so it adds nothing. (The combined single-label path is intentionally
            // not used for this reason.)
            stage = "add-subtitle-renderer";
            _labelRoot = labelObject;
            _englishLabel = CreateEnglishLabel(labelObject.transform);
            _chineseUiLabel = CreateChineseUiLabel(labelObject.transform);

            if (_englishLabel == null && _chineseUiLabel == null)
            {
                UnityEngine.Object.Destroy(labelObject);
                return;
            }

            VoiceSubtitlesLog.Info("Subtitle renderers selected " + RendererDescription());

            stage = "add-lifetime";
            _lifetime = labelObject.AddComponent<VoiceLineOverlayLifetime>();
            _lifetime.Initialize(labelObject);

            _mountedFromVersionLabel = true;

            stage = "apply-settings";
            TryApplySettingsToLabel("mount");
            VoiceSubtitlesLog.Info(
                $"Subtitle label mounted from version label source='{BuildPath(versionLabel.transform)}'"
            );
        }
        catch (Exception ex)
        {
            DestroyCurrentLabel();
            VoiceSubtitlesLog.Warn(
                "Failed to mount subtitle label "
                    + $"stage={stage} "
                    + $"source={DescribeVersionLabel(versionLabel)} "
                    + $"error={ex.GetType().Name}: {ex.Message}\n{ex}"
            );
        }
    }

    public static void Show(VoiceSubtitleCue cue)
    {
        if (!VoiceSubtitlesGate.IsEnabled())
            return;

        var displayId = ++_nextDisplayId;
        var line = cue.Line;
        var text = BuildDisplayText(line);
        if (text.IsEmpty)
        {
            VoiceSubtitlesLog.Info(
                "Subtitle show skipped because resolved text is empty "
                    + $"display={displayId} "
                    + $"attempt={cue.AttemptId} "
                    + $"stem={line.Stem}"
            );
            return;
        }

        var fallbackDuration =
            cue.EventDurationSeconds > 0f
                ? cue.EventDurationSeconds + 0.15f
                : Math.Max(1f, line.DurationSeconds + 0.15f);

        VoiceSubtitlesLog.Info(
            "Subtitle show request "
                + $"display={displayId} "
                + $"attempt={cue.AttemptId} "
                + $"stem={line.Stem} "
                + $"eventDuration={cue.EventDurationSeconds:F3}s "
                + $"lineDuration={line.DurationSeconds:F3}s "
                + $"lifetime={fallbackDuration:F3}s "
                + $"english={VoiceSubtitlesLog.Field(text.English)} "
                + $"chinese={VoiceSubtitlesLog.Field(text.Chinese)}"
        );

        ShowRaw(text, cue, fallbackDuration, displayId, line.Stem);
    }

    public static void QueueShow(VoiceSubtitleCue cue)
    {
        if (!VoiceSubtitlesGate.IsEnabled())
            return;

        lock (QueueSync)
        {
            QueuedShows.Enqueue(cue);
        }
    }

    public static void ProcessQueuedShows()
    {
        while (true)
        {
            VoiceSubtitleCue queued;
            lock (QueueSync)
            {
                if (QueuedShows.Count == 0)
                    return;

                queued = QueuedShows.Dequeue();
            }

            Show(queued);
        }
    }

    public static void Reset()
    {
        DestroyCurrentLabel();
        _nextDisplayId = 0;
        lock (QueueSync)
        {
            QueuedShows.Clear();
        }
    }

    private static void DestroyCurrentLabel()
    {
        if (_labelRoot != null)
            UnityEngine.Object.Destroy(_labelRoot);

        _labelRoot = null;
        _englishLabel = null;
        _chineseUiLabel = null;
        _subtitleFont = null;
        _lifetime = null;
        _mountedFromVersionLabel = false;
        _sourceRect = null;
        _sourceLabel = null;
    }

    // The game's own font for the subtitle: the version label's pre-swap original font when the mod
    // swapped it to LXGW, otherwise the label's current font (which is already the game font on
    // never-swapped in-run labels). Never renders in LXGW.
    private static TMP_FontAsset? ResolveSubtitleFont(TextMeshProUGUI versionLabel) =>
        BppTmpFont.TryGetOriginalFont(versionLabel) ?? versionLabel.font;

    private static void ShowRaw(
        DisplayText text,
        VoiceSubtitleCue cue,
        float durationSeconds,
        int displayId,
        string stem
    )
    {
        var labelObject = CurrentLabelObject;
        if (labelObject == null || _lifetime == null)
        {
            VoiceSubtitlesLog.Warn(
                "Subtitle show skipped because label is unavailable "
                    + $"display={displayId} "
                    + $"attempt={cue.AttemptId} "
                    + $"stem={stem}"
            );
            return;
        }

        var activeBefore = labelObject.activeSelf;
        TryApplySettingsToLabel("show");
        ApplyText(text);
        labelObject.SetActive(true);
        VoiceSubtitlesLog.Info(
            "Subtitle label updated "
                + $"display={displayId} "
                + $"attempt={cue.AttemptId} "
                + $"stem={stem} "
                + $"mountedFromVersionLabel={IsMountedFromVersionLabel} "
                + $"activeBefore={activeBefore} "
                + $"renderer={RendererDescription()} "
                + $"duration={durationSeconds:F3}s"
        );
        _lifetime.ShowUntilVoiceStops(
            cue.IsPlaybackStoppedOrStopping,
            cue.PlaybackStateText,
            durationSeconds,
            displayId,
            cue.AttemptId,
            stem
        );
    }

    private static DisplayText BuildDisplayText(VoiceLine line)
    {
        var settings = VoiceLineSettings.Current;
        var english =
            settings.LanguageMode == SubtitleLanguageMode.ChineseOnly || ShouldHideEnglish(line)
                ? string.Empty
                : line.English.Trim();
        var chinese =
            settings.LanguageMode == SubtitleLanguageMode.EnglishOnly
                ? string.Empty
                : line.Chinese.Trim();

        if (string.IsNullOrEmpty(english) && string.IsNullOrEmpty(chinese))
            return DisplayText.Empty;

        return new DisplayText(english, chinese);
    }

    private static bool ShouldHideEnglish(VoiceLine line)
    {
        return line.Stem.IndexOf("Dooley", StringComparison.OrdinalIgnoreCase) >= 0
            && string.Equals(
                line.English.Trim(),
                line.Stem.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool TryApplySettingsToLabel(string reason)
    {
        if (CurrentLabelObject == null || _sourceRect == null || _sourceLabel == null)
            return false;

        try
        {
            var settings = VoiceLineSettings.Current;
            ConfigureRect(_sourceRect, CurrentRectTransform!, settings.Position);
            ConfigureSplitText(_sourceLabel, _englishLabel, _chineseUiLabel, settings);
            return true;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                "Failed to apply subtitle label settings "
                    + $"reason={reason} "
                    + $"error={ex.GetType().Name}: {ex.Message}\n{ex}"
            );
            return false;
        }
    }

    private static void ConfigureRect(
        RectTransform source,
        RectTransform target,
        SubtitlePosition position
    )
    {
        var anchorX = position switch
        {
            SubtitlePosition.TopRight => 1f,
            SubtitlePosition.TopCenter => 0.5f,
            _ => 0f,
        };
        target.anchorMin = new Vector2(anchorX, 1f);
        target.anchorMax = new Vector2(anchorX, 1f);
        target.pivot = new Vector2(anchorX, 1f);
        target.localScale = source.localScale;
        target.localRotation = Quaternion.identity;

        var xMargin = source.anchoredPosition.x;
        var yMargin = Math.Abs(source.anchoredPosition.y);
        var xPosition = position switch
        {
            SubtitlePosition.TopRight => -Math.Abs(xMargin),
            SubtitlePosition.TopCenter => 0f,
            _ => xMargin,
        };

        target.anchoredPosition = new Vector2(xPosition, -yMargin);
        target.sizeDelta = new Vector2(
            Math.Max(source.sizeDelta.x, 760f),
            Math.Max(source.sizeDelta.y * 2.8f, 64f)
        );
    }

    private static void ConfigureSplitText(
        TextMeshProUGUI source,
        TextMeshProUGUI? english,
        Text? chineseUi,
        VoiceLineSettings settings
    )
    {
        var englishFontSize = Math.Max(source.fontSize * settings.EnglishFontScale, 16f);
        var chineseFontSize = Math.Max(source.fontSize * settings.ChineseFontScale, 16f);
        var lineHeight = Math.Max(englishFontSize, chineseFontSize) * 1.25f;

        if (english != null)
        {
            var englishFont = _subtitleFont ?? source.font;
            if (englishFont != null)
                english.font = englishFont;

            english.fontStyle = source.fontStyle;
            english.characterSpacing = source.characterSpacing;
            english.wordSpacing = source.wordSpacing;
            english.paragraphSpacing = source.paragraphSpacing;
            english.enableAutoSizing = false;
            english.text = string.Empty;
            english.alignment = settings.Position switch
            {
                SubtitlePosition.TopRight => TextAlignmentOptions.TopRight,
                SubtitlePosition.TopCenter => TextAlignmentOptions.Top,
                _ => TextAlignmentOptions.TopLeft,
            };
            // Wrap + Overflow (was NoWrap + Ellipsis): a scaled English line wraps within the box width
            // instead of truncating to "…". ApplyText positions the Chinese line below the English
            // block's measured height so wrapped English never overlaps it.
            english.textWrappingMode = TextWrappingModes.Normal;
            english.overflowMode = TextOverflowModes.Overflow;
            english.richText = false;
            english.raycastTarget = false;
            english.fontSize = englishFontSize;
            english.lineSpacing = 0f;
            english.color = new Color(1f, 0.96f, 0.84f, 0.96f);
        }

        if (chineseUi != null)
        {
            chineseUi.font = FontDiagnostics.ResolveSystemChineseUiFont() ?? chineseUi.font;
            chineseUi.fontStyle = FontStyle.Bold;
            chineseUi.alignment = settings.Position switch
            {
                SubtitlePosition.TopRight => TextAnchor.UpperRight,
                SubtitlePosition.TopCenter => TextAnchor.UpperCenter,
                _ => TextAnchor.UpperLeft,
            };
            chineseUi.horizontalOverflow = HorizontalWrapMode.Overflow;
            chineseUi.verticalOverflow = VerticalWrapMode.Truncate;
            chineseUi.supportRichText = false;
            chineseUi.raycastTarget = false;
            chineseUi.fontSize = Math.Max((int)Math.Round(chineseFontSize), 16);
            chineseUi.lineSpacing = 1f;
            chineseUi.color = new Color(1f, 0.96f, 0.84f, 0.96f);
            chineseUi.text = string.Empty;
        }

        _secondLineOffset = -lineHeight;
        ConfigureLineRect(english?.rectTransform, 0f, lineHeight);
        ConfigureLineRect(chineseUi?.rectTransform, _secondLineOffset, lineHeight);
    }

    private static TextMeshProUGUI? CreateEnglishLabel(Transform parent)
    {
        var labelObject = CreateChildLabelObject(parent, "BazaarLine_EnglishSubtitle");
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        return label;
    }

    private static Text? CreateChineseUiLabel(Transform parent)
    {
        var uiFont = FontDiagnostics.ResolveSystemChineseUiFont();
        if (uiFont == null)
            return null;

        var labelObject = CreateChildLabelObject(parent, "BazaarLine_ChineseSubtitle");
        var label = labelObject.AddComponent<Text>();
        label.font = uiFont;
        label.fontStyle = FontStyle.Bold;
        label.supportRichText = false;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject CreateChildLabelObject(Transform parent, string name)
    {
        var labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.transform.SetParent(parent, worldPositionStays: false);
        labelObject.AddComponent<CanvasRenderer>();
        return labelObject;
    }

    private static void ConfigureLineRect(RectTransform? rect, float yOffset, float lineHeight)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(0f, Math.Max(48f, lineHeight));
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static GameObject? CurrentLabelObject => _labelRoot;

    private static RectTransform? CurrentRectTransform =>
        _labelRoot != null ? _labelRoot.GetComponent<RectTransform>() : null;

    private static void ApplyText(DisplayText text)
    {
        var englishBlockHeight = Math.Abs(_secondLineOffset);
        if (_englishLabel != null)
        {
            _englishLabel.text = text.English;
            var hasEnglish = !string.IsNullOrEmpty(text.English);
            _englishLabel.gameObject.SetActive(hasEnglish);
            if (hasEnglish)
            {
                // Measure the wrapped English height so a multi-line English line pushes the Chinese
                // line down instead of overlapping it (the split layout no longer assumes one line each).
                _englishLabel.ForceMeshUpdate();
                englishBlockHeight = Math.Max(englishBlockHeight, _englishLabel.preferredHeight);
            }
        }

        if (_chineseUiLabel != null)
        {
            _chineseUiLabel.text = text.Chinese;
            _chineseUiLabel.gameObject.SetActive(!string.IsNullOrEmpty(text.Chinese));
            ConfigureLineRect(
                _chineseUiLabel.rectTransform,
                string.IsNullOrEmpty(text.English) ? 0f : -englishBlockHeight,
                Math.Abs(_secondLineOffset)
            );
        }
    }

    private static string RendererDescription()
    {
        return "english=TextMeshProUGUI "
            + $"font={FontDiagnostics.DescribeFont(_englishLabel?.font)} "
            + $"chinese={ChineseRendererDescription()}";
    }

    private static string ChineseRendererDescription()
    {
        if (_chineseUiLabel != null)
        {
            return "UnityUI.Text "
                + $"font='{_chineseUiLabel.font?.name ?? "<null>"}' "
                + $"style={_chineseUiLabel.fontStyle}";
        }

        return "<none>";
    }

    private static string DescribeVersionLabel(TextMeshProUGUI? label)
    {
        if (label == null)
            return "<null>";

        return $"'{BuildPath(label.transform)}' text={VoiceSubtitlesLog.Field(label.text)}";
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

    private readonly struct DisplayText
    {
        public static readonly DisplayText Empty = new(string.Empty, string.Empty);

        public DisplayText(string english, string chinese)
        {
            English = english;
            Chinese = chinese;
        }

        public string English { get; }

        public string Chinese { get; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(English) && string.IsNullOrWhiteSpace(Chinese);
    }
}
