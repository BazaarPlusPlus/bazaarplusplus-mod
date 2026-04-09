from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from datetime import UTC, datetime

from analytics_sync.config import SyncConfig
from analytics_sync.jobs.result import SyncJobResult
from analytics_sync.jobs.sync_job import RUNS_SOURCE, run_sync_job
from analytics_sync.load.sqlite_client import SqliteClient
from analytics_sync.load.sync_checkpoint_repository import SyncCheckpointRepository


@dataclass(frozen=True)
class BackfillUntilResult:
    iterations: int
    reached_target: bool
    stalled: bool
    checkpoint_updated_at: str
    checkpoint_entity_id: str


def _parse_utc_timestamp(value: str) -> datetime:
    normalized = value.replace("Z", "+00:00")
    return datetime.fromisoformat(normalized).astimezone(UTC)


def _read_checkpoint_from_sqlite(config: SyncConfig) -> tuple[str, str]:
    if config.sqlite_path is None:
        return ("1970-01-01T00:00:00Z", "")
    cursor = SyncCheckpointRepository(SqliteClient(config.sqlite_path)).get_cursor(RUNS_SOURCE)
    return (cursor.updated_at, cursor.entity_id)


def run_backfill_until(
    config: SyncConfig,
    *,
    until_updated_at: str,
    dry_run: bool,
    progress: Callable[[str], None] | None = None,
    read_checkpoint: Callable[[], tuple[str, str]] | None = None,
    run_once: Callable[[], SyncJobResult] | None = None,
) -> BackfillUntilResult:
    read_checkpoint_fn = read_checkpoint or (lambda: _read_checkpoint_from_sqlite(config))
    run_once_fn = run_once or (lambda: run_sync_job(config, dry_run=dry_run, progress=progress))

    target = _parse_utc_timestamp(until_updated_at)
    iterations = 0
    stalled = False

    checkpoint_updated_at, checkpoint_entity_id = read_checkpoint_fn()
    while _parse_utc_timestamp(checkpoint_updated_at) < target:
        if progress is not None:
            progress(
                f"phase=backfill iteration={iterations + 1} current_checkpoint_updated_at={checkpoint_updated_at} target_updated_at={until_updated_at}"
            )
        previous_checkpoint = (checkpoint_updated_at, checkpoint_entity_id)
        run_once_fn()
        iterations += 1
        checkpoint_updated_at, checkpoint_entity_id = read_checkpoint_fn()
        if (checkpoint_updated_at, checkpoint_entity_id) == previous_checkpoint:
            stalled = True
            break

    reached_target = _parse_utc_timestamp(checkpoint_updated_at) >= target
    return BackfillUntilResult(
        iterations=iterations,
        reached_target=reached_target,
        stalled=stalled,
        checkpoint_updated_at=checkpoint_updated_at,
        checkpoint_entity_id=checkpoint_entity_id,
    )
