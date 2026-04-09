import threading
import time

from analytics_sync.compat.v3_models import RunBundleUploadRequestV2
from analytics_sync.load.sync_checkpoint_repository import SyncCursor
from analytics_sync.source.base import SourceRunRef
from analytics_sync.source.legacy_cloudflare_provider import LegacyCloudflareRunBundleProvider


class _FakeD1Client:
    def __init__(self) -> None:
        self.calls: list[tuple[str, list[str]]] = []

    def fetch_rows(self, sql: str, params: list[str]) -> list[dict[str, object]]:
        self.calls.append((sql, params))
        if "FROM runs" in sql:
            return [
                {
                    "run_id": "run-1",
                    "player_account_id": None,
                    "hero_id": "hero-1",
                    "summary_object_key": "run-1-summary.json",
                    "updated_at_utc": "2026-04-09T00:00:00Z",
                },
                {
                    "run_id": "run-2",
                    "player_account_id": "player-2",
                    "hero_id": "hero-2",
                    "summary_object_key": "run-2-summary.json",
                    "updated_at_utc": "2026-04-09T01:00:00Z",
                },
            ]
        if "FROM battles" in sql:
            return [
                {
                    "battle_id": "battle-1",
                    "run_id": "run-1",
                    "recorded_at_utc": "2026-04-09T00:05:00Z",
                    "player_account_id": "player-1",
                    "player_rank": "Bronze",
                    "player_rating": 500,
                    "replay_object_key": "battle-1.json",
                },
                {
                    "battle_id": "battle-2",
                    "run_id": "run-2",
                    "recorded_at_utc": "2026-04-09T01:05:00Z",
                    "player_account_id": "player-2",
                    "player_rank": "Silver",
                    "player_rating": 600,
                    "replay_object_key": "battle-2.json",
                },
            ]
        return []


class _FakeR2Client:
    def get_bytes(self, ref) -> bytes:
        payloads = {
            "run-1-summary.json": b'{"status":"completed","ended_at_utc":"2026-04-09T00:10:00Z","hero_name":"Karnok","final_day":8,"final_wins":1,"final_losses":7}',
            "run-2-summary.json": b'{"status":"abandoned","ended_at_utc":"2026-04-09T01:10:00Z","hero_name":"Pygmalien","final_day":4,"final_wins":0,"final_losses":2}',
            "battle-1.json": b'{"player_cards":[{"template_id":"card-1","slot":0,"tier":1,"enchant":"burn"}],"player_skills":[{"template_id":"skill-1","slot":0,"tier":1}],"player_temperatures":[{"slot":0,"state":"high"}]}',
            "battle-2.json": b'{"player_cards":[{"template_id":"card-2","slot":0,"tier":2,"enchant":""}],"player_skills":[{"template_id":"skill-2","slot":0,"tier":2}],"player_temperatures":[]}',
        }
        return payloads[ref.key]


def test_legacy_cloudflare_provider_discovers_run_refs():
    d1 = _FakeD1Client()
    provider = LegacyCloudflareRunBundleProvider(d1_client=d1, r2_client=_FakeR2Client(), bucket="bucket")

    refs = provider.discover_run_refs(cursor=SyncCursor(updated_at="1970-01-01T00:00:00Z", entity_id=""), limit=2)

    assert refs == [
        SourceRunRef(source_updated_at="2026-04-09T00:00:00Z", source_entity_id="run-1", run_id="run-1"),
        SourceRunRef(source_updated_at="2026-04-09T01:00:00Z", source_entity_id="run-2", run_id="run-2"),
    ]


def test_legacy_cloudflare_provider_fetches_v3_bundles_for_run_ids_and_batches_battle_query():
    d1 = _FakeD1Client()
    provider = LegacyCloudflareRunBundleProvider(d1_client=d1, r2_client=_FakeR2Client(), bucket="bucket")

    items = provider.fetch_run_bundles(["run-1", "run-2"])

    assert len(items) == 2
    assert isinstance(items[0].bundle, RunBundleUploadRequestV2)
    assert items[0].bundle.run_projection["player_account_id"] == "player-1"
    battle_queries = [sql for sql, _params in d1.calls if "FROM battles" in sql]
    assert len(battle_queries) == 1


def test_legacy_cloudflare_provider_skips_runs_without_player_account_id():
    d1 = _FakeD1Client()

    def fetch_rows(sql: str, params: list[str]) -> list[dict[str, object]]:
        if "FROM runs" in sql:
            return [
                {
                    "run_id": "run-missing-account",
                    "player_account_id": None,
                    "hero_id": "hero-1",
                    "summary_object_key": "run-1-summary.json",
                    "updated_at_utc": "2026-04-09T00:00:00Z",
                }
            ]
        return []

    d1.fetch_rows = fetch_rows  # type: ignore[method-assign]
    provider = LegacyCloudflareRunBundleProvider(d1_client=d1, r2_client=_FakeR2Client(), bucket="bucket")

    items = provider.fetch_run_bundles(["run-missing-account"])

    assert items == []


class _TrackingR2Client:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self.in_flight = 0
        self.max_in_flight = 0

    def get_bytes(self, ref) -> bytes:
        with self._lock:
            self.in_flight += 1
            self.max_in_flight = max(self.max_in_flight, self.in_flight)
        try:
            time.sleep(0.02)
            return _FakeR2Client().get_bytes(ref)
        finally:
            with self._lock:
                self.in_flight -= 1


def test_legacy_cloudflare_provider_fetches_r2_objects_concurrently():
    d1 = _FakeD1Client()
    r2 = _TrackingR2Client()
    provider = LegacyCloudflareRunBundleProvider(d1_client=d1, r2_client=r2, bucket="bucket", max_workers=4)

    items = provider.fetch_run_bundles(["run-1", "run-2"])

    assert len(items) == 2
    assert r2.max_in_flight > 1
