from __future__ import annotations

from datetime import UTC, datetime
from datetime import timedelta
import time
from collections.abc import Callable

from analytics_sync.config import SyncConfig
from analytics_sync.compat.v3_models import RunBundleUploadRequestV2
from analytics_sync.jobs.result import SyncJobResult
from analytics_sync.load.battle_repository import BattleRepository
from analytics_sync.load.provider import build_sql_client
from analytics_sync.load.run_repository import RunRepository
from analytics_sync.load.sync_job_run_repository import SyncJobRunRepository
from analytics_sync.load.run_task_repository import RunTaskRepository
from analytics_sync.load.sync_checkpoint_repository import SyncCheckpointRepository
from analytics_sync.load.template_repository import TemplateRepository
from analytics_sync.source.base import RunBundleProvider, SourceRunBundleItem
from analytics_sync.source.legacy_cloudflare_provider import LegacyCloudflareRunBundleProvider
from analytics_sync.transform.project_battle import project_battle_rows
from analytics_sync.transform.project_run import project_run_row

RUNS_SOURCE = "runs_d1"
RUNS_PAGE_SIZE = 100
TASK_TYPE_SYNC_RUN = "sync_run"
RUNNING_TASK_STALE_AFTER = timedelta(minutes=15)


def _utc_now() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


def _emit_progress(progress: Callable[[str], None] | None, message: str) -> None:
    if progress is not None:
        progress(message)


def _utc_before(duration: timedelta) -> str:
    return (datetime.now(UTC) - duration).isoformat().replace("+00:00", "Z")


def build_provider(config: SyncConfig) -> RunBundleProvider:
    return LegacyCloudflareRunBundleProvider.from_config(config)


def _merge_run_rank_fields(bundle: RunBundleUploadRequestV2) -> dict[str, object]:
    battles = bundle.battle_projections
    if not battles:
        return dict(bundle.run_projection)

    first_battle = battles[0]
    last_battle = battles[-1]
    merged = dict(bundle.run_projection)
    merged.update(
        {
            "player_rank": (
                first_battle.get("player_rank") if isinstance(first_battle.get("player_rank"), str) else None
            ),
            "player_rating": (
                first_battle.get("player_rating") if isinstance(first_battle.get("player_rating"), int) else None
            ),
            "player_position": (
                first_battle.get("player_position") if isinstance(first_battle.get("player_position"), int) else None
            ),
            "final_player_rank": (
                last_battle.get("player_rank") if isinstance(last_battle.get("player_rank"), str) else None
            ),
            "final_player_rating": (
                last_battle.get("player_rating") if isinstance(last_battle.get("player_rating"), int) else None
            ),
            "final_player_position": (
                last_battle.get("player_position") if isinstance(last_battle.get("player_position"), int) else None
            ),
        }
    )
    return merged


def _process_bundle(
    *,
    item: SourceRunBundleItem,
    run_repository: RunRepository,
    template_repository: TemplateRepository,
    battle_repository: BattleRepository,
    task_repository: RunTaskRepository,
) -> tuple[int, int]:
    run_id = item.bundle.run_projection.get("run_id")
    if not isinstance(run_id, str):
        return 0, 1

    player_account_id = item.bundle.run_projection.get("player_account_id")
    if not isinstance(player_account_id, str):
        task_repository.mark_failed(TASK_TYPE_SYNC_RUN, run_id, item.source_updated_at, "player_account_id missing")
        return 0, 1

    projected_run = project_run_row(_merge_run_rank_fields(item.bundle))
    run_repository.upsert(projected_run)

    battles_written = 0
    for battle in item.bundle.battle_projections:
        battle_id = battle.get("battle_id")
        if not isinstance(battle_id, str):
            continue
        components = item.bundle.battle_components.get(battle_id)
        if components is None:
            continue
        projected = project_battle_rows(
            battle_projection={
                "battle_id": battle["battle_id"],
                "run_id": battle["run_id"],
                "recorded_at_utc": battle["recorded_at_utc"],
            },
            cards=[
                {
                    "side": component.side,
                    "slot_index": component.slot_index,
                    "template_id": component.template_id,
                    "card_tier": component.card_tier,
                    "enchant_code": component.enchant_code,
                }
                for component in components.cards
            ],
            skills=[
                {
                    "side": component.side,
                    "slot_index": component.slot_index,
                    "template_id": component.template_id,
                    "skill_tier": component.skill_tier,
                }
                for component in components.skills
            ],
            temperatures=[
                {
                    "side": component.side,
                    "slot_index": component.slot_index,
                    "temperature_state": component.temperature_state,
                }
                for component in components.temperatures
            ],
        )
        card_ids = template_repository.upsert_card_templates(projected.card_template_ids)
        skill_ids = template_repository.upsert_skill_templates(projected.skill_template_ids)
        battle_repository.upsert_battle(
            battle_row={
                **battle,
                "battle_id": battle["battle_id"],
                "run_id": battle["run_id"],
                "recorded_at_utc": battle["recorded_at_utc"],
            },
            card_rows=[
                {
                    "battle_id": row["battle_id"],
                    "side": row["side"],
                    "slot_index": row["slot_index"],
                    "card_template_id": card_ids[str(row["template_id"])],
                    "card_tier": row.get("card_tier"),
                    "enchant_code": row.get("enchant_code"),
                }
                for row in projected.card_rows
            ],
            skill_rows=[
                {
                    "battle_id": row["battle_id"],
                    "side": row["side"],
                    "slot_index": row["slot_index"],
                    "skill_template_id": skill_ids[str(row["template_id"])],
                    "skill_tier": row.get("skill_tier"),
                }
                for row in projected.skill_rows
            ],
            temperature_rows=projected.temperature_rows,
        )
        battles_written += 1

    task_repository.mark_succeeded(TASK_TYPE_SYNC_RUN, run_id, item.source_updated_at)
    return battles_written, 0


def _record_job_run(
    *,
    repository: SyncJobRunRepository,
    config: SyncConfig,
    dry_run: bool,
    started_at: str,
    result: SyncJobResult,
) -> None:
    repository.insert_job_run(
        {
            "job_name": "sync_once",
            "provider": config.sql_provider,
            "status": result.status,
            "dry_run": 1 if dry_run else 0,
            "started_at": started_at,
            "finished_at": _utc_now(),
            "duration_ms": result.duration_ms,
            "runs_seen": result.runs_seen,
            "runs_written": result.runs_written,
            "battles_seen": result.battles_seen,
            "battles_written": result.battles_written,
            "skipped_runs": result.skipped_runs,
            "pending_tasks": result.pending_tasks,
            "failed_tasks": result.failed_tasks,
            "checkpoint_source": result.checkpoint_source,
            "checkpoint_updated_at": result.checkpoint_updated_at,
            "checkpoint_entity_id": result.checkpoint_entity_id,
            "error_message": result.error_message,
        }
    )


def run_sync_job(
    config: SyncConfig,
    *,
    dry_run: bool,
    progress: Callable[[str], None] | None = None,
) -> SyncJobResult:
    started_at = _utc_now()
    started_monotonic = time.monotonic()
    sql_client = build_sql_client(config)
    if not dry_run:
        sql_client.initialize_schema()

    checkpoint_repository = SyncCheckpointRepository(sql_client)
    job_run_repository = SyncJobRunRepository(sql_client) if not dry_run else None
    cursor = checkpoint_repository.get_cursor(RUNS_SOURCE)
    checkpoint_updated_at = cursor.updated_at
    checkpoint_entity_id = cursor.entity_id
    runs_seen = 0
    runs_written = 0
    battles_seen = 0
    battles_written = 0
    skipped_runs = 0
    pending_tasks = 0
    failed_tasks = 0

    try:
        provider = build_provider(config)
        _emit_progress(
            progress,
            f"phase=discover cursor_updated_at={cursor.updated_at} cursor_entity_id={cursor.entity_id} limit={RUNS_PAGE_SIZE}",
        )
        discovered_refs = provider.discover_run_refs(cursor, RUNS_PAGE_SIZE)
        runs_seen = len(discovered_refs)
        _emit_progress(progress, f"phase=discover discovered_runs={runs_seen}")

        if dry_run:
            run_ids = [ref.run_id for ref in discovered_refs]
            items = provider.fetch_run_bundles(run_ids)
            _emit_progress(progress, f"phase=execute fetched_runs={len(items)} dry_run=1")
            for item in items:
                battles_seen += len(item.bundle.battle_projections)
        else:
            with sql_client.transaction() as write_session:
                task_repository = RunTaskRepository(write_session)
                if discovered_refs:
                    task_repository.upsert_tasks(
                        [
                            (TASK_TYPE_SYNC_RUN, ref.run_id, "pending", ref.source_updated_at)
                            for ref in discovered_refs
                        ]
                    )

                    last_ref = discovered_refs[-1]
                    checkpoint_updated_at = last_ref.source_updated_at
                    checkpoint_entity_id = last_ref.source_entity_id
                    SyncCheckpointRepository(write_session).upsert_cursor(
                        RUNS_SOURCE,
                        checkpoint_updated_at,
                        checkpoint_entity_id,
                    )

                claimed_run_ids = task_repository.claim_run_ids(
                    TASK_TYPE_SYNC_RUN,
                    RUNS_PAGE_SIZE,
                    claimed_at=_utc_now(),
                    stale_before=_utc_before(RUNNING_TASK_STALE_AFTER),
                )
            _emit_progress(progress, f"phase=execute claimed_runs={len(claimed_run_ids)}")
            items = provider.fetch_run_bundles(claimed_run_ids)
            items_by_run_id: dict[str, SourceRunBundleItem] = {}
            for bundle_item in items:
                bundle_run_id = bundle_item.bundle.run_projection.get("run_id")
                if isinstance(bundle_run_id, str):
                    items_by_run_id[bundle_run_id] = bundle_item

            total_claimed = len(claimed_run_ids)
            with sql_client.transaction() as write_session:
                run_repository = RunRepository(write_session)
                template_repository = TemplateRepository(write_session)
                battle_repository = BattleRepository(write_session)
                task_repository = RunTaskRepository(write_session)

                for index, run_id in enumerate(claimed_run_ids, start=1):
                    claimed_item = items_by_run_id.get(run_id)
                    if claimed_item is None:
                        task_repository.mark_failed(TASK_TYPE_SYNC_RUN, run_id, _utc_now(), "bundle missing")
                        skipped_runs += 1
                        if index == 1 or index == total_claimed or index % 10 == 0:
                            _emit_progress(
                                progress,
                                f"phase=execute processed_runs={index}/{total_claimed} runs_written={runs_written} battles_written={battles_written} skipped_runs={skipped_runs}",
                            )
                        continue
                    battles_seen += len(claimed_item.bundle.battle_projections)
                    written_for_run, skipped_for_run = _process_bundle(
                        item=claimed_item,
                        run_repository=run_repository,
                        template_repository=template_repository,
                        battle_repository=battle_repository,
                        task_repository=task_repository,
                    )
                    battles_written += written_for_run
                    if skipped_for_run == 0:
                        runs_written += 1
                    skipped_runs += skipped_for_run
                    if index == 1 or index == total_claimed or index % 10 == 0:
                        _emit_progress(
                            progress,
                            f"phase=execute processed_runs={index}/{total_claimed} runs_written={runs_written} battles_written={battles_written} skipped_runs={skipped_runs}",
                        )

                pending_tasks = task_repository.count_by_status(TASK_TYPE_SYNC_RUN, "pending")
                failed_tasks = task_repository.count_by_status(TASK_TYPE_SYNC_RUN, "failed")

        result = SyncJobResult(
            runs_seen=runs_seen,
            runs_written=0 if dry_run else runs_written,
            battles_seen=battles_seen,
            battles_written=0 if dry_run else battles_written,
            skipped_runs=skipped_runs,
            pending_tasks=pending_tasks,
            failed_tasks=failed_tasks,
            status="success",
            duration_ms=int((time.monotonic() - started_monotonic) * 1000),
            checkpoint_source=RUNS_SOURCE,
            checkpoint_updated_at=checkpoint_updated_at,
            checkpoint_entity_id=checkpoint_entity_id,
        )
        if job_run_repository is not None:
            _record_job_run(
                repository=job_run_repository,
                config=config,
                dry_run=dry_run,
                started_at=started_at,
                result=result,
            )
        return result
    except Exception as exc:
        result = SyncJobResult(
            runs_seen=runs_seen,
            runs_written=runs_written,
            battles_seen=battles_seen,
            battles_written=battles_written,
            skipped_runs=skipped_runs,
            pending_tasks=pending_tasks,
            failed_tasks=failed_tasks,
            status="failed",
            duration_ms=int((time.monotonic() - started_monotonic) * 1000),
            checkpoint_source=RUNS_SOURCE,
            checkpoint_updated_at=checkpoint_updated_at,
            checkpoint_entity_id=checkpoint_entity_id,
            error_message=str(exc),
        )
        if job_run_repository is not None:
            _record_job_run(
                repository=job_run_repository,
                config=config,
                dry_run=dry_run,
                started_at=started_at,
                result=result,
            )
        raise
