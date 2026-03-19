# Combat Log Display Names And Filters Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make Combat Log rows prefer readable card names over raw instance IDs and split the current coarse state filter into separate combatant and card filters.

**Architecture:** Extend the Combat Log runtime model with card display metadata, resolve display names from live card objects when possible, and let the formatter emit friendlier primary/secondary text. Keep the playback-state logic intact, but refine panel filtering and row rendering so the panel stays useful during first play and replay.

**Tech Stack:** C#, Unity IMGUI/uGUI, xUnit-style local test programs, Bazaar runtime data APIs

---

### Task 1: Add failing tests for display names and split filters

**Files:**
- Modify: `tests/CombatLogRuntime.Tests/Program.cs`

**Step 1: Write the failing test**

Add assertions that:
- a card update row prefers a resolved display name instead of `Card <instanceId>`
- a card secondary text retains the short identifier context
- combatant-only filtering hides card rows but keeps player/opponent health and attribute rows
- card-only filtering hides player/opponent state rows but keeps card rows

**Step 2: Run test to verify it fails**

Run: `dotnet run --project /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`
Expected: FAIL because Combat Log rows still render raw instance IDs and the panel state has only one combined state filter.

**Step 3: Write minimal implementation**

Implement the smallest set of model/runtime/panel changes needed for those assertions.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`
Expected: PASS

### Task 2: Add display metadata to Combat Log models

**Files:**
- Modify: `Game/CombatLog/CombatLogModels.cs`

**Step 1: Write the failing test**

Covered by Task 1.

**Step 2: Run test to verify it fails**

Covered by Task 1.

**Step 3: Write minimal implementation**

Add a small display metadata type for combat log card references and extend:
- `CombatLogEventEntry`
- `CombatLogCardUpdateEntry`
- `CombatLogRow`

Keep the row model simple: primary text for the main label, optional secondary text for IDs/debug context.

**Step 4: Run test to verify it passes**

Run the Combat Log runtime tests.

### Task 3: Resolve display names in CombatLogRuntime

**Files:**
- Modify: `Game/CombatLog/CombatLogRuntime.cs`
- Reference: `Game/GameDataReader.cs`

**Step 1: Write the failing test**

Covered by Task 1.

**Step 2: Run test to verify it fails**

Covered by Task 1.

**Step 3: Write minimal implementation**

Build a live lookup from current player/opponent cards and skills keyed by instance ID. Resolve display names with this fallback order:
- localized title
- internal name
- short template ID
- short instance ID

When runtime data is unavailable, keep deterministic fallbacks so tests stay stable.

**Step 4: Run test to verify it passes**

Run the Combat Log runtime tests.

### Task 4: Emit friendlier row text and finer-grained filters

**Files:**
- Modify: `Game/CombatLog/CombatLogFormatter.cs`
- Modify: `Game/CombatLog/CombatLogPanelState.cs`
- Modify: `Game/CombatLog/CombatLogPanel.cs`

**Step 1: Write the failing test**

Covered by Task 1.

**Step 2: Run test to verify it fails**

Covered by Task 1.

**Step 3: Write minimal implementation**

Change formatter output to use display names in primary text and ID/debug context in secondary text. Split the old `State` filter into:
- `Combatants`
- `Cards`

Update panel buttons and row drawing so secondary text is rendered in a muted style under the primary line.

**Step 4: Run test to verify it passes**

Run the Combat Log runtime tests.

### Task 5: Verify adjacent behavior

**Files:**
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs` if needed

**Step 1: Run focused verification**

Run:
- `dotnet run --project /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`
- `dotnet run --project /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS

**Step 2: Refine only if verification reveals coupling**

If Combat Log playback/filter changes break shared playback-state assertions, make the smallest fix and re-run both test suites.
