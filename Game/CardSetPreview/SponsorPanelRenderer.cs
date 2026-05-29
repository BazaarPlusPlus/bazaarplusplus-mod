#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using TheBazaar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CardSetPreview;

// Owns the sponsor panel chrome (background, outline, rich-text label) that
// ItemBoardOverlay overlays on top of the cloned MonsterBoardTooltip. Build,
// styling, color, rich-text, and placement logic moved verbatim from
// ItemBoardOverlay; the overlay now delegates to this renderer.
internal sealed class SponsorPanelRenderer
{
    private const string SponsorPanelObjectName = "BppItemBoardSponsorPanel";
    private const string SponsorTextObjectName = "BppItemBoardSponsorText";
    private const float SponsorPanelFontSize = 17f;
    private static readonly Vector2 DefaultSponsorPanelSize = new(440f, 34f);
    private static readonly Vector2 DefaultSponsorPanelOffset = new(-325f, -315f);
    private const float DefaultSponsorPanelScale = 2f;

    private static readonly object SponsorDebugSyncRoot = new();
    private static Vector2 _sponsorPanelDebugOffset = DefaultSponsorPanelOffset;
    private static float _sponsorPanelDebugScale = DefaultSponsorPanelScale;
    private static TMP_FontAsset? _resolvedSponsorFont;
    private static Material? _resolvedSponsorFontMaterial;
    private static bool _resolvedSponsorFontLogged;
    private static string? _resolvedSponsorFontSourcePath;

    private RectTransform? _sponsorPanelRect;
    private TextMeshProUGUI? _sponsorText;
    private Image? _sponsorPanelBackground;
    private Outline? _sponsorPanelOutline;

    public void CreateSponsorPanel(RectTransform? overlayRootRect)
    {
        if (overlayRootRect == null || _sponsorPanelRect != null)
            return;

        var sponsorPanelObject = new GameObject(
            SponsorPanelObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        _sponsorPanelRect = sponsorPanelObject.GetComponent<RectTransform>();
        _sponsorPanelRect.SetParent(overlayRootRect, false);
        _sponsorPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        _sponsorPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        _sponsorPanelRect.pivot = new Vector2(0f, 0.5f);
        _sponsorPanelRect.sizeDelta = DefaultSponsorPanelSize;

        _sponsorPanelBackground = sponsorPanelObject.GetComponent<Image>();
        _sponsorPanelBackground.color = new Color(0.09f, 0.09f, 0.12f, 0.82f);
        _sponsorPanelBackground.raycastTarget = false;

        _sponsorPanelOutline = sponsorPanelObject.GetComponent<Outline>();
        _sponsorPanelOutline.effectColor = new Color(0.98f, 0.79f, 0.42f, 0.66f);
        _sponsorPanelOutline.effectDistance = new Vector2(1.25f, -1.25f);
        _sponsorPanelOutline.useGraphicAlpha = true;

        var sponsorTextObject = new GameObject(
            SponsorTextObjectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        var sponsorTextRect = sponsorTextObject.GetComponent<RectTransform>();
        sponsorTextRect.SetParent(_sponsorPanelRect, false);
        sponsorTextRect.anchorMin = Vector2.zero;
        sponsorTextRect.anchorMax = Vector2.one;
        sponsorTextRect.offsetMin = new Vector2(10f, 4f);
        sponsorTextRect.offsetMax = new Vector2(-10f, -4f);

        _sponsorText = sponsorTextObject.GetComponent<TextMeshProUGUI>();
        ApplySponsorTextStyle(_sponsorText);
        _sponsorText.fontSize = SponsorPanelFontSize;
        _sponsorText.enableAutoSizing = true;
        _sponsorText.fontSizeMin = 11f;
        _sponsorText.fontSizeMax = SponsorPanelFontSize;
        _sponsorText.alignment = TextAlignmentOptions.MidlineLeft;
        _sponsorText.color = new Color(0.98f, 0.93f, 0.84f, 1f);
        _sponsorText.textWrappingMode = TextWrappingModes.NoWrap;
        _sponsorText.overflowMode = TextOverflowModes.Ellipsis;
        _sponsorText.raycastTarget = false;

        ApplySponsorPanelMetrics();
        sponsorPanelObject.SetActive(false);
    }

    public void UpdateSponsorVisual(
        string? sponsorText,
        string? sponsorName,
        int sponsorTier,
        int candidateIndex,
        int candidateCount,
        bool isAlertState,
        float scale,
        Vector2? viewAnchoredPosition
    )
    {
        if (_sponsorPanelRect == null || _sponsorText == null)
            return;

        var hasSponsorText = !string.IsNullOrWhiteSpace(sponsorText);
        _sponsorPanelRect.gameObject.SetActive(hasSponsorText);
        if (!hasSponsorText)
            return;

        ApplySponsorTextStyle(_sponsorText, sponsorText);
        ApplySponsorTextColor(
            sponsorText ?? string.Empty,
            sponsorName,
            sponsorTier,
            candidateIndex,
            candidateCount,
            isAlertState
        );
        UpdateSponsorPlacement(scale, viewAnchoredPosition);
    }

    public void UpdateSponsorPlacement(float scale, Vector2? viewAnchoredPosition)
    {
        if (_sponsorPanelRect == null || viewAnchoredPosition == null)
            return;

        var clamped = Mathf.Clamp(scale, 0.2f, 2f);
        _sponsorPanelRect.anchoredPosition =
            viewAnchoredPosition.Value + (_sponsorPanelDebugOffset * clamped);
        _sponsorPanelRect.localScale = Vector3.one * (clamped * _sponsorPanelDebugScale);
    }

    public void Reset()
    {
        _sponsorPanelRect = null;
        _sponsorText = null;
        _sponsorPanelBackground = null;
        _sponsorPanelOutline = null;
    }

    private void ApplySponsorTextStyle(TextMeshProUGUI text, string? sampleText = null)
    {
        if (text == null)
            return;

        ResolveSponsorTextStyle(sampleText ?? text.text);
        text.font = _resolvedSponsorFont ?? TMP_Settings.defaultFontAsset;
        if (_resolvedSponsorFontMaterial != null)
            text.fontSharedMaterial = _resolvedSponsorFontMaterial;

        BppTmpFont.TryApply(text, sampleText ?? text.text);
        text.richText = false;
    }

    private void ApplySponsorPanelMetrics()
    {
        if (_sponsorPanelRect != null)
            _sponsorPanelRect.sizeDelta = DefaultSponsorPanelSize;

        if (_sponsorText != null)
        {
            _sponsorText.fontSize = SponsorPanelFontSize;
            _sponsorText.fontSizeMax = SponsorPanelFontSize;
        }
    }

    private void ApplySponsorTextColor(
        string sponsorText,
        string? sponsorName,
        int sponsorTier,
        int candidateIndex,
        int candidateCount,
        bool isAlertState
    )
    {
        if (_sponsorText == null)
            return;

        ApplySponsorPanelChrome(isAlertState);

        if (isAlertState)
        {
            _sponsorText.color = new Color(1f, 0.87f, 0.82f, 1f);
            _sponsorText.richText = false;
            _sponsorText.text = sponsorText;
            return;
        }

        var highlightColor = ResolveSponsorTextColor(sponsorTier, candidateIndex, candidateCount);
        var baseColor = new Color(0.98f, 0.93f, 0.84f, 1f);
        _sponsorText.color = baseColor;
        _sponsorText.richText = true;
        _sponsorText.text = BuildSponsorRichText(
            sponsorText,
            sponsorName,
            highlightColor,
            candidateIndex,
            candidateCount
        );
    }

    private void ApplySponsorPanelChrome(bool isAlertState)
    {
        if (_sponsorPanelBackground != null)
        {
            _sponsorPanelBackground.color = isAlertState
                ? new Color(0.28f, 0.08f, 0.08f, 0.92f)
                : new Color(0.09f, 0.09f, 0.12f, 0.82f);
        }

        if (_sponsorPanelOutline == null)
            return;

        _sponsorPanelOutline.effectColor = isAlertState
            ? new Color(1f, 0.36f, 0.30f, 0.86f)
            : new Color(0.98f, 0.79f, 0.42f, 0.66f);
        _sponsorPanelOutline.effectDistance = new Vector2(1.25f, -1.25f);
        _sponsorPanelOutline.useGraphicAlpha = true;
    }

    private static Color ResolveSponsorTextColor(
        int sponsorTier,
        int candidateIndex,
        int candidateCount
    )
    {
        return sponsorTier switch
        {
            4 => new Color(1f, 0.90f, 0.48f, 1f),
            3 => new Color(0.86f, 0.93f, 1f, 1f),
            2 => new Color(0.93f, 0.72f, 0.50f, 1f),
            _ => new Color(0.96f, 0.95f, 0.93f, 1f),
        };
    }

    private static string BuildSponsorRichText(
        string sponsorText,
        string? sponsorName,
        Color sponsorColor,
        int candidateIndex,
        int candidateCount
    )
    {
        if (string.IsNullOrWhiteSpace(sponsorText))
            return sponsorText;

        var result = sponsorText;
        if (!string.IsNullOrWhiteSpace(sponsorName))
            result = ItemBoardTextHelpers.ReplaceLast(
                result,
                sponsorName,
                ItemBoardTextHelpers.WrapWithColor(sponsorName, sponsorColor)
            );

        if (candidateCount > 1)
        {
            var candidateSegment = ResolveCandidateSegment(candidateIndex, candidateCount);
            if (!string.IsNullOrWhiteSpace(candidateSegment))
            {
                result = ItemBoardTextHelpers.ReplaceFirst(
                    result,
                    candidateSegment,
                    ItemBoardTextHelpers.WrapWithColor(
                        candidateSegment,
                        ResolveCandidateTextColor(candidateIndex, candidateCount)
                    )
                );
            }
        }

        return result;
    }

    private static Color ResolveCandidateTextColor(int candidateIndex, int candidateCount)
    {
        var normalized =
            candidateCount <= 1 ? 0f : Mathf.Clamp01(candidateIndex / (float)(candidateCount - 1));
        return Color.Lerp(
            new Color(0.78f, 0.90f, 1f, 1f),
            new Color(0.95f, 0.98f, 1f, 1f),
            normalized
        );
    }

    private static string ResolveCandidateSegment(int candidateIndex, int candidateCount)
    {
        if (candidateCount <= 1)
            return string.Empty;

        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        var label = LanguageCodeMatcher.IsChinese(languageCode) ? "候选" : "Candidate";
        return $"{label} {candidateIndex + 1}/{candidateCount}";
    }

    private void ResolveSponsorTextStyle(string? sampleText)
    {
        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        var template = ResolveSponsorTemplateText(languageCode, sampleText);

        if (template == null)
            return;

        var templatePath = ItemBoardTextHelpers.BuildTransformPath(template.transform);
        if (
            ReferenceEquals(_resolvedSponsorFont, template.font)
            && ReferenceEquals(_resolvedSponsorFontMaterial, template.fontSharedMaterial)
            && string.Equals(_resolvedSponsorFontSourcePath, templatePath, StringComparison.Ordinal)
        )
        {
            return;
        }

        _resolvedSponsorFont = template.font;
        _resolvedSponsorFontMaterial = template.fontSharedMaterial;
        _resolvedSponsorFontSourcePath = templatePath;

        if (!_resolvedSponsorFontLogged)
            _resolvedSponsorFontLogged = true;

        BppLog.Info(
            "ItemBoardOverlay",
            $"Resolved sponsor TMP font '{_resolvedSponsorFont?.name ?? "<null>"}' material='{_resolvedSponsorFontMaterial?.name ?? "<null>"}' source='{templatePath}' text='{template.text ?? string.Empty}'"
        );
    }

    private static TextMeshProUGUI? ResolveSponsorTemplateText(
        string languageCode,
        string? sampleText
    )
    {
        TextMeshProUGUI? settingsDockCandidate = null;
        TextMeshProUGUI? chineseCandidate = null;
        TextMeshProUGUI? fallback = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (candidate == null || candidate.font == null)
                continue;

            fallback ??= candidate;
            var path = ItemBoardTextHelpers.BuildTransformPath(candidate.transform);
            if (
                settingsDockCandidate == null
                && path.IndexOf("BPP_SettingsDock", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                settingsDockCandidate = candidate;
            }

            if (
                chineseCandidate == null
                && ShouldPreferForLanguage(candidate, languageCode, sampleText)
            )
            {
                chineseCandidate = candidate;
            }
        }

        return chineseCandidate ?? settingsDockCandidate ?? fallback;
    }

    private static bool ShouldPreferForLanguage(
        TextMeshProUGUI candidate,
        string languageCode,
        string? sampleText
    )
    {
        if (!LanguageCodeMatcher.IsChinese(languageCode))
            return false;

        if (ItemBoardTextHelpers.ContainsCjk(candidate.text))
            return true;

        if (!ItemBoardTextHelpers.ContainsCjk(sampleText) || candidate.font == null)
            return false;

        return ItemBoardTextHelpers.FontLooksCjkCapable(candidate.font);
    }
}
