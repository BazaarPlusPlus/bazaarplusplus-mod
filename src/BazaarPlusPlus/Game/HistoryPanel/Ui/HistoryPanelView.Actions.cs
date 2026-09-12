#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private readonly Action _record;
    private readonly Action _delete;
    private readonly Action _checkHealth;
    private readonly Action<string?> _submitAccountCode;
    private readonly Action _toggleAccountLink;
    private readonly Action _markAccountLinked;
    private bool _moreVisible;
    private string _accountCode = string.Empty;
    private TMP_InputField? _accountInput;

    private void BuildActions()
    {
        var m = _model!;
        Button(
            _layout!,
            T("More", "更多"),
            .76f,
            .04f,
            .09f,
            .045f,
            () =>
            {
                _moreVisible = !_moreVisible;
                Rebuild();
            }
        );
        if (m.SectionMode == HistorySectionMode.Runs)
            Button(_layout!, m.DeleteButtonText, .04f, .90f, .16f, .044f, _delete).interactable =
                m.DeleteButtonEnabled;
        Button(
            _layout!,
            m.RecordAndReplayButtonText,
            .58f,
            .90f,
            .13f,
            .055f,
            _record
        ).interactable = m.RecordAndReplayButtonEnabled;
        Button(
            _layout!,
            m.ReplayButtonText,
            .72f,
            .90f,
            .23f,
            .055f,
            _replay,
            true,
            23
        ).interactable = m.ReplayButtonEnabled;
    }

    private void BuildMoreDialog()
    {
        var m = _model!;
        // This dialog must occlude both the panel and the separately owned native boards.
        var shade = CreateRect("HistoryMoreDialog", _layout!, 0, 0, 1, 1);
        var canvas = shade.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        shade.gameObject.AddComponent<GraphicRaycaster>();
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .75f);
        var dialog = CreateRect("Dialog", shade, .24f, .18f, .52f, .66f);
        dialog.gameObject.AddComponent<Image>().color = new Color(.065f, .043f, .025f);
        Panel(dialog, 0, 0, 1, 1);
        Text(dialog, T("History options", "历史记录选项"), .05f, .035f, .65f, .07f, 27);
        Button(
            dialog,
            HistoryPanelText.Close(),
            .81f,
            .045f,
            .14f,
            .06f,
            () =>
            {
                _moreVisible = false;
                Rebuild();
            }
        );
        Text(
            dialog,
            m.DatabaseChipText,
            .05f,
            .14f,
            .53f,
            .055f,
            17,
            StatusColor(m.DatabaseChipSeverity)
        );
        Button(
            dialog,
            m.ServerHealthButtonText,
            .65f,
            .14f,
            .30f,
            .055f,
            _checkHealth
        ).interactable = m.ServerHealthButtonEnabled;
        var status = Text(
            dialog,
            m.StatusMessage ?? string.Empty,
            .05f,
            .21f,
            .90f,
            .075f,
            14,
            StatusColor(m.StatusSeverity)
        );
        status.textWrappingMode = TextWrappingModes.Normal;
        if (m.AccountCardVisible)
            BuildAccountLink(dialog);
    }

    private void BuildAccountLink(RectTransform dialog)
    {
        var m = _model!;
        Text(dialog, m.AccountTitleText, .05f, .31f, .66f, .06f, 21);
        Text(dialog, m.AccountRowStatusText, .05f, .38f, .62f, .045f, 16, Muted);
        if (m.AccountRowActionVisible)
            Button(
                dialog,
                m.AccountLinkFormVisible ? m.AccountLinkCollapseText : m.AccountRowActionText,
                .74f,
                .37f,
                .21f,
                .055f,
                _toggleAccountLink,
                size: 15
            );
        if (!m.AccountLinkFormVisible)
            return;
        var why = Text(
            dialog,
            m.AccountWhyText + "\n" + m.AccountHintText,
            .05f,
            .45f,
            .90f,
            .10f,
            14,
            Muted
        );
        why.textWrappingMode = TextWrappingModes.Normal;
        var inputRect = CreateRect("AccountLinkCode", dialog, .05f, .57f, .55f, .065f);
        var background = inputRect.gameObject.AddComponent<Image>();
        background.color = new Color(.20f, .15f, .09f);
        var viewport = CreateRect("TextViewport", inputRect, .035f, .03f, .93f, .94f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var inputText = Text(viewport, string.Empty, 0, 0, 1, 1, 20);
        var placeholder = Text(
            viewport,
            HistoryPanelText.AccountLink.EmptyCode(),
            0,
            0,
            1,
            1,
            16,
            Muted
        );
        var input = inputRect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onValidateInput = (_, _, character) =>
            char.IsWhiteSpace(character) ? '\0' : character;
        input.SetTextWithoutNotify(_accountCode);
        input.interactable = m.AccountLinkInputEnabled;
        input.onValueChanged.AddListener(value =>
        {
            _accountCode = new string(
                value.Where(character => !char.IsWhiteSpace(character)).Take(10).ToArray()
            );
            if (value != _accountCode)
                input.SetTextWithoutNotify(_accountCode);
        });
        input.onSubmit.AddListener(_ =>
        {
            if (_model?.AccountLinkButtonEnabled == true)
                _submitAccountCode(_accountCode);
        });
        _accountInput = input;
        Button(
            dialog,
            m.AccountLinkButtonText,
            .63f,
            .57f,
            .32f,
            .065f,
            () => _submitAccountCode(_accountCode.Trim()),
            true,
            17
        ).interactable = m.AccountLinkButtonEnabled;
        var banner = Text(
            dialog,
            m.AccountLinkBannerText ?? string.Empty,
            .05f,
            .65f,
            .90f,
            .08f,
            14,
            StatusColor(m.AccountLinkBannerSeverity)
        );
        banner.textWrappingMode = TextWrappingModes.Normal;
        if (m.AccountAlreadyLinkedButtonVisible)
            Button(
                dialog,
                m.AccountAlreadyLinkedButtonText,
                .63f,
                .75f,
                .32f,
                .05f,
                _markAccountLinked,
                size: 15
            );
    }

    private static Color StatusColor(StatusSeverity severity) =>
        severity switch
        {
            StatusSeverity.Failure => new Color(.90f, .40f, .31f),
            StatusSeverity.Success => new Color(.48f, .76f, .42f),
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };
}
