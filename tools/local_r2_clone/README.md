# local_r2_clone

`local_r2_clone` 用来把远端 R2 bucket 镜像到本地，并把可分析字段投影到本地 `SQLite`。

它解决的是两个问题：

- 保留一份本地文件镜像，便于离线排查和重复分析
- 把原始 JSON 投影成结构化表，避免每次分析都全量扫文件

## 目录结构

执行后会在当前工具目录下生成如下工作区：

```text
tools/local_r2_clone/
├── clone_tool.py
├── rebuild_local_r2_metadata.py
├── sync_local_r2_data.py
├── sql/
│   ├── local_r2_clone_queries.sql
│   └── local_r2_clone_schema.sql
└── runtime/
    ├── meta/
    │   ├── clone.db
    │   └── sync_state.json
    └── r2/
```

`runtime/` 已被 `.gitignore` 忽略，不会提交到仓库。

## 目前支持的对象

- `battle-replays/.../*.json` -> `battle_replays`
- `run-summaries/.../*.json` -> `run_summaries`

其他前缀的 JSON 目前会被跳过，不写入投影。

## 前置条件

- 从仓库根目录执行命令
- 本地可用的 Python 3
- 若要执行同步，需要已安装并配置好 `rclone`
- `--remote` 需要使用 `rclone` 语法，例如 `remote-name:bucket`

## 配置 rclone 访问 R2

先在 Cloudflare R2 为目标 bucket 创建一组 S3 API 凭据，至少准备好：

- `Access Key ID`
- `Secret Access Key`
- `S3 API endpoint`，格式通常为 `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`

然后执行：

```bash
rclone config
```

按下面的值创建一个 remote，这里示例名称用 `r2`：

```text
n
name> r2
Storage> s3
provider> Cloudflare
env_auth> false
access_key_id> <YOUR_ACCESS_KEY_ID>
secret_access_key> <YOUR_SECRET_ACCESS_KEY>
region> auto
endpoint> https://<ACCOUNT_ID>.r2.cloudflarestorage.com
y/n> n
y/e/d> y
```

配置完成后，建议先验证 remote 是否可读：

```bash
rclone lsf r2:
rclone lsf r2:bazaarplusplus-pvp-battles
```

## 常用命令

同步远端 R2 并增量更新本地投影：

```bash
python -m tools.local_r2_clone.sync_local_r2_data --remote r2:bazaarplusplus-pvp-battles
```

只基于现有本地镜像重建元数据：

```bash
python -m tools.local_r2_clone.rebuild_local_r2_metadata
```

如果不想把工作区写到默认目录，可以显式指定：

```bash
python -m tools.local_r2_clone.sync_local_r2_data \
  --base-dir tools/local_r2_clone \
  --remote r2:bazaarplusplus-pvp-battles
```

## 产物说明

- `runtime/r2/`: 远端对象的本地镜像
- `runtime/meta/clone.db`: 分析用 `SQLite` 数据库
- `runtime/meta/sync_state.json`: 最近一次运行状态摘要

`clone.db` 里的主要表和视图：

- `sync_runs`: 每次 `sync` 或 `rebuild` 的执行记录
- `sync_run_errors`: 单文件解析错误明细
- `r2_objects`: 本地已见对象及其 hash、mtime、最后一次 seen run
- `battle_replays`: battle replay 元数据投影
- `run_summaries`: run summary 历史记录
- `run_summaries_latest`: 每个 `run_id` 的最新 summary 视图

## 表结构

完整 schema 如下，和 `tools/local_r2_clone/sql/local_r2_clone_schema.sql` 保持一致。

### `sync_runs`

```sql
CREATE TABLE IF NOT EXISTS sync_runs (
  sync_run_id TEXT PRIMARY KEY,
  started_at_utc TEXT NOT NULL,
  finished_at_utc TEXT NULL,
  mode TEXT NOT NULL CHECK (mode IN ('sync', 'rebuild')),
  status TEXT NOT NULL CHECK (status IN ('running', 'succeeded', 'failed')),
  scanned_file_count INTEGER NOT NULL DEFAULT 0,
  changed_file_count INTEGER NOT NULL DEFAULT 0,
  parsed_file_count INTEGER NOT NULL DEFAULT 0,
  error_count INTEGER NOT NULL DEFAULT 0,
  error_message TEXT NULL
);
```

### `sync_run_errors`

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

### `r2_objects`

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

### `battle_replays`

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

### `run_summaries`

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

### `run_summaries_latest`

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

## 行为说明

- `sync_local_r2_data.py` 会先执行 `rclone sync`，再扫描本地镜像并做增量投影
- 同步成功后，会删除本次未再次出现的旧对象投影
- `rebuild_local_r2_metadata.py` 不调用 `rclone`，只根据现有 `runtime/r2/` 全量重建数据库
- 文件内容未变化时会复用已有投影，避免重复解析
- 单个 JSON 解析失败不会中断整次投影，错误会写入 `sync_run_errors`

## 查询示例

常用分析 SQL 在 [sql/local_r2_clone_queries.sql](/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tools/local_r2_clone/sql/local_r2_clone_queries.sql)。

例如直接查看最近的 run：

```bash
sqlite3 tools/local_r2_clone/runtime/meta/clone.db \
  "SELECT run_id, status, hero_name, ended_at_utc FROM run_summaries_latest ORDER BY ended_at_utc DESC LIMIT 20;"
```
