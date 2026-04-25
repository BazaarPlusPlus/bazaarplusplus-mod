# Settings And Debug Surfaces

## Gameplay Settings

`Game/Settings/BppSettingsDockController.cs` 负责把 Bazaar++ 设置按钮挂到原生 settings UI 上，具体定义来自 `Game/Settings/BppSettingsDockCatalog.cs`。

当前内容：

- `Game History` -> 大厅 Bazaar++ panel 内入口，打开 `HistoryPanel`
- `Anonymous Mode` -> `EnableNameOverrideConfig`
- `Legendary Position` -> `LegendaryPositionDisplayModeConfig`
- `Enchant Preview` -> `EnchantPreviewAlwaysShowConfig`
- `Combat Status Bar` -> `EnableCombatStatusBarConfig`
- `Chinese Locale` -> `ChineseLocaleModeConfig`

## Tooltip Keybind Rows

`Patches/Settings/BppKeybindSettingsPatch.cs` 会把两条 Bazaar++ modifier action 注入原生 keybind 设置界面：

- `HoldEnchantPreview`
- `HoldUpgradePreview`

行为由 `Game/Input/BppHotkeyService.cs` 决定：

- 默认值分别为 `Ctrl` 和 `Shift`
- 支持 `LMB`、`RMB`、`MMB`、`BACK`、`FORWARD`
- Bazaar++ 内部冲突会在保存前被拒绝
- `Game/Tooltips/TooltipModifierRefreshController.cs` 会在 hover 期间即时刷新 tooltip

## Anonymous Mode

`Patches/NameOverride/NameOverridePatches.cs` 只在以下条件满足时把本地名称替换为 `Anonymous`：

- `EnableNameOverrideConfig` 已启用
- 能拿到当前本地 profile name
- 当前渲染的 banner 文本确实对应本地玩家

## Debug Surfaces

当前仓库没有单独的 `DebugPanel` runtime。可用的调试/开发入口主要是：

- `F8` 或 `Bazaar++ panel -> Game History`: 打开 `HistoryPanel`
- `HistoryPanel` 打开且按住 `Ctrl` 时的 preview tuning 热键
- 运行日志中的 `BppLog` 分类输出

## Key Files

- `Plugin.cs`
- `Game/Input/KeyBindings.cs`
- `Game/Input/BppHotkeyService.cs`
- `Game/Settings/BppSettingsDockController.cs`
- `Game/Settings/BppSettingsDockCatalog.cs`
- `Patches/Settings/BppSettingsDockPatch.cs`
- `Patches/Settings/SettingsMenuToggleInstaller.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`
- `Patches/NameOverride/NameOverridePatches.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
