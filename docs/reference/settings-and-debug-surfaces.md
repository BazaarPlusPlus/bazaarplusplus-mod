# Settings And Debug Surfaces

## Scope

This note covers active runtime surfaces that are easy to miss when reading only the larger
feature docs. These areas are still live code paths, but they are smaller than the main combat,
monster-preview, and run-logging docs.

## Gameplay Settings Toggles

`BppSettingsDockController` is the runtime entry point for Bazaar++ gameplay settings. It attaches
a custom dock button beside native settings buttons and renders Bazaar++ toggles from
`BppSettingsDockCatalog.Definitions`.

Current toggles:

- `Anonymous Mode` -> `EnableNameOverrideConfig`
- `Enchant Preview` -> `EnchantPreviewAlwaysShowConfig`
- `Combat Status Bar` -> `EnableCombatStatusBarConfig`
- `Use Native Monster Preview` -> `UseNativeMonsterPreviewConfig`

Current dock order:

- `NameOverride`
- `EnchantPreview`
- `CombatStatusBar`
- `NativeMonsterPreview`

`BppSettingsDockCatalog` is the source of truth for toggle ordering, labels, config bindings, and
per-setting side effects such as refreshing visible hero banners or reapplying monster-preview
mode.

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
- `MonsterPreviewDebugController`

Player-facing overlay entry points:

- `F8`: toggle `HistoryPanel`

Debug-only entry points:

- `F2`: toggle `DebugPanel`
- `DebugPanel -> Replays`: launch saved replay playback through `CombatReplayRuntime`
- `DebugPanel -> Preview`: inspect monster-preview anchor / presentation state from
  `MonsterPreviewDebugController`

## Key Files

- [Plugin.cs](../../Plugin.cs)
- [Game/Input/KeyBindings.cs](../../Game/Input/KeyBindings.cs)
- [Game/Input/BppHotkeyService.cs](../../Game/Input/BppHotkeyService.cs)
- [Game/Settings/BppSettingsDockController.cs](../../Game/Settings/BppSettingsDockController.cs)
- [Game/Settings/BppSettingsDockCatalog.cs](../../Game/Settings/BppSettingsDockCatalog.cs)
- [Game/Settings/BppSettingsDockDefinition.cs](../../Game/Settings/BppSettingsDockDefinition.cs)
- [Game/Settings/LocalizedTextSet.cs](../../Game/Settings/LocalizedTextSet.cs)
- [Patches/Settings/BppSettingsDockPatch.cs](../../Patches/Settings/BppSettingsDockPatch.cs)
- [Patches/Settings/SettingsMenuToggleInstaller.cs](../../Patches/Settings/SettingsMenuToggleInstaller.cs)
- [Patches/Settings/BppKeybindSettingsPatch.cs](../../Patches/Settings/BppKeybindSettingsPatch.cs)
- [Patches/NameOverride/NameOverridePatches.cs](../../Patches/NameOverride/NameOverridePatches.cs)
- [Game/Tooltips/TooltipModifierRefreshController.cs](../../Game/Tooltips/TooltipModifierRefreshController.cs)
- [Game/DebugPanel/DebugPanel.cs](../../Game/DebugPanel/DebugPanel.cs)
