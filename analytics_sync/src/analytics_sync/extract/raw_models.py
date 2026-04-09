from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class D1RowPage:
    rows: list[dict[str, object]]
