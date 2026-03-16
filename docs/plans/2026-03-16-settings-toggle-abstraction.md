# Settings Toggle Abstraction Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace duplicated settings-toggle patch logic with a shared installer and add `EnchantPreviewAlwaysShow` to the gameplay settings menu alongside the existing Bazaar++ toggles.

**Architecture:** Introduce a small shared settings-toggle definition/installer layer that owns anchor lookup, cloned row setup, label binding, listener wiring, and row placement. Migrate `CombatStatusBar` and `NameOverride` to this layer first, then add a third definition for `EnchantPreviewAlwaysShow` so all Bazaar++ gameplay toggles use one code path and one refresh hook.

**Tech Stack:** C# 12, Harmony, Unity UI (`Toggle`, `RectTransform`, `LayoutRebuilder`), BepInEx config entries, console-style source/behavior tests

---

### Task 1: Add failing tests for the shared toggle abstraction and enchant preview bridge

**Files:**
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 1: Write the failing tests**

Add tests that cover:

- a shared bridge can read the initial value, write back changes, and optionally invoke `OnChanged`
- an enchant-preview bridge binds to `ModState.EnchantPreviewAlwaysShowConfig` semantics
- the source tree contains an `EnchantPreview` settings patch and label resolver

```csharp
[Fact]
public void SharedSettingsMenuBridge_ReadsInitialValue_WritesBackChanges_AndInvokesOnChanged()
{
    var enabled = false;
    var changes = 0;
    var bridge = new SettingsMenuToggleBridge(() => enabled, value => enabled = value, _ => changes++);

    Assert.False(bridge.GetInitialValue());

    bridge.ApplyValue(true);

    Assert.True(enabled);
    Assert.Equal(1, changes);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: FAIL because the shared bridge and enchant-preview settings files do not exist yet.

**Step 3: Commit the failing tests**

```bash
git add tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj
git commit -m "test: cover shared settings toggle bridge"
```

### Task 2: Implement the shared settings toggle bridge and definitions

**Files:**
- Create: `Game/Settings/SettingsMenuToggleBridge.cs`
- Create: `Game/Settings/SettingsMenuToggleDefinition.cs`
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 1: Write minimal implementation**

Create a reusable bridge:

```csharp
internal sealed class SettingsMenuToggleBridge
{
    private readonly Func<bool> _readValue;
    private readonly Action<bool> _writeValue;
    private readonly Action<bool>? _onChanged;

    internal SettingsMenuToggleBridge(
        Func<bool> readValue,
        Action<bool> writeValue,
        Action<bool>? onChanged = null
    )
    {
        _readValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        _writeValue = writeValue ?? throw new ArgumentNullException(nameof(writeValue));
        _onChanged = onChanged;
    }

    internal bool GetInitialValue() => _readValue();

    internal void ApplyValue(bool value)
    {
        _writeValue(value);
        _onChanged?.Invoke(value);
    }
}
```

And a definition type that holds:

- `ToggleObjectName`
- `ResolveLabel`
- `Bridge`

**Step 2: Run tests**

Run: `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: still FAIL until the old feature-specific bridges are migrated or wrapped.

**Step 3: Commit**

```bash
git add Game/Settings/SettingsMenuToggleBridge.cs Game/Settings/SettingsMenuToggleDefinition.cs tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj
git commit -m "refactor: add shared settings toggle bridge"
```

### Task 3: Refactor settings patches to use the shared installer

**Files:**
- Create: `Patches/Settings/SettingsMenuToggleInstaller.cs`
- Modify: `Patches/Combat/CombatStatusBarSettingsPatch.cs`
- Modify: `Patches/NameOverride/NameOverrideSettingsPatch.cs`
- Modify: `Game/CombatStatusBar/CombatStatusBar.SettingsMenuBridge.cs`
- Modify: `Game/NameOverride/NameOverride.SettingsMenuBridge.cs`
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 1: Write the failing tests**

Add tests that verify the feature-specific bridge files become thin wrappers or are removed in favor of the shared bridge.

```csharp
Assert.Contains("SettingsMenuToggleBridge", source);
Assert.DoesNotContain("_refreshUi?.Invoke();", nameOverrideSource);
```

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: FAIL because the patches still duplicate installer logic.

**Step 3: Implement the shared installer**

Move the common flow into `SettingsMenuToggleInstaller`:

- find anchor toggle via field name or fallback name probe
- get anchor row
- find/create cloned row
- set label text
- sync toggle state
- bind listener
- place row with `SettingsMenuLayoutUtility`

Feature patches should reduce to:

```csharp
private static readonly SettingsMenuToggleDefinition Definition = new(
    "BPP_CombatStatusBarToggle",
    languageCode => CombatStatusBarSettingsMenuLabel.Resolve(languageCode),
    new SettingsMenuToggleBridge(
        CombatStatusBar.GetEnabledSettingValue,
        CombatStatusBar.SetEnabledSettingValue
    )
);
```

**Step 4: Run tests**

Run: `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS for the shared bridge/installer assertions.

**Step 5: Commit**

```bash
git add Patches/Settings/SettingsMenuToggleInstaller.cs Patches/Combat/CombatStatusBarSettingsPatch.cs Patches/NameOverride/NameOverrideSettingsPatch.cs Game/CombatStatusBar/CombatStatusBar.SettingsMenuBridge.cs Game/NameOverride/NameOverride.SettingsMenuBridge.cs tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj
git commit -m "refactor: share gameplay settings toggle installer"
```

### Task 4: Add `EnchantPreviewAlwaysShow` to gameplay settings

**Files:**
- Create: `Game/ItemEnchantPreview/EnchantPreview.SettingsMenuLabel.cs`
- Create: `Patches/Tooltips/EnchantPreviewSettingsPatch.cs`
- Modify: `Patches/Combat/CombatStatusBarSettingsPatch.cs`
- Modify: `tests/ItemEnchantPreview.Tests/Program.cs`
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing tests**

Add assertions that:

- an enchant-preview settings label file exists
- a gameplay settings patch exists
- the patch binds to `ModState.EnchantPreviewAlwaysShowConfig`
- gameplay menu refresh now installs all three Bazaar++ toggles

```csharp
Assert.Contains("EnchantPreviewAlwaysShowConfig", patchSource);
Assert.Contains("BPP_EnchantPreviewToggle", patchSource);
Assert.Contains("EnchantPreviewSettingsAwakePatch.EnsureToggleExists(__instance);", combatPatchSource);
```

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Run: `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: FAIL because the new settings files and gameplay refresh hook do not exist yet.

**Step 3: Write minimal implementation**

Create the label resolver:

```csharp
internal static class EnchantPreviewSettingsMenuLabel
{
    private const string EnglishLabel = "Enchant Preview Always Show";
    private const string SimplifiedChineseLabel = "附魔预览始终显示";
}
```

Create the patch using the shared installer/definition and wire it into the gameplay-open refresh alongside the other two toggles.

`ReadValue`/`WriteValue` should map directly to:

```csharp
var entry = ModState.EnchantPreviewAlwaysShowConfig;
return entry != null && entry.Value;
```

**Step 4: Run tests**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Run: `dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS with the new settings toggle wired in.

**Step 5: Commit**

```bash
git add Game/ItemEnchantPreview/EnchantPreview.SettingsMenuLabel.cs Patches/Tooltips/EnchantPreviewSettingsPatch.cs Patches/Combat/CombatStatusBarSettingsPatch.cs tests/ItemEnchantPreview.Tests/Program.cs tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs
git commit -m "feat: add enchant preview gameplay settings toggle"
```

### Task 5: Run focused and full verification

**Files:**
- No code changes required

**Step 1: Run focused tests**

Run:

```bash
dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj
dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj
```

Expected: PASS

**Step 2: Run broader regression suite**

Run:

```bash
dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj
dotnet run --project tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj
dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj
dotnet build BazaarPlusPlus.csproj
```

Expected: PASS

**Step 3: Commit verification-only changes if needed**

If no files changed, skip commit. Otherwise:

```bash
git add <files>
git commit -m "test: verify shared gameplay settings toggles"
```
