from __future__ import annotations

import argparse
from pathlib import Path

from tools.local_r2_clone.clone_tool import ClonePaths, rebuild_metadata


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Rebuild local R2 metadata from the mirrored files.",
    )
    parser.add_argument(
        "--base-dir",
        default=str(Path(__file__).resolve().parent),
        help="Tool workspace directory. Defaults to this tool folder.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    rebuild_metadata(ClonePaths(Path(args.base_dir).resolve()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
