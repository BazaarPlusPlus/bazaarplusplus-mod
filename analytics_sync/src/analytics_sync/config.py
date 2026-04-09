from __future__ import annotations

from dataclasses import dataclass
import os


@dataclass(frozen=True)
class MysqlConfig:
    host: str
    port: int
    user: str
    password: str
    database: str


@dataclass(frozen=True)
class SyncConfig:
    mysql: MysqlConfig

    @classmethod
    def from_env(cls) -> "SyncConfig":
        return cls(
            mysql=MysqlConfig(
                host=os.environ["BPP_MYSQL_HOST"],
                port=int(os.environ["BPP_MYSQL_PORT"]),
                user=os.environ["BPP_MYSQL_USER"],
                password=os.environ["BPP_MYSQL_PASSWORD"],
                database=os.environ["BPP_MYSQL_DATABASE"],
            )
        )
