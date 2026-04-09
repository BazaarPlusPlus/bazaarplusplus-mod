from __future__ import annotations

from typing import Protocol


class SqlProvider(Protocol):
    def initialize_schema(self) -> None: ...
