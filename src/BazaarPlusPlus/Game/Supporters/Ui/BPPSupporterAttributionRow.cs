#nullable enable
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.Supporters.Ui;

internal static class BPPSupporterAttributionRow
{
    private const string SponsorIcon = "♥";

    public static VisualElement Create()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.marginTop = UiSpacing.Sm;
        UiStyle.FixedHeight(row.style, Sizes.SupporterAttributionReservedHeight);
        return row;
    }

    public static void Bind(
        VisualElement row,
        IReadOnlyList<BPPSupporterSample> supporters,
        string fallbackText,
        Font uiFont
    )
    {
        row.Clear();

        var languageCode = GetLanguageCode();
        var sponsorText = BPPSupporterAttributionText.FormatSponsorAction(languageCode);
        var samples = supporters
            .Where(sample =>
                sample.HasValue
                && NativeGameFonts.IsTextSupported(uiFont, sample.Name, "supporter_attribution")
            )
            .Take(4)
            .ToList();
        if (samples.Count == 0)
        {
            row.Add(CreateFallbackLabel(fallbackText, uiFont));
            row.Add(CreateSponsorButton(sponsorText, uiFont));
            return;
        }

        var prefix = BPPSupporterAttributionText.FormatSupportedByPrefix(languageCode);
        var suffix = BPPSupporterAttributionText.FormatSupportedBySuffix(languageCode);
        row.Add(CreatePrefixLabel(prefix, uiFont));
        for (var index = 0; index < samples.Count; index++)
        {
            if (index > 0)
                row.Add(CreateSeparatorLabel(uiFont));

            row.Add(CreateSupporterName(samples[index], uiFont));
        }

        if (!string.IsNullOrWhiteSpace(suffix))
            row.Add(CreateSuffixLabel(suffix, uiFont));

        row.Add(CreateSponsorButton(sponsorText, uiFont));
    }

    private static Label CreatePlainLabel(string text, Font uiFont)
    {
        var label = new Label(text);
        ApplyNativeFont(label, uiFont);
        label.style.fontSize = Sizes.FontSmall;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.color = Colors.HistorySubtitleText;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginRight = UiSpacing.Xs;
        label.style.marginBottom = UiSpacing.Xs;
        return label;
    }

    private static Label CreateFallbackLabel(string text, Font uiFont)
    {
        var label = CreatePlainLabel(text, uiFont);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginRight = 0f;
        return label;
    }

    private static Label CreatePrefixLabel(string text, Font uiFont)
    {
        var label = CreatePlainLabel(text, uiFont);
        label.style.marginRight = UiSpacing.Sm;
        return label;
    }

    private static Label CreateSuffixLabel(string text, Font uiFont)
    {
        var label = CreatePlainLabel(text, uiFont);
        label.style.marginLeft = UiSpacing.Sm;
        label.style.marginRight = 0f;
        return label;
    }

    private static Label CreateSeparatorLabel(Font uiFont)
    {
        var label = CreatePlainLabel("·", uiFont);
        label.style.color = Colors.WithAlpha(Colors.HistorySubtitleText, 0.56f);
        label.style.marginLeft = UiSpacing.Xs;
        label.style.marginRight = UiSpacing.Xs;
        return label;
    }

    private static Label CreateSupporterName(BPPSupporterSample sample, Font uiFont)
    {
        var label = new Label(sample.Name);
        ApplyNativeFont(label, uiFont);
        label.tooltip = sample.Name;
        label.style.fontSize = Sizes.SupporterAttributionNameFont;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.maxWidth = Sizes.SupporterAttributionNameMaxWidth;
        label.style.flexShrink = 1f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.marginBottom = UiSpacing.Xs;
        label.style.color = ResolveTierText(sample.Tier);
        return label;
    }

    private static Color ResolveTierText(int tier)
    {
        return tier switch
        {
            >= 4 => Colors.SupporterTier4Text,
            3 => Colors.SupporterTier3Text,
            2 => Colors.SupporterTier2Text,
            _ => Colors.SupporterTier1Text,
        };
    }

    private static Button CreateSponsorButton(string text, Font uiFont)
    {
        var button = new Button(OpenSupportPage) { text = $"{SponsorIcon} {text}" };
        ApplyNativeFont(button, uiFont);
        button.tooltip = BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode());
        button.style.height = Sizes.SupporterAttributionHeight;
        button.style.minWidth = Sizes.SupporterActionMinWidth;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.marginLeft = UiSpacing.Sm;
        button.style.marginBottom = UiSpacing.Xs;
        button.style.fontSize = Sizes.FontSmall;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.backgroundColor = Colors.WithAlpha(Colors.ButtonSelectedBackground, 0.16f);
        button.style.color = Colors.SupporterTier4Text;
        UiStyle.HorizontalPadding(button.style, UiSpacing.Sm);
        UiStyle.Radius(button.style, Radii.Status);
        UiStyle.Border(
            button.style,
            Borders.Thin,
            Colors.WithAlpha(Colors.OutcomeGoldBorder, 0.58f)
        );
        return button;
    }

    private static void ApplyNativeFont(TextElement element, Font uiFont)
    {
        element.style.unityFont = uiFont;
        element.style.unityFontDefinition = FontDefinition.FromFont(uiFont);
    }

    private static void OpenSupportPage()
    {
        Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode()));
    }

    private static string GetLanguageCode()
    {
        try
        {
            return PlayerPreferences.Data.LanguageCode ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
