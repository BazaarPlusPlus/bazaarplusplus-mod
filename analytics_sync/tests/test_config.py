from analytics_sync.config import SyncConfig


def test_sync_config_reads_required_mysql_fields(monkeypatch):
    monkeypatch.setenv("BPP_MYSQL_HOST", "127.0.0.1")
    monkeypatch.setenv("BPP_MYSQL_PORT", "3306")
    monkeypatch.setenv("BPP_MYSQL_USER", "tester")
    monkeypatch.setenv("BPP_MYSQL_PASSWORD", "secret")
    monkeypatch.setenv("BPP_MYSQL_DATABASE", "bpp_analytics")

    config = SyncConfig.from_env()

    assert config.mysql.host == "127.0.0.1"
    assert config.mysql.port == 3306
    assert config.mysql.user == "tester"
