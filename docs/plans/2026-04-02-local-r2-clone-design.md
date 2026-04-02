# Local R2 Clone Design

**Date:** 2026-04-02

## Goal

为仓库增加一套本地数据镜像方案，满足以下目标：

- 不依赖远端 `D1`
- 仅以远端 `R2` 对象作为事实源
- 在本地保留一份可重复同步的对象镜像
- 从本地对象重建一份可查询的 `SQLite` 元数据库
- 支持手动同步和手动全量重建
- 为未来接入定时触发预留接口，但本次不实现 `cron`

## Background

当前 `ModCFServer` 的远端数据拆成两层：

- `R2` 保存原始 JSON payload
- `D1` 保存查询友好的元数据投影

这种线上设计适合在线服务，但不适合频繁做本地分析：

- 远端 `D1` 查询不方便
- 远端查询会持续产生读费
- 远端 `R2` 对象虽然完整，但直接分析体验差

因此这次方案改为：

- 以远端 `R2` 为唯一上游
- 把远端对象镜像到本地
- 在本地重建 `SQLite` 投影
- 日常分析全部走本地 `SQLite` 和本地文件

## Current Object Layout

当前代码已经把两类对象写入同一个 bucket：

- replay payload:
  - `battle-replays/<client_id>/<battle_id>/<payload_hash>.json`
- run summary payload:
  - `run-summaries/<client_id>/<run_id>/<payload_hash>.json`

对应实现见：

- `ModCFServer/src/features/uploadBattleArtifact.ts`
- `ModCFServer/src/features/uploadRunSummary.ts`

本设计不修改远端对象 key，也不要求修改上传逻辑。

## Scope

本设计覆盖：

- 本地目录布局
- 本地 `SQLite` 数据模型
- 从本地 R2 镜像重建元数据的规则
- 手动增量同步流程
- 手动全量重建流程
- 常用分析 SQL 的组织方式
- 未来定时执行的预留方式

## Non-Goals

本设计不覆盖：

- 远端 `D1` 导出或增量同步
- 双向同步
- 远端对象删除审计系统
- 线上对象格式改造
- `cron`、`launchd`、CI scheduler 等具体调度实现
- 新的线上 API 或 Worker 路由

## Directory Layout

本地镜像和元数据统一放在仓库根目录，和 `ModCFServer/` 并列。

```text
LocalR2Data/
  r2/
  meta/
    clone.db
    sync_state.json
scripts/
sql/
```

职责约定：

- `LocalR2Data/r2/`
  - 本地 R2 对象镜像
  - 直接保留 `battle-replays/` 和 `run-summaries/` 原始 JSON
- `LocalR2Data/meta/clone.db`
  - 本地 `SQLite` 查询库
- `LocalR2Data/meta/sync_state.json`
  - 最近一次同步状态
- `scripts/`
  - 手动同步和手动重建入口
- `sql/`
  - 本地 clone schema 与常用分析 SQL

## Design Summary

系统按三个阶段运行：

1. 镜像阶段
2. 扫描阶段
3. 投影阶段

### 1. 镜像阶段

使用 `rclone sync` 将远端 bucket 镜像到本地目录：

- 远端：`bazaarplusplus-pvp-battles`
- 本地：`LocalR2Data/r2/`

这一步的目标只有一个：让本地文件树成为远端对象的可分析副本。

### 2. 扫描阶段

扫描 `LocalR2Data/r2/` 下的 JSON 文件，按相对路径推导：

- `object_key`
- `object_kind`
- `client_id`
- 业务主键候选，例如 `battle_id` 或 `run_id`

对象类型规则：

- 路径前缀是 `battle-replays/` -> `battle_replay`
- 路径前缀是 `run-summaries/` -> `run_summary`
- 其他前缀先跳过，不入投影

### 3. 投影阶段

对新增或变化文件进行 JSON 解析，并把字段投影到本地 `SQLite`。

投影结果分成：

- 同步运行记录
- 同步错误记录
- R2 对象去重记录
- battle replay 元数据
- run summary 对象历史
- 每个 run 的最新状态投影

## Local SQLite Data Model

本地 `SQLite` 使用四张核心表，外加一张错误表和一个 latest view。

### 1. `sync_runs`

记录每次执行情况，便于排查失败和统计同步量。

```sql
CREATE TABLE IF NOT EXISTS sync_runs (
  sync_run_id TEXT PRIMARY KEY,
  started_at_utc TEXT NOT NULL,
  finished_at_utc TEXT NULL,
  mode TEXT NOT NULL,
  status TEXT NOT NULL,
  scanned_file_count INTEGER NOT NULL DEFAULT 0,
  changed_file_count INTEGER NOT NULL DEFAULT 0,
  parsed_file_count INTEGER NOT NULL DEFAULT 0,
  error_count INTEGER NOT NULL DEFAULT 0,
  error_message TEXT NULL
);
```

约束：

- `mode` 只允许 `sync` 或 `rebuild`
- `status` 只允许 `running`、`succeeded`、`failed`

### 1.1 `sync_run_errors`

记录单对象错误，避免一次 run 里多个坏文件时只剩最后一条错误信息。

```sql
CREATE TABLE IF NOT EXISTS sync_run_errors (
  sync_run_id TEXT NOT NULL,
  object_key TEXT NULL,
  local_path TEXT NULL,
  stage TEXT NOT NULL,
  error_message TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  FOREIGN KEY (sync_run_id) REFERENCES sync_runs(sync_run_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_sync_run_errors_run
  ON sync_run_errors(sync_run_id);
```

### 2. `r2_objects`

这是本地增量判断的核心表。

```sql
CREATE TABLE IF NOT EXISTS r2_objects (
  object_key TEXT PRIMARY KEY,
  local_path TEXT NOT NULL,
  object_kind TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  mtime_utc TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  last_seen_sync_run_id TEXT NOT NULL,
  parsed_at_utc TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_r2_objects_kind
  ON r2_objects(object_kind);
```

字段说明：

- `object_key`
  - 远端对象 key，对本地 clone 来说是稳定主键
- `local_path`
  - 本地文件绝对路径或相对仓库路径
- `object_kind`
  - `battle_replay` / `run_summary`
- `size_bytes`
  - 本地文件大小
- `mtime_utc`
  - 本地文件最后修改时间
- `content_hash`
  - 文件内容 hash，用于稳妥判断文件是否变化
- `last_seen_sync_run_id`
  - 最近一次看到该对象的同步 run
- `parsed_at_utc`
  - 最近一次成功解析时间

### 3. `battle_replays`

保存 replay 查询所需字段。

```sql
CREATE TABLE IF NOT EXISTS battle_replays (
  object_key TEXT PRIMARY KEY,
  battle_id TEXT NOT NULL,
  run_id TEXT NULL,
  client_id TEXT NULL,
  recorded_at_utc TEXT NOT NULL,
  day INTEGER NULL,
  hour INTEGER NULL,
  player_name TEXT NULL,
  player_account_id TEXT NULL,
  player_hero TEXT NULL,
  player_rank TEXT NULL,
  player_rating INTEGER NULL,
  player_level INTEGER NULL,
  opponent_name TEXT NULL,
  opponent_account_id TEXT NULL,
  opponent_hero TEXT NULL,
  opponent_rank TEXT NULL,
  opponent_rating INTEGER NULL,
  opponent_level INTEGER NULL,
  combat_kind TEXT NULL,
  result TEXT NULL,
  winner_combatant_id TEXT NULL,
  loser_combatant_id TEXT NULL,
  replay_schema_version INTEGER NULL,
  replay_size_bytes INTEGER NULL,
  FOREIGN KEY (object_key) REFERENCES r2_objects(object_key) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_battle_replays_recorded
  ON battle_replays(recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battle_replays_player
  ON battle_replays(player_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battle_replays_opponent
  ON battle_replays(opponent_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battle_replays_run
  ON battle_replays(run_id);
```

设计说明：

- 业务主键仍是 `object_key`
- 同一个 `battle_id` 如果远端将来允许重复版本，仍可保留最新对象级视角
- 需要按 `battle_id` 去重或聚合时，在查询层处理

### 4. `run_summaries`

保存 run summary 对象历史。

```sql
CREATE TABLE IF NOT EXISTS run_summaries (
  object_key TEXT PRIMARY KEY,
  run_id TEXT NOT NULL,
  client_id TEXT NULL,
  status TEXT NOT NULL,
  hero_id TEXT NULL,
  hero_name TEXT NULL,
  started_at_utc TEXT NULL,
  ended_at_utc TEXT NOT NULL,
  final_day INTEGER NULL,
  final_wins INTEGER NULL,
  final_losses INTEGER NULL,
  mmr INTEGER NULL,
  summary_schema_version INTEGER NULL,
  FOREIGN KEY (object_key) REFERENCES r2_objects(object_key) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_run_summaries_ended
  ON run_summaries(ended_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_run_summaries_status
  ON run_summaries(status, ended_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_run_summaries_run_id
  ON run_summaries(run_id);
```

设计说明：

- `run_summaries` 是对象级历史表，不保证每个 `run_id` 只有一行
- 同一个 `run_id` 如果远端保留了多个 summary payload，本地会全部保留
- 所有“按 run 取当前状态”的查询不得直接读 `run_summaries`，必须读 latest view

### 5. `run_summaries_latest`

保存每个 `run_id` 的最新状态视角，供 run 级分析查询使用。

```sql
CREATE VIEW IF NOT EXISTS run_summaries_latest AS
SELECT rs.*
FROM run_summaries rs
WHERE rs.object_key = (
  SELECT rs2.object_key
  FROM run_summaries rs2
  JOIN r2_objects ro2
    ON ro2.object_key = rs2.object_key
  WHERE rs2.run_id = rs.run_id
  ORDER BY rs2.ended_at_utc DESC, ro2.mtime_utc DESC, rs2.object_key DESC
  LIMIT 1
);
```

## Incremental Sync Rules

增量同步不依赖远端事件流，只依赖：

- 本地镜像文件
- `r2_objects`

单个文件的处理逻辑：

1. 通过相对路径得到 `object_key`
2. 推导 `object_kind`
3. 读取本地文件元信息
4. 查询 `r2_objects`
5. 如果未命中，则视为新增对象
6. 如果 `size_bytes` 或 `mtime_utc` 变化，则重新计算 `content_hash`
7. 如果 `content_hash` 变化，则重新解析并 upsert
8. 如果 `content_hash` 不变，则跳过业务解析，仅更新 `last_seen_sync_run_id`

一次 `sync` 的扫描和投影完成后，还需要做一次删除对账：

9. 仅当本次 `rclone sync`、目录扫描和数据库投影都成功结束时，删除 `last_seen_sync_run_id != current_sync_run_id` 的旧对象记录
10. 旧对象删除依赖外键级联，或按 `battle_replays` / `run_summaries` 先删、`r2_objects` 后删

这样可以保证：

- 本地 `clone.db` 反映远端当前对象集合
- 远端已经删除的对象不会长期污染本地查询
- run latest 视图会随着对象删除自动收敛

这样做的优点：

- 逻辑简单
- 可重复执行
- 不依赖远端 D1
- 以后接定时无需变更模型

## Full Rebuild Rules

全量重建基于本地镜像目录，不访问远端。

流程：

1. 创建一个新的 `sync_run`
2. 开启 `PRAGMA foreign_keys = ON`
3. 清空：
   - `battle_replays`
   - `run_summaries`
   - `r2_objects`
   - `sync_run_errors` 中当前 run 之前不需要保留的历史错误可按需要保留或单独清理，但不影响 rebuild 主流程
4. 扫描 `LocalR2Data/r2/`
5. 对所有受支持对象重新解析
6. 重新写入全部投影
7. 标记 `sync_run` 成功或失败

适用场景：

- schema 改动后重建
- 本地库损坏后恢复
- 解析逻辑升级后回填历史数据

## Object Parsing Rules

### Replay Objects

路径：

- `battle-replays/<client_id>/<battle_id>/<payload_hash>.json`

优先从 JSON 提取业务字段：

- `battle_id`
- `run_id`
- `recorded_at_utc`
- `day`
- `hour`
- `player_name`
- `player_account_id`
- `player_hero`
- `player_rank`
- `player_rating`
- `player_level`
- `opponent_name`
- `opponent_account_id`
- `opponent_hero`
- `opponent_rank`
- `opponent_rating`
- `opponent_level`
- `combat_kind`
- `result`
- `winner_combatant_id`
- `loser_combatant_id`
- `schema_version`

同时从路径提取：

- `client_id`

同时从本地文件提取：

- `replay_size_bytes`

### Run Summary Objects

路径：

- `run-summaries/<client_id>/<run_id>/<payload_hash>.json`

优先从 JSON 提取：

- `run_id`
- `status`
- `hero_id`
- `hero_name`
- `started_at_utc`
- `ended_at_utc`
- `final_day`
- `final_wins`
- `final_losses`
- `mmr`
- `schema_version`

同时从路径提取：

- `client_id`

### Parse Failure Policy

单对象解析失败时：

- 记录本次 `sync_run.error_count`
- 保留文件
- 不中断整次同步
- 将错误明细写入 `sync_run_errors`
- `sync_runs.error_message` 只保留摘要，例如第一条或最后一条错误的短说明

目标是保证同步尽可能前进，而不是因为一个坏对象卡住整批数据。

## Script Entry Points

本次只设计两个无交互脚本入口。

### 1. `scripts/sync-local-r2-data.*`

职责：

- 调用 `rclone sync`
- 扫描本地镜像
- 增量更新 `clone.db`
- 更新 `sync_state.json`

要求：

- 幂等
- 可重复执行
- 非交互
- 有明确 exit code

### 2. `scripts/rebuild-local-r2-metadata.*`

职责：

- 直接基于本地镜像全量重建 `clone.db`
- 不访问远端 R2
- 更新 `sync_state.json`

要求：

- 幂等
- 非交互
- 可以在 schema 升级后直接重跑

## `sync_state.json`

这份状态文件只保存最近一次执行摘要，不保存业务数据。

建议结构：

```json
{
  "last_sync_started_at_utc": "",
  "last_sync_finished_at_utc": "",
  "last_sync_status": "",
  "last_sync_run_id": "",
  "last_rclone_success": false
}
```

这份文件的作用是让脚本和使用者快速看到最近一次执行状态，不替代 `sync_runs`。

## SQL Organization

建议新增两类 SQL 文件。

### 1. `sql/local_r2_clone_schema.sql`

职责：

- 创建 `sync_runs`
- 创建 `sync_run_errors`
- 创建 `r2_objects`
- 创建 `battle_replays`
- 创建 `run_summaries`
- 创建 `run_summaries_latest`
- 创建索引

### 2. `sql/local_r2_clone_queries.sql`

职责：

- 存放常用分析 SQL
- 方便本地排查和一次性查询

推荐先放几条高频 SQL。

#### Query 1: 对手 id 是否出现在 player id 集合里

```sql
SELECT EXISTS (
  SELECT 1
  FROM battle_replays b
  WHERE b.opponent_account_id IS NOT NULL
    AND EXISTS (
      SELECT 1
      FROM battle_replays p
      WHERE p.player_account_id = b.opponent_account_id
    )
) AS has_match;
```

#### Query 2: 查看命中的 battle 明细

```sql
SELECT
  battle_id,
  recorded_at_utc,
  player_account_id,
  opponent_account_id,
  result
FROM battle_replays b
WHERE b.opponent_account_id IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM battle_replays p
    WHERE p.player_account_id = b.opponent_account_id
  )
ORDER BY recorded_at_utc DESC;
```

#### Query 3: 按 run 汇总 replay 数量

```sql
SELECT
  r.run_id,
  r.status,
  r.hero_name,
  r.ended_at_utc,
  COUNT(b.battle_id) AS battle_count
FROM run_summaries_latest r
LEFT JOIN battle_replays b
  ON b.run_id = r.run_id
GROUP BY r.run_id, r.status, r.hero_name, r.ended_at_utc
ORDER BY r.ended_at_utc DESC;
```

## Manual Sync Workflow

日常使用流程：

1. 执行 `scripts/sync-local-r2-data.*`
2. 脚本调用 `rclone sync`
3. 本地镜像刷新到 `LocalR2Data/r2/`
4. 脚本扫描目录并增量投影
5. 同步成功后执行未见对象清理
6. 分析时直接查询 `LocalR2Data/meta/clone.db`

维护流程：

1. 需要重建时执行 `scripts/rebuild-local-r2-metadata.*`
2. 基于本地镜像全量扫描
3. 重建 `clone.db`

## Future Scheduled Execution

本次不实现定时，但设计必须允许未来平滑接入：

- 同步入口必须无交互
- 同步入口必须幂等
- 同步结果必须落盘
- 同步失败必须有明确退出码

这样未来无论接：

- `cron`
- `launchd`
- 本地 GUI 按钮
- CI runner

都只需要调用同一个同步脚本，而不需要改数据模型或同步协议。

## Implementation Phases

建议实施顺序如下。

### Phase 1: 目录与 schema

- 建立 `LocalR2Data/` 目录约定
- 编写 `sql/local_r2_clone_schema.sql`
- 能初始化空 `clone.db`

### Phase 2: 全量重建

- 实现 `rebuild` 脚本
- 从现有本地镜像全量扫描
- 能稳定生成 `battle_replays` 与 `run_summaries`

### Phase 3: 增量同步

- 实现 `sync` 脚本
- 接入 `rclone sync`
- 只重新解析新增或变化对象

### Phase 4: 分析查询

- 整理 `sql/local_r2_clone_queries.sql`
- 补高频排查 SQL
- 验证 replay 与 run summary 的 join 查询

## Verification

建议按最小必要验证执行。

### Schema 验证

- 新建空库
- 执行 schema SQL
- 确认五张主表、错误表、latest view 和索引存在
- 确认外键删除行为符合预期

### Rebuild 验证

- 准备少量本地镜像样本
- 执行 `rebuild`
- 确认 `battle_replays` / `run_summaries` 行数正确

### Incremental Sync 验证

- 首次执行 `sync`
- 再执行一次无变化 `sync`
- 确认第二次不会重复导入未变化对象
- 人工删除一个远端对象并再次执行 `sync`
- 确认本地文件、`r2_objects` 和相关投影都会一起消失

### Query 验证

- 跑通常用分析 SQL
- 验证可查出 replay、run summary history 和 run-to-battle 聚合
- 验证 run 级查询走 `run_summaries_latest` 时不会因重复 summary 放大结果

## Acceptance Criteria

完成后应满足：

- 可以把远端 R2 镜像到本地
- 本地可以生成并维护 `clone.db`
- replay 元数据可查询
- run summary 历史可查询
- 每个 run 的最新状态可查询
- `sync` 可以重复执行且具备幂等性
- `sync` 成功后会清除远端已删除对象对应的本地投影
- `rebuild` 可以在不访问远端的情况下重建整库
- 未来接定时时无需调整 schema 或同步协议

## Notes

- 当前重点是“简单、稳定、可重建”，而不是一次性做最省扫描成本的复杂增量系统
- 在对象规模还可接受时，`rclone sync + 本地投影` 的复杂度与收益比最好
- 如果未来对象量显著增长，再考虑基于事件通知或前缀分片优化
