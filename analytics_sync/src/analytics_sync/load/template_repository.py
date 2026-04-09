from __future__ import annotations

from analytics_sync.load.sqlite_client import SqliteClient


class TemplateRepository:
    def __init__(self, client: SqliteClient) -> None:
        self._client = client

    def upsert_card_templates(self, template_ids: set[str]) -> dict[str, int]:
        return self._upsert_templates("card_templates", template_ids)

    def upsert_skill_templates(self, template_ids: set[str]) -> dict[str, int]:
        return self._upsert_templates("skill_templates", template_ids)

    def _upsert_templates(self, table_name: str, template_ids: set[str]) -> dict[str, int]:
        for template_id in template_ids:
            self._client.execute(
                f"INSERT INTO {table_name} (template_id, created_at) VALUES (?, datetime('now')) ON CONFLICT(template_id) DO NOTHING",
                [template_id],
            )

        if not template_ids:
            return {}

        rows = self._client.fetch_all(
            f"SELECT id, template_id FROM {table_name} WHERE template_id IN ({','.join('?' for _ in template_ids)})",
            list(template_ids),
        )
        mapping: dict[str, int] = {}
        for row in rows:
            template_id_obj = row.get("template_id")
            row_id_obj = row.get("id")
            if isinstance(template_id_obj, str) and isinstance(row_id_obj, int):
                mapping[template_id_obj] = row_id_obj
        return mapping
