from __future__ import annotations

from typing import cast

from cloudflare import Cloudflare


class D1Client:
    def __init__(self, client: Cloudflare, account_id: str, database_id: str) -> None:
        self._client = client
        self._account_id = account_id
        self._database_id = database_id

    def fetch_rows(self, sql: str, params: list[str]) -> list[dict[str, object]]:
        response = self._client.d1.database.query(
            account_id=self._account_id,
            database_id=self._database_id,
            sql=sql,
            params=params,
        )
        if not response.result:
            return []

        results = response.result[0].results or []
        return cast(list[dict[str, object]], list(results))
