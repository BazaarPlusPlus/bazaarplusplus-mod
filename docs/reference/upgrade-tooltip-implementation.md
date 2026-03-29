# Upgrade Tooltip Implementation

## Current Behavior

- 普通 hover 仍显示原生 primary tooltip。
- `EnchantPreviewAlwaysShow` 开启时，或按住附魔预览热键时，会在 tooltip 上追加附魔预览文本。
- 按住升级预览热键时，会复用游戏原生 upgrade preview 路径刷新 tooltip。
- hover 期间按下或松开 modifier key，会立即刷新 tooltip。
- 优先级是 `upgrade > enchant`。
- 升级预览仅对 `ItemCard` 生效。

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
- `Game/Input/BppHotkeyService.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`

## 当前要点

- 默认热键仍是 `Ctrl` / `Shift`
- 两个 modifier action 都可以在 settings 中改绑
- 支持鼠标按键绑定
- 切换 modifier 时 tooltip 会即时刷新，而不是等下一次 hover
