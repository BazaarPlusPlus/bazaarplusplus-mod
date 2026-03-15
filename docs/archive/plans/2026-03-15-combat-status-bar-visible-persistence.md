# Combat Status Bar Visible Persistence Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Persist the `F6` combat status bar visibility toggle in config so the last chosen visible state survives restart.

**Architecture:** Keep `CombatStatusBar.Enabled` as the feature gate and add a separate `CombatStatusBar.Visible` runtime-persisted state. Move the visibility flag into the shared `CombatStatusBar` state layer so the runtime component and config layer both read and write the same source of truth, then update the installer config template so it preserves the new key.

**Tech Stack:** C#, xUnit, BepInEx config, Rust

---

### Task 1: Add failing C# tests for persistent overlay visibility state

**Files:**
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`
- Modify: `tests/CombatStatusBarState.Tests/TestCombatStatusBarShims.cs`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 1: Write the failing test**

Add tests that verify:
- overlay visibility defaults to `true`
- toggling visibility flips the state
- setting visibility persists through the partial persistence hook

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: FAIL because the visibility state API does not exist yet.

**Step 3: Write minimal implementation**

Add shared visibility state and persistence hook to `Game/CombatStatusBar/CombatStatusBar.State.cs`.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: PASS

### Task 2: Wire visibility persistence into runtime config and F6 handling

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Modify: `Game/CombatStatusBar/CombatStatusBar.Config.cs`

**Step 1: Write the failing test**

Use the failing tests from Task 1 as the red state for the shared visibility API.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: FAIL until runtime and config are wired to the new API.

**Step 3: Write minimal implementation**

Bind a new `CombatStatusBar.Visible` config entry, initialize shared visibility state from config, persist updates via the partial method, and make `F6` call the shared toggle method.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: PASS

### Task 3: Preserve the new config key in installer config read/write paths

**Files:**
- Modify: `bppinstaller/src-tauri/src/commands/config.rs`

**Step 1: Write the failing test**

Extend the Rust config command tests so they expect `CombatStatusBar.Visible` to exist in default and parsed config maps.

**Step 2: Run test to verify it fails**

Run: `cargo test --manifest-path bppinstaller/src-tauri/Cargo.toml read_mod_config_reports_missing_config_file -- --exact`
Run: `cargo test --manifest-path bppinstaller/src-tauri/Cargo.toml read_mod_config_reports_existing_config_file -- --exact`
Expected: FAIL because the new key is not present yet.

**Step 3: Write minimal implementation**

Add `CombatStatusBar.Visible` to the default config map and emitted config template.

**Step 4: Run test to verify it passes**

Run: `cargo test --manifest-path bppinstaller/src-tauri/Cargo.toml read_mod_config_reports_missing_config_file -- --exact`
Run: `cargo test --manifest-path bppinstaller/src-tauri/Cargo.toml read_mod_config_reports_existing_config_file -- --exact`
Expected: PASS

### Task 4: Final verification

**Files:**
- Verify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Verify: `Game/CombatStatusBar/CombatStatusBar.State.cs`
- Verify: `Game/CombatStatusBar/CombatStatusBar.Config.cs`
- Verify: `bppinstaller/src-tauri/src/commands/config.rs`
- Verify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Run targeted verification**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Run: `cargo test --manifest-path bppinstaller/src-tauri/Cargo.toml read_mod_config_reports_missing_config_file -- --exact`
Run: `cargo test --manifest-path bppinstaller/src-tauri/Cargo.toml read_mod_config_reports_existing_config_file -- --exact`
Expected: PASS

**Step 2: Commit**

```bash
git add docs/plans/2026-03-15-combat-status-bar-visible-persistence.md \
  tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs \
  tests/CombatStatusBarState.Tests/TestCombatStatusBarShims.cs \
  Game/CombatStatusBar/CombatStatusBar.cs \
  Game/CombatStatusBar/CombatStatusBar.State.cs \
  Game/CombatStatusBar/CombatStatusBar.Config.cs \
  bppinstaller/src-tauri/src/commands/config.rs
git commit -m "feat: persist combat status bar visibility"
```
