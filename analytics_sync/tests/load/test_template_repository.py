from pathlib import Path

from analytics_sync.load.sqlite_client import SqliteClient
from analytics_sync.load.template_repository import TemplateRepository


def test_template_repository_upserts_and_returns_ids(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = TemplateRepository(client)

    mapping = repository.upsert_card_templates({"card-a", "card-b"})

    assert mapping["card-a"] > 0
    assert mapping["card-b"] > 0
