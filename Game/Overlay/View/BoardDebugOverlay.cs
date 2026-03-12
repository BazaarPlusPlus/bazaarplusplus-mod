#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal sealed class BoardDebugOverlay
{
    public bool Enabled { get; private set; }

    public bool ShowAnchorPoint { get; private set; }

    public bool ShowItemSlots { get; private set; }

    public bool ShowSkillSlots { get; private set; }

    public bool ShowCardBounds { get; private set; }

    public bool ShowLabels { get; private set; }

    public void Apply(PreviewBoardDebugOptions options)
    {
        options ??= new PreviewBoardDebugOptions();

        Enabled = options.Enabled;
        ShowAnchorPoint = options.Enabled && options.ShowAnchorPoint;
        ShowItemSlots = options.Enabled && options.ShowItemSlots;
        ShowSkillSlots = options.Enabled && options.ShowSkillSlots;
        ShowCardBounds = options.Enabled && options.ShowCardBounds;
        ShowLabels = options.Enabled && options.ShowLabels;
    }
}
