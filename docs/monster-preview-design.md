# Monster Preview 设计总览

## 目标

本文整理 BazaarPlusPlus 当前 `MonsterPreview` 系统的整体设计，说明：

- 系统现在解决什么问题
- 当前线上主链路到底是哪一条
- 各层对象分别负责什么
- 数据、anchor、presentation、render 的边界如何划分
- debug 和 legacy 路径在系统里的位置
- 当前已知的设计风险在哪里

本文以 **2026-03-13 当前代码** 为准，优先描述真实运行路径，而不是历史设计稿。

---

## 1. 系统职责

`MonsterPreview` 的核心职责是：

1. 在特定时机生成一份 monster preview 数据
2. 为这份数据选择一个世界空间锚点和展示参数
3. 将数据渲染为一个 3D `board`
4. 在 show / hide / refresh 过程中维护 preview 生命周期

它不是一个 tooltip 系统，也不是原生 lock mode 的替代品。

当前在线上真正承担的角色是：

- 复用原生 monster 右键 lock 的进入时机
- 在 lock 期间显示 BazaarPlusPlus 自己的 item / skill preview board
- 保持 preview 作为一个世界空间 3D 对象存在

---

## 2. 当前挂载与激活方式

插件启动时会挂载：

- [`Plugin.cs`](Plugin.cs)
  - `MonsterPreviewController`
  - `MonsterLockShowcaseRuntime`
  - debug 模式下再挂 `MonsterPreviewDebugController`

这意味着：

- 当前右键 monster 的正式路径由 `MonsterLockShowcaseRuntime` 驱动
- 旧的 `EncounterTooltipPreviewBridge` 路径已经删除，不再是代码库的一部分

---

## 3. 高层架构

当前系统可以分成 6 层：

1. 触发层
2. runtime 组装层
3. preview session 协调层
4. render target 层
5. board / card object 层
6. patch 与交互保护层

对应关系如下：

```text
Native right-click lock input
  -> ShowcaseTooltipPatches
  -> MonsterLockShowcaseRuntime
  -> MonsterPreviewController
  -> MonsterPreviewOverlayCoordinator
  -> PreviewBoardSession
  -> MonsterPreviewBoardRenderTarget
  -> MonsterPreviewBoard
  -> Item/Skill Card Factories
```

这套架构里最关键的边界是：

- runtime 决定“要不要显示、显示什么”
- session 决定“这一帧是否需要向底层 render”
- render target 决定“board 是否存在、是否可见、是否开始 rebuild”
- board 只关心“怎样把一份 render model 变成场景对象”

---

## 4. 当前正式主链路

### 4.1 输入入口

monster 右键最终会走原生：

- `CardTooltipController.LockTooltipToggle()`

BazaarPlusPlus 在 [`Patches/Showcase/ShowcaseTooltipPatches.cs`](Patches/Showcase/ShowcaseTooltipPatches.cs) 中为这个调用打 patch：

- 如果 `MonsterLockShowcaseRuntime.Instance` 存在，并且 `HandleLockToggle(currentCard)` 返回 `true`
- 则拦截原函数，转由 BazaarPlusPlus 处理本次 toggle

因此当前 monster 右键 preview 的 BPP 入口不是事件监听器，而是 `LockTooltipToggle` 前缀 patch。

### 4.2 runtime 组装

[`MonsterLockShowcaseRuntime.cs`](Game/MonsterPreview/MonsterLockShowcaseRuntime.cs) 负责：

- 判断当前卡是否应该触发 showcase preview
- 根据 monster db 或 tracker cache 生成 `PreviewCardSpec`
- 组装 `PreviewBoardRequest`
- 调用 `MonsterPreviewController.ShowRequest(...)`
- 在再次右键时执行 hide

它不直接操纵 board，也不直接实例化卡对象。

它只是把“原生 lock 输入”翻译成“preview request”。

### 4.3 controller 到 session

[`MonsterPreviewController.cs`](Game/MonsterPreview/MonsterPreviewController.cs) 是 Unity `MonoBehaviour` 外观层。

职责：

- 持有 `_visible`
- 在 `Awake()` 中创建 render target 和 coordinator
- 在 `LateUpdate()` 中驱动 `_coordinator.Tick()`
- 接收外部的 `ShowRequest` / `SetVisible` / `SetCards` / `SetPresentation`

它本身不判断数据签名，不负责 diff，也不直接生成 board。

`MonsterPreviewController` 只负责把“外部命令”交给 `MonsterPreviewOverlayCoordinator`。

### 4.4 coordinator 到 session

[`MonsterPreviewOverlayCoordinator.cs`](Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs) 是 request 汇聚层。

职责：

- 管理 `_visible`
- 维护当前 `_externalRequest`
- 在非 external request 模式下，把 anchor / cards / presentation / debug 拼成一份 request
- 每帧调用 `PreviewBoardSession.Show(...)` 与 `Tick()`

它相当于一个“request assembler”。

这里有两种工作模式：

1. `external request` 模式
   - 例如 `MonsterLockShowcaseRuntime.ShowRequest(...)`
   - coordinator 直接转发这份 request

2. `internal assembled request` 模式
   - 例如 debug controller 分别设置 anchor、cards、presentation
   - coordinator 在 `Tick()` 时临时拼一份 request

### 4.5 session 到 render

[`PreviewBoardSession.cs`](Game/MonsterPreview/Architecture/PreviewBoardSession.cs) 是当前架构的核心状态机。

职责：

- 接收当前 request
- 解析 model
- 解析 pose
- 计算 data signature
- 计算 presentation signature
- 判断本帧是否需要 render
- 在 hide 时清空缓存状态

它不直接生成对象，而是调用 `IBoardRenderTarget`。

当前 `ShouldRender(...)` 的依据是：

- data signature 是否变化
- presentation signature 是否变化
- pose 是否变化

这意味着 session 关心的是“渲染输入是否变化”，而不是卡对象构建过程本身是否完成。

### 4.6 render target 到 board

[`MonsterPreviewBoardRenderTarget.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs) 是 session 和具体 Unity 对象之间的适配层。

职责：

- 确保 `_board` 存在
- 在 `Render(...)` 时把 `presentation`、`pose` 传给 board
- 启动一次 `RebuildAsync(...)`
- 在 `SetVisible(false)` 时隐藏并清空 board

当前 render target 的唯一实现是生产用的 `MonsterPreviewBoardRenderTarget`。

---

## 5. 核心数据模型

### 5.1 `PreviewBoardRequest`

[`PreviewBoardRequest.cs`](Game/MonsterPreview/Architecture/PreviewBoardRequest.cs)

它是 session 的输入封装，包含：

- `IPreviewDataSource DataSource`
- `IBoardAnchorStrategy AnchorStrategy`
- `PreviewBoardModel InitialModel`
- `PreviewBoardPresentation Presentation`
- `PreviewBoardDebugOptions Debug`
- `BoardPose Pose`

设计含义：

- `Request` 不是“已经准备好渲染”的最终数据
- 它允许上游传“源”或“快照”
- session 会优先从 `DataSource` / `AnchorStrategy` 解析最新值

### 5.2 `PreviewBoardModel`

[`PreviewBoardModel.cs`](Game/MonsterPreview/Architecture/PreviewBoardModel.cs)

这是 board 的逻辑数据快照，包含：

- `ItemCards`
- `SkillCards`
- `Title`
- `Signature`
- `Metadata`

它表达的是“要展示什么”，不表达“展示在哪里、用什么样式”。

### 5.3 `PreviewCardSpec`

[`PreviewCardSpec.cs`](Game/MonsterPreview/Architecture/PreviewCardSpec.cs)

单张预览卡的统一描述：

- `TemplateId`
- `SourceName`
- `Tier`
- `Size`
- `Enchant`
- `Attributes`

上游无论来自 monster db、tracker cache、玩家手牌还是 debug 数据，最终都要落成 `PreviewCardSpec`。

### 5.4 `PreviewBoardPresentation`

[`PreviewBoardPresentation.cs`](Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs)

它表达的是“怎样展示”，包括：

- `Visible`
- `DebugEnabled`
- `LocalOffset`
- `CardScale`
- `CardSpacing`
- `BoardSize`
- `SkillBoardWidth`
- `BoardThickness`
- `BorderThickness`
- `BorderHeight`

这个对象的边界很重要：

- 它不包含 card data
- 它不包含 anchor 来源
- 它只描述 board 的视觉参数和显隐

### 5.5 `BoardPose`

[`BoardPose.cs`](Game/MonsterPreview/Architecture/BoardPose.cs)

`BoardPose` 只包含：

- `Position`
- `Rotation`

它是 anchor 层交给 render 层的最终产物。

---

## 6. 数据来源设计

当前预览数据主要有三类来源。

### 6.1 `InMemoryPreviewDataSource`

[`InMemoryPreviewDataSource.cs`](Game/MonsterPreview/Architecture/InMemoryPreviewDataSource.cs)

用途：

- 接收上层临时拼装的数据
- 复制 cards / metadata
- 在 `TryBuild(...)` 时返回新的 `PreviewBoardModel`
- 同时生成 `Signature`

它是当前最常用的数据源实现，`MonsterLockShowcaseRuntime` 和 debug 路径都大量依赖它。

### 6.2 `MonsterDatabasePreviewDataSource`

[`MonsterDatabasePreviewDataSource.cs`](Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs)

用途：

- 根据 encounter id 从 `MonsterDatabase` 生成 preview model
- 把 monster 的 board cards 和 skills 映射为 `PreviewCardSpec`

它代表“正式结构化数据源”。

### 6.3 tracker cache / legacy 转换

当前 runtime 仍保留从 `ModState.EncounterMonsterPreviews` 读取旧 cache 的兜底逻辑。

这部分逻辑散在：

- [`MonsterLockShowcaseRuntime.cs`](Game/MonsterPreview/MonsterLockShowcaseRuntime.cs)

设计上它不是一个独立 `IPreviewDataSource` 实现，而是运行时组装 request 前的 fallback 数据转换。

---

## 7. Anchor 设计

当前 anchor 接口是：

- [`IBoardAnchorStrategy.cs`](Game/MonsterPreview/Architecture/IBoardAnchorStrategy.cs)

生产主路径现在主要用：

- [`FixedAnchorStrategy.cs`](Game/MonsterPreview/Anchor/FixedAnchorStrategy.cs)

默认 pose 在：

- [`MonsterPreviewDefaults.cs`](Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs)

当前设计特征：

- preview board 是世界空间对象
- anchor 层只负责给出 `BoardPose`
- board 不关心锚点来自 monster、tooltip、固定点还是 debug 调参

现在正式路径实际上仍使用固定锚点，而不是跟踪某个原生 lock 容器 transform。

这意味着当前 preview 是“借原生 lock mode 触发时机”，但显示位置仍是 BazaarPlusPlus 自己定义的 3D 世界 anchor。

---

## 8. Board 与渲染对象设计

### 8.1 `MonsterPreviewBoard`

[`MonsterPreviewBoard.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs)

这是实际的 Unity 场景对象实现。

内部结构大致分成：

- `_boardRoot`
- `_visualRoot`
- `_itemContentRoot`
- `_skillContentRoot`
- item slots / skill slots
- board fill / border / branding / markers

职责：

- 创建 board 根对象与视觉底板
- 管理 item / skill 两个内容区
- 创建和销毁 card anchor / marker / card object
- 应用 layout
- 应用 presentation
- 更新世界 pose

`MonsterPreviewBoard` 不知道 request 的来源，也不直接持有 session 状态。

### 8.2 card factory

当前 card factory 接口：

- [`IPreviewCardFactory.cs`](Game/MonsterPreview/GameObjectFactory/IPreviewCardFactory.cs)

两个实现：

- [`MonsterPreviewItemCardFactory.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs)
- [`MonsterPreviewSkillCardFactory.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs)

职责：

- 把 `PreviewCardSpec` 变成实际游戏里的卡对象
- 调原生 `AssetLoader.InstantiateCardAsync(...)`
- 对生成出的对象做 `ShowCard(true)`、禁用移动、挂 `ShowcaseCardMarker`
- 在销毁时清理并返还对象池

当前 skill factory 额外有一次 `RefreshSpawnedSkillAsync(...)`，用于修正技能卡的美术状态。

### 8.3 `RebuildAsync(...)`

board 重建流程是：

1. `Clear()`
2. `RebuildItemsAsync(...)`
3. `RebuildSkillsAsync(...)`
4. `RefreshLayout()`

这里的关键事实是：

- card 生成是异步的
- board 本身是共享对象
- rebuild 不是由 session `await` 完成，而是 fire-and-forget 启动

这也是当前主要稳定性风险所在。

---

## 9. 默认样式与展示参数

默认展示参数在：

- [`MonsterPreviewDefaults.cs`](Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs)

现在区分两套入口：

- `CreateShowcasePresentation()`
- `CreateDebugPresentation()`

但当前 debug 默认直接复用 showcase presentation。

含义是：

- debug 和正式路径已经在结构上统一
- 差别主要来自数据源、anchor 和 `DebugOptions`
- 样式参数本身暂时没有完全分叉

---

## 10. 交互与补丁层

Monster preview 的显示虽然是 3D board，但它能正常工作还依赖几组 patch。

### 10.1 `ShowcaseTooltipPatches`

[`ShowcaseTooltipPatches.cs`](Patches/Showcase/ShowcaseTooltipPatches.cs)

职责：

- 接管 `LockTooltipToggle`，把 monster 右键切到 BPP runtime
- 对 showcase 卡 hover 期间的 tooltip lock 判断做绕过
- 避免原生 `DisableLockModeCanvas` 在 showcase tooltip 展示时把 lock 视觉层误关掉

它解决的是“如何接入原生 lock mode 生命周期”。

### 10.2 `PreviewBoardSurfacePatches`

[`PreviewBoardSurfacePatches.cs`](Patches/Showcase/PreviewBoardSurfacePatches.cs)

职责：

- 当鼠标射线首先命中 preview board surface 时
- 阻止下层普通卡把自己判定为 `IsPointerOverThis == true`

它解决的是“preview board 存在时，不要让其下方卡牌继续误吃 hover”。

### 10.3 `ShowcaseCardInteractionPatches`

[`ShowcaseCardInteractionPatches.cs`](Patches/Showcase/ShowcaseCardInteractionPatches.cs)

职责：

- 对挂了 `ShowcaseCardMarker` 的卡，只允许右键 click 路径继续

它的效果是：

- showcase card 不会像正常场上卡一样参与左键点击行为
- 但仍保留当前 preview 依赖的右键交互路径

---

## 11. Debug 路径

[`MonsterPreviewDebugController.cs`](Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs) 只在 debug 模式挂载。

职责：

- 提供一个屏幕调试面板
- 手动切换 preview 可见状态
- 调整 anchor、offset、board size、scale 等参数
- 在 monster db / hand preview / locked showcase 之间切换调试目标

它不是另一套 preview 实现，而是对同一个 `MonsterPreviewController` 的调参壳层。

这说明当前架构已经基本实现了一个目标：

- debug 路径和正式路径共享同一套 controller / coordinator / session / board
- debug 只改输入，不重建一套并行渲染逻辑

---

## 12. Legacy 路径

旧的 `EncounterTooltipPreviewBridge` 已经从代码库删除。

现在保留下来的 legacy 成分主要是：

- `MonsterLockShowcaseRuntime` 中对 `ModState.EncounterMonsterPreviews` 的 fallback 转换
- 一些文档和设计记录中对旧事件驱动方案的描述

因此当前讨论 legacy 时，重点应放在“旧数据来源兜底是否还需要继续保留”，而不是旧 bridge 本身。

---

## 13. 当前设计的关键边界

可以把当前系统概括成 4 个稳定边界。

### 13.1 业务边界

`MonsterLockShowcaseRuntime` 负责：

- 什么场景下要显示 preview
- preview 数据从哪里来

它不负责底层 board 生命周期。

### 13.2 request/session 边界

`MonsterPreviewOverlayCoordinator` + `PreviewBoardSession` 负责：

- 当前 request 是什么
- 本帧是否需要 render

它们不直接创建 Unity card object。

### 13.3 渲染边界

`MonsterPreviewBoardRenderTarget` + `MonsterPreviewBoard` 负责：

- board 是否存在
- board 是否显示
- board 如何清理和重建

它们不决定 preview 数据从哪里来。

### 13.4 交互边界

patch 层负责：

- 与原生 tooltip/lock 输入整合
- 避免 preview 对底层普通卡产生错误 hover/click 行为

它不应该持有 preview 数据状态。

---

## 14. 当前已知问题与设计风险

### 14.1 visible 状态与内容状态不是一回事

当前系统里：

- `Visible` 只是展示状态
- board 内容是否完整，取决于异步 rebuild 是否成功收敛

所以“visible=true”并不自动等于“用户一定能看到正确内容”。

### 14.2 session 追踪的是输入变化，不是 rebuild 完成状态

`PreviewBoardSession` 缓存的是：

- data signature
- presentation signature
- pose

它不知道底层异步 rebuild 是否真正完成，也不知道内容是否已被旧任务覆盖。

这会导致 session 可能判断“无需 render”，但底层 board 实际已经空了或内容不对。

### 14.3 render target 当前没有真正的取消机制

`MonsterPreviewBoardRenderTarget.Render(...)` 现在直接 fire-and-forget：

- 启动 `_board.RebuildAsync(...)`
- 传入 `() => false`

这意味着：

- 旧 rebuild 不会自动过期
- hide/show/re-render 可能与旧任务交叉
- 多个 rebuild 会写同一个 `MonsterPreviewBoard`

### 14.4 board 是共享可变对象

`MonsterPreviewBoard` 在异步 rebuild 期间会持续写：

- `_cards`
- `_cardAnchors`
- `_cardSizes`
- `_cardCenterMarkers`
- `_skillCards`
- `_boardRoot` 子树

因此当前并发风险不是抽象意义上的，而是直接作用于共享 Unity 对象树。

---

## 15. 结论

截至 2026-03-13，`MonsterPreview` 的真实设计可以概括为：

- 它是一个世界空间 3D preview board 系统
- 通过 patch 接入原生 monster 右键 lock 输入
- 由 `MonsterLockShowcaseRuntime` 组装 request
- 由 `MonsterPreviewController -> MonsterPreviewOverlayCoordinator -> PreviewBoardSession` 管理生命周期与 render 决策
- 由 `MonsterPreviewBoardRenderTarget -> MonsterPreviewBoard` 完成实际对象构建
- debug 路径复用同一套底层结构
- legacy bridge 保留但不在线上主链路中

当前架构已经把数据、anchor、presentation、render 的职责基本拆开，但仍有一个重要未收敛点：

- session 是同步状态机
- board rebuild 是异步共享写入

这条边界上的状态一致性，正是当前 monster preview 稳定性问题的主要来源。

---

## 16. 模块拆解

如果从“谁负责什么”来拆，当前 `MonsterPreview` 可以进一步细分为下面 8 个模块。

### 16.1 原生输入接入模块

相关文件：

- [`Patches/Showcase/ShowcaseTooltipPatches.cs`](Patches/Showcase/ShowcaseTooltipPatches.cs)

职责：

- 接住原生 `CardTooltipController.LockTooltipToggle()`
- 决定这次右键是否改走 BazaarPlusPlus preview
- 保持 showcase hover 与原生 tooltip/lock mode 兼容

这是系统和原生输入系统的边界。

### 16.2 业务决策模块

相关文件：

- [`Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`](Game/MonsterPreview/MonsterLockShowcaseRuntime.cs)
- [`Game/MonsterPreview/Showcase/MonsterLockShowcaseController.cs`](Game/MonsterPreview/Showcase/MonsterLockShowcaseController.cs)

职责：

- 判断当前卡是否应该显示 preview
- 选择数据来源
- 构造 `PreviewBoardRequest`
- 决定何时 show / hide

这里是“monster lock 业务规则”所在的位置。

### 16.3 request 组装模块

相关文件：

- [`Game/MonsterPreview/MonsterPreviewController.cs`](Game/MonsterPreview/MonsterPreviewController.cs)
- [`Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs`](Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs)

职责：

- 对外暴露 Unity 层入口
- 汇总 cards / anchor / presentation / debug
- 维护当前 request 来源

这一层不做重建，只做“把输入送到 session”。

### 16.4 session 状态机模块

相关文件：

- [`Game/MonsterPreview/Architecture/PreviewBoardSession.cs`](Game/MonsterPreview/Architecture/PreviewBoardSession.cs)
- [`Game/MonsterPreview/Architecture/PreviewBoardSignature.cs`](Game/MonsterPreview/Architecture/PreviewBoardSignature.cs)

职责：

- 解析 request
- 计算 signature / presentation signature / pose
- 决定这一帧是否需要 render
- 维护 hide/show 相关缓存状态

这是当前真正的“render 决策中心”。

### 16.5 数据模型与数据源模块

相关文件：

- [`Game/MonsterPreview/Architecture/PreviewBoardRequest.cs`](Game/MonsterPreview/Architecture/PreviewBoardRequest.cs)
- [`Game/MonsterPreview/Architecture/PreviewBoardModel.cs`](Game/MonsterPreview/Architecture/PreviewBoardModel.cs)
- [`Game/MonsterPreview/Architecture/PreviewCardSpec.cs`](Game/MonsterPreview/Architecture/PreviewCardSpec.cs)
- [`Game/MonsterPreview/Architecture/InMemoryPreviewDataSource.cs`](Game/MonsterPreview/Architecture/InMemoryPreviewDataSource.cs)
- [`Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs`](Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs)

职责：

- 统一 preview 输入模型
- 统一 card 描述格式
- 把 monster db / cache / 临时数据转换成 `PreviewBoardModel`

这是“展示什么”的边界。

### 16.6 anchor 与 presentation 模块

相关文件：

- [`Game/MonsterPreview/Architecture/BoardPose.cs`](Game/MonsterPreview/Architecture/BoardPose.cs)
- [`Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs`](Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs)
- [`Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs`](Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs)
- [`Game/MonsterPreview/Anchor/FixedAnchorStrategy.cs`](Game/MonsterPreview/Anchor/FixedAnchorStrategy.cs)

职责：

- 定义 board 在世界中的 pose
- 定义 board 的视觉样式参数
- 提供默认展示参数

这是“展示在哪里、长什么样”的边界。

### 16.7 实际渲染模块

相关文件：

- [`Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs)
- [`Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs)
- [`Game/MonsterPreview/GameObjectFactory/IPreviewCardFactory.cs`](Game/MonsterPreview/GameObjectFactory/IPreviewCardFactory.cs)
- [`Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs)
- [`Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs`](Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs)

职责：

- 确保 board 存在
- 应用 pose 与 presentation
- 异步创建/清理 item 与 skill 卡对象
- 维护整个 preview board 的 Unity 对象树

这是“怎么画出来”的边界。

### 16.8 调试与交互保护模块

相关文件：

- [`Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs`](Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)
- [`Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs`](Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs)
- [`Patches/Showcase/PreviewBoardSurfacePatches.cs`](Patches/Showcase/PreviewBoardSurfacePatches.cs)
- [`Patches/Showcase/ShowcaseCardInteractionPatches.cs`](Patches/Showcase/ShowcaseCardInteractionPatches.cs)
- [`Game/MonsterPreview/View/PreviewBoardSurfaceMarker.cs`](Game/MonsterPreview/View/PreviewBoardSurfaceMarker.cs)

职责：

- 提供 debug 面板和调参入口
- 阻断 preview board 下方普通卡误吃 hover
- 限制 showcase 卡的点击行为

这个模块不产生 preview 数据，也不驱动 render 决策。

---

## 17. 当前没有用到的东西清单

截至 2026-03-13，这一轮已确认未使用的遗留入口、闲置 view 分支和相关辅助类都已经从代码库删除。

当前这一节只保留“仍在文件里、但只有部分实现被使用”的对象。

### 17.1 部分实现未被走到的类

这些类不是完全没用，而是 **只有部分实现被使用**。

- [`Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs`](Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs)
  - 当前会使用其静态 `BuildModel(...)` 帮助方法。
  - 但没有任何地方 `new MonsterDatabasePreviewDataSource(...)`，也没有地方走它作为 `IPreviewDataSource` 的实例路径。
  - 换句话说：这个文件“部分使用，部分闲置”。

- [`Game/MonsterPreview/GameObjectFactory/PreviewCardLifecyclePolicy.cs`](Game/MonsterPreview/GameObjectFactory/PreviewCardLifecyclePolicy.cs)
  - `ShouldRefreshAfterInstantiate(...)` 有调用点。
  - `ShouldReturnToPool(...)` 当前没有任何调用点。

### 17.2 仍在用，但容易被误判成没用的东西

这些文件虽然看起来边缘，但当前系统里确实仍在用，不应列入清理候选。

- [`Game/MonsterPreview/Showcase/MonsterLockShowcaseController.cs`](Game/MonsterPreview/Showcase/MonsterLockShowcaseController.cs)
  - 被 `MonsterLockShowcaseRuntime` 持有，用于决定 lock 时是否应显示 preview。

- [`Game/MonsterPreview/View/PreviewBoardSurfaceMarker.cs`](Game/MonsterPreview/View/PreviewBoardSurfaceMarker.cs)
  - 由 `MonsterPreviewBoard` 挂到 board surface 上。
  - `PreviewBoardSurfacePatches` 依赖它来屏蔽下层卡牌 hover。

- [`Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs`](Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs)
  - 被 `MonsterLockShowcaseRuntime` 和 `MonsterPreviewDebugController` 使用。

---

## 18. 清理建议

本轮已完成一批明确死代码的清理，包括：

- `CompositePreviewDataSource`
- `AdjustableAnchorStrategy`
- `AnchorAdjustment`
- `LockCanvasHoleOverlay`
- `LockCanvasHoleLayout`
- `BoardView`
- `BoardDebugOverlay`
- `BoardLayoutSnapshot`
- `EncounterTooltipPreviewBridge`
- `PreviewBoardRequestFactory`

后续如果继续做目录瘦身，重点应转向“文件仍在，但只有部分实现被用到”的对象。

### 18.1 下一步更值得处理的对象

- `MonsterDatabasePreviewDataSource`
  - 可考虑保留静态 helper，删除未用的实例型 datasource 入口。

- `PreviewCardLifecyclePolicy`
  - 可考虑删除未用方法 `ShouldReturnToPool(...)`，保留当前在用的刷新策略。
