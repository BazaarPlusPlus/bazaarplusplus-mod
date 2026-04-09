# Analytics MySQL Schema Design

## Goal

在 mod 仓库根目录新增一个独立的 Python 数据清洗项目，定时从现有 Cloudflare `D1 + R2` 拉取上传数据，将旧 `ModCFServer` 数据拼接成以 `RunBundleUploadRequestV2` 语义为标准的中间事实，再清洗入用户自有的 MySQL 分析库。

本设计当前只定义分析库的目标 schema 和清洗映射边界，不定义最终统计宽表，也不开始实现具体 Python 代码。

## Scope

本设计覆盖：

- 旧 `ModCFServer` 的 `D1 + R2` 数据如何作为上游事实源
- 以 V3 `RunBundleUploadRequestV2` 语义为事实标准的清洗边界
- MySQL 分析库的核心表结构
- `run`、`battle`、`card`、`skill`、厨师温度槽位状态的保留策略
- 模板维表与最小可复原字段的取舍

本设计不覆盖：

- 最终分析宽表或报表模型
- replay 的长期保留与分析
- 完整数值快照、board 数值状态或逐帧重放
- V3 服务端正式协议的实现
- Python 项目的任务调度、部署和运行方式细节

## Upstream Facts

当前上游事实源来自旧 `ModCFServer`：

- `D1` 保存可查询的 `runs` / `battles` 元数据
- `D1` 行中保存 `summary_object_key` / `replay_object_key`
- `R2` 保存 run summary JSON 和 replay artifact JSON

清洗链路采用：

1. 从 `D1` 增量读取 `runs`、`battles`
2. 从 `D1` 行中读取对应 `R2 object key`
3. 读取 `R2` 原始对象
4. 将旧数据拼接并映射为接近 `RunBundleUploadRequestV2` 语义的中间事实
5. 将该中间事实投影进 MySQL 分析库

## Design Principles

### 1. V3 语义是事实标准

旧 `ModCFServer` 格式不是下游分析事实模型。兼容层只负责把旧数据拼成符合 V3 语义的中间对象。

### 2. 缺失字段保留 `null`

对于旧数据中不存在、或无法可靠推断的字段，第一版不做推断补值，统一保留 `null`。

### 3. 分析库不保留 replay

本轮清洗的目标是 run / battle / 构筑分析，不保留 replay 本体，也不把 replay 作为分析库事实的一部分。

### 4. Battle 需要可复原 `PlayerCard` / `PlayerSkill`

分析库必须能够通过 battle 级明细重新组装出双方 battle 时刻的：

- `PlayerCard[]`
- `PlayerSkill[]`

因此不能只保留 battle 级摘要，也不能只存一份不可查询的 JSON blob。

### 5. 只保留最小可复原字段

卡牌和技能明细不保留冗余名字与高噪音数值快照，只保留可以稳定复原构筑所需的最小字段。

### 6. 模板身份走维表

`template_id` 在 battle 明细中会高频重复，因此分析库不直接在明细表中重复存储模板字符串，而是通过模板维表映射到整数外键。

### 7. 槽位环境状态独立建模

厨师高温 / 低温位置不是卡牌自身属性，而是战场槽位状态；这类状态不能塞进 `battle_cards`，应单独建表。

## Chosen Model

核心业务表：

- `runs`
- `battles`
- `battle_cards`
- `battle_skills`
- `battle_slot_temperatures`

模板维表：

- `card_templates`
- `skill_templates`

整体边界：

- `runs` / `battles` 存局级事实
- `battle_cards` / `battle_skills` 存最小可复原构筑
- `battle_slot_temperatures` 存厨师槽位环境
- `card_templates` / `skill_templates` 负责模板去重

## Table Definitions

### `runs`

一条 run 一行，对应清洗后的 `run_projection` 语义。

字段：

- `run_id` `VARCHAR(64)` `PRIMARY KEY`
- `installation_id` `VARCHAR(64)` `NULL`
- `player_account_id` `VARCHAR(64)` `NOT NULL`
- `plugin_version` `VARCHAR(32)` `NULL`
- `game_version` `VARCHAR(32)` `NULL`
- `submitted_at_utc` `DATETIME(6)` `NULL`
- `status` `VARCHAR(32)` `NOT NULL`
- `hero_id` `VARCHAR(64)` `NULL`
- `hero_name` `VARCHAR(64)` `NULL`
- `player_rank` `VARCHAR(32)` `NULL`
- `player_rating` `INT` `NULL`
- `player_position` `INT` `NULL`
- `started_at_utc` `DATETIME(6)` `NULL`
- `ended_at_utc` `DATETIME(6)` `NOT NULL`
- `final_day` `INT` `NULL`
- `final_wins` `INT` `NULL`
- `final_losses` `INT` `NULL`
- `final_player_rank` `VARCHAR(32)` `NULL`
- `final_player_rating` `INT` `NULL`
- `final_player_position` `INT` `NULL`
- `created_at` `DATETIME(6)` `NOT NULL`
- `updated_at` `DATETIME(6)` `NOT NULL`

索引：

- `(player_account_id, ended_at_utc)`
- `(hero_id, ended_at_utc)`
- `(status, ended_at_utc)`

说明：

- 保留 `hero_name` 是可接受的，因为 run 摘要级读取经常直接展示该字段，且行数远小于明细表
- `installation_id` 来自兼容层；旧数据没有时允许为 `NULL`

### `battles`

一场 battle 一行，对应清洗后的 `battle_projection` 语义。

字段：

- `battle_id` `VARCHAR(64)` `PRIMARY KEY`
- `run_id` `VARCHAR(64)` `NOT NULL`
- `recorded_at_utc` `DATETIME(6)` `NOT NULL`
- `day` `INT` `NULL`
- `player_name` `VARCHAR(64)` `NULL`
- `player_account_id` `VARCHAR(64)` `NULL`
- `player_hero` `VARCHAR(64)` `NULL`
- `player_rank` `VARCHAR(32)` `NULL`
- `player_rating` `INT` `NULL`
- `player_level` `INT` `NULL`
- `opponent_name` `VARCHAR(64)` `NULL`
- `opponent_account_id` `VARCHAR(64)` `NULL`
- `opponent_hero` `VARCHAR(64)` `NULL`
- `opponent_rank` `VARCHAR(32)` `NULL`
- `opponent_rating` `INT` `NULL`
- `opponent_level` `INT` `NULL`
- `result` `VARCHAR(32)` `NULL`
- `created_at` `DATETIME(6)` `NOT NULL`
- `updated_at` `DATETIME(6)` `NOT NULL`

约束与索引：

- `FOREIGN KEY (run_id) REFERENCES runs(run_id)`
- `(run_id, recorded_at_utc)`
- `(player_account_id, recorded_at_utc)`
- `(opponent_account_id, recorded_at_utc)`
- `(player_hero, day)`
- `(opponent_hero, day)`

说明：

- `battles` 只保留 battle 级事实，不存 replay object key
- `player_name` / `opponent_name` 保留在 battle 级摘要中，但不下沉到明细表

### `card_templates`

卡牌模板维表，用于去重 `template_id`。

字段：

- `id` `BIGINT UNSIGNED` `PRIMARY KEY AUTO_INCREMENT`
- `template_id` `VARCHAR(64)` `NOT NULL`
- `created_at` `DATETIME(6)` `NOT NULL`

约束：

- `UNIQUE KEY (template_id)`

说明：

- 第一版不强行把更多静态维度塞进来
- 后续如果需要补卡牌分类、流派、稀有度等静态属性，可以扩在此表或其旁路维表

### `skill_templates`

技能模板维表，用于去重 `template_id`。

字段：

- `id` `BIGINT UNSIGNED` `PRIMARY KEY AUTO_INCREMENT`
- `template_id` `VARCHAR(64)` `NOT NULL`
- `created_at` `DATETIME(6)` `NOT NULL`

约束：

- `UNIQUE KEY (template_id)`

### `battle_cards`

一张卡一行，双方都记，用于复原 battle 时刻的 `PlayerCard[]`。

字段：

- `id` `BIGINT UNSIGNED` `PRIMARY KEY AUTO_INCREMENT`
- `battle_id` `VARCHAR(64)` `NOT NULL`
- `side` `VARCHAR(16)` `NOT NULL`
- `slot_index` `INT` `NOT NULL`
- `card_template_id` `BIGINT UNSIGNED` `NOT NULL`
- `card_tier` `INT` `NULL`
- `enchant_code` `VARCHAR(64)` `NULL`
- `created_at` `DATETIME(6)` `NOT NULL`

约束与索引：

- `FOREIGN KEY (battle_id) REFERENCES battles(battle_id)`
- `FOREIGN KEY (card_template_id) REFERENCES card_templates(id)`
- `UNIQUE KEY (battle_id, side, slot_index)`
- `(battle_id, side)`
- `(card_template_id)`
- `(card_template_id, card_tier)`

字段语义：

- `side` 取值固定为 `player` / `opponent`
- `slot_index` 表示 battle 时刻该 side 的槽位索引
- `card_tier` 对应用户确认后的 `card_tier` 命名，而不是 `card_level`
- `enchant_code` 只保留一个可复原附魔字段，不冗余存名称

说明：

- 不存 `template_id` 字符串，统一走维表
- 不存 `name`
- 不存额外数值快照

### `battle_skills`

一个技能一行，双方都记，用于复原 battle 时刻的 `PlayerSkill[]`。

字段：

- `id` `BIGINT UNSIGNED` `PRIMARY KEY AUTO_INCREMENT`
- `battle_id` `VARCHAR(64)` `NOT NULL`
- `side` `VARCHAR(16)` `NOT NULL`
- `slot_index` `INT` `NOT NULL`
- `skill_template_id` `BIGINT UNSIGNED` `NOT NULL`
- `skill_tier` `INT` `NULL`
- `created_at` `DATETIME(6)` `NOT NULL`

约束与索引：

- `FOREIGN KEY (battle_id) REFERENCES battles(battle_id)`
- `FOREIGN KEY (skill_template_id) REFERENCES skill_templates(id)`
- `UNIQUE KEY (battle_id, side, slot_index)`
- `(battle_id, side)`
- `(skill_template_id)`
- `(skill_template_id, skill_tier)`

说明：

- 技能没有 `enchant`
- 不存 `name`
- `slot_index` 保留技能位置，用于复原

### `battle_slot_temperatures`

厨师高温 / 低温槽位状态表，一条槽位状态一行。

字段：

- `id` `BIGINT UNSIGNED` `PRIMARY KEY AUTO_INCREMENT`
- `battle_id` `VARCHAR(64)` `NOT NULL`
- `side` `VARCHAR(16)` `NOT NULL`
- `slot_index` `INT` `NOT NULL`
- `temperature_state` `VARCHAR(16)` `NOT NULL`
- `created_at` `DATETIME(6)` `NOT NULL`

约束与索引：

- `FOREIGN KEY (battle_id) REFERENCES battles(battle_id)`
- `UNIQUE KEY (battle_id, side, slot_index)`
- `(battle_id, side)`

字段语义：

- `temperature_state` 取值固定为 `normal` / `high` / `low`

说明：

- 这是槽位环境状态，不是卡牌字段
- 单独建表是为了避免把战场状态耦合进 `battle_cards`

## Why Not JSON-only Loadouts

不采用“只在 `battles` 上挂 JSON 构筑”的原因：

- 后续按卡、技能、槽位、tier、附魔统计会非常不自然
- 很难对 `template_id` 建有效索引
- 无法稳定支持“复原构筑”和“做统计查询”两个目标

因此本设计接受 battle 明细表的适度行数膨胀，换取可分析性和可维护性。

## Reconstruction Rules

### Reconstruct `PlayerCard[]`

按 `(battle_id, side)` 查询：

1. `battle_cards`
2. `card_templates`
3. `battle_slot_temperatures`

再按 `slot_index` 合并，即可还原单侧 battle 卡组状态。

### Reconstruct `PlayerSkill[]`

按 `(battle_id, side)` 查询：

1. `battle_skills`
2. `skill_templates`

再按 `slot_index` 排序，即可还原单侧 battle 技能组状态。

## Cleaning and Mapping Rules

### 1. Run-level mapping

旧 `D1.runs` + `R2 run summary` 共同映射到 `runs`：

- `run_id` -> `runs.run_id`
- `status` -> `runs.status`
- `hero_id` / `hero_name` -> 对应 run 字段
- `started_at_utc` / `ended_at_utc` -> 对应时间字段
- `final_day` / `final_wins` / `final_losses` -> 对应终局字段
- 旧字段 `mmr` 映射为 `player_rating` 或 `final_player_rating` 时，只在来源语义明确时写入；否则保留 `NULL`
- 旧数据缺少 `installation_id`、`player_position`、`final_player_position` 时保留 `NULL`

### 2. Battle-level mapping

旧 `D1.battles` + `R2 replay artifact` 共同映射到 `battles` 与 battle 明细表：

- `battle_id` -> `battles.battle_id`
- `run_id` -> `battles.run_id`
- `recorded_at_utc` -> `battles.recorded_at_utc`
- `day` -> `battles.day`
- 双方名称、账号、英雄、rank、rating、level -> 对应 battle 字段
- `result` -> `battles.result`

### 3. Template dictionary mapping

从清洗出的 card / skill 明细中读取原始 `template_id`：

- 首先 upsert `card_templates(template_id)`
- 首先 upsert `skill_templates(template_id)`
- 取得维表整数主键后，再写入 `battle_cards` / `battle_skills`

### 4. Card mapping

从旧 replay artifact 或 battle manifest 中提取单张卡的最小可复原字段：

- `side`
- `slot_index`
- `template_id`
- `card_tier`
- `enchant_code`

无法可靠读取的字段保留 `NULL`。

### 5. Skill mapping

从旧 replay artifact 或 battle manifest 中提取单个技能的最小可复原字段：

- `side`
- `slot_index`
- `template_id`
- `skill_tier`

### 6. Temperature mapping

从 battle artifact 中提取厨师槽位温度状态：

- `side`
- `slot_index`
- `temperature_state`

如果该 battle 或该 side 不存在温度语义，则不写 `battle_slot_temperatures`。

## Nullability Policy

第一版统一采用以下策略：

- 源数据明确提供的字段，正常写入
- 源数据不存在的字段，保留 `NULL`
- 语义不确定、需要猜测的字段，保留 `NULL`
- 不为了“看起来完整”而做推断填充

这是有意的产品边界，不是临时妥协。

## Write Strategy

建议按 battle / run 幂等 upsert：

- `runs` 以 `run_id` 为幂等键
- `battles` 以 `battle_id` 为幂等键
- `battle_cards` 以 `(battle_id, side, slot_index)` 为幂等键
- `battle_skills` 以 `(battle_id, side, slot_index)` 为幂等键
- `battle_slot_temperatures` 以 `(battle_id, side, slot_index)` 为幂等键
- `card_templates` / `skill_templates` 以 `template_id` 为幂等键

对于单个 battle 的明细重建，允许采用：

1. upsert `battles`
2. 删除该 battle 旧的 `battle_cards` / `battle_skills` / `battle_slot_temperatures`
3. 插入该 battle 当前清洗出的完整明细

这样简单且稳定，避免局部 diff 带来的复杂性。

## Deferred Decisions

以下内容明确延后，不纳入本设计：

- 最终分析宽表
- 卡牌和技能的更丰富静态维表
- replay 相关表
- 通用化的战场槽位状态模型
- 大规模冷热分层或归档策略

## Recommended Next Step

下一步应针对这个 schema 设计一份实现计划，覆盖：

- Python 项目目录结构
- D1 读取与增量游标策略
- R2 对象下载与解码
- 旧格式到中间 V3 语义对象的兼容层
- MySQL DDL 与入库流程
- 最小验证方案
