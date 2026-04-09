from __future__ import annotations

from collections import defaultdict
import json

from cloudflare import Cloudflare

from analytics_sync.compat.run_bundle_builder import build_run_bundle_upload_request_v2
from analytics_sync.compat.run_identity_fill import fill_run_player_account_id
from analytics_sync.config import SyncConfig
from analytics_sync.extract.artifact_decoder import decode_artifact_bytes
from analytics_sync.extract.d1_client import D1Client
from analytics_sync.extract.d1_queries import build_battles_for_run_ids_query, build_runs_query
from analytics_sync.extract.r2_client import R2Client, R2ObjectRef
from analytics_sync.load.sync_checkpoint_repository import SyncCursor
from analytics_sync.source.base import SourceRunBundleItem, SourceRunRef


def _group_battles_by_run_id(battle_rows: list[dict[str, object]]) -> dict[str, list[dict[str, object]]]:
    grouped: dict[str, list[dict[str, object]]] = defaultdict(list)
    for battle in battle_rows:
        run_id = battle.get("run_id")
        if isinstance(run_id, str):
            grouped[run_id].append(battle)
    return dict(grouped)


class LegacyCloudflareRunBundleProvider:
    def __init__(self, *, d1_client: D1Client, r2_client: R2Client, bucket: str) -> None:
        self._d1_client = d1_client
        self._r2_client = r2_client
        self._bucket = bucket

    @classmethod
    def from_config(cls, config: SyncConfig) -> "LegacyCloudflareRunBundleProvider":
        cf_client = Cloudflare(api_token=config.cloudflare.api_token)
        d1_client = D1Client(cf_client, config.cloudflare.account_id, config.cloudflare.d1_database_id)
        r2_client = R2Client.create(
            account_id=config.r2.account_id,
            access_key_id=config.r2.access_key_id,
            secret_access_key=config.r2.secret_access_key,
        )
        return cls(d1_client=d1_client, r2_client=r2_client, bucket=config.r2.bucket)

    def discover_run_refs(self, cursor: SyncCursor, limit: int) -> list[SourceRunRef]:
        runs = self._d1_client.fetch_rows(
            build_runs_query(),
            [cursor.updated_at, cursor.updated_at, cursor.entity_id, str(limit)],
        )
        refs: list[SourceRunRef] = []
        for run_row in runs:
            run_id = run_row.get("run_id")
            updated_at = run_row.get("updated_at_utc")
            if isinstance(run_id, str) and isinstance(updated_at, str):
                refs.append(SourceRunRef(source_updated_at=updated_at, source_entity_id=run_id, run_id=run_id))
        return refs

    def fetch_run_bundles(self, run_ids: list[str]) -> list[SourceRunBundleItem]:
        if not run_ids:
            return []
        runs = self._d1_client.fetch_rows(
            f"""
            SELECT run_id, player_account_id, status, hero_id, hero_name,
                   started_at_utc, ended_at_utc, final_day, final_wins, final_losses,
                   summary_schema_version, summary_object_key, created_at_utc, updated_at_utc
            FROM runs
            WHERE run_id IN ({','.join('?' for _ in run_ids)})
            ORDER BY updated_at_utc, run_id
            """.strip(),
            run_ids,
        )
        if not runs:
            return []

        run_ids_from_rows = [run_id for run in runs if isinstance((run_id := run.get("run_id")), str)]
        battle_rows = (
            self._d1_client.fetch_rows(build_battles_for_run_ids_query(len(run_ids_from_rows)), run_ids_from_rows)
            if run_ids_from_rows
            else []
        )
        battles_by_run_id = _group_battles_by_run_id(battle_rows)

        items: list[SourceRunBundleItem] = []
        for run_row in runs:
            run_id = run_row.get("run_id")
            summary_key = run_row.get("summary_object_key")
            updated_at = run_row.get("updated_at_utc")
            if not isinstance(run_id, str) or not isinstance(summary_key, str) or not isinstance(updated_at, str):
                continue
            battles = battles_by_run_id.get(run_id, [])
            filled_run = fill_run_player_account_id(run_row, battles)
            player_account_id = filled_run.get("player_account_id")
            if not isinstance(player_account_id, str):
                continue
            run_summary = json.loads(
                decode_artifact_bytes(self._r2_client.get_bytes(R2ObjectRef(bucket=self._bucket, key=summary_key)))
            )
            battle_artifacts: dict[str, dict[str, object]] = {}
            for battle_row in battles:
                battle_id = battle_row.get("battle_id")
                replay_key = battle_row.get("replay_object_key")
                if not isinstance(battle_id, str) or not isinstance(replay_key, str):
                    continue
                battle_artifacts[battle_id] = json.loads(
                    decode_artifact_bytes(
                        self._r2_client.get_bytes(R2ObjectRef(bucket=self._bucket, key=replay_key))
                    )
                )

            items.append(
                SourceRunBundleItem(
                    source_updated_at=updated_at,
                    source_entity_id=run_id,
                    bundle=build_run_bundle_upload_request_v2(
                        run_row=filled_run,
                        run_summary=run_summary,
                        battle_rows=battles,
                        battle_artifacts=battle_artifacts,
                    ),
                )
            )
        return items
