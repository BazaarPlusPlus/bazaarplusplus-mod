#nullable enable
using BazaarPlusPlus.Game.Supporters.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private void BuildSupporterHeader()
    {
        var header = CreateRect("SupporterHeader", _layout!, .415f, .04f, .33f, .045f);
        var attribution = new BPPSupporterNativeAttributionRow(
            header,
            _font!,
            TextAnchor.MiddleRight
        );
        attribution.SetSkin(_skin?[0], _skin?[1]);
        attribution.Bind(_model!.Supporters, HistoryPanelText.Subtitle());
    }
}
