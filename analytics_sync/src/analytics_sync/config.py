from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path


def load_dotenv(path: Path) -> None:
    if not path.exists():
        return

    for raw_line in path.read_text().splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip())


@dataclass(frozen=True)
class CloudflareConfig:
    api_token: str
    account_id: str
    d1_database_id: str


@dataclass(frozen=True)
class R2Config:
    account_id: str
    access_key_id: str
    secret_access_key: str
    bucket: str


@dataclass(frozen=True)
class MysqlConfig:
    host: str
    port: int
    user: str
    password: str
    database: str


@dataclass(frozen=True)
class SyncConfig:
    sql_provider: str
    sqlite_path: Path | None
    mysql: MysqlConfig | None
    cloudflare: CloudflareConfig
    r2: R2Config

    @classmethod
    def from_env(cls) -> "SyncConfig":
        load_dotenv(Path(__file__).resolve().parents[2] / ".env")

        sql_provider = os.environ.get("BPP_SQL_PROVIDER", "sqlite")
        sqlite_path = Path(os.environ["BPP_SQLITE_PATH"]) if sql_provider == "sqlite" else None
        mysql = None
        if sql_provider == "mysql":
            mysql = MysqlConfig(
                host=os.environ["BPP_MYSQL_HOST"],
                port=int(os.environ["BPP_MYSQL_PORT"]),
                user=os.environ["BPP_MYSQL_USER"],
                password=os.environ["BPP_MYSQL_PASSWORD"],
                database=os.environ["BPP_MYSQL_DATABASE"],
            )

        return cls(
            sql_provider=sql_provider,
            sqlite_path=sqlite_path,
            mysql=mysql,
            cloudflare=CloudflareConfig(
                api_token=os.environ["BPP_CF_API_TOKEN"],
                account_id=os.environ["BPP_CF_ACCOUNT_ID"],
                d1_database_id=os.environ["BPP_CF_D1_DATABASE_ID"],
            ),
            r2=R2Config(
                account_id=os.environ["BPP_R2_ACCOUNT_ID"],
                access_key_id=os.environ["BPP_R2_ACCESS_KEY_ID"],
                secret_access_key=os.environ["BPP_R2_SECRET_ACCESS_KEY"],
                bucket=os.environ["BPP_R2_BUCKET"],
            ),
        )
