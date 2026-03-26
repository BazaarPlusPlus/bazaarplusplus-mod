# Settings And Debug Surfaces

## Scope

This note covers active runtime surfaces that are easy to miss when reading only the larger
feature docs. These areas are still live code paths, but they are smaller than the main combat,
monster-preview, and run-logging docs.

## Gameplay Settings Toggles

`BppGameplaySettingsCoordinator.EnsureAll(...)` is the entry point that keeps Bazaar++ gameplay
settings rows installed and synchronized inside `OptionsDialogController`.

Current toggles:

- `Anonymous Mode` -> `EnableNameOverrideConfig`
- `Enchant Preview` -> `EnchantPreviewAlwaysShowConfig`
- `Combat Status Bar` -> `EnableCombatStatusBarConfig`

Current row order:

- `BPP_NameOverrideToggle`
- `BPP_EnchantPreviewToggle`
- `BPP_CombatStatusBarToggle`

`SettingsMenuToggleInstaller` clones a native gameplay toggle row, rewrites the label, and binds
the toggle state back into `BppConfig`.

## Tooltip Keybind Rows

`BppKeybindSettingsPatch` clones native keybind rows into the settings UI for two Bazaar++
modifier actions:

- `HoldEnchantPreview`
- `HoldUpgradePreview`

`BppHotkeyService` is the runtime source of truth for these bindings.

Current behavior:

- defaults stay `Ctrl` for enchant preview and `Shift` for upgrade preview
- supported mouse bindings include `LMB`, `RMB`, `MMB`, `BACK`, and `FORWARD`
- conflicting Bazaar++ actions are rejected before the new binding is stored
- `TooltipModifierRefreshController` refreshes the hovered item tooltip when the modifier mode
  changes during hover

## Anonymous Mode

`NameOverrideHelper` replaces the local profile name with `Anonymous` only when:

- `EnableNameOverrideConfig` is enabled
- `Data.Profile.Username` is available
- the banner text being rendered matches the local profile name

The runtime patch points are `HeroBannerController.UpdatePlayer(...)` and
`HeroBannerController.SetHeroName(...)`. Toggling the setting also triggers a best-effort refresh
of visible hero banners through `NameOverrideUiRefresh`.

## Debug-Only Surfaces

When `BppBuild.IsDebug` is true, `Plugin.Awake()` adds:

- `DebugPanel`
- `CombatLogController`
- `CombatLogOverlay`
- `MonsterPreviewDebugController`

Key debug entry points:

- `F2`: toggle `DebugPanel`
- `F7`: toggle standalone `CombatLogOverlay`
- `DebugPanel -> Replays`: launch saved replay playback through `CombatReplayRuntime`
- `DebugPanel -> Preview`: inspect monster-preview anchor / presentation state from
  `MonsterPreviewDebugController`

`CombatLogOverlay` renders the current combat timeline and uses
`CombatStatusBar.ProcessedCombatFrames` to decide which rows are visible during playback.

## Key Files

- [Plugin.cs](../../Plugin.cs)
- [Game/Input/KeyBindings.cs](../../Game/Input/KeyBindings.cs)
- [Game/Input/BppHotkeyService.cs](../../Game/Input/BppHotkeyService.cs)
- [Game/Settings/SettingsMenuToggleDefinition.cs](../../Game/Settings/SettingsMenuToggleDefinition.cs)
- [Patches/Settings/SettingsMenuToggleInstaller.cs](../../Patches/Settings/SettingsMenuToggleInstaller.cs)
- [Patches/Settings/BppGameplaySettingsCoordinator.cs](../../Patches/Settings/BppGameplaySettingsCoordinator.cs)
- [Patches/Settings/BppKeybindSettingsPatch.cs](../../Patches/Settings/BppKeybindSettingsPatch.cs)
- [Patches/NameOverride/NameOverridePatches.cs](../../Patches/NameOverride/NameOverridePatches.cs)
- [Patches/NameOverride/NameOverrideSettingsPatch.cs](../../Patches/NameOverride/NameOverrideSettingsPatch.cs)
- [Patches/Tooltips/EnchantPreviewSettingsPatch.cs](../../Patches/Tooltips/EnchantPreviewSettingsPatch.cs)
- [Game/Tooltips/TooltipModifierRefreshController.cs](../../Game/Tooltips/TooltipModifierRefreshController.cs)
- [Game/DebugPanel/DebugPanel.cs](../../Game/DebugPanel/DebugPanel.cs)
- [Game/CombatLog/CombatLogController.cs](../../Game/CombatLog/CombatLogController.cs)
- [Game/CombatLog/CombatLogOverlay.cs](../../Game/CombatLog/CombatLogOverlay.cs)
