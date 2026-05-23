# Upgrade Tooltip Implementation

## Current Behavior

- 普通 hover 仍显示原生 primary tooltip。
- 附魔与升级各有独立的 3 态可视性模式（`Off` / `AutoOnPedestalChoice` / `Always`，默认 Auto），由 `EnchantPreviewModeConfig` / `UpgradePreviewModeConfig` 持久化。
- 在 `ChoiceState` 选择屏遇到对应种类 pedestal 时，对应预览会自动展开；非 pedestal 选项或非 ChoiceState 不会自动触发。
- 按住 `HoldEnchantPreview`（默认 Ctrl）/ `HoldUpgradePreview`（默认 Shift）总是显示对应预览，优先级最高，覆盖所有模式。
- hover 期间按下或松开 modifier key、或选择屏 SelectionSet 改变，都会立即刷新 tooltip。
- 优先级：hotkey > Always > AutoOnPedestalChoice 匹配 > Normal；两个模式同时 `Always` 时升级胜出（详见 `Game/Tooltips/TooltipPreviewModePolicy.cs`）。
- 升级预览仅对 `ItemCard` 生效；附魔预览仍受 `ItemEnchantPreviewService` 的 hand / stash / opponent-board 范围与战斗排除限制。

## 实现方式

Bazaar++ 没有自己造一套升级 tooltip UI，而是借用原生卡牌升级预览状态：

1. 正常 hover 显示主 tooltip。
2. 运行时检测 `HoldUpgradePreview`。
3. 等待原生 primary tooltip controller 就绪。
4. 隐藏当前 tooltip。
5. 调用 `cardController.EnterUpgradePreview()`。
6. 重建 `CardTooltipData` 并重新显示原生 tooltip。

## 关键挂点

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`
- `Game/Tooltips/TooltipPreviewModePolicy.cs`（三方共享的优先级判定）
- `Game/Encounter/ChoiceScreenPedestalResolver.cs`（SelectionSet → pedestal kind）
- `Game/Encounter/EncounterStateProbe.cs`（缓存 + ChoiceState 检测）
- `Game/Input/BppHotkeyService.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`

## 当前要点

- 默认热键仍是 `Ctrl` / `Shift`
- 两个 modifier action 都可以在 settings 中改绑
- 支持鼠标按键绑定
- 切换 modifier 时 tooltip 会即时刷新，而不是等下一次 hover
