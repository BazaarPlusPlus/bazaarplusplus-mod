#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Placeholder cell geometry per tab. Numbers are conservative initial guesses; the design's
// Phase 0 spike measures real prefab (nativeWidth, nativeHeight) for the three Item sizes
// and the Skill prefab and feeds them back here. Keeping the values in one place makes the
// later edit a one-file change.
internal static class CollectionGridConstants
{
    public const float ItemCellWidth = 230f;
    public const float ItemCellHeight = 300f;

    public const float SkillCellWidth = 200f;
    public const float SkillCellHeight = 200f;

    public const float GridGap = 14f;

    public const int ItemMinColumns = 4;
    public const int ItemMaxColumns = 10;
    public const int SkillMinColumns = 6;
    public const int SkillMaxColumns = 14;

    public const int RowOverscan = 1;

    // Per-frame wall-clock budget for cold cell binds (cards entering window for the first
    // time). Reposition is free; bind triggers Addressables loads on the Item path.
    public const float ColdBindBudgetMs = 3f;

    // Sorting layers: UITK panel at 26, card overlay one above at 27. Anything we put
    // between them (e.g., a full-screen GraphicRaycaster blocker) intercepts every click
    // and wheel event before UITK can see it, freezing all panel chrome — so we don't.
    public const int UiToolkitSortingOrder = 26;
    public const int OverlaySortingOrder = 27;

    // true (default): polled hover. No per-card hit Image, no overlay GraphicRaycaster;
    // Mouse.current is polled each Update to dispatch OnHover/OnHoverOut on the cell under
    // the cursor. UITK at sortingOrder 26 receives every click and wheel event uninterrupted.
    //
    // false: raycaster hover. Per-card transparent Image + overlay GraphicRaycaster fire
    // hover via IPointerEnter/Exit. The card prefab's own RawImage has raycastTarget=true,
    // so the overlay raycaster catches wheel events landing on cards and drops them —
    // ScrollView wheel scrolling stops working in the card region. Only flip for diagnostic
    // work; a real fix would also need an IScrollHandler forwarder on the overlay root.
    //
    // `static readonly` (not `const`) so the unused branch does not dead-code-warn when the
    // flag is flipped in source.
    public static readonly bool UsePolledHover = true;

    // Animation tuning. All durations are characteristic times for an exponential lerp
    // (t = 1 - exp(-dt / tau)), so the visible motion finishes within ~3*tau seconds.
    public const float CardFadeInSeconds = 0.18f;

    // Open is a presentation (deliberate); close is a dismissal (snappy). With out at 0.04
    // the close-fade visually settles in ~120ms, fast enough not to feel like the panel
    // is "lingering" after Escape / F9, but still smooth enough to avoid a hard pop.
    public const float PanelFadeInSeconds = 0.12f;
    public const float PanelFadeOutSeconds = 0.04f;

    // UITK points scrolled per mouse-wheel notch. The previous smooth-wheel intercept
    // tried to lerp scrollOffset itself but the WheelEvent interaction with ScrollView's
    // default handler killed wheel scrolling entirely on this UITK version; we ship the
    // built-in instant snap instead and just give each notch enough travel to feel meaty.
    public const float MouseWheelScrollPoints = 300f;

    public static float CellWidthFor(ECardType type) =>
        type == ECardType.Skill ? SkillCellWidth : ItemCellWidth;

    public static float CellHeightFor(ECardType type) =>
        type == ECardType.Skill ? SkillCellHeight : ItemCellHeight;

    public static int MinColumnsFor(ECardType type) =>
        type == ECardType.Skill ? SkillMinColumns : ItemMinColumns;

    public static int MaxColumnsFor(ECardType type) =>
        type == ECardType.Skill ? SkillMaxColumns : ItemMaxColumns;
}
