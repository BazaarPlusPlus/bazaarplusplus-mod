from __future__ import annotations

from analytics_sync.load.provider import SqlExecutor


class TemplateRepository:
    def __init__(self, client: SqlExecutor) -> None:
        self._client = client
        self._card_cache: dict[str, int] = {}
        self._skill_cache: dict[str, int] = {}

    def upsert_card_templates(self, template_ids: set[str]) -> dict[str, int]:
        return self._upsert_templates("card_templates", template_ids, self._card_cache)

    def upsert_skill_templates(self, template_ids: set[str]) -> dict[str, int]:
        return self._upsert_templates("skill_templates", template_ids, self._skill_cache)

    def _upsert_templates(
        self,
        table_name: str,
        template_ids: set[str],
        cache: dict[str, int],
    ) -> dict[str, int]:
        missing_ids = sorted(template_id for template_id in template_ids if template_id not in cache)
        if missing_ids:
            if self._client.dialect == "mysql":
                insert_sql = (
                    f"INSERT IGNORE INTO {table_name} (template_id, created_at) VALUES (?, UTC_TIMESTAMP(6))"
                )
            else:
                insert_sql = (
                    f"INSERT INTO {table_name} (template_id, created_at) VALUES (?, datetime('now')) "
                    "ON CONFLICT(template_id) DO NOTHING"
                )
            self._client.executemany(insert_sql, [[template_id] for template_id in missing_ids])

            rows = self._client.fetch_all(
                f"SELECT id, template_id FROM {table_name} WHERE template_id IN ({','.join('?' for _ in missing_ids)})",
                list(missing_ids),
            )
            for row in rows:
                template_id_obj = row.get("template_id")
                row_id_obj = row.get("id")
                if isinstance(template_id_obj, str) and isinstance(row_id_obj, int):
                    cache[template_id_obj] = row_id_obj

        return {template_id: cache[template_id] for template_id in template_ids if template_id in cache}
