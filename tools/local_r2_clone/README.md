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

## 常用命令

同步远端 R2 并增量更新本地投影：

```bash
python -m tools.local_r2_clone.sync_local_r2_data --remote remote-name:bucket
```

只基于现有本地镜像重建元数据：

```bash
python -m tools.local_r2_clone.rebuild_local_r2_metadata
```

如果不想把工作区写到默认目录，可以显式指定：

```bash
python -m tools.local_r2_clone.sync_local_r2_data \
  --base-dir tools/local_r2_clone \
  --remote remote-name:bucket
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
