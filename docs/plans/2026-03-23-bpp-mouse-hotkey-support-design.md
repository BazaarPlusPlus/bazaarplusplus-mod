# BazaarPlusPlus Mouse Hotkey Support Design

## Summary

目标是在现有 BazaarPlusPlus 自定义快捷键系统中加入鼠标按钮支持，使附魔预览和升级预览可以绑定到鼠标侧键及其他鼠标按钮，同时保持当前键盘绑定、设置页 UI 和 tooltip 运行时行为不变。

本设计延续现有边界：

- 继续复用 BazaarPlusPlus 自己的热键存储和运行时查询
- 继续把设置行注入原生 `OptionsDialogController`
- 不接入原生 `PlayerPreferences.Data.KeyBindings`
- 不支持滚轮轴、鼠标位置、delta 等连续输入

## Current State

当前实现仅支持键盘：

- `Game/Input/BppHotkeyService.cs`
  - 仅接受 `<Keyboard>/...` 路径
  - `IsHeld(...)` 只读取 `Keyboard.current`
- `Game/Input/BppKeyBindRowController.cs`
  - rebind 期间仅遍历 `Keyboard.current.allKeys`
- `Core/Config/BppConfig.cs`
  - 当前默认值为 `<Keyboard>/ctrl` 和 `<Keyboard>/shift`

因此鼠标侧键无法被设置页捕获，也无法在运行时生效。

## Goal

支持把 BazaarPlusPlus 热键绑定到：

- 任意键盘按键
- 任意鼠标按钮类输入，包括但不限于：
  - `leftButton`
  - `rightButton`
  - `middleButton`
  - `backButton`
  - `forwardButton`
  - 其他 Mouse 设备上的按钮控件

明确不支持：

- `scroll`
- `position`
- `delta`
- 其他非按钮型连续控件

## Decision

采用“宽松版按钮支持”：

- 允许 `Keyboard` 和 `Mouse` 设备上的按钮型控件参与绑定
- 用 Input System 的控件类型做过滤，而不是维护一个硬编码白名单
- 继续保存 Input System path，例如：
  - `<Keyboard>/e`
  - `<Mouse>/forwardButton`
- 继续保持“一次只绑定一个离散输入”

不采用：

- 不开放到任意设备类型，例如 `Gamepad`
- 不支持组合键
- 不接受所有 `<Mouse>/...` 路径后再在运行时兜底失败，因为那会把无效连续控件写进配置

## Architecture

### 1. `BppHotkeyService`

文件：

- `Game/Input/BppHotkeyService.cs`

职责调整：

- `NormalizeBindingPath(...)` 从“只接受 `<Keyboard>/`”扩展为：
  - 接受 `<Keyboard>/...`
  - 接受 `<Mouse>/...` 且该 path 对应的控件是按钮型输入
- `IsHeld(...)` 从“只查键盘”扩展为：
  - 键盘路径时读取 `Keyboard.current`
  - 鼠标路径时读取 `Mouse.current`
- `GetBindingDisplay(...)` 保留现有别名逻辑，并为常见鼠标按钮提供可读名称
- 冲突检测继续复用当前逻辑，因为其核心比较的是规范化后的 path 集

实现原则：

- 统一由一个“path -> 是否为受支持按钮控件”的判断入口控制合法性
- 不允许把非按钮控件 path 写进配置
- 若现有配置值损坏或不再合法，读取时自动回退默认值

### 2. `BppKeyBindRowController`

文件：

- `Game/Input/BppKeyBindRowController.cs`

职责调整：

- rebind 状态同时监听：
  - `Keyboard.current.allKeys`
  - `Mouse.current.allControls` 中的按钮型控件
- 捕获顺序保持“本帧首次按下即提交”
- `Escape` 继续作为取消 rebind 的专用键
- 若捕获到不支持控件，则显示现有 `Unsupported key` 提示

实现原则：

- 保持当前 UI 状态切换逻辑不变
- 鼠标按钮重绑与键盘重绑共享同一个 `TrySetBindingPath(...)` 校验入口

### 3. Tooltip Consumers

文件：

- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- `Game/Tooltips/TooltipModifierRefreshController.cs`

这些文件不需要直接感知鼠标逻辑。

它们继续只调用：

- `BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)`
- `BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview)`

鼠标支持只在热键服务内生效。

## Data Flow

### Rebind Flow

1. 用户在设置页点击 BazaarPlusPlus 的 keybind row
2. `BppKeyBindRowController` 进入 rebind 状态
3. 控制器监听当前帧的新按下输入
4. 如果输入来自：
   - `Keyboard` 的 key control，构造 `<Keyboard>/...`
   - `Mouse` 的 button control，构造 `<Mouse>/...`
5. 把 path 交给 `BppHotkeyService.TrySetBindingPath(...)`
6. 校验通过则写入配置并刷新显示
7. 校验失败则显示 warning

### Runtime Flow

1. tooltip 逻辑调用 `BppHotkeyService.IsHeld(...)`
2. 热键服务读取配置 path
3. 根据 path 的设备类型选择 `Keyboard.current` 或 `Mouse.current`
4. 若对应按钮当前按下，则返回 `true`

## UX Requirements

- 设置页展示的文案保持当前多语言逻辑不变
- 键位显示尽量采用人类可读文本，例如：
  - `Ctrl`
  - `Shift`
  - `Mouse Back`
  - `Mouse Forward`
  - `Mouse Left`
- 不为鼠标支持新增独立开关或说明文案
- 旧用户升级后无需迁移配置；现有键盘绑定继续工作

## Validation

需要覆盖以下场景：

- `<Mouse>/backButton` 与 `<Mouse>/forwardButton` 可被设置并生效
- 任意鼠标按钮型控件会被接受
- `<Mouse>/scroll` 等非按钮路径会被拒绝
- tooltip 逻辑无需改调用方式即可响应鼠标绑定
- 设置页在语言刷新后仍能正确显示绑定名称

## Risks

### 1. Input System 对鼠标控件命名与预期不一致

缓解：

- 优先用 `InputControlPath.ToHumanReadableString(...)`
- 对常见按钮保留显式别名映射

### 2. rebind 捕获到鼠标左键，影响设置页按钮点击

缓解：

- 仅在进入 rebind 后捕获后续帧的 `wasPressedThisFrame`
- 不把进入 rebind 的同一次点击直接当作新绑定

### 3. 配置里已有非法 `<Mouse>/...` 值

缓解：

- 读取时复用合法性校验
- 非法值回退默认绑定

## Out of Scope

- 原生游戏 keybind backend 集成
- 组合键支持
- 手柄支持
- 多设备通用热键抽象
