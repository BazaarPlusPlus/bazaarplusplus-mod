from __future__ import annotations

import argparse
from collections.abc import Sequence

from analytics_sync.config import SyncConfig
from analytics_sync.jobs.backfill_until import run_backfill_until
from analytics_sync.jobs.sync_job import run_sync_job


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="analytics-sync")
    subparsers = parser.add_subparsers(dest="command", required=True)

    once = subparsers.add_parser("once")
    once.add_argument("--dry-run", action="store_true")
    once.add_argument("--provider", choices=["sqlite", "mysql"], default=None)

    backfill_until = subparsers.add_parser("backfill-until")
    backfill_until.add_argument("--until-updated-at", required=True)
    backfill_until.add_argument("--dry-run", action="store_true")
    backfill_until.add_argument("--provider", choices=["sqlite", "mysql"], default=None)

    loop = subparsers.add_parser("loop")
    loop.add_argument("--interval-seconds", type=int, default=300)
    loop.add_argument("--provider", choices=["sqlite", "mysql"], default=None)

    return parser.parse_args(list(argv) if argv is not None else None)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    config = SyncConfig.from_env()
    if args.provider is not None:
        config = SyncConfig(
            sql_provider=args.provider,
            sqlite_path=config.sqlite_path if args.provider == "sqlite" else None,
            mysql=config.mysql,
            cloudflare=config.cloudflare,
            r2=config.r2,
        )

    if args.command == "once":
        result = run_sync_job(config, dry_run=args.dry_run, progress=print)
        print(f"provider={config.sql_provider}")
        if config.sql_provider == "sqlite" and config.sqlite_path is not None:
            print(f"sqlite_path={config.sqlite_path}")
        print(f"status={result.status}")
        print(f"duration_ms={result.duration_ms}")
        print(f"runs_seen={result.runs_seen}")
        print(f"runs_written={result.runs_written}")
        print(f"battles_seen={result.battles_seen}")
        print(f"battles_written={result.battles_written}")
        print(f"skipped_runs={result.skipped_runs}")
        print(f"pending_tasks={result.pending_tasks}")
        print(f"failed_tasks={result.failed_tasks}")
        print(f"checkpoint_source={result.checkpoint_source or ''}")
        print(f"checkpoint_updated_at={result.checkpoint_updated_at or ''}")
        print(f"checkpoint_entity_id={result.checkpoint_entity_id or ''}")
        return 0

    if args.command == "backfill-until":
        result = run_backfill_until(
            config,
            until_updated_at=args.until_updated_at,
            dry_run=args.dry_run,
            progress=print,
        )
        print(f"provider={config.sql_provider}")
        print(f"target_updated_at={args.until_updated_at}")
        print(f"iterations={result.iterations}")
        print(f"reached_target={str(result.reached_target).lower()}")
        print(f"stalled={str(result.stalled).lower()}")
        print(f"checkpoint_updated_at={result.checkpoint_updated_at}")
        print(f"checkpoint_entity_id={result.checkpoint_entity_id}")
        return 0

    print(
        f"loop mode is not implemented yet; provider={config.sql_provider}, interval={args.interval_seconds}"
    )
    return 0
