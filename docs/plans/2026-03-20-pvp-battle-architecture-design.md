# PVP Battle Architecture Design

## Goal

把当前“`CombatReplay` 驱动 battle logging”的结构改成“`PVP battle` 驱动 replay / run logging / export”的结构，同时保留 saved replay 所需的原始消息载荷，并把 `CombatLog` 纳入统一设计边界。

## Decision

采用双对象模型，但只保留一个身份标识：

- `PvpBattleManifest` 作为业务主对象和 canonical read model
- `PvpReplayPayload` 作为 replay 资产对象
- 两者共享同一个 `BattleId`

运行时先捕获一个内部中间结果 `PvpBattleCaptureArtifact`，再分别投影成 manifest 和 replay payload。

只支持 `PVPCombat`。是否属于可记录战斗的业务判定统一放在 capture 层，其他层只保留防御式校验。

额外的硬规则：

- `BattleId` 只分配一次，不再并行维护一个永远相等的 `ReplayId`
- saved replay 的“可见性”由 manifest 决定，不由 payload 文件决定
- payload store 只负责资产存取，不负责列表、标签、metadata 查询
- run logging 是 manifest 的衍生消费方，不定义 battle 成功与否

## Why This Design

当前结构的问题不是“有没有 replay”，而是 battle summary、sqlite 持久化、run logging 都依附在 `CombatReplayRecord` 上，导致：

- battle 记录语义被 replay DTO 绑住
- `CombatReplayCaptureService` 同时承担状态机、快照抓取、DTO 组装三类职责
- `CombatLog` 与 replay / battle 数据链路之间的边界不清晰
- replay 文件同时承担“资产”和“索引”两种职责，读写边界混乱

如果继续以 `CombatReplayRecord` 为主对象，未来只支持 `PVPCombat` 的方向仍然会被“通用 replay”语义拖累。相反，把主语切成 `PvpBattleManifest` 后：

- sqlite / export / run log / replay metadata 只依赖 battle 业务事实
- replay playback 只依赖 replay payload 和 manifest snapshots
- DebugPanel / replay list / metadata lookup 都可以走统一的 manifest read side
- `CombatLog` 保持纯消费方，只在 replay 场景下附加读取 manifest 的显示元数据

## Scope

### In Scope

- 重新定义 battle capture 的领域边界
- 引入 `PvpBattleManifest` / `PvpReplayPayload`
- 引入统一 `BattleId`
- 拆分当前 `CombatReplayCaptureService`
- 统一 `PVPCombat` 过滤入口
- 引入 manifest read side，承接 replay list / metadata lookup
- 明确 `CombatLog` 与 battle/replay 的关系
- 设计迁移顺序和测试策略

### Out of Scope

- 变更 saved replay 的底层 bootstrap 流程
- 改写 `CombatLog` 的 timeline / row formatting 行为
- 引入通用 combat 类型支持
- 把 replay payload 并入 sqlite
- 为 battle 和 replay 设计独立生命周期

## Current State

当前保存链路大致如下：

```text
NetMessage stream
  -> CombatReplayCaptureService
    -> CombatReplayRecord
      -> replay json file
      -> pvp_battles sqlite row
      -> run logging event
```

主要问题：

1. `CombatReplayCaptureService` 过于集中。  
   它同时负责：
   - opening / combat / closing 状态机
   - player/opponent snapshots 抓取
   - replay record 组装

2. `CombatReplayRecord` 承担了两个概念：
   - battle 业务记录
   - replay 原始消息载荷

3. replay json 文件既是资产又是索引。
   - `ListSavedReplays`
   - DebugPanel 标签
   - playback lookup  
   目前都直接依赖 replay 文件本身。

4. `CombatLog` 现在是 `CombatSim` 的纯消费者，这个定位本身是对的，但设计上没有被显式纳入整体架构。

## Proposed Architecture

### High-Level Flow

```text
NetMessage stream
  -> PvpBattleSequenceMatcher
    -> PvpBattleSequenceWindow
      -> PvpBattleSnapshotCollector
        -> PvpBattleCaptureArtifact
          -> PvpBattleManifestFactory -> PvpBattleManifest
          -> PvpReplayPayloadFactory  -> PvpReplayPayload

PvpReplayPayload
  -> CombatReplayPayloadStore

PvpBattleManifest
  -> PvpBattleCatalog
    -> PvpBattleSqliteStore
    -> RunLoggingController
    -> export_run_log.py
    -> replay-side metadata source
    -> DebugPanel / replay list

CombatReplayRuntime
  -> payload store save
  -> manifest catalog save
  -> best-effort run logging event

CombatSim
  -> CombatLogRuntime
```

### Core Rules

#### 1. Single Entry Rule For PVP Filtering

业务规则只保留一个真入口：

- 只有 opening `NetMessageGameSim` 的 `CurrentState.StateName == ERunState.PVPCombat`
  才允许创建 capture candidate。

这条规则由 `PvpBattleSequenceMatcher` 单独负责。

下游组件如 sqlite store、catalog、run logging controller 可以保留：

- `warn and ignore`
- `assert and return`

但它们不再承担业务过滤职责。

#### 2. Single Identity Rule

当前设计刻意不保留独立 `ReplayId`。

- `BattleId` 是 battle 和 replay payload 的共享 identity
- payload 文件名直接使用 `BattleId`
- run logging event 直接记录 `BattleId`
- playback 入口直接使用 `BattleId`

只有在将来明确出现“battle 与 replay 生命周期分离”的需求时，才重新引入独立 `ReplayId`。

#### 3. Visibility Rule

只有 manifest 持久化成功，battle 才算“对外可见”。

固定顺序：

1. 生成 `PvpBattleManifest`
2. 用 `manifest.BattleId` 生成 `PvpReplayPayload`
3. `CombatReplayPayloadStore.Save(payload)`
4. `PvpBattleCatalog.Save(manifest)`
5. `RunLoggingController.CapturePvpBattle(manifest)`，作为 best-effort 衍生事件

这个规则的后果：

- payload save 成功但 manifest save 失败时，只会留下 orphan payload，不会出现“列表可见但不可回放”的 battle
- run logging 失败不回滚 battle manifest；battle 持久化仍算成功

## Data Model

### 1. `PvpBattleManifest`

业务主对象，供 sqlite / export / run logging / replay metadata / replay list 读取。

字段建议：

- `BattleId`
- `RunId`
- `SavedAtUtc`
- `CombatKind`
- `Day`
- `Hour`
- `EncounterId`
- `Participants`
- `Outcome`
- `Snapshots`

manifest 不保存 full replay payload。

### 2. `PvpBattleParticipants`

- `PlayerName`
- `PlayerAccountId`
- `OpponentName`
- `OpponentAccountId`

### 3. `PvpBattleOutcome`

- `Result`
- `WinnerCombatantId`
- `LoserCombatantId`

### 4. `PvpBattleSnapshots`

四组 snapshot 不再直接暴露为裸数组，而是统一成 capture-status 结构：

- `PlayerHand`
- `PlayerSkills`
- `OpponentHand`
- `OpponentSkills`

这四个字段的类型都应为 `PvpBattleCardSetCapture`。

### 5. `PvpBattleCardSetCapture`

字段建议：

- `Items`
- `Status`
- `Source`

其中：

- `Items` 继续复用现有 `CombatReplayCardSnapshot` 的字段语义
- `Status` 用于区分“没抓到”和“抓到了空列表”
- `Source` 用于描述最终采用的数据来源

建议枚举：

- `PvpBattleCaptureStatus`
  - `Missing`
  - `CapturedEmpty`
  - `Captured`
- `PvpBattleCaptureSource`
  - `NotAttempted`
  - `OpeningMessage`
  - `LiveDataFallback`
  - `LiveRetry`

这套结构要同时覆盖 player/opponent 的 hand/skills 四组数据，而不是只给 player 一侧附加状态。

### 6. `PvpReplayPayload`

只为 replay playback 服务。

- `BattleId`
- `Version`
- `SpawnMessageBase64`
- `CombatMessageBase64`
- `DespawnMessageBase64`

payload 不混入 battle summary、participants、outcome、snapshots。

### 7. `PvpBattleCaptureArtifact`

仅在 capture pipeline 内部使用，不直接持久化。

字段建议：

- `SequenceWindow`
- `SavedAtUtc`
- `RunId`
- `CombatKind`
- `Day`
- `Hour`
- `EncounterId`
- `Participants`
- `Outcome`
- `Snapshots`

这个对象的作用是把“抓取”与“持久化形状”分开，避免 capture 层直接绑定 sqlite schema 或 replay file schema。

artifact 必须已经包含所有 battle facts。factory 的职责是 projection，不应在 factory 内继续读取 live `Data`。

### 8. `PvpBattleSequenceWindow`

显式表示一场 battle 对应的原始消息窗口：

- `OpeningMessage`
- `CombatMessage`
- `ClosingMessage`

## Runtime Components

### 1. `PvpBattleSequenceMatcher`

职责：

- 接收 `INetMessage`
- 维护 opening / combat / closing 状态机
- 只识别 `PVPCombat`
- 输出完整的 `PvpBattleSequenceWindow`

不负责：

- 读取 live `Data`
- 抓取 cards / skills
- 生成 manifest 或 replay payload

需要显式定义的异常序列规则：

- opening `PVPCombat` 到来时创建新 candidate
- candidate 尚未闭合时，如果又来一个 opening `PVPCombat`，旧 candidate 直接丢弃并重新开始
- candidate 尚未拿到 `CombatMessage` 时，如果来了非 PVP 的 opening / closing，candidate 丢弃
- 没有 candidate 时到来的 `CombatMessage` 一律忽略
- 已有 `CombatMessage` 后再次收到 `CombatMessage`，candidate 丢弃
- 没有 `CombatMessage` 的 candidate 不得产出 battle artifact

### 2. `PvpBattleSnapshotCollector`

职责：

- 从 `PvpBattleSequenceWindow + live Data` 抓取：
  - participants
  - outcome
  - snapshots

关键规则：

- opening `GameSim` 的 opponent 信息优先于 live `Data`
- live `Data` 只做 fallback
- 对 player live snapshots，空列表不能直接视为“采集成功”
- 对 opponent hand / skills 也必须输出明确的 `Status + Source`
- 应记录 source quality / fallback path 的日志，便于之后继续排查 replay 丢字段问题

collector 的输出应该已经能回答这类问题：

- 这组 cards 是真的为空，还是抓取失败
- 最终用了 opening message、live fallback，还是 live retry
- 哪一侧数据更可靠

### 3. `PvpBattleManifestFactory`

职责：

- 从 `PvpBattleCaptureArtifact` 生成 `PvpBattleManifest`
- 分配 `BattleId`

规则：

- `BattleId` 只在这里分配一次
- 分配后的 `BattleId` 由 runtime 传给 payload factory
- manifest factory 不负责生成 replay payload

### 4. `PvpReplayPayloadFactory`

职责：

- 从 `BattleId + PvpBattleSequenceWindow` 生成 `PvpReplayPayload`
- 只关注 replay 需要的原始消息内容

规则：

- 不生成自己的 id
- 不读取 `Data`
- 不混入业务摘要字段

### 5. `PvpBattleCatalog`

这是 manifest 的统一读写边界，负责承接 battle 的 read side。

职责：

- `Save(PvpBattleManifest manifest)`
- `TryLoad(string battleId)`
- `ListRecentBattles(...)`
- 供 replay list / DebugPanel / replay metadata lookup 读取 manifest

第一版可以直接由 sqlite-backed store 实现，但接口语义应先明确成“catalog / repository”，而不是继续把 payload store 当作列表来源。

### 6. `CombatReplayRuntime`

保留现有 runtime 作为协调器，但不再拥有 capture 语义。

新的职责：

- 观察消息
- 调用 matcher / collector / factories
- 按固定顺序保存 payload 与 manifest
- 在 manifest 成功后再通知 run logging
- 提供 replay playback 入口

换句话说，它是 battle artifacts 的消费协调器，不是业务规则的定义者。

### 7. `CombatReplayController`

replay 控制器的依赖边界也要随之变化：

- 列表和标签来自 `PvpBattleCatalog`
- payload 来自 `CombatReplayPayloadStore`
- loader 只负责从 payload 反序列化原始消息

因此 controller 的读取顺序应为：

1. 从 catalog 取 manifest
2. 用 `manifest.BattleId` 从 payload store 取 payload
3. 反序列化 payload
4. 用 manifest snapshots 完成 replay-side rehydrate / metadata

## Persistence Design

### 1. Manifest Store: `PvpBattleSqliteStore`

只保存 manifest projection，并通过 `PvpBattleCatalog` 暴露读写接口。

建议保留现有 `pvp_battles` 表的总体方向，但输入改成 `PvpBattleManifest`，并明确字段语义是 manifest projection，而不是 replay record projection。

建议内容：

- battle identity
- run identity
- timestamps
- player/opponent identity
- result / winner / loser
- snapshot projection

不保存：

- full replay payload
- 单独的 `replay_id`

对 snapshot projection，建议最小化 schema churn：

- 继续保留 player/opponent hand/skills 四个 JSON 槽位
- 但每个 JSON 槽位不再是裸数组，而是完整的 `PvpBattleCardSetCapture`

这样 export 和调试读取时可以同时拿到：

- `items`
- `status`
- `source`

而不会丢掉“空列表”和“抓取失败”的区别。

### 2. Asset Store: `CombatReplayPayloadStore`

只保存 `PvpReplayPayload`。

规则：

- 文件名使用 `BattleId`
- 只暴露 `Save / Load / Exists`
- 不承担 replay list / metadata catalog 的职责

### 3. Write Ordering And Failure Semantics

固定顺序：

1. `payloadStore.Save(payload)`
2. `battleCatalog.Save(manifest)`
3. `runLogging.CapturePvpBattle(manifest)`

语义：

- manifest save 成功之前，battle 不可见
- payload save 失败，整场 battle capture 失败
- manifest save 失败，整场 battle capture 失败
- run logging 失败，只记错误日志，不回滚已保存的 battle

### 4. Read Side Responsibilities

以下能力都应统一走 `PvpBattleCatalog`：

- recent battles 列表
- DebugPanel 标签展示
- `Replay Latest`
- replay metadata lookup
- `CombatLog` replay metadata source

以下能力只走 `CombatReplayPayloadStore`：

- 根据 `BattleId` 取 payload 原文

## CombatLog Integration

`CombatLog` 的定位保持不变：它是 `CombatSim` 的显示层消费者，不进入 battle capture 的持久化主链路。

### Keep

- `CombatLogController` 继续监听 `Events.CombatSimReceived`
- `CombatLogRuntime` 继续消费 `CombatSim`
- playback pass 仍然由 live/replay 上下文区分

### Add

给 `CombatLogRuntime` 增加一个可选的 metadata source 抽象，例如：

- `ICombatCardMetadataSource`

用途：

- live combat 时，从 `Data` 解析 card display info
- replay combat 时，从 `PvpBattleManifest.Snapshots` 解析 card display info

metadata 解析优先级必须固定：

1. 显式注入的 replay metadata source
2. live `Data`
3. fallback short identifier

这样能避免 replay 场景里 metadata 在 `Data`、manifest snapshots、fallback strings 之间漂移。

### Do Not Do

- 不让 `CombatLogRuntime` 直接依赖 sqlite battle rows
- 不让 `CombatLogRuntime` 直接读取 replay payload 文件
- 不把 `CombatLog` timeline 作为 battle 主记录的一部分存储

## Migration Strategy

### Phase 1: Introduce New Models

新增：

- `PvpBattleManifest`
- `PvpBattleParticipants`
- `PvpBattleOutcome`
- `PvpBattleSnapshots`
- `PvpBattleCardSetCapture`
- `PvpBattleCaptureStatus`
- `PvpBattleCaptureSource`
- `PvpReplayPayload`
- `PvpBattleCaptureArtifact`
- `PvpBattleSequenceWindow`

同时明确：

- 单一 `BattleId`
- payload 文件名用 `BattleId`
- manifest 不再暴露 `ReplayId`

### Phase 2: Split Capture Logic

从现有 `CombatReplayCaptureService` 拆出：

- `PvpBattleSequenceMatcher`
- `PvpBattleSnapshotCollector`
- `PvpBattleManifestFactory`
- `PvpReplayPayloadFactory`

此阶段重点是职责切分，不先处理 UI 或 replay list。

### Phase 3: Establish Manifest Read Side

新增或改造：

- `PvpBattleCatalog`
- `PvpBattleSqliteStore.Save(TryLoad/ListRecentBattles...)`
- DebugPanel / replay list 从 catalog 读 battle metadata

到这个阶段为止，payload store 不再承担列表职责。

### Phase 4: Move Replay To Payload + Manifest

改造 replay 相关：

- `CombatReplayStore` -> `CombatReplayPayloadStore`
- `CombatReplayLoader` 从 `PvpReplayPayload` 反序列化
- `CombatReplayController` 从 catalog 取 manifest，再用 `BattleId` 取 payload
- replay-side rehydrate 使用 manifest snapshots

### Phase 5: Remove Legacy Replay-First Model

清理：

- `CombatReplayRecord`
- `CombatReplayStore`
- `CombatReplayCaptureService`
- 依赖 replay-file list 的旧读侧逻辑

## File Layout

建议的新目录结构：

```text
Game/PvpBattles/
  PvpBattleManifest.cs
  PvpBattleParticipants.cs
  PvpBattleOutcome.cs
  PvpBattleSnapshots.cs
  PvpBattleCardSetCapture.cs
  PvpBattleCaptureStatus.cs
  PvpBattleCaptureSource.cs
  PvpReplayPayload.cs
  PvpBattleCaptureArtifact.cs
  PvpBattleSequenceWindow.cs
  PvpBattleSequenceMatcher.cs
  PvpBattleSnapshotCollector.cs
  PvpBattleManifestFactory.cs
  PvpReplayPayloadFactory.cs
  Persistence/
    PvpBattleCatalog.cs
    PvpBattleSqliteStore.cs

Game/CombatReplay/
  CombatReplayRuntime.cs
  CombatReplayPayloadStore.cs
  CombatReplayLoader.cs
  CombatReplayController.cs
```

如果希望减少路径 churn，也可以先把新类型放在 `Game/CombatReplay/` 下，待稳定后再统一迁目录。第一版更重要的是职责切分和接口边界，而不是物理位置的绝对完美。

## Testing Strategy

### Contract Tests

- 只有 `PVPCombat` opening 会启动 capture
- 普通 `Combat` opening 不产生 battle artifact
- stray `CombatMessage` 不产生 battle artifact
- 没有 `CombatMessage` 的 candidate 不产生 battle artifact
- `BattleId` 只分配一次，并在 manifest / payload / run logging 间共享

### Snapshot Tests

- opponent identity 优先来自 opening `GameSim`
- live `Data` 缺失时仍能从 opening message 提取 opponent identity
- player hand / skills 在 opening 时为空时，应允许后续 retry
- opponent hand / skills 也必须产出明确的 `Status + Source`
- `CapturedEmpty` 与 `Missing` 必须可区分

### Persistence Tests

- sqlite store 保存 manifest 而非 replay record
- payload file 只包含原始消息载荷
- payload save 失败时，battle 不可见
- manifest save 失败时，battle 不可见
- recent battles 列表来自 catalog，而不是 payload 目录
- export 脚本读出的 JSON shape 与真实 store 产物一致
- export 结果要保留 `status / source / items`

### Replay Tests

- manifest + payload 可以完成 saved replay playback
- replay controller 先读 manifest，再按 `BattleId` 找 payload
- replay-side rehydrate 使用 manifest snapshots
- payload 缺失时应报错清晰

### CombatLog Tests

- live 场景仍按 `CombatSim` 构建 timeline
- replay 场景优先从 manifest snapshots 解析显示名
- metadata source 优先级固定为 `injected source -> live Data -> fallback`
- `CombatLog` 不依赖 sqlite / payload 文件即可运行

## Risks

### 1. Partial Migration Can Recreate The Same Coupling

如果只改名字，不拆职责，`PvpBattleManifest` 仍可能退化成新的“胖 replay record”。必须坚持：

- manifest 不存 full payload
- payload 不混入业务摘要字段
- payload store 不承担列表和 metadata 查询职责

### 2. Snapshot Projection Can Lose Capture Semantics

如果 sqlite/export 只保留裸数组而不保留 `Status + Source`，那么：

- `CapturedEmpty` 与 `Missing` 会重新混在一起
- replay metadata 与调试日志会再次失去诊断信息

因此 capture-status 不是 collector 内部细节，而是 manifest 的一部分。

### 3. Read-Side Drift

如果 replay list、DebugPanel、CombatLog metadata source 各自从不同地方读数据：

- 有的从 payload 文件名推断
- 有的从 sqlite row 推断
- 有的从 live `Data` fallback

那么最终会重新出现 label、回放入口、metadata 不一致的问题。

必须坚持：

- battle list / metadata 统一从 `PvpBattleCatalog` 读取
- payload store 只提供资产载荷
- `CombatLog` 只通过注入的 metadata source 读取 replay metadata

## Recommendation

先做 identity 和读写边界，再做命名迁移。

最小可执行顺序：

1. 引入 `BattleId`、`PvpBattleCardSetCapture`、`Status/Source`
2. 拆 `CombatReplayCaptureService`
3. 建立 `PvpBattleCatalog`
4. 让 runtime 按固定顺序产出并保存 manifest + payload
5. 让 replay list / DebugPanel / controller 改读 catalog
6. 给 `CombatLog` 增加 manifest snapshot metadata source
7. 删除 replay-first 的旧主链路

这个顺序能把 battle logging 的主语从 replay 平滑切换到 `PVP battle`，同时避免把 replay payload 继续当成列表索引和业务主对象。
