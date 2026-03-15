# Name Override Native Settings Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a native in-game settings toggle for BazaarPlusPlus name override / anonymous mode.

**Architecture:** Mirror the existing Combat Status Bar native settings injection pattern. Add a small bridge and localized label helper for name override, then inject a cloned toggle into `OptionsDialogController` that reads and writes `ModState.EnableNameOverrideConfig`.

**Tech Stack:** C#, Harmony, Unity UI, xUnit

---

### Task 1: Add failing bridge and label tests

**Files:**
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add tests that expect:
- `NameOverrideSettingsMenuBridge` reads the initial value and writes updated values
- `NameOverrideSettingsMenuLabel.Resolve(...)` returns `匿名模式` for `zh-Hans` / `zh-CN`
- `NameOverrideSettingsMenuLabel.Resolve(...)` returns `Anonymous Mode` for other languages

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj --filter NameOverride`

Expected: FAIL because the new settings bridge/label files and types do not exist yet.

### Task 2: Implement native settings helpers

**Files:**
- Create: `Game/NameOverride/NameOverride.SettingsMenuBridge.cs`
- Create: `Game/NameOverride/NameOverride.SettingsMenuLabel.cs`

**Step 1: Write minimal implementation**

Add:
- a small delegate-backed bridge with `GetInitialValue()` and `ApplyValue(bool)`
- a localized label helper returning simplified Chinese only for simplified Chinese locales

**Step 2: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj --filter NameOverride`

Expected: PASS

### Task 3: Inject the toggle into the native options menu

**Files:**
- Create: `Patches/NameOverride/NameOverrideSettingsPatch.cs`

**Step 1: Write the integration code**

Follow the existing `CombatStatusBarSettingsPatch` structure:
- clone the anchor toggle from `OptionsDialogController`
- rename the object to a stable BPP-specific name
- set the label text using `NameOverrideSettingsMenuLabel.Resolve(...)`
- sync the current value from `ModState.EnableNameOverrideConfig`
- update the config when the user changes the toggle
- patch both `Awake` and `OnEnable`

**Step 2: Run focused tests**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS

### Task 4: Verify the touched area

**Files:**
- Verify only

**Step 1: Run fresh verification**

Run:
- `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS with all tests green.
