from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import subprocess
import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Callable

MODULE_DIR = Path(__file__).resolve().parent
SQL_DIR = MODULE_DIR / "sql"
DEFAULT_BASE_DIR = MODULE_DIR


def utc_now() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


@dataclass(frozen=True)
class ClonePaths:
    base_dir: Path

    @property
    def runtime_dir(self) -> Path:
        return self.base_dir / "runtime"

    @property
    def mirror_dir(self) -> Path:
        return self.runtime_dir / "r2"

    @property
    def meta_dir(self) -> Path:
        return self.runtime_dir / "meta"

    @property
    def db_path(self) -> Path:
        return self.meta_dir / "clone.db"

    @property
    def sync_state_path(self) -> Path:
        return self.meta_dir / "sync_state.json"


def default_sync_state() -> dict[str, Any]:
    return {
        "last_sync_started_at_utc": "",
        "last_sync_finished_at_utc": "",
        "last_sync_status": "",
        "last_sync_run_id": "",
        "last_rclone_success": False,
    }


def connect_db(db_path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(db_path)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON;")
    return connection


def ensure_workspace(paths: ClonePaths) -> None:
    paths.mirror_dir.mkdir(parents=True, exist_ok=True)
    paths.meta_dir.mkdir(parents=True, exist_ok=True)
    with connect_db(paths.db_path) as connection:
        connection.executescript((SQL_DIR / "local_r2_clone_schema.sql").read_text(encoding="utf-8"))
    if not paths.sync_state_path.exists():
        write_sync_state(paths, default_sync_state())


def write_sync_state(paths: ClonePaths, state: dict[str, Any]) -> None:
    paths.sync_state_path.write_text(
        json.dumps(state, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def build_sync_state(
    *,
    started_at_utc: str,
    finished_at_utc: str,
    status: str,
    sync_run_id: str,
    last_rclone_success: bool,
) -> dict[str, Any]:
    state = default_sync_state()
    state["last_sync_started_at_utc"] = started_at_utc
    state["last_sync_finished_at_utc"] = finished_at_utc
    state["last_sync_status"] = status
    state["last_sync_run_id"] = sync_run_id
    state["last_rclone_success"] = last_rclone_success
    return state


def rebuild_metadata(paths: ClonePaths) -> str:
    ensure_workspace(paths)
    sync_run_id = uuid.uuid4().hex
    started_at_utc = utc_now()
    stats = ProjectionStats()
    final_status = "failed"
    error_message: str | None = None
    try:
        with connect_db(paths.db_path) as connection:
            start_sync_run(connection, sync_run_id, "rebuild", started_at_utc)
            connection.execute("DELETE FROM battle_replays;")
            connection.execute("DELETE FROM run_summaries;")
            connection.execute("DELETE FROM r2_objects;")
            stats = project_mirror(connection, paths, sync_run_id, cleanup_missing=False)
            finish_sync_run(
                connection,
                sync_run_id,
                status="succeeded",
                finished_at_utc=utc_now(),
                stats=stats,
                error_message=None,
            )
        final_status = "succeeded"
        return sync_run_id
    except Exception as exc:
        error_message = str(exc)
        raise
    finally:
        finished_at_utc = utc_now()
        if final_status == "failed":
            with connect_db(paths.db_path) as connection:
                finish_sync_run(
                    connection,
                    sync_run_id,
                    status="failed",
                    finished_at_utc=finished_at_utc,
                    stats=stats,
                    error_message=error_message,
                )
        write_sync_state(
            paths,
            build_sync_state(
                started_at_utc=started_at_utc,
                finished_at_utc=finished_at_utc,
                status=final_status,
                sync_run_id=sync_run_id,
                last_rclone_success=False,
            ),
        )


def run_sync(
    paths: ClonePaths,
    *,
    remote: str,
    rclone_runner: Callable[..., Any] = subprocess.run,
) -> str:
    ensure_workspace(paths)
    sync_run_id = uuid.uuid4().hex
    started_at_utc = utc_now()
    stats = ProjectionStats()
    final_status = "failed"
    error_message: str | None = None
    rclone_success = False
    try:
        with connect_db(paths.db_path) as connection:
            start_sync_run(connection, sync_run_id, "sync", started_at_utc)

        result = rclone_runner(
            ["rclone", "sync", remote, str(paths.mirror_dir)],
            check=False,
        )
        if getattr(result, "returncode", 0) != 0:
            raise RuntimeError(f"rclone sync failed with exit code {result.returncode}")
        rclone_success = True

        with connect_db(paths.db_path) as connection:
            stats = project_mirror(connection, paths, sync_run_id, cleanup_missing=True)
            finish_sync_run(
                connection,
                sync_run_id,
                status="succeeded",
                finished_at_utc=utc_now(),
                stats=stats,
                error_message=None,
            )
        final_status = "succeeded"
        return sync_run_id
    except Exception as exc:
        error_message = str(exc)
        raise
    finally:
        finished_at_utc = utc_now()
        if final_status == "failed":
            with connect_db(paths.db_path) as connection:
                finish_sync_run(
                    connection,
                    sync_run_id,
                    status="failed",
                    finished_at_utc=finished_at_utc,
                    stats=stats,
                    error_message=error_message,
                )
        write_sync_state(
            paths,
            build_sync_state(
                started_at_utc=started_at_utc,
                finished_at_utc=finished_at_utc,
                status=final_status,
                sync_run_id=sync_run_id,
                last_rclone_success=rclone_success,
            ),
        )


@dataclass
class ProjectionStats:
    scanned_file_count: int = 0
    changed_file_count: int = 0
    parsed_file_count: int = 0
    error_count: int = 0
    error_message: str | None = None


def start_sync_run(
    connection: sqlite3.Connection,
    sync_run_id: str,
    mode: str,
    started_at_utc: str,
) -> None:
    connection.execute(
        """
        INSERT OR REPLACE INTO sync_runs (
          sync_run_id,
          started_at_utc,
          finished_at_utc,
          mode,
          status,
          scanned_file_count,
          changed_file_count,
          parsed_file_count,
          error_count,
          error_message
        ) VALUES (?, ?, NULL, ?, 'running', 0, 0, 0, 0, NULL)
        """,
        (sync_run_id, started_at_utc, mode),
    )


def finish_sync_run(
    connection: sqlite3.Connection,
    sync_run_id: str,
    *,
    status: str,
    finished_at_utc: str,
    stats: ProjectionStats,
    error_message: str | None,
) -> None:
    connection.execute(
        """
        UPDATE sync_runs
        SET finished_at_utc = ?,
            status = ?,
            scanned_file_count = ?,
            changed_file_count = ?,
            parsed_file_count = ?,
            error_count = ?,
            error_message = ?
        WHERE sync_run_id = ?
        """,
        (
            finished_at_utc,
            status,
            stats.scanned_file_count,
            stats.changed_file_count,
            stats.parsed_file_count,
            stats.error_count,
            error_message or stats.error_message,
            sync_run_id,
        ),
    )


def project_mirror(
    connection: sqlite3.Connection,
    paths: ClonePaths,
    sync_run_id: str,
    *,
    cleanup_missing: bool,
) -> ProjectionStats:
    stats = ProjectionStats()
    for file_path in sorted(paths.mirror_dir.rglob("*.json")):
        if not file_path.is_file():
            continue
        relative_path = file_path.relative_to(paths.mirror_dir).as_posix()
        object_kind = classify_object_key(relative_path)
        if object_kind is None:
            continue
        stats.scanned_file_count += 1
        process_object(
            connection,
            paths,
            file_path,
            relative_path,
            object_kind,
            sync_run_id,
            stats,
        )

    if cleanup_missing:
        connection.execute(
            "DELETE FROM r2_objects WHERE last_seen_sync_run_id != ?",
            (sync_run_id,),
        )
    return stats


def process_object(
    connection: sqlite3.Connection,
    paths: ClonePaths,
    file_path: Path,
    object_key: str,
    object_kind: str,
    sync_run_id: str,
    stats: ProjectionStats,
) -> None:
    file_bytes = file_path.read_bytes()
    stat_result = file_path.stat()
    size_bytes = stat_result.st_size
    mtime_utc = datetime.fromtimestamp(stat_result.st_mtime, UTC).isoformat().replace("+00:00", "Z")
    local_path = file_path.relative_to(paths.base_dir).as_posix()
    existing = connection.execute(
        """
        SELECT size_bytes, mtime_utc, content_hash, parsed_at_utc
        FROM r2_objects
        WHERE object_key = ?
        """,
        (object_key,),
    ).fetchone()
    if (
        existing is not None
        and existing["size_bytes"] == size_bytes
        and existing["mtime_utc"] == mtime_utc
    ):
        connection.execute(
            """
            UPDATE r2_objects
            SET local_path = ?, last_seen_sync_run_id = ?
            WHERE object_key = ?
            """,
            (local_path, sync_run_id, object_key),
        )
        return

    stats.changed_file_count += 1
    content_hash = hashlib.sha256(file_bytes).hexdigest()
    if existing is not None and existing["content_hash"] == content_hash:
        upsert_r2_object(
            connection,
            object_key=object_key,
            local_path=local_path,
            object_kind=object_kind,
            size_bytes=size_bytes,
            mtime_utc=mtime_utc,
            content_hash=content_hash,
            last_seen_sync_run_id=sync_run_id,
            parsed_at_utc=existing["parsed_at_utc"],
        )
        return

    try:
        payload = json.loads(file_bytes.decode("utf-8"))
        upsert_r2_object(
            connection,
            object_key=object_key,
            local_path=local_path,
            object_kind=object_kind,
            size_bytes=size_bytes,
            mtime_utc=mtime_utc,
            content_hash=content_hash,
            last_seen_sync_run_id=sync_run_id,
            parsed_at_utc=utc_now(),
        )
        if object_kind == "battle_replay":
            upsert_battle_replay(connection, object_key, payload, object_key.split("/", 3)[1], size_bytes)
        else:
            upsert_run_summary(connection, object_key, payload, object_key.split("/", 3)[1])
        stats.parsed_file_count += 1
    except Exception as exc:
        connection.execute("DELETE FROM battle_replays WHERE object_key = ?", (object_key,))
        connection.execute("DELETE FROM run_summaries WHERE object_key = ?", (object_key,))
        upsert_r2_object(
            connection,
            object_key=object_key,
            local_path=local_path,
            object_kind=object_kind,
            size_bytes=size_bytes,
            mtime_utc=mtime_utc,
            content_hash=content_hash,
            last_seen_sync_run_id=sync_run_id,
            parsed_at_utc=None,
        )
        stats.error_count += 1
        stats.error_message = str(exc)
        connection.execute(
            """
            INSERT INTO sync_run_errors (
              sync_run_id,
              object_key,
              local_path,
              stage,
              error_message,
              created_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            (sync_run_id, object_key, local_path, "parse", str(exc), utc_now()),
        )


def upsert_r2_object(
    connection: sqlite3.Connection,
    *,
    object_key: str,
    local_path: str,
    object_kind: str,
    size_bytes: int,
    mtime_utc: str,
    content_hash: str,
    last_seen_sync_run_id: str,
    parsed_at_utc: str | None,
) -> None:
    connection.execute(
        """
        INSERT INTO r2_objects (
          object_key,
          local_path,
          object_kind,
          size_bytes,
          mtime_utc,
          content_hash,
          last_seen_sync_run_id,
          parsed_at_utc
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(object_key) DO UPDATE SET
          local_path = excluded.local_path,
          object_kind = excluded.object_kind,
          size_bytes = excluded.size_bytes,
          mtime_utc = excluded.mtime_utc,
          content_hash = excluded.content_hash,
          last_seen_sync_run_id = excluded.last_seen_sync_run_id,
          parsed_at_utc = excluded.parsed_at_utc
        """,
        (
            object_key,
            local_path,
            object_kind,
            size_bytes,
            mtime_utc,
            content_hash,
            last_seen_sync_run_id,
            parsed_at_utc,
        ),
    )


def upsert_battle_replay(
    connection: sqlite3.Connection,
    object_key: str,
    payload: dict[str, Any],
    client_id: str,
    replay_size_bytes: int,
) -> None:
    manifest = expect_mapping(payload.get("battle_manifest"), "battle_manifest")
    participants = expect_mapping(manifest.get("participants"), "battle_manifest.participants")
    outcome = expect_mapping(manifest.get("outcome") or {}, "battle_manifest.outcome")
    replay_payload = expect_mapping(payload.get("replay_payload"), "replay_payload")
    battle_id = expect_text(payload.get("battle_id"), "battle_id")
    recorded_at_utc = expect_text(manifest.get("recorded_at_utc"), "battle_manifest.recorded_at_utc")
    connection.execute(
        """
        INSERT INTO battle_replays (
          object_key,
          battle_id,
          run_id,
          client_id,
          recorded_at_utc,
          day,
          hour,
          player_name,
          player_account_id,
          player_hero,
          player_rank,
          player_rating,
          player_level,
          opponent_name,
          opponent_account_id,
          opponent_hero,
          opponent_rank,
          opponent_rating,
          opponent_level,
          combat_kind,
          result,
          winner_combatant_id,
          loser_combatant_id,
          replay_schema_version,
          replay_size_bytes
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(object_key) DO UPDATE SET
          battle_id = excluded.battle_id,
          run_id = excluded.run_id,
          client_id = excluded.client_id,
          recorded_at_utc = excluded.recorded_at_utc,
          day = excluded.day,
          hour = excluded.hour,
          player_name = excluded.player_name,
          player_account_id = excluded.player_account_id,
          player_hero = excluded.player_hero,
          player_rank = excluded.player_rank,
          player_rating = excluded.player_rating,
          player_level = excluded.player_level,
          opponent_name = excluded.opponent_name,
          opponent_account_id = excluded.opponent_account_id,
          opponent_hero = excluded.opponent_hero,
          opponent_rank = excluded.opponent_rank,
          opponent_rating = excluded.opponent_rating,
          opponent_level = excluded.opponent_level,
          combat_kind = excluded.combat_kind,
          result = excluded.result,
          winner_combatant_id = excluded.winner_combatant_id,
          loser_combatant_id = excluded.loser_combatant_id,
          replay_schema_version = excluded.replay_schema_version,
          replay_size_bytes = excluded.replay_size_bytes
        """,
        (
            object_key,
            battle_id,
            text_or_none(payload.get("run_id")),
            client_id,
            recorded_at_utc,
            number_or_none(manifest.get("day")),
            number_or_none(manifest.get("hour")),
            text_or_none(participants.get("player_name")),
            text_or_none(participants.get("player_account_id")),
            text_or_none(participants.get("player_hero")),
            text_or_none(participants.get("player_rank")),
            number_or_none(participants.get("player_rating")),
            number_or_none(participants.get("player_level")),
            text_or_none(participants.get("opponent_name")),
            text_or_none(participants.get("opponent_account_id")),
            text_or_none(participants.get("opponent_hero")),
            text_or_none(participants.get("opponent_rank")),
            number_or_none(participants.get("opponent_rating")),
            number_or_none(participants.get("opponent_level")),
            text_or_none(manifest.get("combat_kind")),
            text_or_none(outcome.get("result")),
            text_or_none(outcome.get("winner_combatant_id")),
            text_or_none(outcome.get("loser_combatant_id")),
            number_or_none(replay_payload.get("version")),
            replay_size_bytes,
        ),
    )


def upsert_run_summary(
    connection: sqlite3.Connection,
    object_key: str,
    payload: dict[str, Any],
    client_id: str,
) -> None:
    run_id = expect_text(payload.get("run_id"), "run_id")
    status = expect_text(payload.get("status"), "status")
    ended_at_utc = expect_text(payload.get("ended_at_utc"), "ended_at_utc")
    connection.execute(
        """
        INSERT INTO run_summaries (
          object_key,
          run_id,
          client_id,
          status,
          hero_id,
          hero_name,
          started_at_utc,
          ended_at_utc,
          final_day,
          final_wins,
          final_losses,
          mmr,
          summary_schema_version
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(object_key) DO UPDATE SET
          run_id = excluded.run_id,
          client_id = excluded.client_id,
          status = excluded.status,
          hero_id = excluded.hero_id,
          hero_name = excluded.hero_name,
          started_at_utc = excluded.started_at_utc,
          ended_at_utc = excluded.ended_at_utc,
          final_day = excluded.final_day,
          final_wins = excluded.final_wins,
          final_losses = excluded.final_losses,
          mmr = excluded.mmr,
          summary_schema_version = excluded.summary_schema_version
        """,
        (
            object_key,
            run_id,
            client_id,
            status,
            text_or_none(payload.get("hero_id")),
            text_or_none(payload.get("hero_name")),
            text_or_none(payload.get("started_at_utc")),
            ended_at_utc,
            number_or_none(payload.get("final_day")),
            number_or_none(payload.get("final_wins")),
            number_or_none(payload.get("final_losses")),
            number_or_none(payload.get("mmr")),
            number_or_none(payload.get("schema_version")),
        ),
    )


def classify_object_key(object_key: str) -> str | None:
    if object_key.startswith("battle-replays/"):
        return "battle_replay"
    if object_key.startswith("run-summaries/"):
        return "run_summary"
    return None


def expect_mapping(value: Any, field_name: str) -> dict[str, Any]:
    if isinstance(value, dict):
        return value
    raise ValueError(f"{field_name} is required")


def expect_text(value: Any, field_name: str) -> str:
    text = text_or_none(value)
    if text is None:
        raise ValueError(f"{field_name} is required")
    return text


def text_or_none(value: Any) -> str | None:
    if isinstance(value, str):
        stripped = value.strip()
        return stripped or None
    return None


def number_or_none(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float) and value.is_integer():
        return int(value)
    return None


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Local R2 clone helper.")
    parser.add_argument("command", choices=["sync", "rebuild"])
    parser.add_argument(
        "--base-dir",
        default=str(DEFAULT_BASE_DIR),
        help="Tool workspace directory. Defaults to the tool folder itself.",
    )
    parser.add_argument(
        "--remote",
        help="Remote R2 source in rclone syntax. Required for sync.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    paths = ClonePaths(Path(args.base_dir).resolve())
    if args.command == "rebuild":
        rebuild_metadata(paths)
        return 0
    if not args.remote:
        parser.error("--remote is required for sync")
    run_sync(paths, remote=args.remote)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
