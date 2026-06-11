---
status: implemented
archived: 2026-06-12
superseded-by: code
---

> Status: IMPLEMENTED. `SupporterAttributionCount=4`, per-hero `HeroBuildCounts` corpus summary, and the fixed refresh strip all shipped (HEAD merge "live build pull strip and corpus detail", verified `LiveBuildPanel.cs:31,138`, `TenWinCorpusSummary.cs:30`). Retained as the design record.

# Live Build Panel Refresh Rail Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the LiveBuildPanel right rail feel like a compact data/control surface: fixed-size pull action, more supporter names, detailed ten-win corpus status by hero, and less noisy empty-state copy.

**Architecture:** Keep recommendation refresh ownership inside `Game/LiveBuildPanel/Recommendations`; do not reintroduce HistoryPanel ownership or cross-feature calls. Treat `TenWinBuildCorpus` as the pure parser/summary source and let `LiveBuildPanel`/text/view code format the UI. Keep row geometry stable so native card previews and foreground candidate markers do not churn.

**Tech Stack:** C# 12, Unity UI Toolkit, BepInEx 5.x, The Bazaar publicized assemblies, executable test projects under `tests/`.

---

## Status

Draft plan, not implemented. Send this back for confirmation before editing production code.

## Code Evidence

- LiveBuildPanel already owns the live-card reader, recommendation repository, refresh service, and candidate state (`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:33-37`).
- Opening LiveBuildPanel currently samples exactly two supporters via `BPPSupporters.SampleMany(2)` (`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:137`), while the shared attribution row can render up to four valid samples (`src/BazaarPlusPlus/Game/Supporters/Ui/BPPSupporterAttributionRow.cs:37`).
- The current right rail is fixed at 330 px and places a full-width `_finalBuildRefreshButton` under the candidate chip (`src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:300-349`).
- Pull feedback is a separate label immediately below that button (`src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:351-359`) and is compacted to 150 characters on refresh (`src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:133-142`).
- The current success status only receives `GeneratedAtUtc`, total `BuildCount`, and total `HeroCount` (`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:261-269`); `TenWinCorpusSummary` only stores those three values (`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs:510-524`).
- The parser already has the per-hero dictionary needed for hero counts (`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs:61`) and computes total build count from that dictionary (`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs:74`).
- The row VM currently always supplies empty text for shop/board/stash rows (`src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveBuildPanelSnapshot.cs:42-59`), and the view shows that text whenever a row has zero cards (`src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:393-404`).
- `LiveCardSnapshotReader` returns an empty snapshot when there is no active run/player (`src/BazaarPlusPlus/GameInterop/LiveCards/LiveCardSnapshotReader.cs:26-36`), so no-run state can currently trigger all three row empty messages at once.
- Existing focused coverage lives in `tests/LiveBuildPanel.Tests/Program.cs` for panel text/layout contracts and `tests/LiveBuildRecommendations.Tests/Program.cs` for parser/repository behavior (`tests/LiveBuildPanel.Tests/Program.cs:8-14`, `tests/LiveBuildRecommendations.Tests/Program.cs:24-41`).

## Product Decisions

1. Pull action becomes a compact fixed-width/fixed-height button inside a data-status strip, not a full-width primary rail button.
2. Supporter attribution shows up to four names in LiveBuildPanel, matching the shared row's current rendering cap.
3. Pull status moves beside the fixed pull button and can use two lines: primary status plus corpus detail. Only the status text column flexes; the button and strip dimensions are fixed.
4. Successful pull status includes generated time, total build count, total hero count, and each hero's build count.
5. Row empty messages should not all fire during no-run state. No-run remains a single rail/recommendation status.
6. Active-run empty rows stay visible, but use compact row copy; detailed text belongs in tooltip, not as three long labels.

## Non-Goals

- Do not change recommendation recall or ranking.
- Do not change analyzer JSON schema or server endpoint.
- Do not change LiveBuildPanel open/close hotkey behavior.
- Do not import CollectionPanel text helpers into LiveBuildPanel just to format hero labels.
- Do not add a new settings toggle for this polish.
- Do not update public supporter-list data; this plan only changes how many already-sampled supporters LiveBuildPanel asks to display.

## Target UX

Right rail order:

1. Title row and close button.
2. Supporter attribution row with up to four supporter names.
3. Candidate count chip.
4. Corpus status strip:
   - left side: short status primary line;
   - right side: fixed-width/fixed-height `Pull Builds` / `拉取阵容` button.
5. Corpus detail panel with data time, total count, hero count, and per-hero counts.
6. Recommendation status card.
7. Previous / next navigation.

Status strip behavior:

- Idle with loaded corpus: show data time, total count, and per-hero counts in the detail panel if available.
- Idle without loaded corpus: hide detail or show no corpus copy only if needed.
- Pending: primary text `Pulling ten-win builds...` / `正在拉取十胜阵容...`; button disabled and fixed width/height preserved.
- Success: primary text `Ten-win builds updated.` / `十胜阵容已更新。`; detail text includes `data <time>`, total builds, total heroes, and all hero counts.
- Failure: primary text includes the error from `BuildRecommendationRefreshResult.Error`; previous recommendations remain visible.
- Already running: primary text says pull is already in progress; do not start a second request.

Empty-row behavior:

- No active run: suppress shop/board/stash row empty labels and rely on `No active run.` / `当前没有进行中的对局。`.
- Active run, empty shop/board/stash: suppress visible empty labels; full current row-specific copy remains as tooltip only.
- Final build row remains empty when no recommendation; recommendation status already explains `No matching ten-win build.` or `Select item candidates...`.

## File Structure

- Modify `src/BazaarPlusPlus/Infrastructure/UiTokens/Sizes.cs`
  - Add fixed LiveBuildPanel refresh-control constants.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`
  - Request four supporters.
  - Keep refresh state, but build richer status fields for the snapshot.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveBuildPanelSnapshot.cs`
  - Add active-run and refresh-status detail fields.
  - Gate row empty text on active-run state.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveItemBoardRowVm.cs`
  - Carry both visible empty text and tooltip empty text if the view needs compact copy.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs`
  - Replace full-width refresh button + standalone status label with compact status strip.
  - Keep status-only refreshes from restarting native preview rendering.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.Refresh.cs`
  - Add corpus detail formatting with per-hero counts.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.Status.cs`
  - Add compact empty-row copy.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.FontSample.cs`
  - Warm all new CJK status and compact empty-state glyphs.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs`
  - Add per-hero count summary without adding Unity/game references.
- Modify `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs`
  - Include per-hero counts in `GetCorpusSummary()`.
- Modify `tests/LiveBuildPanel.Tests/Program.cs`
  - Add focused tests for supporter count constant, empty-state suppression, and font warm-up.
- Modify `tests/LiveBuildRecommendations.Tests/Program.cs`
  - Add parser/repository summary tests for per-hero counts.

## Implementation Tasks

### Task 1: Fix Supporter Count at Four

**Files:**
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`
- Modify: `tests/LiveBuildPanel.Tests/Program.cs`

- [ ] **Step 1: Add a named constant**

Add near the other constants:

```csharp
private const int SupporterAttributionCount = 4;
```

- [ ] **Step 2: Use the constant on panel open**

Replace:

```csharp
_supporters = BPPSupporters.SampleMany(2);
```

with:

```csharp
_supporters = BPPSupporters.SampleMany(SupporterAttributionCount);
```

- [ ] **Step 3: Add a guard test**

In `tests/LiveBuildPanel.Tests/Program.cs`, add a test that reflects the constant and asserts it is `4`. This keeps future rail changes from accidentally reducing the visible supporter count while `BPPSupporterAttributionRow.Bind(...)` still caps at four.

- [ ] **Step 4: Run focused test**

Run:

```bash
dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
```

Expected: `LiveBuildPanel checks passed.`

### Task 2: Add Per-Hero Corpus Summary

**Files:**
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs`
- Modify: `tests/LiveBuildRecommendations.Tests/Program.cs`

- [ ] **Step 1: Add a pure summary row type**

Add near `TenWinCorpusSummary`:

```csharp
internal readonly struct TenWinHeroBuildCount
{
    public TenWinHeroBuildCount(string hero, int buildCount)
    {
        Hero = hero ?? string.Empty;
        BuildCount = buildCount;
    }

    public string Hero { get; }

    public int BuildCount { get; }
}
```

- [ ] **Step 2: Store hero counts on the corpus**

Add a property:

```csharp
public IReadOnlyList<TenWinHeroBuildCount> HeroBuildCounts { get; }
```

Populate it in the constructor from `_heroes`, sorted by descending count then hero name for stable display:

```csharp
HeroBuildCounts = heroes
    .Select(pair => new TenWinHeroBuildCount(pair.Key, pair.Value.Builds.Count))
    .OrderByDescending(pair => pair.BuildCount)
    .ThenBy(pair => pair.Hero, StringComparer.Ordinal)
    .ToArray();
```

- [ ] **Step 3: Extend `TenWinCorpusSummary`**

Change the constructor to accept the hero-count list and expose it as `IReadOnlyList<TenWinHeroBuildCount> HeroBuildCounts`.

- [ ] **Step 4: Return hero counts from repository summary**

Change `BuildRecommendationRepository.GetCorpusSummary()` so the non-null summary includes `corpus.HeroBuildCounts`.

- [ ] **Step 5: Add repository/parser tests**

Add a two-hero payload test in `tests/LiveBuildRecommendations.Tests/Program.cs` that parses a corpus and asserts:

- `HeroCount == 2`
- `BuildCount` equals both heroes' build rows combined
- `HeroBuildCounts` includes both hero names with exact counts
- ordering is stable

- [ ] **Step 6: Run focused test**

Run:

```bash
dotnet run --project tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
```

Expected: `LiveBuild recommendation checks passed.`

### Task 3: Split Refresh Status into Primary and Detail

**Files:**
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveBuildPanelSnapshot.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.Refresh.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.FontSample.cs`
- Modify: `tests/LiveBuildPanel.Tests/Program.cs`

- [ ] **Step 1: Add snapshot fields**

Add:

```csharp
public string BuildRefreshStatusDetailText { get; init; } = string.Empty;
```

Keep `BuildRefreshStatusText` as the primary line and keep `BuildRefreshStatusSeverity`.

- [ ] **Step 2: Track status detail in `LiveBuildPanel`**

Add:

```csharp
private string _buildRefreshStatusDetailText = string.Empty;
```

Change `SetBuildRefreshStatus(...)` to accept an optional detail string and clear severity to neutral only when both primary and detail are blank.

- [ ] **Step 3: Format summary detail from `TenWinCorpusSummary`**

Add a text helper that formats the detail line, for example:

```csharp
public static string FinalBuildRefreshDetail(TenWinCorpusSummary summary)
```

It should include generated time when present, total build count, total hero count, and the full `HeroBuildCounts` list. Keep `TenWinBuildCorpus` pure; any display naming belongs here.

- [ ] **Step 4: Use the detail on success**

After successful refresh, call `GetCorpusSummary()` once and set:

```csharp
SetBuildRefreshStatus(
    LiveBuildPanelText.FinalBuildRefreshSucceeded(),
    LiveBuildRefreshSeverity.Success,
    summary.HasValue ? LiveBuildPanelText.FinalBuildRefreshDetail(summary.Value) : string.Empty
);
```

- [ ] **Step 5: Keep failure/pending behavior compact**

Pending and failure may leave detail blank. Do not concatenate long error/details into the button text.

- [ ] **Step 6: Warm new glyphs**

Update `FontAtlasSample()` to include the new detail formatter with sample hero names and digits.

- [ ] **Step 7: Add text warm-up test**

Extend `TestRefreshFinalBuildsTextsAreAtlasWarmed()` to assert the new detail output is included in the sample.

### Task 4: Replace Full-Width Pull Button with a Fixed Data Strip

**Files:**
- Modify: `src/BazaarPlusPlus/Infrastructure/UiTokens/Sizes.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs`

- [ ] **Step 1: Add UI constants**

Add:

```csharp
public const float LiveBuildRefreshButtonWidth = 108f;
public const float LiveBuildRefreshButtonHeight = ButtonStandardHeight;
public const float LiveBuildRefreshStripHeight = 58f;
public const float LiveBuildRefreshDetailMaxHeight = 96f;
```

- [ ] **Step 2: Replace the refresh button block**

Remove the standalone full-width button/label block currently added after `_candidateCount`. Add a `VisualElement` strip with:

- fixed strip height: `height`, `minHeight`, and `maxHeight` all set to `Sizes.LiveBuildRefreshStripHeight`
- left `VisualElement` text column, `flexGrow = 1`, `flexShrink = 1`, `minWidth = 0`
- `_buildRefreshStatus` as primary label
- `_finalBuildRefreshButton` on the right with fixed width, fixed height, no grow, and no shrink
- new `_buildRefreshDetailStatus` as a separate panel below the strip

- [ ] **Step 3: Button sizing rules**

Set:

```csharp
_finalBuildRefreshButton.style.width = Sizes.LiveBuildRefreshButtonWidth;
_finalBuildRefreshButton.style.minWidth = Sizes.LiveBuildRefreshButtonWidth;
_finalBuildRefreshButton.style.maxWidth = Sizes.LiveBuildRefreshButtonWidth;
_finalBuildRefreshButton.style.height = Sizes.LiveBuildRefreshButtonHeight;
_finalBuildRefreshButton.style.minHeight = Sizes.LiveBuildRefreshButtonHeight;
_finalBuildRefreshButton.style.maxHeight = Sizes.LiveBuildRefreshButtonHeight;
_finalBuildRefreshButton.style.flexGrow = 0f;
_finalBuildRefreshButton.style.flexShrink = 0f;
```

- [ ] **Step 4: Status display rules**

Primary status display is hidden only when both primary and detail are blank. Detail can wrap and is compacted separately from primary. Tooltips should carry the un-compacted full text.

- [ ] **Step 5: Preserve preview behavior**

Keep `RefreshRailView()` as a UITK-only refresh path. Do not restart `_previewRenderer.Render(snapshot)` for status-only updates.

### Task 5: Make Empty States Less Noisy

**Files:**
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveBuildPanelSnapshot.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveItemBoardRowVm.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.Status.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.FontSample.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs`
- Modify: `tests/LiveBuildPanel.Tests/Program.cs`

- [ ] **Step 1: Add active-run state to snapshot**

Add:

```csharp
public bool HasActiveRun => Hero != null;
```

- [ ] **Step 2: Carry compact visible text and full tooltip**

Change `LiveItemBoardRowVm` to keep:

```csharp
public string EmptyText { get; }
public string EmptyTooltip { get; }
```

Constructor should accept both, with tooltip defaulting to visible text when not supplied.

- [ ] **Step 3: Keep empty copy tooltip-only**

Do not add a visible compact empty label. Keep existing `EmptyShop()`, `EmptyBoard()`,
and `EmptyStash()` as full tooltips only.

- [ ] **Step 4: Gate row empty text**

In `LiveBuildPanelSnapshot.Rows`, shop/board/stash rows should always use `string.Empty`
for visible empty text. When `HasActiveRun == true`, keep the row-specific tooltip.
When inactive, suppress both visible text and tooltip.

- [ ] **Step 5: Bind tooltip separately**

In `LiveBuildPanelView.RefreshRow(...)`, set visible label from `row.EmptyText` and tooltip from `row.EmptyTooltip`. If both are blank, hide or clear the empty label.

- [ ] **Step 6: Add tests**

Add tests that:

- no-run snapshots do not emit shop/board/stash empty text;
- active-run snapshots emit compact visible copy;
- row tooltips preserve the more specific shop/board/stash text.

### Task 6: Final Verification

**Files:**
- All modified files from previous tasks.

- [ ] **Step 1: Run focused executable tests**

```bash
dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
dotnet run --project tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
```

Expected:

- `LiveBuildPanel checks passed.`
- `LiveBuild recommendation checks passed.`
- `Supporters checks passed.`

- [ ] **Step 2: Run build**

```bash
./run.sh build
```

Expected: Debug build succeeds. If the game path is available, the build may auto-copy to `BepInEx/plugins/`.

- [ ] **Step 3: Check whitespace**

```bash
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 4: Manual runtime validation**

Launch through Steam, not direct app launch:

```bash
open "steam://run/1617400"
```

Open LiveBuildPanel with CapsLock and verify:

- four supporters can appear when enough supporter entries are available;
- pull button is fixed-size and does not dominate the rail;
- pending/success/failure status appears in the status strip, not as a loose label under a large button;
- success detail includes every hero's build count;
- no active run shows one no-run status instead of three row empty messages;
- active run empty shop/board/stash rows still communicate emptiness without long noisy copy.

## Risks

- Four supporter names may wrap more often in the 330 px rail. The existing attribution row already reserves height and caps at four, so the first implementation should use four before changing row height.
- Per-hero detail can become long if the corpus adds many heroes. Use tooltip for full text and compact visible text only if it becomes visually noisy.
- Do not place refresh status in a board row. Board-row text affects geometry callbacks and can cause unnecessary preview redraws.
- Do not add game references to `TenWinBuildCorpus`; it currently parses with System + Newtonsoft only, which keeps recommendation tests lightweight.

## Confirmation Needed

1. Fixed pull button size: start with `108f x 32f` unless you want a smaller icon-only action.
2. Supporter count: use four names, matching the current attribution-row cap.
3. Hero-count status: show raw hero keys like `Vanessa`, `Dooley`, `Pygmalien` unless you want short badges like `VAN 512`.

## Suggested Rule Additions

None yet. This plan is based on one UI polish request; it does not establish a repeated trap.
