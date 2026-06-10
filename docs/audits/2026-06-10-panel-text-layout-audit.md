# History / Collection / Live Build Panel Text Layout Audit

Date: 2026-06-10

Scope:
- History panel
- Collection panel
- Live build panel ("十胜阵容")

Constraint: the game was not launched during this review, per request. Validation is code-level plus local build/test coverage.

## Review Method

I reviewed the UI Toolkit tree construction, refresh/binding paths, and shared size tokens for the three panels. The audit focused on text that can change at runtime: status banners, detail metadata, empty-state copy, filter chip labels, button labels, title/count labels, and preview/debug labels.

## Problems Found

### 1. History panel detail/status text could reflow the operation rail

Evidence before the fix:
- `HEAD:src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:193` assigned `model.StatusMessage` directly to the footer status label.
- `HEAD:src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:219-231` assigned detail metadata, snapshot text, placeholder text, and ghost-eliminated notice text directly.

Impact:
- Long server health/error messages could increase the footer banner height and push action buttons.
- Long detail metadata or snapshot strings could grow the selected-battle card, competing with the fixed footer and causing visible rail movement.
- Long ghost notice text could consume the bottom of the detail card and shift neighboring content.

Fix:
- Runtime text is now compacted before binding and the full value is kept in a tooltip: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:194-240`.
- Detail labels now have fixed max heights and hidden overflow: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:241-286`.
- The selected detail card and footer status label now hide overflow and cap status height: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:199-205`, `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:412-424`.

### 2. History panel preview/debug/opponent labels could visually overflow

Evidence before the fix:
- The row binding path exposed opponent names directly in row labels, while the detail opponent label depended on flex shrink rather than text compaction.
- Preview labels were absolute overlays and did not affect layout flow, but long text could still cover the preview area.

Impact:
- Long opponent names could consume horizontal space in rows or detail pills.
- Preview/debug copy could obscure the preview image instead of staying in a predictable overlay slot.

Fix:
- Battle-row opponent names are compacted with tooltips: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs:338-340`.
- Detail opponent and preview/debug labels now use no-wrap/overflow constraints or height caps: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:120-141`, `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:228-233`.
- Buttons now hide overflowing internal text and expose tooltips: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:264-273`.

### 3. Collection panel operation rail mixed unbounded controls and status in one vertical flow

Evidence before the fix:
- `HEAD:src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:70-198` placed primary controls, every filter section, and the status label directly in the rail.
- `HEAD:src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:194-198` gave the status label normal wrapping but no min/max height.

Impact:
- Wrapped filter chips, long status copy, or extra controls could make the whole rail compete with the card grid height.
- The status label could appear/disappear with variable height and shift the controls above it.
- Scrollbar behavior was coupled to the entire panel rather than the control area.

Fix:
- The rail is now a fixed-height column with hidden overflow, and controls live inside their own vertical `ScrollView`: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:31-84`.
- The status label is outside the controls scroll region and has a stable min/max height: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:210-219`.
- Filter sections no longer shrink and section titles are no-wrap/hidden: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:280-292`.

### 4. Collection panel chip/button/loading text lacked stable width and truncation rules

Evidence before the fix:
- `HEAD:src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:422-438` created tag chip labels with `flexShrink = 0` and assigned full native labels.
- `HEAD:src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:860-879` styled button text without no-wrap/overflow constraints.

Impact:
- Long tag labels could widen chips and change row wrapping, which changes filter-section height and scroll position.
- Localized button labels could overflow their fixed slots.
- Loading/status messages could occupy more visual space than intended.

Fix:
- Tag chip labels now have max width, no-wrap, hidden overflow, compacted text, and full-value tooltips: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:422-443`.
- Generic Collection buttons now hide overflowing internal text and use tooltips: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs:868-892`.
- Loading and status messages are compacted before binding: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:298-301`, `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:320-323`.
- Empty/loading overlays now hide overflow in their fixed viewport area: `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:394-421`.

### 5. Live build panel rail and row labels had unbounded runtime text

Evidence before the fix:
- `HEAD:src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:130-139` assigned refresh and recommendation status text directly.
- `HEAD:src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:235-242` allowed row title and empty text to wrap without a height cap.
- `HEAD:src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:289-346` built the rail without min-height/overflow containment or status height limits.

Impact:
- A long refresh failure or recommendation message could grow the right rail and push navigation/buttons.
- Empty-row copy could expand the fixed row label column, causing board-row height pressure.
- Long titles/count/button labels could overflow horizontally.

Fix:
- Refresh and recommendation status text is compacted with tooltips: `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:126-148`.
- Panel/board/rail containers now have min-height containment and hidden overflow where needed: `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:181-196`, `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:300-308`.
- Row title and empty text are constrained; empty text is compacted when shown: `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:236-255`, `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:393-404`.
- Rail title/count/status/button labels now use stable no-wrap/height/overflow rules: `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:316-365`, `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:581-614`.

## Shared Implementation

Added a small shared helper for text slots that must not resize their parent:
- `src/BazaarPlusPlus/Infrastructure/StablePanelText.cs:11-23` collapses whitespace and truncates to the caller's character budget.
- `src/BazaarPlusPlus/Infrastructure/StablePanelText.cs:26-50` normalizes multi-line or repeated whitespace to single spaces.

Added shared UI size caps:
- `src/BazaarPlusPlus/Infrastructure/UiTokens/Sizes.cs:46-53` defines max heights for panel status/detail/live-build text and a max width for tag facet chips.
- `src/BazaarPlusPlus/Infrastructure/UiTokens/Sizes.cs:25` increases the package-toggle width so its label fits the fixed rail better.

## Test Coverage

Added `UiFoundation` text-slot tests:
- Short text remains unchanged: `tests/UiFoundation.Tests/Program.cs:87-92`.
- Non-positive budgets hide the text instead of expanding the slot: `tests/UiFoundation.Tests/Program.cs:93-96`.
- Medium text collapses newlines/tabs/repeated spaces without truncation: `tests/UiFoundation.Tests/Program.cs:97-101`.
- Long diagnostic text is capped, keeps the actionable prefix, and advertises truncation: `tests/UiFoundation.Tests/Program.cs:103-116`.
- A 300-character unbroken string is capped to the caller budget: `tests/UiFoundation.Tests/Program.cs:118-122`.

Commands run:
- `dotnet run --project tests/UiFoundation.Tests/UiFoundation.Tests.csproj`
- `dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj`
- `dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj`
- `dotnet run --project tests/HistoryPanelPreview.Tests/HistoryPanelPreview.Tests.csproj`
- `dotnet run --project tests/CollectionGridLayout.Tests/CollectionGridLayout.Tests.csproj`
- `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `./run.sh test`

Result: all commands passed. The full test run still emits existing nullable/style warnings in unrelated test projects, but no test or build command failed.

## Final Verification Summary

- Short, medium, long, and unbroken text scenarios are covered by the new `StablePanelText` tests.
- All three panels now apply one or both of these protections to dynamic text slots: content compaction with tooltip, and UI Toolkit max-size/overflow containment.
- Collection panel controls can scroll independently from the fixed bottom status label, preventing filter growth from moving the status/actions.
- History and live-build status/detail text now stays within stable slots instead of resizing the rail or rows.
- Runtime in-game visual validation was intentionally not performed because the game must not be launched in this task.
