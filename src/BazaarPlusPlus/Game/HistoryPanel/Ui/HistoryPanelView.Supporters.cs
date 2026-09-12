#nullable enable
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private void BuildSupporterHeader()
    {
        var language = PlayerPreferences.Data.LanguageCode ?? string.Empty;
        var header = CreateRect("SupporterHeader", _layout!, .415f, .04f, .33f, .045f);
        var names = CreateRect("SupporterNames", header, 0, 0, .78f, 1);
        var layout = names.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.spacing = 5;

        var supporters = _model!.Supporters.Where(sample => sample.HasValue).Take(4).ToArray();
        if (supporters.Length == 0)
            SupporterLabel(names, HistoryPanelText.Subtitle(), Muted, shrink: true, maxWidth: 360);
        else
        {
            SupporterLabel(
                names,
                BPPSupporterAttributionText.FormatSupportedByPrefix(language),
                Muted
            );
            for (var i = 0; i < supporters.Length; i++)
            {
                if (i > 0)
                    SupporterLabel(names, "·", new Color(Muted.r, Muted.g, Muted.b, .55f));
                var supporter = supporters[i];
                var color = supporter.Tier switch
                {
                    >= 4 => Colors.SupporterTier4Text,
                    3 => Colors.SupporterTier3Text,
                    2 => Colors.SupporterTier2Text,
                    _ => Colors.SupporterTier1Text,
                };
                var label = SupporterLabel(names, supporter.Name, color, shrink: true);
                if (supporter.Tier >= 2)
                {
                    var highlight = Color.Lerp(color, Color.white, .55f);
                    label.color = Color.white;
                    label.enableVertexGradient = true;
                    label.colorGradient = new VertexGradient(highlight, highlight, color, color);
                }
            }
            var suffix = BPPSupporterAttributionText.FormatSupportedBySuffix(language);
            if (!string.IsNullOrWhiteSpace(suffix))
                SupporterLabel(names, suffix, Muted);
        }

        var sponsor = Button(
            header,
            T("Support", "赞赏"),
            .805f,
            .12f,
            .195f,
            .76f,
            () => Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(language)),
            size: 14
        );
        sponsor.gameObject.name = "SupporterAction";
        var sponsorLabel = sponsor.GetComponentInChildren<TextMeshProUGUI>();
        sponsorLabel.color = Colors.SupporterTier4Text;
    }

    private TextMeshProUGUI SupporterLabel(
        Transform parent,
        string value,
        Color color,
        bool shrink = false,
        float maxWidth = 96
    )
    {
        var label = Text(parent, value, 0, 0, 1, 1, 14, color);
        // Names come from the supporter catalog and must stay literal, including markup characters.
        label.richText = false;
        label.fontStyle = FontStyles.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var width = label.GetPreferredValues(value).x + 2;
        var element = label.gameObject.AddComponent<LayoutElement>();
        element.minWidth = shrink ? Mathf.Min(width, 24) : width;
        element.preferredWidth = shrink ? Mathf.Min(width, maxWidth) : width;
        element.flexibleWidth = 0;
        return label;
    }
}
