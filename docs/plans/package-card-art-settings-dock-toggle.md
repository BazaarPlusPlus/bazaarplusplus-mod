---
status: active
calibrated: 2026-06-11
---

# Package Card Art Settings Dock Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent SettingsDock switch that enables or disables Bazaar++ package-card art replacement while preserving the current default behavior.

**Architecture:** Keep the existing custom-art catalog and bundled installer intact. Add a BepInEx config-backed policy, gate both card-art replacement Harmony postfixes through that policy, and expose the policy through the existing SettingsDock entry registry.

**Tech Stack:** C# 12, BepInEx `ConfigEntry<bool>`, Harmony postfixes, Unity card materials, xUnit test projects.

---

## Current Code Baseline

- Custom card art is embedded from `src/BazaarPlusPlus/Resources/CustomCardArt/*.jpg` with logical names under `BazaarPlusPlus.Resources.CustomCardArt.*` (`src/BazaarPlusPlus/BazaarPlusPlus.csproj:22-30`).
- Startup prepares the custom-art directory, installs missing bundled art, builds `CustomCardArtCatalog`, texture/material caches, and publishes `CardArtReplacementFeature.Current` (`src/BazaarPlusPlus/Game/CardArtReplacement/CardArtReplacementFeature.cs:29-70`).
- The bundled installer writes only missing resources and never overwrites an existing file (`src/BazaarPlusPlus/Game/CardArtReplacement/BundledCustomCardArtInstaller.cs:32-61`).
- Live in-game item visuals are changed in the `ItemVisualsController.SetCardFrameMaterial` postfix after resolving the current card and checking package identity (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:28-59`).
- CollectionPanel preview art is changed in the `CardPreviewItem.LoadArt` postfix, and only for CollectionPanel-owned previews (`src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:14-61`).
- Package identity is centralized as `PackageIdentity.IsPackage(card.HiddenTags)` through `CardArtInjector.IsPackageCard` / `IsPackageTemplate` (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:59-62`).
- Config follows the `IBppConfig` + `BppConfig.Initialize(ConfigFile)` pattern (`src/BazaarPlusPlus/Core/Config/IBppConfig.cs:7-25`, `src/BazaarPlusPlus/Core/Config/BppConfig.cs:33-89`).
- SettingsDock entries are feature-owned `ISettingsDockEntry` objects with stable `Order`; the registry materializes `(Order, Definition)` and the catalog sorts them (`src/BazaarPlusPlus/Game/Settings/ISettingsDockEntry.cs:11-18`, `src/BazaarPlusPlus/Game/Settings/SettingsDockEntryRegistry.cs:27-34`, `src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs:20-27`).
- SettingsDock toggle rows already use `SettingsMenuToggleBridge` and show dynamic ON/OFF status (`src/BazaarPlusPlus/Game/Settings/BppSettingsDockDefinition.cs:8-20`, `src/BazaarPlusPlus/Game/Settings/SettingsMenuToggleBridge.cs:12-32`, `src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs:315-352`).
- Existing SettingsDock orders use `0, 1, 2, 3, 5, 6, 7`, leaving `Order => 4` available between enchant preview and combat status bar (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelSettingsDockEntry.cs:9`, `src/BazaarPlusPlus/Game/NameOverride/NameOverrideSettingsDockEntry.cs:9`, `src/BazaarPlusPlus/Game/LegendaryPosition/LegendaryPositionSettingsDockEntry.cs:10`, `src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewSettingsDockEntry.cs:11`, `src/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBarSettingsDockEntry.cs:10`, `src/BazaarPlusPlus/Game/Settings/ChineseLocaleModeSettingsDockEntry.cs:19`, `src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadSettingsDockEntry.cs:9`).

## Decision

Yes, this should be a SettingsDock switch.

The switch should not delete files, skip bundled-resource installation, or mutate the on-disk `CustomCardArt` directory. Those paths are startup/catalog concerns and are intentionally idempotent. The correct product behavior is a runtime policy gate: if disabled, both art-replacement postfixes return before loading/applying custom package art.

Default must be `true` so existing users keep the current package-card replacement unless they explicitly opt out.

The visible SettingsDock row name should be `快递掉包` in Chinese. Use a concise English fallback such as `Package Swap`, but do not keep the old Chinese copy `包裹卡面`.

The first implementation should not force-refresh every currently visible card when the switch is turned off. The current logic mutates or swaps materials after native art loading (`ItemVisualsArtReplacePatch.cs:59`, `CardPreviewItemArtReplacePatch.cs:59-61`), so immediate rollback would require broader native refresh/reflection work. The acceptance target is: disabling the setting prevents replacement on new card material setup / preview load; reopening CollectionPanel or causing native card redraw shows original art. The SettingsDock may still refresh its own row state after a click; that is not a card/material refresh.

## File Structure

- Modify `src/BazaarPlusPlus/Core/Config/IBppConfig.cs`
  Add the config contract for the package-card art replacement setting.
- Modify `src/BazaarPlusPlus/Core/Config/BppConfig.cs`
  Bind the new `CardArtReplacement.EnablePackageArtReplacement` boolean with default `true`.
- Create `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementPolicy.cs`
  Single source of truth for reading/writing the setting and default fallback.
- Create `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsMenuLabel.cs`
  SettingsDock row label.
- Create `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsDockEntry.cs`
  Feature-owned SettingsDock contribution using `SettingsMenuToggleBridge`.
- Modify `src/BazaarPlusPlus/BppComposition.cs`
  Register the new SettingsDock entry.
- Modify `src/BazaarPlusPlus.Localization/Properties/AssemblyInfo.cs`
  Allow `SettingsDockRegistry.Tests` to install localization test providers for label assertions.
- Modify `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs`
  Gate live item art replacement before package/texture work.
- Modify `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs`
  Gate CollectionPanel preview art replacement before package/texture work.
- Modify `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs`
  Add policy tests for default-enabled and config read/write behavior.
- Modify `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs`
  Add a SettingsDock entry test for order, key, localized label, ON/OFF status, activation, and config reload persistence.

## Implementation Tasks

### Task 1: Add Config Contract And Policy

**Files:**
- Modify: `src/BazaarPlusPlus/Core/Config/IBppConfig.cs`
- Modify: `src/BazaarPlusPlus/Core/Config/BppConfig.cs`
- Create: `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementPolicy.cs`

- [ ] **Step 1: Add the interface property**

In `IBppConfig`, add this property after `BazaarDbUploadEnabled`:

```csharp
ConfigEntry<bool>? EnablePackageCardArtReplacementConfig { get; }
```

- [ ] **Step 2: Add the concrete config property**

In `BppConfig`, add:

```csharp
public ConfigEntry<bool>? EnablePackageCardArtReplacementConfig { get; private set; }
```

- [ ] **Step 3: Bind the config value**

In `BppConfig.Initialize`, after the `BazaarDbUploadEnabled` binding, add:

```csharp
EnablePackageCardArtReplacementConfig = config.Bind(
    "CardArtReplacement",
    "EnablePackageArtReplacement",
    true,
    "Whether BazaarPlusPlus should replace package card art with bundled custom package art."
);
```

- [ ] **Step 4: Add the policy helper**

Create `PackageCardArtReplacementPolicy.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal static class PackageCardArtReplacementPolicy
{
    internal const bool DefaultEnabled = true;

    internal static bool IsEnabled(IBppConfig? config) =>
        config?.EnablePackageCardArtReplacementConfig?.Value ?? DefaultEnabled;

    internal static void SetEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EnablePackageCardArtReplacementConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
```

- [ ] **Step 5: Run the focused compile check**

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: build succeeds; no missing interface implementation errors.

### Task 2: Gate Both Replacement Paths

**Files:**
- Modify: `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs`
- Modify: `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs`

- [ ] **Step 1: Gate live item visuals**

In `ItemVisualsArtReplacePatch.Postfix`, replace:

```csharp
_ = BppPatchHost.Services;
```

with:

```csharp
var services = BppPatchHost.Services;
if (!PackageCardArtReplacementPolicy.IsEnabled(services.Config))
    return;
```

Keep this before `TryResolveCard`, `IsPackageCard`, and `TryGetTexture` so the disabled path does not do package detection or texture loading.

- [ ] **Step 2: Gate CollectionPanel preview art**

In `CardPreviewItemArtReplacePatch.ApplyAfterLoad`, add this at the start of the `try` block, after the `instance` null check:

```csharp
var services = BppPatchHost.Services;
if (!PackageCardArtReplacementPolicy.IsEnabled(services.Config))
    return;
```

Keep it before `var card = instance._cardData;`.

- [ ] **Step 3: Run the focused compile check**

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: build succeeds.

### Task 3: Add The SettingsDock Row

**Files:**
- Create: `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsMenuLabel.cs`
- Create: `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsDockEntry.cs`
- Modify: `src/BazaarPlusPlus/BppComposition.cs`

- [ ] **Step 1: Add the localized row label**

Create `PackageCardArtReplacementSettingsMenuLabel.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal static class PackageCardArtReplacementSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new("Package Swap", "快递掉包");

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
```

- [ ] **Step 2: Add the SettingsDock entry**

Create `PackageCardArtReplacementSettingsDockEntry.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class PackageCardArtReplacementSettingsDockEntry : ISettingsDockEntry
{
    public int Order => 4;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "PackageCardArtReplacement",
            PackageCardArtReplacementSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => PackageCardArtReplacementPolicy.IsEnabled(config),
                enabled => PackageCardArtReplacementPolicy.SetEnabled(config, enabled)
            )
        );
}
```

- [ ] **Step 3: Register the entry**

In `BppComposition`, add this registration alongside the other `_settingsDockRegistry.Register(...)` calls:

```csharp
_settingsDockRegistry.Register(new PackageCardArtReplacementSettingsDockEntry());
```

The visible SettingsDock placement is controlled by `Order => 4`, not by registration source order. The existing `using BazaarPlusPlus.Game.CardArtReplacement;` is already present at the top of `BppComposition.cs`, so no new using should be required.

- [ ] **Step 4: Run the focused compile check**

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: build succeeds and the new entry compiles through the main project reference.

### Task 4: Add Automated Tests

**Files:**
- Modify: `src/BazaarPlusPlus.Localization/Properties/AssemblyInfo.cs`
- Modify: `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs`
- Modify: `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs`

- [ ] **Step 1: Add policy tests**

In `CardArtReplacementTests.cs`, add these `using` directives:

```csharp
using BazaarPlusPlus.Core.Config;
using BepInEx.Configuration;
```

Add these tests before `Dispose()`:

```csharp
[Fact]
public void Package_art_replacement_policy_defaults_enabled_when_config_is_missing()
{
    Assert.True(PackageCardArtReplacementPolicy.IsEnabled(null));
}

[Fact]
public void Package_art_replacement_policy_reads_and_writes_config()
{
    var configPath = Path.Combine(_tempDir, "BazaarPlusPlus.cfg");
    var configFile = new ConfigFile(configPath, saveOnInit: false);
    var config = new BppConfig();
    config.Initialize(configFile);

    Assert.True(PackageCardArtReplacementPolicy.IsEnabled(config));

    PackageCardArtReplacementPolicy.SetEnabled(config, false);

    Assert.False(PackageCardArtReplacementPolicy.IsEnabled(config));
}
```

- [ ] **Step 2: Add SettingsDock entry test**

In `src/BazaarPlusPlus.Localization/Properties/AssemblyInfo.cs`, add:

```csharp
[assembly: InternalsVisibleTo("SettingsDockRegistry.Tests")]
```

In `SettingsDockRegistryTests.cs`, add these `using` directives:

```csharp
using BazaarPlusPlus.Game.CardArtReplacement;
using BazaarPlusPlus.Localization;
using BepInEx.Configuration;
```

Add these test providers inside `SettingsDockRegistryTests`:

```csharp
private sealed class TestLanguageProvider : ILanguageProvider
{
    public string CurrentLanguageCode => "en";
}

private sealed class TestLocaleModeProvider : ILocaleModeProvider
{
    public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
}
```

Add this test after `MaterializeWithOrder_returns_entries_paired_with_their_Order`:

```csharp
[Fact]
public void PackageCardArtReplacementDockEntry_uses_order_four_and_toggles_config()
{
    var configPath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-package-art-settings-{Guid.NewGuid():N}.cfg"
    );
    try
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
        var configFile = new ConfigFile(configPath, saveOnInit: false);
        var config = new BppConfig();
        config.Initialize(configFile);
        var entry = new PackageCardArtReplacementSettingsDockEntry();

        var definition = entry.Build(config);

        Assert.Equal(4, entry.Order);
        Assert.Equal("PackageCardArtReplacement", definition.Key);
        Assert.Equal("Package Swap", definition.ResolveLabel("en"));
        Assert.Equal("快递掉包", definition.ResolveLabel("zh-CN"));
        Assert.True(definition.IsActive());
        Assert.Equal("ON", definition.ResolveStatus("en"));

        definition.Activate();

        Assert.False(config.EnablePackageCardArtReplacementConfig!.Value);
        Assert.False(definition.IsActive());
        Assert.Equal("OFF", definition.ResolveStatus("en"));
        Assert.False(definition.CollapseAfterActivate);

        configFile.Save();

        var reloadedConfigFile = new ConfigFile(configPath, saveOnInit: false);
        var reloadedConfig = new BppConfig();
        reloadedConfig.Initialize(reloadedConfigFile);

        Assert.False(PackageCardArtReplacementPolicy.IsEnabled(reloadedConfig));
    }
    finally
    {
        if (File.Exists(configPath))
            File.Delete(configPath);
    }
}
```

- [ ] **Step 3: Run focused tests**

Run:

```bash
dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj
```

Expected: both projects pass.

### Task 5: Full Verification And Runtime Check

**Files:**
- No additional source edits expected.

- [ ] **Step 1: Run architecture tests**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
```

Expected: pass. This confirms the new config and SettingsDock changes did not trip existing layering guards.

- [ ] **Step 2: Run the repo test wrapper**

Run:

```bash
./run.sh test
```

Expected: pass. If the wrapper reports `Failed test projects:`, inspect those exact project names before calling the change verified.

- [ ] **Step 3: Build the mod**

Run:

```bash
./run.sh build
```

Expected: Debug build succeeds and copies the plugin to the game plugin directory if the game install is detected.

- [ ] **Step 4: Runtime validation through Steam**

Launch through Steam, not the app directly:

```bash
open "steam://run/1617400"
```

Expected:

1. Open the in-game SettingsDock.
2. Confirm a `Package Swap` / `快递掉包` row appears between enchant preview and combat status bar.
3. With the row ON, open CollectionPanel package tab and confirm package cards use the bundled custom art.
4. Toggle the row OFF and confirm the SettingsDock row status changes immediately. Do not require currently visible card art to change before a native reload/redraw.
5. Close/reopen CollectionPanel and confirm package cards use native/original art.
6. Toggle the row ON again, close/reopen CollectionPanel, and confirm package cards use bundled custom art again.
7. Restart the game and confirm the last selected toggle state persists.
8. Read `<GameDir>/BepInEx/LogOutput.log` and confirm no `[BPP][CardArtReplacement] Postfix failed` or `Preview postfix failed` warnings appeared during the toggle checks.

## Non-Goals

- Do not remove, rename, or stop embedding `Resources/CustomCardArt/*.jpg`.
- Do not change `BundledCustomCardArtInstaller.InstallMissing`; installing missing bundled art remains harmless even when runtime replacement is disabled.
- Do not add a broad immediate-refresh system for currently visible card materials in this pass. If that becomes a requirement, implement it as a follow-up with explicit native card refresh evidence. The existing SettingsDock row refresh after activation is allowed because it only updates dock UI state.
- Do not change package detection. Keep `EHiddenTag.Package` as the identity source.

## Acceptance Criteria

- Fresh installs default to package-card art replacement ON.
- SettingsDock has a persistent toggle row labeled `Package Swap` / `快递掉包`, with stable key `PackageCardArtReplacement` and `Order => 4`.
- Turning the setting OFF prevents both live item-card and CollectionPanel preview replacement paths from applying custom package art on subsequent native load/setup.
- Toggling OFF immediately updates only the SettingsDock row status; it does not force-refresh already visible card materials.
- Turning the setting ON restores the existing behavior on subsequent native load/setup.
- The selected setting survives config reload / game restart.
- Focused tests, architecture tests, `./run.sh test`, and `./run.sh build` pass.
