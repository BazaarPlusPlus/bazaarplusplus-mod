from __future__ import annotations

import argparse
from pathlib import Path

from tools.local_r2_clone.clone_tool import ClonePaths, run_sync


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Mirror remote R2 data locally and incrementally project metadata.",
    )
    parser.add_argument(
        "--base-dir",
        default=str(Path(__file__).resolve().parent),
        help="Tool workspace directory. Defaults to this tool folder.",
    )
    parser.add_argument(
        "--remote",
        required=True,
        help="Remote R2 source in rclone syntax, for example remote-name:bucket.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    run_sync(
        ClonePaths(Path(args.base_dir).resolve()),
        remote=args.remote,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
