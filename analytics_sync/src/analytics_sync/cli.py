from __future__ import annotations

from analytics_sync.config import SyncConfig


def main() -> int:
    SyncConfig.from_env()
    return 0
