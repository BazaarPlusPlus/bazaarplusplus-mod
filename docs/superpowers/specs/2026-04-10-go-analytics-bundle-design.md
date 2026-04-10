# Go Analytics Ingest Bundle Design

## Goal

将当前 `analytics_sync/` 的“上游输入 -> 中间 bundle -> 分析库”这段链路重构为 Go 实现，并重新划分职责边界。

本设计的核心目标不是简单把 Python 改写成 Go，而是先定义一套长期稳定的 ingest 架构：

- `provider` 是可替换输入源，不作为事实模型
- `bundle` 是长期稳定的 canonical 业务契约
- 数据库 schema 是 `bundle` 的下游投影，不反向主导上游模型
- 第一阶段不依赖 mirror，不把 mirror 作为架构前提

## Scope

本设计覆盖：

- Go 版 ingest 服务的模块边界与职责划分
- canonical `bundle` 的定位与字段组织方式
- `provider -> normalizer -> bundle -> projector -> repository` 的处理链路
- `bundle` 到分析库表结构的投影规则
- 幂等、批处理、错误处理、任务编排的职责边界
- 后续 provider 演进时如何保持下游稳定

本设计不覆盖：

- 旧 Python `analytics_sync/` 的逐文件迁移计划
- 旧 `ModCFServer` 在线写入逻辑改造
- mirror 方案
- 最终报表宽表、OLAP 聚合表、BI 可视化层
- 新版本上线后的线上协议细节

## Problem Statement

当前 Python 版本已经把事实表与 battle 明细表初步建起来，但它的职责边界仍偏向“同步脚本”：

- 输入源形态和最终数据库写模型耦合较紧
- `bundle` 主要作为内部过渡对象存在，还不是明确的长期契约
- provider、补字段逻辑、投影逻辑、任务编排的边界还不够清晰
- 如果后续上游从离线同步切到新版本正式接入，当前结构容易把变化传导到下游表和 job

因此这次 Go 重构的重点应是先建立清晰边界，而不是先做实现层面的性能优化。

## Design Principles

### 1. Canonical bundle is the contract

`bundle` 表达稳定业务语义，而不是某个 provider 的原始格式，也不是数据库写入参数集合。

任何上游输入都必须先归一化为 canonical bundle，数据库只是 bundle 的一个 consumer。

### 2. Providers are replaceable

第一阶段可以是离线 provider；后续可以接新版本 provider。两者都不应直接决定分析库 schema。

### 3. Normalize before project

字段补齐、语义统一、校验、排序、去重，都发生在 normalize 阶段。project 阶段只做“bundle 到表”的投影。

### 4. Facts stay relational

分析库继续使用关系模型承接稳定高频分析维度，不引入 JSONB-first 设计，不把 battle 构筑回退成 blob。

### 5. Explicit staging boundaries

输入抓取、业务归一化、数据库投影、持久化写入、任务调度分别归属于不同层，避免单个模块同时承担多种职责。

## Chosen Architecture

采用五层结构：

1. `provider`
2. `normalizer`
3. `canonical bundle`
4. `projector`
5. `repositories`

job 层只负责编排，不负责业务语义。

处理链路：

`provider -> normalizer -> canonical bundle -> projector -> repositories`

未来如果需要派生表或聚合表，可在 `projector/repositories` 之后追加：

`fact tables -> derived models`

但第一阶段先不实现派生层。

## Responsibilities

### Provider

Provider 只负责“从某种来源读取原始输入”。

它可以来自：

- 离线文件
- 本地导出数据
- D1 查询
- 新版本正式上报接口

Provider 负责：

- 分页或批量读取
- 基础反序列化
- 对来源级错误进行包装
- 返回原始记录与来源游标

Provider 不负责：

- 补齐业务字段
- 推断 canonical 语义
- 直接写分析库
- 决定 `runs/battles/cards/...` 这些表如何组织

### Normalizer

Normalizer 是 ingest 核心业务层。

它负责把 provider 的原始输入归一化为 canonical bundle，并在这里完成：

- run / battle 语义统一
- 来源字段映射
- 稳定排序
- 缺失字段补齐
- 非法输入过滤
- 轻度推断
- 领域级校验

Normalizer 负责输出“可被下游长期依赖”的 canonical bundle。

### Canonical Bundle

Bundle 是长期稳定契约。

它的目标不是贴近当前数据库，也不是贴近当前 provider，而是表达：

- 一个 run 的稳定身份
- 这个 run 包含的 battle 序列
- 每个 battle 时刻可复原的构筑事实
- 下游事实化所需的最小稳定字段

Bundle 必须：

- provider 无关
- 数据库无关
- 可序列化
- 可测试
- 可独立做 schema versioning

### Projector

Projector 只负责把 canonical bundle 投影为数据库写模型。

它负责：

- 将 bundle 拆成 `run`、`battle`、`battle_card`、`battle_skill`、`battle_slot_temperature` 等行集合
- 决定哪些字段进入事实表，哪些保留为空
- 产出 repository 可批量写入的数据结构

Projector 不负责：

- 连接数据库
- 事务控制
- provider 语义
- 任务调度

### Repositories

Repository 层只负责数据库读写。

它负责：

- 批量 upsert / insert / replace
- 事务内顺序写入
- 模板维表 key lookup
- 任务状态记录
- checkpoint 存取

Repository 不负责：

- 来源映射
- 领域推断
- bundle 构造

### Job / Orchestrator

Job 层只负责编排。

它负责：

- 拉取批次
- 调用 provider
- 调用 normalizer
- 调用 projector
- 组织事务
- 记录指标和错误
- 处理并发、重试、进度和 checkpoint

Job 不应持有复杂业务规则。

## Canonical Bundle Design

### Positioning

Canonical bundle 的定位是：

- 一个 run 级 ingest 单元
- 一个可长期演进的领域对象
- 一个可被多个 consumer 使用的标准契约

第一版 bundle 只容纳“确定稳定且下游确实需要”的字段。

### Top-level shape

建议 Go 模型分为以下对象：

- `CanonicalRunBundle`
- `CanonicalRun`
- `CanonicalBattle`
- `CanonicalBattleBoard`
- `CanonicalCard`
- `CanonicalSkill`
- `CanonicalSlotState`
- `SourceMetadata`

建议结构关系：

```text
CanonicalRunBundle
  - schema_version
  - source
  - run
  - battles[]
```

其中：

- `run` 表达 run 级稳定事实
- `battles[]` 按 battle 时序排列
- battle 内部包含双方 board 状态

### Source metadata

保留最小来源元数据，但不让它污染业务字段。

建议：

- `source.provider`
- `source.cursor_updated_at`
- `source.cursor_entity_id`
- `source.observed_at`
- `source.trace_id`

这些字段用于调试、排障、重放，不参与分析事实主语义。

### Run object

`CanonicalRun` 建议包含：

- `run_id`
- `installation_id`
- `player_account_id`
- `plugin_version`
- `game_version`
- `submitted_at_utc`
- `status`
- `hero_id`
- `hero_name`
- `started_at_utc`
- `ended_at_utc`
- `final_day`
- `final_wins`
- `final_losses`
- `opening_rank`
- `opening_rating`
- `opening_position`
- `final_rank`
- `final_rating`
- `final_position`

说明：

- 这里使用领域命名，不直接绑定当前表字段名
- `opening_*` / `final_*` 比 `player_*` / `final_player_*` 语义更清楚
- 后续 projector 再映射到数据库列名

### Battle object

`CanonicalBattle` 建议包含：

- `battle_id`
- `run_id`
- `sequence`
- `recorded_at_utc`
- `day`
- `result`
- `player`
- `opponent`

其中 `player` / `opponent` 都是 `CanonicalBattleBoard`。

`sequence` 是 run 内稳定排序键，normalize 阶段生成。它不必作为分析库第一版持久字段，但 bundle 中应存在，避免以后排序逻辑散落到各处。

### Battle board object

`CanonicalBattleBoard` 建议包含：

- `account_id`
- `display_name`
- `hero_id_or_name`
- `rank`
- `rating`
- `position`
- `level`
- `cards[]`
- `skills[]`
- `slot_states[]`

其中：

- `cards[]` 按 `slot_index` 排序
- `skills[]` 按 `slot_index` 排序
- `slot_states[]` 只包含非默认槽位状态

### Card object

`CanonicalCard` 建议包含：

- `side`
- `slot_index`
- `template_id`
- `tier`
- `enchant_code`

不放：

- 冗余名称
- 高频变化数值快照
- UI 展示文案

### Skill object

`CanonicalSkill` 建议包含：

- `side`
- `slot_index`
- `template_id`
- `tier`

### Slot state object

`CanonicalSlotState` 建议包含：

- `side`
- `slot_index`
- `kind`
- `value`

第一版只使用：

- `kind = temperature`
- `value = high | low`

这样比直接写死 `temperature_state` 更利于后续扩展其他槽位环境语义，同时 projector 仍可以把当前版本投影到 `battle_slot_temperatures`。

## What does not belong in the bundle

以下内容第一版不进入 canonical bundle：

- provider 原始 payload
- 原始 replay blob
- 数据库内部主键
- 模板维表整数 id
- repository 级错误状态
- job 调度信息
- 下游宽表字段
- 临时排障字段

这些内容分别属于 provider、repository、job 或调试存储层。

## Normalization Rules

Normalizer 必须输出一份“可直接投影”的 bundle，因此需要固定以下规则。

### Identity rules

- `run_id` 必须存在，否则整个 bundle 无效
- `player_account_id` 必须存在，否则 run 不入分析事实
- `battle_id` 必须存在，否则该 battle 丢弃

### Ordering rules

- `battles[]` 按 `(recorded_at_utc, battle_id)` 排序
- `sequence` 从 0 或 1 生成，但必须稳定且全局一致
- `cards[]` / `skills[]` / `slot_states[]` 按 `(side, slot_index)` 排序

### Nullability rules

- 不可靠字段统一保留空值
- 不在 normalize 阶段做重推断的字段，projector 不再补

### Deduplication rules

- 同一 battle 内 `(side, slot_index)` 冲突时，normalize 阶段决定保留策略
- 进入 projector 前，bundle 内不应再出现重复槽位记录

### Compatibility rules

- 允许不同 provider 提供不同字段完整度
- 但 canonical 字段一旦存在，其语义必须稳定

## Database Projection Design

### Principle

数据库是 canonical bundle 的投影结果，而不是 bundle 定义来源。

第一版继续维持事实表方向：

- `runs`
- `battles`
- `card_templates`
- `skill_templates`
- `battle_cards`
- `battle_skills`
- `battle_slot_temperatures`

### Projection mapping

建议拆为三个 projector：

- `RunProjector`
- `BattleProjector`
- `TemplateProjector`

它们共同产出一个 `ProjectedBundleWriteModel`：

```text
ProjectedBundleWriteModel
  - run_row
  - battle_rows[]
  - card_template_keys[]
  - skill_template_keys[]
  - battle_card_rows[]
  - battle_skill_rows[]
  - battle_slot_temperature_rows[]
```

这样 repository 层可以一次事务写完。

### Run projection

`CanonicalRun` 投影到 `runs`：

- `opening_rank` -> `player_rank`
- `opening_rating` -> `player_rating`
- `opening_position` -> `player_position`
- `final_rank` -> `final_player_rank`
- `final_rating` -> `final_player_rating`
- `final_position` -> `final_player_position`

领域命名与数据库命名在 projector 边界处完成转换。

### Battle projection

`CanonicalBattle` 投影到 `battles`：

- 顶层 battle 事实进入 `battles`
- `player` / `opponent` 的摘要字段拆到 battle 行
- board 明细拆到三张明细表

### Slot state projection

第一版 projector 仅将：

- `kind = temperature`

投影到：

- `battle_slot_temperatures`

其他未来槽位状态即便进入 canonical bundle，也不强行写入当前事实表。

## Persistence Design

### Repository set

建议 Go 版 repository 结构：

- `RunRepository`
- `BattleRepository`
- `TemplateRepository`
- `TaskRepository`
- `CheckpointRepository`
- `JobRunRepository`

### Transaction boundary

单个 bundle batch 的事实写入必须在一个事务内完成：

1. upsert runs
2. upsert template dictionaries
3. write battles
4. write battle details
5. mark task success / failure

这样能保证 bundle 不会只写进一半。

### Idempotency

幂等键建议保持：

- `runs`: `run_id`
- `battles`: `battle_id`
- `battle_cards`: `(battle_id, side, slot_index)`
- `battle_skills`: `(battle_id, side, slot_index)`
- `battle_slot_temperatures`: `(battle_id, side, slot_index)`
- templates: `template_id`

但 battle 级明细不应永久采用“冲突即忽略”的策略。

推荐原则：

- battle 摘要允许 upsert
- battle 明细在 bundle 语义认为是完整快照时，应采用“replace for battle”语义

即：

1. 删除该 battle 旧 detail rows
2. 插入该 battle 当前 detail rows

这样更符合 canonical bundle 代表“该时刻完整事实”的语义。

## Job Design

### Job responsibilities

Go 版 ingest job 应只做：

- 请求 provider 获取一批原始输入
- 调用 normalizer 构造 bundle
- 调用 projector 得到写模型
- 调用 repositories 落库
- 更新 task/checkpoint
- 记录 progress / metrics / job runs

### Batch strategy

建议以 `bundle` 为最小处理单元，以小批次为事务单元。

推荐：

- 单 bundle 负责完整 run 事实
- 多个 bundle 组成一个 commit batch
- worker 并发在 batch 层，而不是在单 battle 层

### Retry strategy

重试边界放在 batch/job 层，不放在 normalizer/projector 内部。

normalizer/projector 应尽可能纯函数化，使失败可重放。

## Recommended Go Package Layout

建议目录：

```text
analytics_ingest/
  cmd/analytics-ingest/
  internal/app/
  internal/provider/
  internal/provider/<source_name>/
  internal/normalize/
  internal/domain/bundle/
  internal/project/
  internal/store/
  internal/store/sql/
  internal/job/
  internal/clock/
  internal/logging/
```

职责：

- `internal/domain/bundle`: canonical bundle 类型定义
- `internal/provider`: provider 接口与来源实现
- `internal/normalize`: provider raw input -> bundle
- `internal/project`: bundle -> write model
- `internal/store/sql`: repository 与事务实现
- `internal/job`: 编排逻辑

## Interface Sketch

建议接口风格：

```go
type Provider interface {
    FetchBatch(ctx context.Context, cursor Cursor, limit int) ([]RawEnvelope, Cursor, error)
}

type Normalizer interface {
    Normalize(ctx context.Context, raw RawEnvelope) (*bundle.CanonicalRunBundle, error)
}

type Projector interface {
    Project(*bundle.CanonicalRunBundle) (*ProjectedBundleWriteModel, error)
}

type Writer interface {
    WriteBatch(ctx context.Context, batch []*ProjectedBundleWriteModel) error
}
```

关键点：

- provider 输入是 raw envelope，不直接暴露数据库行
- normalizer 产出 canonical bundle
- projector 与 writer 分离

## Migration Direction

建议迁移顺序：

1. 先在 Go 中定义 canonical bundle 与 projector contract
2. 用离线 provider 打通最小闭环
3. 落地 SQL repositories
4. 迁移任务编排与 checkpoint
5. 最后再接新的正式 provider

不要先按 Python 文件结构逐个翻译，那样会把旧边界直接带进 Go 版本。

## Why this design

选择 `canonical bundle` 作为核心契约，而不是直接围绕数据库设计，主要因为：

- provider 会变化
- 新版本上线后输入会变化
- 分析库 schema 也会继续演进

如果上游和下游直接耦合，任何一端变化都会穿透整个系统。

而采用：

`provider -> normalize -> canonical bundle -> project -> repository`

之后，变化会被局部化：

- provider 变了，主要改 provider/normalizer
- 分析库变了，主要改 projector/repository
- job 和 bundle 可以保持稳定

## Open Decisions

以下点可以在实现阶段再细化，但不影响本设计主干：

- `sequence` 是否持久化到 `battles`
- `slot_state.kind/value` 是否第一版就做通用表
- task queue 最终是否沿用当前 `sync_run_tasks` 形式
- provider raw envelope 是否需要落本地调试文件

## Final Recommendation

本次 Go 重构不应被视为“把 Python sync 脚本翻译成 Go”，而应被视为：

“围绕 canonical bundle 重建一条 provider 可替换、数据库可演进的 ingest 主链路”。

推荐最终采用：

- provider 可替换
- canonical bundle 为中心
- projector 显式负责领域到关系表的映射
- repository 只负责持久化
- job 只负责编排

这是当前阶段最稳、最能承接后续正式版本接入的方案。
