#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal interface IPostCombatImpactTooltipView : IDisposable
{
    string Header { get; }

    bool Show(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        CombatImpactSource? source
    );

    bool Position(AuxiliaryTooltipController auxiliary, CardTooltipController primary);

    void Hide();

    bool OnNativeTooltipChanging(CardTooltipController controller);

    bool OnNativeAuxiliaryTooltipChanging(AuxiliaryTooltipController controller);
}
