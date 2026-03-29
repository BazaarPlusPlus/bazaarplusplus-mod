# ModCFServer Production Design

**Date:** 2026-03-29

## Goal

为 `ModCFServer` 整理一版正式版后端方案，明确：

- 正式版继续使用 `Cloudflare Workers + D1 + R2`
- `D1` 负责结构化元数据、状态机、查询投影
- `R2` 负责大对象存储
- 表设计围绕 run upload、replay upload、ghost battle 查询来组织

这份文档描述的是目标设计，不等同于当前代码已经全部实现。

## Current Readiness

当前 `ModCFServer` 已经具备基础部署形态，但还不应视为“已经可以直接上线”的正式版状态。

当前情况：

- Worker 路由、D1 migration、R2 bucket 绑定、下载签名逻辑都已经在仓库内存在
- `ModCFServer/test` 当前全部通过
- `ModCFServer/wrangler.toml` 里的 `database_id` 仍是 `replace-me`
- 当前代码没有完整的账号绑定写入闭环，且现有服务端设计仍把 `uid` 当作身份中心
- run upload 还没有把原始 run payload 归档到 `R2`
- 本地执行 `npm run check` 时因缺少 `node` 类型定义失败；这是当前工作区依赖状态问题，不是已证明的运行时逻辑回归

结论：

- 现在可以开始准备部署环境和资源
- 但正式版上线前，仍应先补齐生产资源配置、run 原文归档方案、以及账号绑定闭环

## Production Architecture

正式版采用单 Worker API。

- `Worker`
  - 验签
  - nonce 去重
  - 写入接收表
  - 写入 `R2`
  - 投影 `pvp_battles`
  - 生成 replay 下载签名
- `D1`
  - 保存客户端注册信息
  - 保存请求幂等与防重放状态
  - 保存账号绑定关系
  - 保存 run/replay 接收记录
  - 保存 ghost battle 查询投影
- `R2`
  - 保存 replay 原始 payload
  - 保存 run 原始 snapshot

设计原则：

- `D1` 不是 blob 仓库
- 原始大 payload 优先落 `R2`
- `D1` 只存查询需要的结构化字段、对象定位信息和处理状态
- 读模型与接收层分离

## Data Model Layers

正式版把表分成 5 层：

1. 身份与认证
2. 设备与账号绑定
3. 上传接收层
4. 查询投影层
5. 运维与审计字段

推荐保留单库，不引入额外数据库。

## D1 Table Design

### 1. `registered_clients`

用途：客户端注册表。一个 `client_id` 对应一个上传身份。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `client_id` | `TEXT PRIMARY KEY` | 业务主键 |
| `install_id` | `TEXT NOT NULL` | 安装实例标识 |
| `purpose` | `TEXT NOT NULL` | `runs` 或 `replays` |
| `modulus_b64` | `TEXT NOT NULL` | RSA 公钥 modulus |
| `exponent_b64` | `TEXT NOT NULL` | RSA 公钥 exponent |
| `plugin_version` | `TEXT NULL` | 客户端版本 |
| `registered_at_utc` | `TEXT NOT NULL` | 首次或最近注册时间 |

索引建议：

- 主键已足够
- 如后续需要按安装查询，可补 `install_id`

### 2. `request_nonces`

用途：防重放 nonce 去重表。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `nonce_key` | `TEXT PRIMARY KEY` | 推荐格式：`{purpose}:{client_id}:{nonce}` |
| `created_at_utc` | `TEXT NOT NULL` | 插入时间 |

说明：

- 继续通过 cron 定期清理即可
- 不需要复杂建模

### 3. `client_player_account_bindings`

用途：设备客户端与 Bazaar `player_account_id` 的绑定历史。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `binding_id` | `TEXT PRIMARY KEY` | 唯一绑定记录 id |
| `client_id` | `TEXT NOT NULL` | 对应注册客户端 |
| `player_account_id` | `TEXT NOT NULL` | 游戏账号 `player_account_id` |
| `binding_source` | `TEXT NOT NULL` | 例如 `manual`, `observed`, `imported` |
| `confidence` | `INTEGER NOT NULL` | 0-100，便于后续治理 |
| `bound_at_utc` | `TEXT NOT NULL` | 绑定开始时间 |
| `unbound_at_utc` | `TEXT NULL` | 解绑时间；`NULL` 代表 active |

索引建议：

- `(player_account_id)`
- `(client_id)`
- 唯一部分索引：`client_id` where `unbound_at_utc IS NULL`
- 普通索引：`(player_account_id, unbound_at_utc)`

说明：

- 当前 mod 运行时稳定暴露的是 `player_account_id`，没有可可靠上报的 Bazaar `uid`
- 正式版应保留历史，不要只保留当前态
- `binding_source` 和 `confidence` 有助于后续修正错误绑定

### 4. `client_player_account_observations`

用途：记录某个客户端曾观察到哪些 `player_account_id`，用于审计和排查绑定漂移。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `client_id` | `TEXT NOT NULL` | 注册客户端 id |
| `player_account_id` | `TEXT NOT NULL` | 对战中出现或显式上报的账号 id |
| `first_seen_at_utc` | `TEXT NOT NULL` | 首次观察时间 |
| `last_seen_at_utc` | `TEXT NOT NULL` | 最近观察时间 |
| `evidence_count` | `INTEGER NOT NULL DEFAULT 1` | 累计观察次数 |

主键建议：

- `PRIMARY KEY (client_id, player_account_id)`

索引建议：

- `(client_id, last_seen_at_utc DESC)`
- `(player_account_id)`

说明：

- 这张表不参与 ghost battle 主查询，只用于保留客户端观察证据
- `evidence_count` 可以帮助过滤噪声映射

### 5. `run_uploads`

用途：run 上传接收总表。记录是否收到、原文在哪、是否完成投影。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `run_id` | `TEXT PRIMARY KEY` | run 主键 |
| `client_id` | `TEXT NOT NULL` | 上传客户端 |
| `install_id` | `TEXT NOT NULL` | 安装实例 |
| `payload_sha256` | `TEXT NOT NULL` | 内容 hash |
| `payload_object_key` | `TEXT NULL` | 指向 `R2` run snapshot |
| `payload_bytes` | `INTEGER NULL` | 原始字节数 |
| `schema_version` | `INTEGER NULL` | 上传 payload schema 版本 |
| `projection_version` | `INTEGER NOT NULL` | 当前投影逻辑版本 |
| `projection_status` | `TEXT NOT NULL` | `received/stored/projecting/projected/failed` |
| `projected_battle_count` | `INTEGER NOT NULL DEFAULT 0` | 成功投影出的 battle 数 |
| `projected_at_utc` | `TEXT NULL` | 成功投影时间 |
| `last_error_code` | `TEXT NULL` | 机器可读错误码 |
| `last_error_detail` | `TEXT NULL` | 诊断信息 |
| `created_at_utc` | `TEXT NOT NULL` | 首次接收时间 |
| `updated_at_utc` | `TEXT NOT NULL` | 最近更新时间 |

索引建议：

- `(projection_status, updated_at_utc DESC)`
- `(client_id, created_at_utc DESC)`
- `(payload_sha256)`

说明：

- 正式版推荐把 run 原始 payload 存 `R2`
- `run_uploads` 不再只是“收到了一个 hash”，而是 ingestion ledger
- 这张表为补投影和故障恢复提供依据

### 6. `replay_uploads`

用途：replay 元数据与对象索引表。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `battle_id` | `TEXT PRIMARY KEY` | battle 主键 |
| `client_id` | `TEXT NOT NULL` | 上传客户端 |
| `install_id` | `TEXT NOT NULL` | 安装实例 |
| `run_id` | `TEXT NULL` | 对应 run，可为空 |
| `payload_sha256` | `TEXT NOT NULL` | 内容 hash |
| `object_key` | `TEXT NOT NULL` | `R2` 对象 key |
| `payload_bytes` | `INTEGER NULL` | 字节数 |
| `schema_version` | `INTEGER NULL` | replay payload 版本 |
| `content_type` | `TEXT NOT NULL` | 默认 `application/json` |
| `created_at_utc` | `TEXT NOT NULL` | 首次写入时间 |
| `updated_at_utc` | `TEXT NOT NULL` | 最近更新时间 |
| `uploaded_at_utc` | `TEXT NOT NULL` | 最近一次有效上传时间 |

索引建议：

- `(run_id)`
- `(client_id, uploaded_at_utc DESC)`
- `(payload_sha256)`

说明：

- replay 是天然对象存储场景
- `D1` 只需要提供 battle 到对象的稳定映射

### 7. `pvp_battles`

用途：ghost battle 和历史查询的核心读模型。

建议字段：

| column | type | notes |
| --- | --- | --- |
| `battle_id` | `TEXT PRIMARY KEY` | battle 主键 |
| `run_id` | `TEXT NULL` | 来源 run |
| `source_client_id` | `TEXT NOT NULL` | 来源客户端 |
| `recorded_at_utc` | `TEXT NOT NULL` | 战斗记录时间 |
| `day` | `INTEGER NULL` | 游戏 day |
| `hour` | `INTEGER NULL` | 游戏 hour |
| `encounter_id` | `TEXT NULL` | 遭遇标识 |
| `player_name` | `TEXT NULL` | 玩家名 |
| `player_account_id` | `TEXT NULL` | 玩家账号 id |
| `player_hero` | `TEXT NULL` | 玩家英雄 |
| `player_rank` | `TEXT NULL` | 玩家段位文案 |
| `player_rating` | `INTEGER NULL` | 玩家 rating |
| `player_level` | `INTEGER NULL` | 玩家等级 |
| `opponent_name` | `TEXT NULL` | 对手名 |
| `opponent_account_id` | `TEXT NULL` | 对手账号 id |
| `opponent_hero` | `TEXT NULL` | 对手英雄 |
| `opponent_rank` | `TEXT NULL` | 对手段位文案 |
| `opponent_rating` | `INTEGER NULL` | 对手 rating |
| `opponent_level` | `INTEGER NULL` | 对手等级 |
| `combat_kind` | `TEXT NOT NULL` | 例如 `PVPCombat` |
| `result` | `TEXT NULL` | `win/loss/draw` 等 |
| `winner_combatant_id` | `TEXT NULL` | 胜者 combatant id |
| `loser_combatant_id` | `TEXT NULL` | 败者 combatant id |
| `summary_json` | `TEXT NULL` | 轻量展示快照 |
| `replay_available` | `INTEGER NOT NULL DEFAULT 0` | 是否可下载 replay |
| `projection_version` | `INTEGER NOT NULL` | 投影版本 |
| `created_at_utc` | `TEXT NOT NULL` | 创建时间 |
| `updated_at_utc` | `TEXT NOT NULL` | 更新时间 |

索引建议：

- `(recorded_at_utc DESC)`
- `(opponent_account_id, recorded_at_utc DESC)`
- `(player_account_id, recorded_at_utc DESC)`
- `(combat_kind, recorded_at_utc DESC)`
- `(opponent_account_id, combat_kind, result, recorded_at_utc DESC)`
- `(source_client_id, run_id)`

说明：

- 正式版建议把当前 `payload_json` 缩成 `summary_json`
- 真正的原文应该从 `R2` run snapshot 或 replay object 回溯
- 这张表应该只保留查询和展示所需字段

## R2 Object Design

正式版建议只把大对象存到 `R2`，分为两类：

- run snapshots
- replay payloads

### Run Snapshot Key

推荐格式：

`runs/{client_id}/{run_id}/{payload_sha256}.json`

说明：

- 同一 run 多次上传时，hash 变化可直接区分版本
- 不依赖时间戳作为唯一 key
- 便于按 client、run 批量清理或排查

### Replay Object Key

推荐格式：

`replays/{client_id}/{battle_id}/{payload_sha256}.json`

说明：

- 与当前实现思路一致，但建议去掉多余目录层级命名噪声
- `battle_id` 是 replay 的天然主键

### R2 Metadata

建议为对象补充 metadata：

- `content-type`
- `payload-sha256`
- `client-id`
- `run-id` 或 `battle-id`
- `schema-version`
- `uploaded-at-utc`

这样后续做对象治理时不必完全依赖 `D1`。

## Write Flow

### Run Upload

正式版建议流程：

1. 校验签名和时间戳
2. 校验并消费 nonce
3. 解析 payload，提取 `run_id`
4. 原始 run payload 写入 `R2`
5. upsert `run_uploads` 为 `stored`
6. 执行 battle projection，重建该 run 的 `pvp_battles`
7. 更新 `run_uploads` 为 `projected` 或 `failed`

这样做的好处：

- 即使投影失败，也不会丢原始输入
- 可做离线补投影
- 可追查客户端与服务端解析分歧

### Replay Upload

正式版建议流程：

1. 校验签名和时间戳
2. 校验并消费 nonce
3. 校验 `battle_id`
4. replay payload 写入 `R2`
5. upsert `replay_uploads`
6. 更新对应 `pvp_battles.replay_available`

## Query Flow

### Ghost Battles Against Me

查询链路建议保持：

1. 从 `client_player_account_bindings` 找 active `player_account_id`
2. 从 `pvp_battles` 查 `opponent_account_id = ?`
3. 用 `replay_uploads` 或 `pvp_battles.replay_available` 判断是否可下载 replay

这条查询链的重点是：

- `player_account_id` 既是客户端绑定身份，也是对战查询索引
- `pvp_battles` 是最终读模型

## Status Model

### `run_uploads.projection_status`

建议值：

- `received`
- `stored`
- `projecting`
- `projected`
- `failed`

建议语义：

- `received`: 已通过签名校验，准备接收
- `stored`: 原始 payload 已写入 `R2`
- `projecting`: 正在投影
- `projected`: 读模型已更新
- `failed`: 某一步失败，等待重试或人工排查

### Error Recording

正式版建议使用：

- `last_error_code`
- `last_error_detail`

避免只有一段自由文本，便于后续统计：

- `invalid_payload`
- `r2_put_failed`
- `projection_failed`
- `binding_resolution_failed`

## Current To Target Gaps

当前仓库到正式版目标仍有这些差距：

1. `run_uploads` 还没有 `R2` 原文归档能力
2. `pvp_battles` 仍承载较重的 JSON 负荷
3. `client_player_account_bindings` 还没有完整绑定写入链路
4. 表里缺少对象大小、版本、投影版本和标准错误码
5. replay 可用性目前通过查询时联表推断；正式版可显式冗余到 `pvp_battles.replay_available`
6. 生产部署资源配置尚未落地，`database_id` 仍是占位符

## Recommendation

推荐采用“分层方案”：

- `D1` 做结构化索引、状态和投影
- `R2` 做 run/replay 原文存储
- `pvp_battles` 保持轻量化查询模型
- `run_uploads` 升级为正式 ingestion ledger

这是当前仓库最稳妥的正式版演进方向：

- 和现有 Worker 路由兼容
- 和现有测试意图兼容
- 能支撑 ghost battle、replay 下载和后续补投影
- 不会把 `D1` 用成难维护的大对象仓库
