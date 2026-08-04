#nullable enable
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CurrentReplayRecordingTooltipStyle
{
    private const float TooltipScale = 0.94f;
    private const float TooltipFontScale = 0.94f;

    private AuxiliaryTooltipController? _styledTooltip;
    private Vector3 _styledTooltipOriginalLocalScale;
    private float _styledTooltipOriginalScaleRatio;
    private float _styledTooltipOriginalHeaderFontSize;
    private float _styledTooltipOriginalBodyFontSize;
    private bool _styledTooltipOriginalHeaderAutoSizing;
    private bool _styledTooltipOriginalBodyAutoSizing;
    private TextAlignmentOptions _styledTooltipOriginalHeaderAlignment;
    private TextAlignmentOptions _styledTooltipOriginalBodyAlignment;
    private VerticalLayoutGroup? _styledTooltipLayout;
    private TextAnchor _styledTooltipOriginalChildAlignment;

    internal void Apply(AuxiliaryTooltipController tooltip)
    {
        if (!ReferenceEquals(_styledTooltip, tooltip))
        {
            Restore();
            _styledTooltip = tooltip;
            _styledTooltipOriginalLocalScale = tooltip.transform.localScale;
            _styledTooltipOriginalScaleRatio = tooltip.scaleRatio;

            if (tooltip.headerText != null)
            {
                _styledTooltipOriginalHeaderFontSize = tooltip.headerText.fontSize;
                _styledTooltipOriginalHeaderAutoSizing = tooltip.headerText.enableAutoSizing;
                _styledTooltipOriginalHeaderAlignment = tooltip.headerText.alignment;
            }

            if (tooltip.bodyText != null)
            {
                _styledTooltipOriginalBodyFontSize = tooltip.bodyText.fontSize;
                _styledTooltipOriginalBodyAutoSizing = tooltip.bodyText.enableAutoSizing;
                _styledTooltipOriginalBodyAlignment = tooltip.bodyText.alignment;
            }

            _styledTooltipLayout = tooltip.auxParent?.GetComponent<VerticalLayoutGroup>();
            if (_styledTooltipLayout != null)
                _styledTooltipOriginalChildAlignment = _styledTooltipLayout.childAlignment;
        }

        ApplyTextStyle(tooltip.headerText, _styledTooltipOriginalHeaderFontSize);
        ApplyTextStyle(tooltip.bodyText, _styledTooltipOriginalBodyFontSize);

        tooltip.scaleRatio = _styledTooltipOriginalScaleRatio * TooltipScale;
        if (tooltip._coroutine == null)
            tooltip.transform.localScale = _styledTooltipOriginalLocalScale * TooltipScale;

        if (_styledTooltipLayout != null)
            _styledTooltipLayout.childAlignment = TextAnchor.MiddleCenter;

        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip.PositioningRectTransform);
    }

    private static void ApplyTextStyle(TMP_Text? text, float originalFontSize)
    {
        if (text == null)
            return;

        text.enableAutoSizing = false;
        text.fontSize = originalFontSize * TooltipFontScale;
        text.alignment = TextAlignmentOptions.MidlineLeft;
    }

    internal void Restore()
    {
        var tooltip = _styledTooltip;
        if (tooltip == null)
        {
            _styledTooltip = null;
            return;
        }

        tooltip.transform.localScale = _styledTooltipOriginalLocalScale;
        tooltip.scaleRatio = _styledTooltipOriginalScaleRatio;
        if (tooltip.headerText != null)
        {
            tooltip.headerText.fontSize = _styledTooltipOriginalHeaderFontSize;
            tooltip.headerText.enableAutoSizing = _styledTooltipOriginalHeaderAutoSizing;
            tooltip.headerText.alignment = _styledTooltipOriginalHeaderAlignment;
        }
        if (tooltip.bodyText != null)
        {
            tooltip.bodyText.fontSize = _styledTooltipOriginalBodyFontSize;
            tooltip.bodyText.enableAutoSizing = _styledTooltipOriginalBodyAutoSizing;
            tooltip.bodyText.alignment = _styledTooltipOriginalBodyAlignment;
        }
        if (_styledTooltipLayout != null)
            _styledTooltipLayout.childAlignment = _styledTooltipOriginalChildAlignment;

        _styledTooltip = null;
        _styledTooltipLayout = null;
    }
}
