# BazaarPlusPlus Native Settings Keybind Design

## Summary

目标是在 The Bazaar 原生设置菜单中展示 BazaarPlusPlus 自己的快捷键配置项，并提供与原生改键体验一致的交互：

- 在原生设置页中出现 BazaarPlusPlus 的快捷键行
- 用户可点击行进入 rebind 状态
- 支持冲突检测、恢复默认、显示当前绑定
- 绑定结果由 BazaarPlusPlus 自己持久化和消费

本设计明确复用原生设置菜单的 UI 位置与视觉样式，但不复用原生 `InputManager.Actions` 和 `PlayerPreferences.Data.KeyBindings` 作为后端。

## Current State

当前 BazaarPlusPlus 的快捷键实现是硬编码轮询：

- `Game/Input/KeyBindings.cs`
- `Game/CombatStatusBar/CombatStatusBar.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`

原生游戏的改键系统则是：

- `decompiled/TheBazaarRuntime/TheBazaar.UI/KeyBindController.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs`
- `decompiled/TheBazaarRuntime/InputSystemActions.cs`
- `decompiled/TheBazaarRuntime/PlayerPreferences.cs`

问题在于原生 `KeyBindController` 强依赖原生 `InputManager.Actions` 枚举和 `_mappedActions` 字典，而该枚举只覆盖少量内建动作，无法稳定承载 BazaarPlusPlus 新增功能。

## Decision

采用以下边界：

- 复用原生设置菜单容器与键位行样式
- 自行实现 BazaarPlusPlus 的快捷键注册、保存、读取与运行时查询
- BazaarPlusPlus 设置项通过 Harmony patch 注入 `OptionsDialogController`
- BazaarPlusPlus 快捷键值不写入原生 `PlayerPreferences.Data.KeyBindings`

不采用以下方案：

- 不复用原生 `KeyBindController` 直接驱动 BazaarPlusPlus 动作
- 不 patch 原生 `InputManager` 的内部 `_mappedActions`
- 不占用原生 `Playlist` / `Atlas` 等动作槽位伪装成 BazaarPlusPlus 功能

## User-Facing Scope

首批支持 3 个 BazaarPlusPlus 动作：

- `ToggleCombatStatusBar`
- `HoldEnchantPreview`
- `HoldUpgradePreview`

行为定义：

- `ToggleCombatStatusBar` 为单次触发动作，按下一次切换状态条显隐
- `HoldEnchantPreview` 为按住型动作，按住时显示附魔信息
- `HoldUpgradePreview` 为按住型动作，按住时显示升级信息

用户可将附魔和升级预览绑定为任意单键，例如 `E` / `R`，而不是仅限 `Ctrl` / `Shift`。

默认值：

- `ToggleCombatStatusBar = F6`
- `HoldEnchantPreview = E`
- `HoldUpgradePreview = R`

如果需要兼容当前行为，可在首次升级时把历史默认值保留为：

- `ToggleCombatStatusBar = F6`
- `HoldEnchantPreview = LeftCtrl/RightCtrl`
- `HoldUpgradePreview = LeftShift/RightShift`

但当前需求已经明确希望支持 `E` / `R`，因此推荐直接切换为普通单键默认值。

## Architecture

### 1. `BppActionId`

新增 BazaarPlusPlus 自己的动作标识枚举，作为运行时查询和设置界面的统一键。

建议位置：

- `Game/Input/BppActionId.cs`

初始内容：

- `ToggleCombatStatusBar`
- `HoldEnchantPreview`
- `HoldUpgradePreview`

### 2. `BppHotkeyService`

新增统一快捷键服务，负责：

- 默认绑定定义
- 读取当前 binding path
- 保存 override
- 冲突检测
- 查询某动作当前是否按下 / 按住
- 恢复默认

建议位置：

- `Game/Input/BppHotkeyService.cs`

建议职责边界：

- 对外暴露 `WasPressed(BppActionId)` 和 `IsHeld(BppActionId)`
- 对外暴露 `GetBindingDisplay(BppActionId)`、`TryRebind(...)`、`ResetToDefault(...)`
- 内部保存 Input System 风格的 path，例如 `"<Keyboard>/e"`

### 3. 持久化模型

不使用原生 `PlayerPreferences.Data.KeyBindings`。

推荐保存到 BazaarPlusPlus 自己的配置节，结构上使用“动作 -> binding path”映射。核心要求：

- 保存的是 path，不是单纯的 `Key` 枚举名
- 初始化时如果没有配置，自动回退到默认 path
- 若配置损坏，回退默认值并记录日志

建议位置：

- `Models/ModState.cs` 中集中管理 BPP 级别配置入口，或新增 `Game/Input/BppHotkeyStore.cs`

### 4. `BppKeyBindRowController`

新增设置行控制器，交互上模仿原生 `KeyBindController`：

- 进入 rebind 状态
- 等待用户输入
- 排除不想接受的 control
- 检测重复绑定
- 应用绑定并刷新显示
- 恢复默认

建议位置：

- `Game/Input/BppKeyBindRowController.cs`

注意点：

- 逻辑不依赖原生 `InputManager.Actions`
- 使用 BazaarPlusPlus 自己的 action id
- UI 文案和 warning 尽量复用原生体验

### 5. `OptionsDialogController` 注入

像现有 `CombatStatusBarSettingsPatch` 一样，通过 Harmony 在原生设置页中插入 BazaarPlusPlus 行。

建议位置：

- `Patches/Settings/BppKeybindSettingsPatch.cs`

建议做法：

- 在 `OptionsDialogController.Awake` / `OnEnable` 中寻找 `_keybindObjects` 区域
- 克隆一条已有 keybind row 作为模板
- 替换标题文本
- 替换掉原有 `KeyBindController` 依赖，挂上 `BppKeyBindRowController`
- 把多条 BazaarPlusPlus 行组织在同一区域，必要时加一个 `BazaarPlusPlus` 小标题

## Data Flow

### Runtime Input Consumption

当前代码迁移为统一查询 `BppHotkeyService`：

- `CombatStatusBar.Update()` 不再直接读取 `Keyboard.current`
- `TooltipModifierRefreshController.GetCurrentMode()` 不再硬编码 `Ctrl`
- `UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip()` 和协程中的持续检查不再硬编码 `Shift`

迁移后：

- `ToggleCombatStatusBar` 使用 `WasPressed`
- `HoldEnchantPreview` 使用 `IsHeld`
- `HoldUpgradePreview` 使用 `IsHeld`

### Settings Interaction

设置页打开时：

- patch 保证 BazaarPlusPlus keybind rows 存在
- 每一行读取 `BppHotkeyService` 当前绑定并展示 display string

用户点击一行进入 rebind：

- 行进入 editing 状态
- 暂时屏蔽游戏输入消费
- 捕获一个新的单键 binding
- 若与其他 BazaarPlusPlus 动作重复，则显示 warning 并拒绝提交
- 成功则写入 BPP 配置并刷新显示

### Persistence

启动时：

- `BppHotkeyService` 读取保存值
- 若缺失则填充默认值

用户改键后：

- 立即写入 BazaarPlusPlus 配置
- 不依赖原生 settings 的保存按钮才能持久化

## UI/UX Requirements

- 外观与原生 keybind rows 保持一致
- 文案至少提供英文和简体中文
- Warning 风格尽量接近原生 `KeyBindController`
- `Reset` 仅在当前值不是默认值时显示或可点击
- 如果当前平台为 mobile，则与原生行为一致，不显示键位设置

建议文案：

- `Combat Status Bar Toggle` / `战斗状态栏开关`
- `Show Enchant Preview` / `显示附魔预览`
- `Show Upgrade Preview` / `显示升级预览`

## Conflict Policy

初期只做 BazaarPlusPlus 内部冲突检测：

- 不允许两个 BPP 动作绑定到同一个键

初期不做：

- 不检测与原生动作的全局冲突

原因：

- 原生动作解析链和 BazaarPlusPlus 自己的动作解析链并不统一
- 全局冲突检测需要跨系统检查，复杂度和脆弱性都明显更高

后续如有需要，可增加“只提示不阻止”的原生冲突警告。

## Testing Strategy

建议优先做纯逻辑测试，减少对 Unity 场景对象的依赖：

- 默认绑定回退
- 覆盖绑定加载
- 冲突检测
- 恢复默认
- 显示字符串格式化
- 按住型 / 单次触发型查询逻辑

UI 注入部分至少验证：

- 能找到键位区域
- 重复打开设置页不会重复插入
- 每行都能正确显示 BazaarPlusPlus 文案和当前绑定

## Risks

### 1. 原生 keybind row 结构变化

如果游戏更新导致原生设置页节点结构变化，基于 clone 的 patch 可能失效。

缓解：

- 通过稳定锚点查找模板
- 插入前检查已有节点，避免重复创建
- 失败时记录明确日志，不影响游戏继续运行

### 2. 输入监听与游戏逻辑同时消费

如果 rebind 期间没有正确屏蔽 BPP 自己的输入消费，可能导致边改键边触发功能。

缓解：

- `BppKeyBindRowController` 在 editing 状态设置全局“暂停 BPP 快捷键消费”标志

### 3. 历史硬编码行为残留

如果某些地方仍直接读取 `Keyboard.current`，会造成设置页改键和实际行为不一致。

缓解：

- 明确把所有 BPP 快捷键入口统一迁移到 `BppHotkeyService`

## Recommendation

最终推荐方案：

- 用原生设置菜单承载 BazaarPlusPlus 快捷键条目
- 自建 BazaarPlusPlus 的快捷键服务和持久化
- 统一迁移现有功能消费逻辑

这是在可维护性、用户体验和版本稳定性之间最平衡的方案。
