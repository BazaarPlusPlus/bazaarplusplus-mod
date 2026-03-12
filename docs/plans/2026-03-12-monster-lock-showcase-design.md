# Monster Lock Showcase Design

## Goal

在原生 monster 右键 lock 大画面出现后，展示 BazaarPlusPlus 自己的 showcase 卡片。

这些 showcase 卡片需要满足：

- 只在 monster lock 大画面期间显示
- 展示形式尽量保持当前已有 showcase 实现，不重做卡面渲染
- 每张 showcase 卡可以 hover
- hover 时复用原生主 tooltip 展示内容
- 底层棋盘在 lock 期间仍不可交互
- 原生 lock 的退出方式保持不变

当前阶段不处理：

- showcase hover 时切换掉 monster 主 tooltip 的体验问题
- showcase 卡自身进入 lock mode
- showcase 卡拖拽、点击购买、socket 落位等 board 行为

---

## Current Native Behavior

原生右键 monster 的大画面，本质是 tooltip lock mode：

1. 先通过普通 hover 显示主 tooltip
2. 右键触发 `InputManager.Actions.Lock`
3. `CardTooltipController.LockTooltipToggle()` 切到 locked 状态
4. `DesktopLockModeController.Lock(...)`：
   - 提升当前 monster 卡的渲染层
   - 打开 `LockModeContainer`
   - 移动卡和 tooltip 到锁定展示位置
   - 让 tooltip canvas 开启交互和 raycast 拦截

结果是：

- monster 进入右键大画面
- 底层棋盘基本碰不到
- 再次右键、退出按钮，或某些锁定对象点击路径，会退出该大画面

因此 BazaarPlusPlus 不需要自己重建“大画面模式”，只需要把 showcase 接到这套模式上。

---

## Design Summary

当前采用的方案：

- 保留现有 showcase 卡生成和渲染逻辑
- 不以现有 `EncounterTooltipPreviewBridge` 作为新架构核心
- 继续让 showcase 保持在已有 world anchor 展示链路上
- 不再尝试把 showcase 挪进新的前景 root
- 改为在原生 lock canvas 上增加一个固定矩形“输入洞”
- 洞外由原生 lock 前景继续拦截输入
- 洞内允许后方的 showcase 3D 卡收到 hover
- hover 时复用原生主 tooltip

一句话概括：

保留现有 3D showcase 展示层；原生负责 lock 模式切换和大部分输入屏蔽；BazaarPlusPlus 只在 lock canvas 上挖一个固定矩形洞，让 showcase 所在区域的 hover 能透过去。

---

## Why This Approach

### Why not rebuild tooltip / lock

不推荐复制或重建整套 tooltip / lock 系统，因为原生 lock 机制已经解决了最难的几个问题：

- 进入与退出时机
- 底层棋盘不可交互
- monster 大画面的视觉和输入边界

重复实现这些逻辑，耦合高且容易产生双系统冲突。

### Why switch from foreground root to hole punching

在进一步确认后，showcase 仍然是现有的 3D 卡对象，而不是 UI 卡。

这意味着：

- 直接迁到普通 canvas/root 会引入 3D 对象与 UI 体系的错配
- 只换一个新的 world anchor 仍然会被 lock 前景挡住
- 真正的问题不是“showcase 在哪里”，而是“lock 前景是否允许那块区域透输入”

因此最终方案改为：

- 保留现有 world anchor showcase
- 在原生 lock canvas 的前景输入层上开一个固定矩形洞
- 让 showcase 继续在后方世界中渲染和接 hover

### Why reuse current showcase rendering

用户已经确认展示形式希望与当前实现保持一致。

所以本设计不重做：

- 卡片美术资源获取
- 卡面渲染逻辑
- 当前 showcase 布局/展示形式

只改：

- 出现时机
- 父节点 / 层级
- hover 行为
- 退出清理

### Why not treat `EncounterTooltipPreviewBridge` as the new foundation

当前的 `EncounterTooltipPreviewBridge` 更接近一版旧实验实现：

- 它围绕 fixed world anchor overlay 展开
- 它把 showcase 当成固定世界空间预览板
- 它不符合当前已经确认的“固定前景 root + lock 生命周期控制”方向

因此新方案不应继续围绕它扩展。

新方案的原则是：

- `EncounterTooltipPreviewBridge` 可以只作为旧代码和少量数据获取逻辑参考
- 新实现应由新的 lock 驱动 controller 作为主入口
- 现有可复用部分主要是 showcase 卡的生成、渲染与布局能力

---

## Proposed Structure

### 1. MonsterLockShowcaseController

职责：

- 监听 `Events.TooltipLock`
- 读取当前 locked monster
- 判断是否需要展示 showcase
- 创建或显示 `showcase root`
- 在 `Events.TooltipUnlock` 时清理 `showcase root`

这是 lock 生命周期和 BazaarPlusPlus 展示层之间的适配器。

它应作为新实现的核心入口，替代旧 bridge 作为主控制器。

### 2. Lock Canvas Hole Overlay

职责：

- 在原生 `lockModeContainer` / `lockModeCanvas` 上生成 4 块 blocker
- 围出一个固定矩形洞
- 洞外继续拦截输入
- 洞内不拦截输入，让后方 showcase 收到 hover

当前实现采用最简单、最稳的矩形拓扑：

- 上 blocker
- 下 blocker
- 左 blocker
- 右 blocker

而不是 shader 或异形 raycast filter。

### 3. Showcase Hover Item

职责：

- 承载单张 showcase 卡的可见内容
- 提供 hover 命中区域
- 在 hover 时调用原生主 tooltip

重要原则：

- 只有卡本体吃输入
- 空白区域继续交给原生 lock overlay

---

## Layering and Input Model

这是本设计最关键的部分。

### Input Ownership

输入按“洞内/洞外”分配：

1. 洞外
   - 仍由原生 lock 前景拦截
   - 保持底层棋盘不可交互

2. 洞内
   - 原生 lock 前景不再拦截
   - 后方 showcase 3D 卡接收 hover / raycast

这样可同时满足：

- showcase 卡可以 hover
- 底层棋盘仍然不会被误点
- 原生 lock 的退出行为大部分保持不变

---

## Tooltip Strategy

当前阶段直接复用原生主 tooltip。

### Behavior

- hover 到 showcase 卡
- 构造该卡对应的 tooltip 数据
- 调用原生主 tooltip 显示

### Accepted Tradeoff

当前接受如下体验：

- showcase hover 会占用主 tooltip
- monster 原本的主 tooltip 内容会被切换为 showcase 卡的 tooltip

用户已明确表示，现阶段不处理这个体验问题。

### Why this is acceptable now

这样可以避免：

- 再复制一套 tooltip controller
- 再处理 secondary / private tooltip 生命周期
- 再引入额外 lock mode 冲突

在当前目标下，复用主 tooltip 是最低成本路径。

---

## Data and Rendering Reuse

showcase 卡本体的展示应尽量复用现有实现。

### Reuse

- 现有 showcase 卡生成流程
- 现有卡面渲染和资源获取逻辑
- 现有布局形式
- 旧 bridge 中和 monster 数据解析相关的少量可用代码片段

### Do not rewrite now

- 不重做卡片渲染系统
- 不重做美术资源解析
- 不把 showcase 卡改造成完整原生 board card
- 不沿用 fixed world anchor overlay 作为新架构基础

这样可以把本次需求的工作集中在：

- lock 生命周期接入
- 层级控制
- hover tooltip 接入

---

## Lifecycle

### Enter

触发条件：

- 原生 `TooltipLock`

流程：

1. 获取当前 locked card
2. 判断是否为目标 monster/combat encounter
3. 根据当前 monster 数据构建 showcase 内容
4. 显示 `showcase root`
5. 将 showcase 放到固定位置

### Active

在 lock 大画面持续期间：

- showcase 卡可 hover
- hover 时显示主 tooltip
- 底层棋盘仍由原生 lock 机制屏蔽

### Exit

触发条件：

- 原生 `TooltipUnlock`

流程：

1. 清理当前 showcase 卡
2. 隐藏或销毁 `showcase root`
3. 清掉本次 monster 对应的 hover 状态

---

## Constraints

### What this design assumes

- 原生 lock 模式仍是唯一的大画面宿主
- showcase root 可以稳定放在比 lock 内容更高的可见层
- 现有 showcase 卡渲染逻辑可复用

### Known Risks

1. 主 tooltip 共享
   - showcase hover 会改写主 tooltip 内容

2. 层级调试
   - 需要实际确认 showcase root 不会被原生 lock 内容盖住

3. 输入边界
   - 若 root 设计成大面积吞输入，可能影响原生退出路径

### Risk Mitigation

- root 只做容器，不做整层输入拦截
- 仅卡本体拥有 hover 命中区
- 生命周期严格绑定 `TooltipLock/Unlock`

---

## Success Criteria

实现完成后，行为应为：

1. 面对 3 个 monster 时，界面保持原始状态
2. 右键某个 monster，进入原生大画面
3. BazaarPlusPlus showcase 卡在该大画面上方出现
4. 底层棋盘依旧不可交互
5. 鼠标移到 showcase 卡上时，可显示该卡 tooltip
6. showcase 空白区域不会误把交互漏到棋盘
7. 再次右键或原生退出行为触发时，大画面和 showcase 一起消失

---

## Recommended Next Step

实现时按以下最小变更顺序推进：

1. 接入 `TooltipLock/Unlock` 生命周期
2. 把当前 showcase root 挂到独立前景层
3. 确认 lock 状态下可见和清理正确
4. 给 showcase 卡补 hover -> 主 tooltip 的适配
5. 最后再调层级和交互细节
