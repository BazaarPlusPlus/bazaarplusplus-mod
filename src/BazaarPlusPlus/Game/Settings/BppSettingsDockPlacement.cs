#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum BppSettingsDockSide
{
    LeftOfAnchor,
    RightOfAnchor,
    AboveAnchor,
}

internal enum BppSettingsDockPanelDirection
{
    UpLeft,
    UpRight,
}

internal enum BppSettingsDockSceneKind
{
    Default,
    ChestOpening,
}

internal readonly struct BppSettingsDockResolvedPlacement(
    BppSettingsDockSide side,
    BppSettingsDockPanelDirection panelDirection,
    float siblingGap,
    int siblingStepCount
)
{
    internal BppSettingsDockSide Side { get; } = side;

    internal BppSettingsDockPanelDirection PanelDirection { get; } = panelDirection;

    internal float SiblingGap { get; } = siblingGap;

    internal int SiblingStepCount { get; } = siblingStepCount > 0 ? siblingStepCount : 1;
}

internal readonly struct BppSettingsDockPlacement
{
    private const float DefaultSiblingGap = 18f;
    private readonly BppSettingsDockResolvedPlacement _defaultPlacement;
    private readonly BppSettingsDockResolvedPlacement _chestOpeningPlacement;
    private readonly bool _hasChestOpeningPlacement;

    private BppSettingsDockPlacement(
        string key,
        BppDockButtonIconKind buttonIconKind,
        BppSettingsDockResolvedPlacement defaultPlacement,
        bool hasChestOpeningPlacement = false,
        BppSettingsDockResolvedPlacement chestOpeningPlacement = default
    )
    {
        Key = key;
        ButtonIconKind = buttonIconKind;
        _defaultPlacement = defaultPlacement;
        _hasChestOpeningPlacement = hasChestOpeningPlacement;
        _chestOpeningPlacement = chestOpeningPlacement;
    }

    internal string Key { get; }

    internal BppSettingsDockSide Side => _defaultPlacement.Side;

    internal BppSettingsDockPanelDirection PanelDirection => _defaultPlacement.PanelDirection;

    internal float SiblingGap => _defaultPlacement.SiblingGap;

    internal BppDockButtonIconKind ButtonIconKind { get; }

    internal int SiblingStepCount => _defaultPlacement.SiblingStepCount;

    internal string DockButtonObjectName => $"BPP_SettingsDockButton_{Key}";

    internal string PanelObjectName => $"BPP_SettingsDockPanel_{Key}";

    internal static BppSettingsDockPlacement LeftOfSettingButton(
        string key,
        BppDockButtonIconKind buttonIconKind
    ) =>
        new(
            key,
            buttonIconKind,
            new BppSettingsDockResolvedPlacement(
                BppSettingsDockSide.LeftOfAnchor,
                BppSettingsDockPanelDirection.UpLeft,
                DefaultSiblingGap,
                siblingStepCount: 1
            )
        );

    internal static BppSettingsDockPlacement AboveSettingButton(
        string key,
        BppDockButtonIconKind buttonIconKind,
        int siblingStepCount = 1
    ) =>
        new(
            key,
            buttonIconKind,
            new BppSettingsDockResolvedPlacement(
                BppSettingsDockSide.AboveAnchor,
                BppSettingsDockPanelDirection.UpLeft,
                DefaultSiblingGap,
                siblingStepCount
            )
        );

    internal BppSettingsDockPlacement WithChestOpeningPlacement(
        BppSettingsDockSide side,
        BppSettingsDockPanelDirection panelDirection,
        int siblingStepCount
    ) =>
        new(
            Key,
            ButtonIconKind,
            _defaultPlacement,
            hasChestOpeningPlacement: true,
            new BppSettingsDockResolvedPlacement(side, panelDirection, SiblingGap, siblingStepCount)
        );

    internal BppSettingsDockPlacement ResolveForScene(BppSettingsDockSceneKind sceneKind)
    {
        if (sceneKind != BppSettingsDockSceneKind.ChestOpening || !_hasChestOpeningPlacement)
            return this;

        return new(Key, ButtonIconKind, _chestOpeningPlacement);
    }
}
