# History Panel Eliminated Row Indicator — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface "ghost opponent eliminated" on each Ghost-tab battle row via a gold accent bar and a "Knocked Out / 对手出局" chip, so players can spot elimination wins without opening the detail panel.

**Architecture:** Reuse the existing `IsGhostOpponentEliminated()` predicate; extend `BattleRowRefs` with one new `Label` chip; extend `MakeBattleRow` to construct it (default hidden); extend `BindBattleRow` to toggle visibility; extend `ApplyBattleRowState` with an elimination color branch. No data-layer or formatter changes.

**Tech Stack:** C#, Unity UI Toolkit (`UnityEngine.UIElements`), .NET targeting via `BazaarPlusPlus.csproj`, Bazaar `LocalizedTextSet` infrastructure.

**Spec:** [docs/history-panel-eliminated-row-indicator.md](history-panel-eliminated-row-indicator.md)

**Build command (used in every task):**
```
dotnet build BazaarPlusPlus.csproj -c Debug
```
Per `.rules`, this is the smallest relevant build for code-only changes inside `Game/HistoryPanel/`. No new tests are added — the spec documents that this UI Toolkit panel has no automated test seam in this repo.

---

## File Structure

| File | Responsibility | Modifying |
|---|---|---|
| [Game/HistoryPanel/HistoryPanelText.cs](../Game/HistoryPanel/HistoryPanelText.cs) | Localized strings for the panel | Add one `LocalizedTextSet` + one accessor |
| [Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs](../Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs) | Battle row construction, binding, styling | Extend `BattleRowRefs`, `MakeBattleRow`, `BindBattleRow`, `ApplyBattleRowState` |

No new files. No changes outside `Game/HistoryPanel/`.

---

## Task 1: Add Short-Form Localization String

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanelText.cs:185-190` (add adjacent text set)
- Modify: `Game/HistoryPanel/HistoryPanelText.cs:270-271` (add adjacent accessor)

- [ ] **Step 1: Add `GhostOpponentEliminatedShortText` after the existing long-form set**

Open `Game/HistoryPanel/HistoryPanelText.cs`. Find the existing text set ending around line 190:

```csharp
private static readonly LocalizedTextSet GhostOpponentEliminatedNoticeText = new(
    "After this battle, the opponent is eliminated.",
    "打完这场战斗后，对手直接出局。",
    "打完這場戰鬥後，對手直接出局。",
    "打完這場戰鬥後，對手直接出局。"
);
```

Insert immediately after, preserving the blank-line separator pattern used elsewhere in the file:

```csharp
private static readonly LocalizedTextSet GhostOpponentEliminatedShortText = new(
    "Knocked Out",
    "对手出局",
    "對手出局",
    "對手出局"
);
```

The 4-arg order matches the sibling text set (EN, zh-CN, zh-TW, zh-TW). Using exactly that ordering avoids a constructor-arity mismatch with `LocalizedTextSet`.

- [ ] **Step 2: Add the accessor after `GhostOpponentEliminatedNotice()`**

Find around line 270:

```csharp
internal static string GhostOpponentEliminatedNotice() =>
    Resolve(GhostOpponentEliminatedNoticeText);
```

Insert immediately after:

```csharp
internal static string GhostOpponentEliminatedShort() =>
    Resolve(GhostOpponentEliminatedShortText);
```

- [ ] **Step 3: Build to verify**

Run:
```
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: Build succeeds. The new accessor is unused at this point — no warning is expected because the method is `internal`, not `private`.

- [ ] **Step 4: Commit**

```
git add Game/HistoryPanel/HistoryPanelText.cs
git commit -m "Add short-form localized text for ghost elimination chip"
```

Per `.rules` PR-hygiene conventions, the commit message uses an imperative verb and no conventional-commit prefix.

---

## Task 2: Plumb `EliminatedChip` Through Row Construction

This task introduces the chip into the visual tree but keeps it hidden by default. After this task the row looks identical to before — no behavioral regression and no behavioral gain. Wiring to data happens in Task 3.

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs:198-266` (`MakeBattleRow`)
- Modify: `Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs:479-524` (`BattleRowRefs`)

- [ ] **Step 1: Extend `BattleRowRefs` with the new field**

Find the class around line 479. Update the constructor signature, body, and add the property. Replace the existing class with:

```csharp
private sealed class BattleRowRefs
{
    public BattleRowRefs(
        VisualElement root,
        VisualElement accent,
        Label dayBubble,
        Label time,
        Label opponentRankPill,
        Label playerSummaryChip,
        Label opponentHeroPill,
        Label opponentSummaryChip,
        Label eliminatedChip,
        Label opponentName
    )
    {
        Root = root;
        Accent = accent;
        DayBubble = dayBubble;
        Time = time;
        OpponentRankPill = opponentRankPill;
        PlayerSummaryChip = playerSummaryChip;
        OpponentHeroPill = opponentHeroPill;
        OpponentSummaryChip = opponentSummaryChip;
        EliminatedChip = eliminatedChip;
        OpponentName = opponentName;
        Index = -1;
    }

    public VisualElement Root { get; }

    public VisualElement Accent { get; }

    public Label DayBubble { get; }

    public Label Time { get; }

    public Label OpponentRankPill { get; }

    public Label PlayerSummaryChip { get; }

    public Label OpponentHeroPill { get; }

    public Label OpponentSummaryChip { get; }

    public Label EliminatedChip { get; }

    public Label OpponentName { get; }

    public int Index { get; set; }
}
```

The new parameter is positioned **before** `opponentName` to mirror the visual order (chip appears left of the name in the row) and to match the construction order in `MakeBattleRow` (Step 2).

- [ ] **Step 2: Construct and configure the chip in `MakeBattleRow`**

Find `MakeBattleRow` around line 198. Locate the block that builds `opponentRow`:

```csharp
var opponentRow = CreateInfoChipRow(content, 6f, 6f);
var opponentHeroPill = CreateInlinePill(opponentRow, 64f);
SetFixedPillWidth(opponentHeroPill, 80f);
var opponentSummaryChip = CreateInfoChip(
    opponentRow,
    HistoryPanelText.OpponentSideShort(),
    100f
);
opponentSummaryChip.style.marginRight = 0f;
opponentSummaryChip.style.marginLeft = 8f;
var opponentName = CreateInlineText(opponentRow, 12, new Color(0.76f, 0.80f, 0.87f, 0.92f));
opponentName.style.marginLeft = 10f;
opponentName.style.flexGrow = 1f;
opponentName.style.unityTextAlign = TextAnchor.MiddleRight;
```

Insert the chip construction between `opponentSummaryChip` configuration and the `opponentName` creation. The block becomes:

```csharp
var opponentRow = CreateInfoChipRow(content, 6f, 6f);
var opponentHeroPill = CreateInlinePill(opponentRow, 64f);
SetFixedPillWidth(opponentHeroPill, 80f);
var opponentSummaryChip = CreateInfoChip(
    opponentRow,
    HistoryPanelText.OpponentSideShort(),
    100f
);
opponentSummaryChip.style.marginRight = 0f;
opponentSummaryChip.style.marginLeft = 8f;

var eliminatedChip = CreateInlinePill(opponentRow, 0f);
eliminatedChip.style.marginLeft = 10f;
eliminatedChip.style.marginRight = 0f;
eliminatedChip.style.paddingLeft = 10f;
eliminatedChip.style.paddingRight = 10f;
eliminatedChip.style.fontSize = 11;
eliminatedChip.style.color = new Color(0.99f, 0.90f, 0.68f, 1f);
eliminatedChip.style.backgroundColor = new Color(0.32f, 0.24f, 0.10f, 0.96f);
eliminatedChip.style.borderTopWidth = 1f;
eliminatedChip.style.borderRightWidth = 1f;
eliminatedChip.style.borderBottomWidth = 1f;
eliminatedChip.style.borderLeftWidth = 1f;
var eliminatedBorder = new Color(0.94f, 0.70f, 0.28f, 0.55f);
eliminatedChip.style.borderTopColor = eliminatedBorder;
eliminatedChip.style.borderRightColor = eliminatedBorder;
eliminatedChip.style.borderBottomColor = eliminatedBorder;
eliminatedChip.style.borderLeftColor = eliminatedBorder;
eliminatedChip.style.display = DisplayStyle.None;

var opponentName = CreateInlineText(opponentRow, 12, new Color(0.76f, 0.80f, 0.87f, 0.92f));
opponentName.style.marginLeft = 10f;
opponentName.style.flexGrow = 1f;
opponentName.style.unityTextAlign = TextAnchor.MiddleRight;
```

Notes on the styling:
- `CreateInlinePill(parent, 0f)` reuses the existing 20-height pill helper (line 315) and sets minWidth to 0, so the chip auto-sizes to its content (~50px CN / ~80px EN).
- Color values are exactly the spec's gold palette.
- Border (1px gold-with-alpha) gives the chip a slightly stronger silhouette than other chips, signaling "this row is special".
- `display = None` is the default; Task 3 toggles it.

- [ ] **Step 3: Update the `BattleRowRefs` construction site**

Still inside `MakeBattleRow`, find the existing constructor call:

```csharp
var refs = new BattleRowRefs(
    row,
    accent,
    dayBubble,
    timeLabel,
    opponentRankPill,
    playerSummaryChip,
    opponentHeroPill,
    opponentSummaryChip,
    opponentName
);
```

Add `eliminatedChip` in the matching position (before `opponentName`):

```csharp
var refs = new BattleRowRefs(
    row,
    accent,
    dayBubble,
    timeLabel,
    opponentRankPill,
    playerSummaryChip,
    opponentHeroPill,
    opponentSummaryChip,
    eliminatedChip,
    opponentName
);
```

- [ ] **Step 4: Build to verify**

Run:
```
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: Build succeeds. No new warnings.

- [ ] **Step 5: Smoke test (optional but recommended)**

If you have a quick way to launch the game with the mod (BepInEx debug copy in `BazaarPlusPlus.csproj` Debug configuration), open the History Panel → Ghost tab → confirm rows render unchanged (chip is hidden). If you don't have a quick launch path, skip — Task 3 includes a full manual verification pass.

- [ ] **Step 6: Commit**

```
git add Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs
git commit -m "Add hidden eliminated chip slot to history battle row"
```

---

## Task 3: Wire Chip Visibility and Eliminate-Win Accent Color

This task makes the indicator user-visible. After this task, the spec's behavior is fully implemented.

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs:268-310` (`BindBattleRow`)
- Modify: `Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs:334-375` (`ApplyBattleRowState`)

- [ ] **Step 1: Toggle chip text and visibility in `BindBattleRow`**

Find `BindBattleRow` around line 268. Locate the trailing block that sets opponent name and applies row state (around line 305-309):

```csharp
refs.OpponentName.text = battle.OpponentName ?? string.Empty;
refs.OpponentName.style.display = string.IsNullOrWhiteSpace(refs.OpponentName.text)
    ? DisplayStyle.None
    : DisplayStyle.Flex;
ApplyBattleRowState(refs, _battleList?.selectedIndex == index, battle);
```

Insert chip wiring **before** the `ApplyBattleRowState` call:

```csharp
refs.OpponentName.text = battle.OpponentName ?? string.Empty;
refs.OpponentName.style.display = string.IsNullOrWhiteSpace(refs.OpponentName.text)
    ? DisplayStyle.None
    : DisplayStyle.Flex;

var isEliminated = HistoryPanelFormatter.IsGhostOpponentEliminated(battle);
refs.EliminatedChip.text = HistoryPanelText.GhostOpponentEliminatedShort();
refs.EliminatedChip.style.display = isEliminated ? DisplayStyle.Flex : DisplayStyle.None;

ApplyBattleRowState(refs, _battleList?.selectedIndex == index, battle);
```

`IsGhostOpponentEliminated` is null-safe (returns false for `null`), so no extra guard is needed. Setting the text every bind (rather than once at construction) ensures locale changes propagate when the panel reopens in a different language.

- [ ] **Step 2: Add the elimination branch to `ApplyBattleRowState`**

Find `ApplyBattleRowState` around line 334. Replace the entire method body with:

```csharp
private static void ApplyBattleRowState(
    BattleRowRefs refs,
    bool selected,
    HistoryBattleRecord battle
)
{
    var isWin = HistoryPanelFormatter.IsBattleWin(battle);
    var isLoss = HistoryPanelFormatter.IsBattleLoss(battle);
    var isEliminated = HistoryPanelFormatter.IsGhostOpponentEliminated(battle);

    refs.Root.style.backgroundColor = selected
        ? isEliminated
            ? new Color(0.22f, 0.18f, 0.10f, 0.99f)
            : isWin
                ? new Color(0.13f, 0.23f, 0.22f, 0.99f)
                : isLoss
                    ? new Color(0.24f, 0.18f, 0.16f, 0.99f)
                    : new Color(0.18f, 0.24f, 0.31f, 0.99f)
        : isEliminated
            ? new Color(0.18f, 0.14f, 0.08f, 0.98f)
            : isWin
                ? new Color(0.10f, 0.15f, 0.16f, 0.98f)
                : isLoss
                    ? new Color(0.15f, 0.13f, 0.15f, 0.98f)
                    : new Color(0.13f, 0.15f, 0.18f, 0.98f);

    refs.Accent.style.backgroundColor =
        isEliminated ? new Color(0.94f, 0.70f, 0.28f, 0.95f)
        : isWin ? new Color(0.23f, 0.54f, 0.47f, 0.95f)
        : isLoss ? new Color(0.63f, 0.36f, 0.24f, 0.95f)
        : new Color(0.34f, 0.47f, 0.64f, 0.95f);
    var borderColor =
        isEliminated ? new Color(0.62f, 0.46f, 0.18f, 0.50f)
        : isWin ? new Color(0.22f, 0.44f, 0.40f, 0.42f)
        : isLoss ? new Color(0.44f, 0.27f, 0.20f, 0.42f)
        : new Color(0.24f, 0.31f, 0.41f, 0.42f);
    refs.Root.style.borderLeftColor = borderColor;
    refs.Root.style.borderRightColor = borderColor;
    refs.Root.style.borderTopColor = borderColor;
    refs.Root.style.borderBottomColor = borderColor;
    refs.DayBubble.style.backgroundColor =
        isEliminated ? new Color(0.24f, 0.18f, 0.08f, 0.98f)
        : isWin ? new Color(0.13f, 0.28f, 0.23f, 0.98f)
        : isLoss ? new Color(0.33f, 0.20f, 0.15f, 0.98f)
        : new Color(0.18f, 0.23f, 0.31f, 0.98f);
    refs.DayBubble.style.borderLeftColor = borderColor;
    refs.DayBubble.style.borderRightColor = borderColor;
    refs.DayBubble.style.borderTopColor = borderColor;
    refs.DayBubble.style.borderBottomColor = borderColor;
}
```

The `isEliminated` branch is checked **first** in every conditional. Because `IsGhostOpponentEliminated()` already requires a win, the `isEliminated == true` case implicitly covers `isWin == true`; placing it first means we never fall through to the regular green-win colors.

Color rationale:
- Accent `(0.94, 0.70, 0.28)` matches the gold the detail panel banner already uses
- Row backgrounds are darker, more saturated gold variants (selected vs unselected mirrors the existing 4-tone scheme)
- Border is a desaturated 50%-alpha gold to match the win/loss border alpha pattern

- [ ] **Step 3: Build to verify**

Run:
```
dotnet build BazaarPlusPlus.csproj -c Debug
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```
git add Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs
git commit -m "Highlight ghost elimination wins on history battle rows"
```

---

## Task 4: Manual Verification

The spec defines seven acceptance cases. Walk through each in-game and confirm the expected visual outcome. There is no automated test, per project convention.

**Setup:**
- Build the mod (`dotnet build BazaarPlusPlus.csproj -c Debug`); the Debug copy step deploys it to `BepInEx/plugins`
- Launch the game, open History Panel
- Have at least one Ghost run with both elimination and non-elimination wins recorded; if you don't, do a couple of quick ghost runs to seed data

- [ ] **Step 1: Ghost tab — elimination win**

Action: Switch to Ghost tab. Find a row from a bundle's last battle that you won.

Expected:
- Left accent strip is gold (not green)
- Row background is dark-gold tinted
- "对手出局" / "Knocked Out" chip is visible in the opponent row, between the opponent stats chip and the opponent name
- Click into the row → detail panel still shows the existing yellow `_ghostOpponentEliminatedNotice` banner above the preview

- [ ] **Step 2: Ghost tab — non-final win**

Action: Find a row from a non-final battle in a bundle that you won.

Expected:
- Accent is normal green
- No chip visible
- Detail panel shows no elimination banner

- [ ] **Step 3: Ghost tab — final battle loss**

Action: Find a row where you lost the final battle of a bundle.

Expected:
- Accent is red (loss colors)
- No chip visible
- Detail panel shows no elimination banner

- [ ] **Step 4: Local Runs tab**

Action: Switch to Runs tab. Drill into any run that contains battles. Inspect the battle list for that run.

Expected:
- All rows render with the existing green/red/blue-gray accents
- No row anywhere shows the gold accent or chip (Local battles always have `Source != Ghost`, predicate returns false)

- [ ] **Step 5: Locale switch**

Action: Switch the game language EN → 简体中文 → 繁體中文. Reopen the History Panel each time. Re-inspect a known elimination row.

Expected:
- Chip text updates to "Knocked Out" / "对手出局" / "對手出局"
- Row layout doesn't break at any locale (chip auto-width handles the difference)
- The opponent name still right-aligns to the row edge, possibly truncated with ellipsis if the row is narrow

- [ ] **Step 6: Selection states**

Action: Click an elimination row to select it; click another row to deselect it.

Expected:
- Selected elimination row: brighter gold background (`0.22, 0.18, 0.10`)
- Unselected elimination row: darker gold background (`0.18, 0.14, 0.08`)
- Both states keep the gold accent strip
- Compared with a selected normal-win row, the elimination row is visibly more orange/gold

- [ ] **Step 7: Narrow-panel layout**

Action: Resize the panel as narrow as the parent layout allows.

Expected:
- Chip retains its width and shape (~50px / ~80px)
- `OpponentName` is the element that truncates first, exactly as in current behavior
- No layout overflow, no chip elements wrapping or disappearing

- [ ] **Step 8: Record results**

If all seven acceptance cases pass, the implementation is complete. If any fail, file a regression note inline in this plan checkbox before deciding next action.

No commit for this task — verification only.

---

## Done Criteria

- 3 commits landed on the working branch (one per Task 1/2/3)
- All seven acceptance cases (Task 4) pass
- `dotnet build BazaarPlusPlus.csproj -c Debug` succeeds with no new warnings
- No changes outside `Game/HistoryPanel/HistoryPanelText.cs` and `Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs`
