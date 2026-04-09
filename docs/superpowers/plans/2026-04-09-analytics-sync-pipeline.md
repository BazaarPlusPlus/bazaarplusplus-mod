# Analytics Sync Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在仓库根目录落地一个基于 `uv` 的 Python 项目，定时从 Cloudflare `D1 + R2` 拉取旧 `ModCFServer` 数据，经兼容层拼接成 V3 语义对象后，清洗入用户自有的分析库；本地测试使用 SQLite provider，线上正式环境使用远程 MySQL provider。

**Architecture:** 项目采用五层结构：`extract` 负责 D1/R2 读取，`compat` 负责把旧 `ModCFServer` 数据补齐为 V3 语义对象，`transform` 负责把 V3 语义对象投影到分析库行模型，`load` 负责 SQLite / MySQL 双 provider 的 DDL 与幂等写入，`jobs` 负责增量游标与定时任务入口。回填 `runs.player_account_id` 的职责明确放在前置兼容层；入库层只接收满足 schema 约束的对象。

**Tech Stack:** Python 3.12+, `uv`, `pytest`, `ruff`, `mypy`, Cloudflare Python SDK, `boto3`, SQLite stdlib driver, MySQL client library

---

## File Map

### New Python project (`analytics_sync/`)

- Create: `analytics_sync/pyproject.toml`
  - `uv` 项目元数据、依赖、工具配置
- Create: `analytics_sync/uv.lock`
  - 锁定依赖
- Create: `analytics_sync/README.md`
  - 不创建；按仓库规则保持最小文档
- Create: `analytics_sync/src/analytics_sync/__init__.py`
- Create: `analytics_sync/src/analytics_sync/cli.py`
  - 顶层 CLI 入口
- Create: `analytics_sync/src/analytics_sync/config.py`
  - 环境变量和配置加载
- Create: `analytics_sync/src/analytics_sync/logging.py`
  - 结构化日志初始化
- Create: `analytics_sync/src/analytics_sync/clock.py`
  - 时间注入辅助

### Extract layer

- Create: `analytics_sync/src/analytics_sync/extract/d1_client.py`
  - D1 查询封装
- Create: `analytics_sync/src/analytics_sync/extract/d1_queries.py`
  - 增量查询 SQL 和分页逻辑
- Create: `analytics_sync/src/analytics_sync/extract/r2_client.py`
  - R2 下载封装
- Create: `analytics_sync/src/analytics_sync/extract/raw_models.py`
  - D1 / R2 原始对象类型

### Compat layer

- Create: `analytics_sync/src/analytics_sync/compat/v3_models.py`
  - V3 语义对象模型
- Create: `analytics_sync/src/analytics_sync/compat/run_bundle_builder.py`
  - 旧格式 -> V3 语义对象
- Create: `analytics_sync/src/analytics_sync/compat/run_identity_fill.py`
  - `player_account_id` 前置补齐
- Create: `analytics_sync/src/analytics_sync/compat/battle_artifact_parser.py`
  - 从旧 replay artifact 中提取 cards / skills / temperatures

### Transform layer

- Create: `analytics_sync/src/analytics_sync/transform/row_models.py`
  - MySQL 表行模型
- Create: `analytics_sync/src/analytics_sync/transform/project_run.py`
- Create: `analytics_sync/src/analytics_sync/transform/project_battle.py`
- Create: `analytics_sync/src/analytics_sync/transform/project_templates.py`

### Load layer

- Create: `analytics_sync/src/analytics_sync/load/mysql_schema.py`
  - DDL 常量和 schema version
- Create: `analytics_sync/src/analytics_sync/load/sqlite_schema.py`
  - SQLite DDL 常量
- Create: `analytics_sync/src/analytics_sync/load/provider.py`
  - provider 协议与选择
- Create: `analytics_sync/src/analytics_sync/load/sqlite_client.py`
  - SQLite 连接与事务封装
- Create: `analytics_sync/src/analytics_sync/load/mysql_client.py`
  - MySQL 连接与事务封装
- Create: `analytics_sync/src/analytics_sync/load/template_repository.py`
  - `card_templates` / `skill_templates` upsert + id lookup
- Create: `analytics_sync/src/analytics_sync/load/run_repository.py`
- Create: `analytics_sync/src/analytics_sync/load/battle_repository.py`
- Create: `analytics_sync/src/analytics_sync/load/checkpoint_repository.py`
  - 增量游标持久化
- Create: `analytics_sync/src/analytics_sync/load/run_task_repository.py`
  - run 级任务持久化

### Jobs layer

- Create: `analytics_sync/src/analytics_sync/jobs/sync_job.py`
  - 单次同步主流程
- Create: `analytics_sync/src/analytics_sync/jobs/scheduler.py`
  - 定时循环
- Create: `analytics_sync/src/analytics_sync/jobs/result.py`
  - 同步统计与退出码

### Tests

- Create: `analytics_sync/tests/test_config.py`
- Create: `analytics_sync/tests/extract/test_d1_queries.py`
- Create: `analytics_sync/tests/compat/test_run_identity_fill.py`
- Create: `analytics_sync/tests/compat/test_run_bundle_builder.py`
- Create: `analytics_sync/tests/compat/test_battle_artifact_parser.py`
- Create: `analytics_sync/tests/transform/test_project_run.py`
- Create: `analytics_sync/tests/transform/test_project_battle.py`
- Create: `analytics_sync/tests/load/test_mysql_schema.py`
- Create: `analytics_sync/tests/load/test_sqlite_schema.py`
- Create: `analytics_sync/tests/load/test_run_task_repository.py`
- Create: `analytics_sync/tests/jobs/test_sync_job.py`
- Create: `analytics_sync/tests/fixtures/*.json`
  - 旧 `D1` / `R2` 样本

## Task 1: Scaffold the `uv` Python project

**Files:**
- Create: `analytics_sync/pyproject.toml`
- Create: `analytics_sync/src/analytics_sync/__init__.py`
- Create: `analytics_sync/src/analytics_sync/cli.py`
- Create: `analytics_sync/src/analytics_sync/config.py`
- Create: `analytics_sync/src/analytics_sync/logging.py`
- Create: `analytics_sync/tests/test_config.py`

- [ ] **Step 1: 先创建最小 `uv` 项目骨架**

命令：

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
uv init analytics_sync --package
```

然后把默认骨架调整成 `src/` 布局：

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
mkdir -p src/analytics_sync tests
mv analytics_sync src/
```

- [ ] **Step 2: 在 `pyproject.toml` 中固定工具链与依赖**

至少包含：

```toml
[project]
name = "analytics-sync"
version = "0.1.0"
requires-python = ">=3.12"
dependencies = [
  "boto3>=1.39.0",
  "cloudflare>=4.3.1",
  "mysql-connector-python>=9.0.0",
  "pydantic>=2.11.0",
]

[dependency-groups]
dev = [
  "mypy>=1.15.0",
  "pytest>=8.3.0",
  "ruff>=0.11.0",
]

[tool.pytest.ini_options]
testpaths = ["tests"]
```

- [ ] **Step 3: 先写配置加载测试**

在 `analytics_sync/tests/test_config.py` 先写：

```python
from analytics_sync.config import SyncConfig


def test_sync_config_reads_required_mysql_fields(monkeypatch):
    monkeypatch.setenv("BPP_MYSQL_HOST", "127.0.0.1")
    monkeypatch.setenv("BPP_MYSQL_PORT", "3306")
    monkeypatch.setenv("BPP_MYSQL_USER", "tester")
    monkeypatch.setenv("BPP_MYSQL_PASSWORD", "secret")
    monkeypatch.setenv("BPP_MYSQL_DATABASE", "bpp_analytics")

    config = SyncConfig.from_env()

    assert config.mysql.host == "127.0.0.1"
    assert config.mysql.port == 3306
    assert config.mysql.user == "tester"
```

- [ ] **Step 4: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/test_config.py -q
```

Expected:

- FAIL，提示 `analytics_sync.config` 或 `SyncConfig` 尚不存在

- [ ] **Step 5: 写最小配置与 CLI 实现**

在 `analytics_sync/src/analytics_sync/config.py` 至少实现：

```python
from dataclasses import dataclass
import os


@dataclass(frozen=True)
class MysqlConfig:
    host: str
    port: int
    user: str
    password: str
    database: str


@dataclass(frozen=True)
class SyncConfig:
    mysql: MysqlConfig

    @classmethod
    def from_env(cls) -> "SyncConfig":
        return cls(
            mysql=MysqlConfig(
                host=os.environ["BPP_MYSQL_HOST"],
                port=int(os.environ["BPP_MYSQL_PORT"]),
                user=os.environ["BPP_MYSQL_USER"],
                password=os.environ["BPP_MYSQL_PASSWORD"],
                database=os.environ["BPP_MYSQL_DATABASE"],
            )
        )
```

在 `analytics_sync/src/analytics_sync/cli.py` 先提供占位入口：

```python
from analytics_sync.config import SyncConfig


def main() -> int:
    SyncConfig.from_env()
    return 0
```

- [ ] **Step 6: 重跑测试并静态检查**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/test_config.py -q
uv run ruff check .
uv run mypy src
```

Expected:

- `pytest` PASS
- `ruff` PASS
- `mypy` PASS

- [ ] **Step 7: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Scaffold analytics sync uv project"
```

## Task 2: Define dual-provider schema, checkpoint model, and run task table

**Files:**
- Create: `analytics_sync/src/analytics_sync/load/mysql_schema.py`
- Create: `analytics_sync/src/analytics_sync/load/sqlite_schema.py`
- Create: `analytics_sync/src/analytics_sync/load/provider.py`
- Create: `analytics_sync/src/analytics_sync/load/checkpoint_repository.py`
- Create: `analytics_sync/src/analytics_sync/load/run_task_repository.py`
- Create: `analytics_sync/tests/load/test_mysql_schema.py`
- Create: `analytics_sync/tests/load/test_sqlite_schema.py`
- Create: `analytics_sync/tests/load/test_run_task_repository.py`

- [ ] **Step 1: 先写 schema 测试，固定七张业务表、检查点表和 run 任务表**

在 `analytics_sync/tests/load/test_mysql_schema.py` 先写：

```python
from analytics_sync.load.mysql_schema import TABLE_DDLS


def test_schema_contains_core_tables():
    ddl_blob = "\n".join(TABLE_DDLS)

    assert "CREATE TABLE runs" in ddl_blob
    assert "CREATE TABLE battles" in ddl_blob
    assert "CREATE TABLE card_templates" in ddl_blob
    assert "CREATE TABLE skill_templates" in ddl_blob
    assert "CREATE TABLE battle_cards" in ddl_blob
    assert "CREATE TABLE battle_skills" in ddl_blob
    assert "CREATE TABLE battle_slot_temperatures" in ddl_blob
    assert "CREATE TABLE sync_checkpoints" in ddl_blob
    assert "CREATE TABLE sync_run_tasks" in ddl_blob
```

在 `analytics_sync/tests/load/test_sqlite_schema.py` 再写：

```python
from analytics_sync.load.sqlite_schema import TABLE_DDLS


def test_sqlite_schema_contains_runs_and_battles():
    ddl_blob = "\n".join(TABLE_DDLS)

    assert "CREATE TABLE runs" in ddl_blob
    assert "CREATE TABLE battles" in ddl_blob
    assert "CREATE TABLE battle_cards" in ddl_blob
```

- [ ] **Step 2: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/load/test_mysql_schema.py -q
```

Expected:

- FAIL，提示 schema 模块尚不存在

- [ ] **Step 3: 写最小双 provider DDL 常量**

在 `analytics_sync/src/analytics_sync/load/mysql_schema.py` 和 `analytics_sync/src/analytics_sync/load/sqlite_schema.py` 写出：

- `runs`
- `battles`
- `card_templates`
- `skill_templates`
- `battle_cards`
- `battle_skills`
- `battle_slot_temperatures`
- `sync_checkpoints`
- `sync_run_tasks`

其中 `sync_checkpoints` 最小字段：

```sql
CREATE TABLE sync_checkpoints (
  source_name VARCHAR(64) PRIMARY KEY,
  cursor_updated_at DATETIME(6) NOT NULL,
  cursor_entity_id VARCHAR(64) NOT NULL,
  updated_at DATETIME(6) NOT NULL
)
```

以及 `sync_run_tasks`：

```sql
CREATE TABLE sync_run_tasks (
  id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
  task_type VARCHAR(32) NOT NULL,
  run_id VARCHAR(64) NOT NULL,
  status VARCHAR(32) NOT NULL,
  attempt_count INT NOT NULL,
  next_run_at DATETIME(6) NOT NULL,
  last_error TEXT NULL,
  created_at DATETIME(6) NOT NULL,
  updated_at DATETIME(6) NOT NULL,
  UNIQUE KEY uk_sync_run_tasks_task_type_run_id (task_type, run_id)
)
```

同时在 `analytics_sync/src/analytics_sync/load/provider.py` 定义最小协议：

```python
from typing import Protocol


class SqlProvider(Protocol):
    def initialize_schema(self) -> None: ...
```

- [ ] **Step 4: 重跑 schema 测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/load/test_mysql_schema.py -q
uv run pytest tests/load/test_sqlite_schema.py -q
```

Expected:

- PASS

- [ ] **Step 5: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Define analytics MySQL schema"
```

## Task 3: Implement D1 query client and incremental selection

**Files:**
- Create: `analytics_sync/src/analytics_sync/extract/d1_client.py`
- Create: `analytics_sync/src/analytics_sync/extract/d1_queries.py`
- Create: `analytics_sync/src/analytics_sync/extract/raw_models.py`
- Create: `analytics_sync/tests/extract/test_d1_queries.py`
- Create: `analytics_sync/tests/fixtures/d1_runs_page.json`
- Create: `analytics_sync/tests/fixtures/d1_battles_page.json`

- [ ] **Step 1: 先写查询构造测试**

在 `analytics_sync/tests/extract/test_d1_queries.py` 先写：

```python
from analytics_sync.extract.d1_queries import build_runs_query


def test_build_runs_query_orders_by_updated_at_and_run_id():
    sql = build_runs_query()

    assert "FROM runs" in sql
    assert "ORDER BY updated_at_utc, run_id" in sql
    assert "LIMIT :page_size" in sql
```

- [ ] **Step 2: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/extract/test_d1_queries.py -q
```

Expected:

- FAIL，提示查询模块尚不存在

- [ ] **Step 3: 实现最小查询构造和 D1 客户端**

在 `analytics_sync/src/analytics_sync/extract/d1_queries.py` 至少实现：

```python
def build_runs_query() -> str:
    return """
    SELECT run_id, player_account_id, status, hero_id, hero_name,
           started_at_utc, ended_at_utc, final_day, final_wins, final_losses,
           summary_schema_version, summary_object_key, created_at_utc, updated_at_utc
    FROM runs
    WHERE (updated_at_utc > :cursor_updated_at)
       OR (updated_at_utc = :cursor_updated_at AND run_id > :cursor_id)
    ORDER BY updated_at_utc, run_id
    LIMIT :page_size
    """.strip()
```

在 `analytics_sync/src/analytics_sync/extract/d1_client.py` 至少实现：

```python
class D1Client:
    def fetch_rows(self, sql: str, params: dict[str, object]) -> list[dict[str, object]]:
        ...
```

实现先只包 Cloudflare SDK 调用，不做复杂重试。

- [ ] **Step 4: 重跑测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/extract/test_d1_queries.py -q
```

Expected:

- PASS

- [ ] **Step 5: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Implement analytics D1 query client"
```

## Task 4: Implement R2 download client

**Files:**
- Create: `analytics_sync/src/analytics_sync/extract/r2_client.py`
- Create: `analytics_sync/tests/extract/test_r2_client.py`

- [ ] **Step 1: 先写 R2 对象键读取测试**

```python
from analytics_sync.extract.r2_client import R2ObjectRef


def test_object_ref_keeps_bucket_and_key():
    ref = R2ObjectRef(bucket="pvp-bucket", key="runs/a/b.json")

    assert ref.bucket == "pvp-bucket"
    assert ref.key == "runs/a/b.json"
```

- [ ] **Step 2: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/extract/test_r2_client.py -q
```

Expected:

- FAIL，提示 `r2_client` 尚不存在

- [ ] **Step 3: 实现最小 `boto3` 客户端封装**

在 `analytics_sync/src/analytics_sync/extract/r2_client.py` 至少实现：

```python
from dataclasses import dataclass


@dataclass(frozen=True)
class R2ObjectRef:
    bucket: str
    key: str


class R2Client:
    def get_bytes(self, ref: R2ObjectRef) -> bytes:
        ...
```

要求：

- endpoint 使用 R2 S3-compatible endpoint
- 只做 `get_object`
- 缺失对象时抛出明确异常，供上层决定是否跳过

- [ ] **Step 4: 重跑测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/extract/test_r2_client.py -q
```

Expected:

- PASS

- [ ] **Step 5: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Implement analytics R2 client"
```

## Task 5: Build the compatibility layer from old data to V3 semantics

**Files:**
- Create: `analytics_sync/src/analytics_sync/compat/v3_models.py`
- Create: `analytics_sync/src/analytics_sync/compat/run_identity_fill.py`
- Create: `analytics_sync/src/analytics_sync/compat/run_bundle_builder.py`
- Create: `analytics_sync/src/analytics_sync/compat/battle_artifact_parser.py`
- Create: `analytics_sync/tests/compat/test_run_identity_fill.py`
- Create: `analytics_sync/tests/compat/test_run_bundle_builder.py`
- Create: `analytics_sync/tests/compat/test_battle_artifact_parser.py`
- Create: `analytics_sync/tests/fixtures/legacy_run_summary.json`
- Create: `analytics_sync/tests/fixtures/legacy_battle_artifact.json`

- [ ] **Step 1: 先写 `player_account_id` 回填测试**

```python
from analytics_sync.compat.run_identity_fill import fill_run_player_account_id


def test_fill_run_player_account_id_uses_related_battle_when_summary_missing():
    run_row = {"run_id": "run-1", "player_account_id": None}
    battle_rows = [{"run_id": "run-1", "player_account_id": "player-123"}]

    filled = fill_run_player_account_id(run_row, battle_rows)

    assert filled["player_account_id"] == "player-123"
```

- [ ] **Step 2: 先写 V3 语义对象组装测试**

```python
from analytics_sync.compat.run_bundle_builder import build_v3_semantic_bundle


def test_build_v3_semantic_bundle_preserves_null_for_missing_fields():
    bundle = build_v3_semantic_bundle(
        run_row={"run_id": "run-1", "player_account_id": "player-123", "hero_id": None},
        run_summary={"status": "completed", "ended_at_utc": "2026-04-09T00:00:00Z"},
        battle_rows=[],
        battle_artifacts={},
    )

    assert bundle.run_projection.run_id == "run-1"
    assert bundle.run_projection.hero_id is None
    assert bundle.battle_projections == []
```

- [ ] **Step 3: 先写 battle artifact 解析测试**

```python
from analytics_sync.compat.battle_artifact_parser import parse_battle_components


def test_parse_battle_components_extracts_cards_skills_and_temperatures():
    result = parse_battle_components({
        "player_cards": [{"template_id": "card-a", "slot": 0, "tier": 2, "enchant": "burning"}],
        "player_skills": [{"template_id": "skill-a", "slot": 0, "tier": 1}],
        "player_temperatures": [{"slot": 1, "state": "high"}],
    })

    assert result.cards[0].template_id == "card-a"
    assert result.cards[0].card_tier == 2
    assert result.skills[0].template_id == "skill-a"
    assert result.temperatures[0].temperature_state == "high"
```

- [ ] **Step 4: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/compat -q
```

Expected:

- FAIL，提示兼容层尚不存在

- [ ] **Step 5: 实现最小兼容层**

要求：

- `run_identity_fill.py` 只负责补齐 run 的 `player_account_id`
- `run_bundle_builder.py` 产出内部 V3 语义对象，而不是直接写 MySQL 行
- `battle_artifact_parser.py` 只提取最小可复原字段：
  - cards: `side`, `slot_index`, `template_id`, `card_tier`, `enchant_code`
  - skills: `side`, `slot_index`, `template_id`, `skill_tier`
  - temperatures: `side`, `slot_index`, `temperature_state`
- `temperature_state` 只保留 `high` / `low`
- 缺失槽位即默认 `normal`，不产生记录

- [ ] **Step 6: 重跑兼容层测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/compat -q
```

Expected:

- PASS

- [ ] **Step 7: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Add analytics compatibility layer"
```

## Task 6: Project V3 semantic objects into MySQL row models

**Files:**
- Create: `analytics_sync/src/analytics_sync/transform/row_models.py`
- Create: `analytics_sync/src/analytics_sync/transform/project_run.py`
- Create: `analytics_sync/src/analytics_sync/transform/project_battle.py`
- Create: `analytics_sync/src/analytics_sync/transform/project_templates.py`
- Create: `analytics_sync/tests/transform/test_project_run.py`
- Create: `analytics_sync/tests/transform/test_project_battle.py`

- [ ] **Step 1: 先写 run 投影测试**

```python
from analytics_sync.transform.project_run import project_run_row


def test_project_run_row_requires_player_account_id():
    projected = project_run_row(
        run_projection={"run_id": "run-1", "player_account_id": "player-123", "status": "completed"}
    )

    assert projected["run_id"] == "run-1"
    assert projected["player_account_id"] == "player-123"
```

- [ ] **Step 2: 先写 battle 投影测试**

```python
from analytics_sync.transform.project_battle import project_battle_rows


def test_project_battle_rows_keeps_card_template_identity():
    rows = project_battle_rows(
        battle_projection={"battle_id": "battle-1", "run_id": "run-1", "recorded_at_utc": "2026-04-09T00:00:00Z"},
        cards=[{"side": "player", "slot_index": 0, "template_id": "card-a", "card_tier": 2, "enchant_code": "burning"}],
        skills=[],
        temperatures=[],
    )

    assert rows.card_refs[0].template_id == "card-a"
    assert rows.card_rows[0]["slot_index"] == 0
```

- [ ] **Step 3: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/transform -q
```

Expected:

- FAIL，提示投影模块尚不存在

- [ ] **Step 4: 实现最小投影层**

要求：

- `project_run.py` 在 `player_account_id` 缺失时抛出明确异常
- `project_battle.py` 产出：
  - `battles` 行
  - `battle_cards` 行
  - `battle_skills` 行
  - `battle_slot_temperatures` 行
  - 需要的模板引用列表
- `project_templates.py` 负责去重模板引用，避免重复 upsert

- [ ] **Step 5: 重跑投影测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/transform -q
```

Expected:

- PASS

- [ ] **Step 6: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Add analytics projection layer"
```

## Task 7: Implement SQLite/MySQL repositories and idempotent writes

**Files:**
- Create: `analytics_sync/src/analytics_sync/load/sqlite_client.py`
- Create: `analytics_sync/src/analytics_sync/load/mysql_client.py`
- Create: `analytics_sync/src/analytics_sync/load/template_repository.py`
- Create: `analytics_sync/src/analytics_sync/load/run_repository.py`
- Create: `analytics_sync/src/analytics_sync/load/battle_repository.py`
- Modify: `analytics_sync/src/analytics_sync/load/checkpoint_repository.py`
- Create: `analytics_sync/src/analytics_sync/load/run_task_repository.py`
- Create: `analytics_sync/tests/load/test_template_repository.py`
- Create: `analytics_sync/tests/load/test_battle_repository.py`
- Create: `analytics_sync/tests/load/test_sqlite_provider.py`
- Create: `analytics_sync/tests/load/test_run_task_repository.py`

- [ ] **Step 1: 先写 battle 明细重建策略测试**

```python
from analytics_sync.load.battle_repository import build_replace_statements


def test_build_replace_statements_deletes_old_battle_details_before_insert():
    statements = build_replace_statements("battle-1")

    assert statements[0].startswith("DELETE FROM battle_cards")
    assert statements[1].startswith("DELETE FROM battle_skills")
    assert statements[2].startswith("DELETE FROM battle_slot_temperatures")
```

- [ ] **Step 2: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/load/test_battle_repository.py -q
```

Expected:

- FAIL，提示 repository 尚不存在

- [ ] **Step 3: 实现最小双 provider repository**

要求：

- `sqlite_client.py`
  - 使用 `sqlite3` 建连
  - 支持本地文件库初始化
- `template_repository.py`
  - upsert `card_templates` / `skill_templates`
  - 返回整数主键映射
- `run_repository.py`
  - 按 `run_id` upsert
- `battle_repository.py`
  - 按 `battle_id` upsert battle 摘要
  - 删除旧明细
  - 插入当前完整明细
- `checkpoint_repository.py`
  - 读写 `sync_checkpoints`
- `run_task_repository.py`
  - upsert `sync_run_tasks`
  - claim due tasks
  - mark success / failure / skipped

要求：

- repository 层通过 provider 抽象选择 SQLite 或 MySQL
- SQLite provider 与 MySQL provider 的幂等行为保持一致

- [ ] **Step 4: 重跑 repository 测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/load/test_battle_repository.py tests/load/test_template_repository.py -q
uv run pytest tests/load/test_sqlite_provider.py -q
uv run pytest tests/load/test_run_task_repository.py -q
```

Expected:

- PASS

- [ ] **Step 5: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Implement analytics MySQL repositories"
```

## Task 8: Implement the sync job and scheduler

**Files:**
- Create: `analytics_sync/src/analytics_sync/jobs/sync_job.py`
- Create: `analytics_sync/src/analytics_sync/jobs/scheduler.py`
- Create: `analytics_sync/src/analytics_sync/jobs/result.py`
- Modify: `analytics_sync/src/analytics_sync/cli.py`
- Create: `analytics_sync/tests/jobs/test_sync_job.py`
- Create: `analytics_sync/tests/jobs/test_scheduler.py`

- [ ] **Step 1: 先写单次同步流程测试**

```python
from analytics_sync.jobs.result import SyncJobResult


def test_sync_job_result_tracks_processed_counts():
    result = SyncJobResult(runs_seen=3, runs_written=2, battles_written=5, skipped_runs=1)

    assert result.runs_seen == 3
    assert result.skipped_runs == 1
```

- [ ] **Step 2: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/jobs/test_sync_job.py -q
```

Expected:

- FAIL，提示 job 模块尚不存在

- [ ] **Step 3: 实现单次同步主流程**

`sync_job.py` 最小流程：

1. 读取 checkpoint
2. 分页查询 D1 runs / battles
3. 计算受影响的 `run_id`
4. upsert `sync_run_tasks`
5. 更新 checkpoint
6. claim 到期 `sync_run_tasks`
7. 拉取 run summary R2 对象
8. 读取关联 battles
9. 拉取 battle artifact R2 对象
10. 走兼容层补齐 V3 语义对象
11. 走投影层生成数据库行
12. 事务写入模板、run、battle、明细
13. 标记任务成功或失败

- [ ] **Step 4: 实现定时循环**

`scheduler.py` 最小 API：

```python
def run_forever(interval_seconds: int) -> None:
    ...
```

要求：

- 每轮执行一次 `sync_job`
- 失败时记录异常并等待下一轮
- 不在 scheduler 中做复杂退避策略
- 不直接以“最近一小时”作为唯一同步边界，而是依赖 checkpoint + task table

- [ ] **Step 5: 更新 CLI**

`cli.py` 至少支持：

```bash
uv run analytics-sync once
uv run analytics-sync loop --interval-seconds 300
```

- [ ] **Step 6: 重跑 jobs 测试和全量单元测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest -q
uv run ruff check .
uv run mypy src
```

Expected:

- `pytest` PASS
- `ruff` PASS
- `mypy` PASS

- [ ] **Step 7: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Add analytics sync job pipeline"
```

## Task 9: Add a local dry-run verification seam

**Files:**
- Modify: `analytics_sync/src/analytics_sync/cli.py`
- Create: `analytics_sync/tests/jobs/test_cli.py`
- Create: `analytics_sync/tests/fixtures/sample_sync_bundle.json`

- [ ] **Step 1: 先写 dry-run CLI 测试**

```python
from analytics_sync.cli import parse_args


def test_parse_args_supports_dry_run():
    args = parse_args(["once", "--dry-run"])

    assert args.command == "once"
    assert args.dry_run is True
```

- [ ] **Step 2: 跑测试确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest tests/jobs/test_cli.py -q
```

Expected:

- FAIL，提示 `parse_args` 或 `--dry-run` 尚不存在

- [ ] **Step 3: 实现 dry-run**

要求：

- `once --dry-run` 时完成：
  - D1 查询
  - 任务表入队
  - R2 下载
  - 兼容层补齐
  - 投影层生成行模型
- 不执行 MySQL 写入
- 默认不执行任何数据库写入
- 打印：
  - runs seen / written
  - battles written
  - cards written
  - skills written
- skipped runs

- [ ] **Step 3.5: 增加本地 SQLite 真写入模式**

要求：

- `once --provider sqlite --sqlite-path ./tmp/analytics.db` 时允许真实写入 SQLite
- 该模式用于本地联调，不影响远程 MySQL
- CLI 至少支持：

```bash
uv run analytics-sync once --provider sqlite --sqlite-path ./tmp/analytics.db
uv run analytics-sync once --provider mysql
```

- [ ] **Step 4: 跑 dry-run 测试和全量验证**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/analytics_sync
uv run pytest -q
uv run ruff check .
uv run mypy src
uv run python -m compileall src
```

Expected:

- 全部 PASS

- [ ] **Step 5: Commit**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
git add analytics_sync
git commit -m "Add analytics dry run mode"
```

## Self-Review

- Spec coverage:
  - `uv` 工具链: Task 1
  - D1 查询: Task 3
  - R2 下载: Task 4
  - 旧格式 -> V3 语义兼容层: Task 5
  - SQLite / MySQL 双 provider: Task 2, 7, 9
  - checkpoint + run task 双层同步控制: Task 2, 7, 8
  - `player_account_id` 前置补齐责任边界: Task 5, 6
  - scheduler / once 模式: Task 8
  - 可验证 dry-run: Task 9
- Placeholder scan:
  - 所有任务都给出明确文件、最小代码骨架和运行命令，无 `TODO` / `TBD`
- Type consistency:
  - `card_tier` / `skill_tier` / `temperature_state` / `player_account_id` 命名与 spec 一致
