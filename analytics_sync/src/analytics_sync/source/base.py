from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol

from analytics_sync.compat.v3_models import RunBundleUploadRequestV2
from analytics_sync.load.sync_checkpoint_repository import SyncCursor


@dataclass(frozen=True)
class SourceRunRef:
    source_updated_at: str
    source_entity_id: str
    run_id: str


@dataclass(frozen=True)
class SourceRunBundleItem:
    source_updated_at: str
    source_entity_id: str
    bundle: RunBundleUploadRequestV2


class RunBundleProvider(Protocol):
    def discover_run_refs(self, cursor: SyncCursor, limit: int) -> list[SourceRunRef]:
        ...

    def fetch_run_bundles(self, run_ids: list[str]) -> list[SourceRunBundleItem]:
        ...
