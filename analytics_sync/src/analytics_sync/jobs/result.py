from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class SyncJobResult:
    runs_seen: int
    runs_written: int
    battles_seen: int
    battles_written: int
    skipped_runs: int
    pending_tasks: int = 0
    failed_tasks: int = 0
    status: str = "success"
    duration_ms: int = 0
    checkpoint_source: str | None = None
    checkpoint_updated_at: str | None = None
    checkpoint_entity_id: str | None = None
    error_message: str | None = None
