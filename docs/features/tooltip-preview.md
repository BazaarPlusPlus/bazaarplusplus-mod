# Enchant & Upgrade Preview

hover 物品时，在原生 primary tooltip 之外按需展示**附魔预览**或**升级预览**。

> 怪物 / CardSet 预览是另一条独立路径，见 [monster-preview.md](monster-preview.md)，不属于本 feature。

## 附魔预览：3 态可视性 + pedestal 感知

附魔预览有独立的 `Off` / `AutoOnPedestalChoice` / `Always` 模式（默认 `AutoOnPedestalChoice`），由 `EnchantPreviewModeConfig` 持久化。选择 3 态而非旧的布尔 `AlwaysShow` 的理由见 [ADR-0004](../adr/0004-preview-visibility-three-state-mode.md)。

- **自动触发（pedestal-aware）**：在 `ChoiceState` 选择屏遇到附魔 pedestal 时，hover 物品自动展开附魔预览；非 pedestal 选项或非 ChoiceState 不会自动触发。pedestal 种类由 `Game/Encounter/ChoiceScreenPedestalResolver.cs` 从 `RunState.SelectionSet` 推导，配合 `Game/Encounter/EncounterStateProbe.cs`（缓存 + ChoiceState 检测）。「pedestal」术语见 [CONTEXT.md](../../CONTEXT.md)。
- **按 pedestal 类型过滤**：选择屏提供的附魔 pedestal 带一个具体附魔类型（`TPedestalBehaviorEnchant.Enchantment`）或一个随机池（`TPedestalBehaviorEnchantRandom`）。自动触发时，预览只展示这些被提供的类型；同屏可同时提供多个附魔 pedestal，类型取**并集**。手动 hotkey、`Always`、或不在 pedestal 选择屏时不带过滤，展示物品全部可附魔类型。
- **首启迁移**：旧的 `[EnchantPreview] AlwaysShow = true/false` 在首次启动时自动迁移为 `[EnchantPreview] Mode = Always / AutoOnPedestalChoice`，并从配置文件移除旧键。

## 升级预览：仅 Shift 热键

升级预览**没有可视性模式设置**，只在按住 `HoldUpgradePreview`（默认 `Shift`）时显示，且仅对可升级的 `ItemCard` 生效。早期曾有与附魔对称的 3 态模式，已移除——升级预览不是剧透、属于按需查询，自动触发那层没有价值；详见 [ADR-0004](../adr/0004-preview-visibility-three-state-mode.md) 的更新说明。

## 优先级与刷新

完整优先级：**hotkey（`Shift` 升级 / `Ctrl` 附魔）> 附魔 `Always` > 附魔 `AutoOnPedestalChoice` 匹配 > Normal**。一次只解析出一种预览模式。hover 期间按下 / 松开 modifier、或 SelectionSet 改变，都会即时刷新 tooltip（不等下一次 hover）。三处调用方（`TooltipModifierRefreshController`、`ItemEnchantPreviewPatch`、`UpgradePreviewTooltipPatch`）共享 `Game/Tooltips/TooltipPreviewModePolicy.cs` 的判定，保证行为一致。

热键默认 `Ctrl`（附魔）/ `Shift`（升级），均可改绑、支持鼠标按键——**默认值与改绑规则的唯一权威是 [hotkeys-reference.md](../reference/hotkeys-reference.md)**，本文不重复。

## 实现方式

Bazaar++ 不自造升级 tooltip UI，而是借用原生卡牌升级预览状态：正常 hover 显示主 tooltip → 检测 `HoldUpgradePreview` → 等原生 primary tooltip controller 就绪 → 隐藏当前 tooltip → `cardController.EnterUpgradePreview()` → 重建 `CardTooltipData` 并重新显示原生 tooltip。附魔预览受 `ItemEnchantPreviewService` 的 hand / stash / opponent-board 范围与战斗排除限制，并按上面的 pedestal 类型过滤。

## 关键文件

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`、`UpgradePreviewTooltipPatch.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`、`TooltipPreviewModePolicy.cs`（共享的优先级判定）
- `Game/Encounter/ChoiceScreenPedestalResolver.cs`、`EncounterStateProbe.cs`
- `Game/Input/BppHotkeyService.cs`、`Patches/Settings/BppKeybindSettingsPatch.cs`
