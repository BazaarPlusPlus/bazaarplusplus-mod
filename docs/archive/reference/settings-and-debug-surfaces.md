---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Settings And Debug Surfaces

## Gameplay Settings

`src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs` 负责把 Bazaar++ 设置按钮挂到原生 settings UI 上。条目在 `src/BazaarPlusPlus/BppComposition.cs` 注册为 `ISettingsDockEntry`；`src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs` 负责收集、排序与物化。

当前内容：

- `Game History` -> 大厅 Bazaar++ panel 内入口，打开 `HistoryPanel`
- `Anonymous Mode` -> `EnableNameOverrideConfig`
- `Legendary Position` -> `LegendaryPositionDisplayModeConfig`
- `Enchant Preview` -> `EnchantPreviewModeConfig`（3 态：Off / AutoOnPedestalChoice / Always，click 循环切换）
- `Combat Status Bar` -> `EnableCombatStatusBarConfig`
- `Upload screenshots to BazaarDB`(dock key `BazaarDbUpload`)-> `BazaarDbUploadEnabled`
- `Chinese Locale` -> `ChineseLocaleModeConfig`

## Tooltip Keybind Rows

`src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs` 会把两条 Bazaar++ modifier action 注入原生 keybind 设置界面：

- `HoldEnchantPreview`
- `HoldUpgradePreview`

行为由 `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs` 决定：

- 默认值分别为 `Ctrl` 和 `Shift`
- 支持 `LMB`、`RMB`、`MMB`、`BACK`、`FORWARD`
- Bazaar++ 内部冲突会在保存前被拒绝
- `src/BazaarPlusPlus/Game/Tooltips/TooltipModifierRefreshController.cs` 会在 hover 期间即时刷新 tooltip

## Anonymous Mode

`src/BazaarPlusPlus/Patches/NameOverride/NameOverridePatches.cs` 只在以下条件满足时把本地名称替换为 `Anonymous`：

- `EnableNameOverrideConfig` 已启用
- 能拿到当前本地 profile name
- 当前渲染的 banner 文本确实对应本地玩家

## Debug Surfaces

当前仓库没有单独的 `DebugPanel` runtime。可用的调试/开发入口主要是：

- `F8` 或 `Bazaar++ panel -> Game History`: 打开 `HistoryPanel`
- 运行日志中的 `BppLog` 分类输出

CollectionPanel 的大厅 dock 按钮不走 ISettingsDockEntry：由 `src/BazaarPlusPlus/Patches/Settings/BppSettingsDockPatch.cs` 克隆原生按钮注入；面板 Tab 热键硬编码于 `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`（keyboard.tabKey）。

## Key Files

- `src/BazaarPlusPlus/Plugin.cs`
- `src/BazaarPlusPlus/Game/Input/KeyBindings.cs`
- `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`
- `src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.cs`
- `src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs`
- `src/BazaarPlusPlus/BppComposition.cs`
- `src/BazaarPlusPlus/Patches/Settings/BppSettingsDockPatch.cs`
- `src/BazaarPlusPlus/Patches/Settings/SettingsMenuToggleInstaller.cs`
- `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs`
- `src/BazaarPlusPlus/Patches/NameOverride/NameOverridePatches.cs`
- `src/BazaarPlusPlus/Game/Tooltips/TooltipModifierRefreshController.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs`
