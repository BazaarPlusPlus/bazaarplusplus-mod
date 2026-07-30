#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal interface IPostCombatImpactTooltipView : IDisposable
{
    bool Show(CardTooltipController controller, string sourceId, CombatImpactSource? source);

    void Hide();

    bool OnNativeTooltipChanging(CardTooltipController controller);
}
