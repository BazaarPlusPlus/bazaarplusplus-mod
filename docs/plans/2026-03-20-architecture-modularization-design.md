# Architecture Modularization Design

## Goal

把当前 BazaarPlusPlus 的“全局静态状态 + `MonoBehaviour.Instance` 单例 + Patch 直连业务”结构，重构成“显式模块边界 + 事件驱动集成 + 只读查询服务”的运行时架构，优先降低 feature 间牵连，便于后续持续加功能。

## Decision

采用单程序集内模块化架构，不立即拆仓库或拆程序集，但把运行时边界明确下来：

- `Plugin` 只负责启动 `BppRuntimeHost`
- `BppRuntimeHost` 统一装配 core services、event bus、query services、feature modules
- Harmony Patch 降级为 adapter，只做参数提取和事件发布
- 每个 feature 统一收敛为 `Module -> RuntimeFacade -> Services -> Infrastructure`
- 跨 feature 通信默认走 `IBppEventBus`
- 运行时查询默认走只读 query 接口
- `ModState` 被拆除，替换为按职责分离的 context/config/cache/path services

## Why This Design

当前项目的主要问题不是目录结构，而是集成层失控：

1. `ModState` 同时承载 logger、config、run lifecycle、encounter cache、combat data、disk path，已经是隐式 service locator。
2. Patch、runtime、controller、service 之间大量点对点直连，feature 边界不清晰。
3. `GameDataReader` 这类 helper 同时承担游戏态读取和 feature-specific DTO 组装，容易继续膨胀。
4. `CombatReplayRuntime`、`MonsterLockShowcaseRuntime` 这种运行时对象已经承担过多职责，修改风险持续抬升。

如果继续沿用现状，新增功能通常意味着：

- 新 patch 直接调现有 runtime
- 新 runtime 直接找别的 `Instance`
- 再顺手往 `ModState` 里加字段

这会让代码库越来越难扩展。相反，模块化运行时有几个直接收益：

- 新 feature 的默认接入方式统一
- 初始化顺序从“隐式依赖”变成“显式装配”
- 共享状态的写入口能被收紧
- 未来若要拆多程序集/多仓库，演进路径明确

## Scope

### In Scope

- 重画运行时装配结构
- 拆分 `ModState`
- 引入内部 event bus
- 引入只读 query services
- 把 patch 改造成 adapters
- 逐步把 feature 状态收回各自模块内部
- 为后续多程序集演进预留边界

### Out of Scope

- 立即拆成多仓库
- 立即引入外部 DI 框架
- 一次性重写所有 feature
- 改变现有功能行为或 UI 交互
- 优先针对性能做系统性优化

## Current State

当前运行时大致可概括为：

```text
Plugin
  -> PatchAll
  -> 初始化 ModState
  -> AddComponent(多个 Runtime/Controller)

Harmony Patch
  -> 直接调 Runtime.Instance / 静态方法 / ModState

Feature Runtime
  -> 直接读写 ModState
  -> 直接调用其他 Feature Runtime.Instance
  -> 直接从游戏态拼自己需要的数据
```

主要结构性问题：

### 1. `ModState` 过载

`ModState` 当前同时管理：

- logger
- 配置项
- run lifecycle
- combat 临时状态
- encounter cache
- data path

这导致依赖方向变成“谁都能拿、谁都能改”，而不是“谁拥有、谁暴露接口”。

### 2. 跨 feature 直连

典型链路是：

```text
Patch -> FeatureRuntime.Instance -> 另一个 FeatureRuntime.Instance -> ModState
```

这使得：

- 启动顺序成为隐藏约束
- 单个 feature 难以独立替换
- 测试时需要构造一整条运行时链路

### 3. helper 膨胀

`GameDataReader` 这类工具类同时承担：

- 从游戏态取数据
- 构造通用快照
- 构造 RunLogging DTO
- 依赖 encounter cache 和 run lifecycle

这本质上已经不是单一“reader”，而是跨域组装层。

### 4. 重型运行时对象

`CombatReplayRuntime` 同时负责：

- capture observer
- payload/catalog persistence
- replay bootstrap
- replay start/rollback
- reflection-based dependency resolution

这类对象会持续演变成高风险改动点。

## Proposed Architecture

## High-Level Structure

```text
Plugin
  -> BppRuntimeHost
    -> Core Services
    -> Event Bus
    -> Query Services
    -> Feature Modules
    -> Patch Adapters
```

### 1. `Plugin`

职责收缩为：

- 初始化日志入口
- 创建 `BppRuntimeHost`
- 调用 `host.Install()` / `host.Start()`

`Plugin` 不再直接：

- 初始化 feature-specific 状态
- 手工把所有 feature 组件挂到同一个对象上
- 隐式承担 composition root 以外的逻辑

### 2. `BppRuntimeHost`

它是新的 composition root，负责：

- 创建 `IBppEventBus`
- 创建 core services
- 创建模块实例
- 注册模块间订阅关系
- 创建必要的 Unity runtime/view adapter
- 在退出时统一 teardown

### 3. Core Services

建议至少包含：

- `IBppLogger`
- `IBppConfig`
- `IPathService`
- `IGameStateProbe`
- `IRunContext`

其中：

- `IGameStateProbe` 只负责判断当前游戏宏观状态
- `IRunContext` 负责 run 生命周期相关可变状态
- 这两者不要再混到一个“万能状态类”里

## Communication Model

### Event Bus: For "Something Happened"

`IBppEventBus` 负责一次性事件通知，例如：

- `RunLifecycleChanged`
- `SelectionObserved`
- `CombatSimObserved`
- `NetMessageObserved`
- `PvpBattleCaptured`
- `ReplayPlaybackStarted`
- `ReplayPlaybackEnded`

规则：

- Patch 只发布事件，不做业务路由
- 事件 DTO 尽量小，不传大块 Unity/Game 内部对象
- feature 订阅事件，但不能依赖订阅顺序

### Query Services: For "What Is The Current State"

只读查询服务负责暴露稳定当前状态，例如：

- `IRunContextQuery`
- `IEncounterSelectionQuery`
- `IMonsterPreviewSourceResolver`
- `IReplayCatalogQuery`

规则：

- query 只读
- 返回快照或不可变模型
- 不通过 query 做副作用更新

## Feature Shape

每个 feature 统一按以下结构组织：

```text
Feature
  -> PatchAdapter / GameEventAdapter
  -> Module
  -> RuntimeFacade
  -> Application Services
  -> Infrastructure
  -> StateStore
  -> Query
```

### 模块职责说明

- `PatchAdapter / GameEventAdapter`
  - 把 Harmony/game callbacks 转成标准化事件
- `Module`
  - feature 入口，注册事件订阅，协调状态和服务
- `RuntimeFacade`
  - 面向 Unity/MonoBehaviour/UI 层的薄入口
- `Application Services`
  - 真正的业务逻辑
- `Infrastructure`
  - sqlite/json/filesystem/unity-specific resource 等
- `StateStore`
  - 本模块内部可写状态
- `Query`
  - 对外只读访问

## Module Mapping For This Repository

### Core

迁入：

- `Infrastructure/BppLog.cs`
- `Data/CardJsonPathResolver.cs`
- `Models/ModState.cs` 中的 config/path/run context 部分

拆分目标：

- `Core/Logging/BppLogger`
- `Core/Config/BppConfig`
- `Core/Paths/BppPathService`
- `Core/RunContext/RunContextStore`
- `Core/GameState/GameStateProbe`

### Shared.GameSnapshots

当前 `Game/GameDataReader.cs` 应拆为：

- `Shared/GameSnapshots/RunSnapshotReader`
- `Shared/GameSnapshots/SelectionSnapshotReader`
- `Shared/GameSnapshots/CombatSnapshotReader`

它只负责“读游戏态并返回标准化快照”，不再直接构造某个 feature 的命令对象。

### EncounterTracking

当前 `EncounterTracker` 独立成正式模块：

- 拥有当前 encounter selection 和 monster preview cache
- 发布 `SelectionObserved`
- 暴露 `IEncounterSelectionQuery`

其他 feature 不能直接写它的状态。

### RunLogging

保留已有边界较好的部分：

- `RunLogSessionManager`
- `RunLogCaptureService`
- `IRunLogStore`

但运行时入口改成：

- 订阅 `RunLifecycleChanged`
- 订阅 `SelectionObserved`
- 订阅 `PvpBattleCaptured`

`RunLogging` 不再主动去别的模块拿实例再调用。

### CombatReplay

拆分当前 `CombatReplayRuntime` 为：

- `ReplayCaptureGateway`
- `ReplayCatalogService`
- `ReplayPlaybackService`
- `ReplayBootstrapper`
- `ReplayRuntimeFacade`

目标是把 capture / persistence / playback bootstrap 分离。

### MonsterPreview

保留已有内部抽象：

- `IPreviewDataSource`
- `IBoardRenderTarget`
- `PreviewBoardSession`

但改造数据来源：

- `MonsterLockShowcaseRuntime` 不再读全局状态
- 通过 `IMonsterPreviewSourceResolver` 获取 preview source
- source 可以组合：
  - `MonsterDatabaseSource`
  - `EncounterCacheSource`

### CombatStatusBar

把 UI 和状态机拆开：

- `CombatStatusBarView`
- `CombatStatusBarModule`
- `CombatStatusBarStateStore`

Patch 只发布 combat-related events，不再直接调 UI 类型静态方法。

## `ModState` Replacement

`ModState` 会被以下对象替代：

- `IBppLogger`
- `IBppConfig`
- `IRunContext`
- `IEncounterTrackingState`
- `IPathService`

迁移原则：

- 共享状态必须有 owner
- 除 owner 外只允许读，不允许写
- 可变状态通过模块接口或事件流变更

## Example Flows

### Flow 1: Encounter Selection -> Run Logging

重构前：

```text
CardDealt event
  -> EncounterTracker
    -> ModState
    -> RunLoggingController.Instance.CaptureSelectionFromCurrentState()
```

重构后：

```text
CardDealt event
  -> EncounterTrackingModule
    -> update EncounterTrackingStateStore
    -> publish SelectionObserved

RunLoggingModule
  -> subscribe SelectionObserved
  -> append run log event
```

### Flow 2: Replay Capture -> Run Logging

重构前：

```text
Patch
  -> CombatReplayRuntime.Instance.ObserveMessage()
    -> save payload
    -> save manifest/catalog
    -> RunLoggingController.Instance.CapturePvpBattle()
```

重构后：

```text
PatchAdapter
  -> publish NetMessageObserved

ReplayCaptureModule
  -> subscribe NetMessageObserved
  -> capture/save battle replay
  -> publish PvpBattleCaptured

RunLoggingModule
  -> subscribe PvpBattleCaptured
  -> append run log event
```

### Flow 3: Monster Preview Lookup

重构前：

```text
MonsterLockShowcaseRuntime
  -> ModState.EncounterMonsterPreviews
  -> MonsterDatabase
```

重构后：

```text
MonsterLockShowcaseRuntime
  -> IMonsterPreviewSourceResolver
    -> EncounterCacheSource
    -> MonsterDatabaseSource
```

## Migration Strategy

采用四阶段迁移：

1. 搭新骨架
2. Patch adapter 化
3. 逐模块收状态
4. 删除兼容层

详细步骤见配套实施计划。

## Risks And Guardrails

### Risk 1: 新 event bus 变成新的 `ModState`

约束：

- event bus 由 host 创建，不设全局静态入口
- 只有模块安装阶段能注册长期订阅
- query service 只读，不暴露可写状态

### Risk 2: 一次性重写导致运行时回归

约束：

- 每个 feature 采用“外层包裹、内部掏空”的迁移策略
- 不直接重写 `CombatReplayRuntime` / `MonsterLockShowcaseRuntime`
- 保留兼容层直到新模块稳定

### Risk 3: 设计复杂度上升但收益不落地

约束：

- 优先处理最频繁跨 feature 的链路
- 不在第一阶段引入外部 DI 框架
- 不为未来多仓库拆分预先过度抽象

## Recommendation

推荐采用“单程序集内模块化 + event bus + query services”的中长期方案，并按彻底重构目标分阶段落地。它比“只拆 `ModState`”更能解决当前最核心的问题，同时又比“立刻多仓库/多程序集”更稳。
