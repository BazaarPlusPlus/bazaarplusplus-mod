from analytics_sync.config import SyncConfig


def test_sync_config_reads_required_mysql_fields(monkeypatch):
    monkeypatch.setenv("BPP_SQL_PROVIDER", "mysql")
    monkeypatch.setenv("BPP_CF_API_TOKEN", "token")
    monkeypatch.setenv("BPP_CF_ACCOUNT_ID", "account")
    monkeypatch.setenv("BPP_CF_D1_DATABASE_ID", "db")
    monkeypatch.setenv("BPP_R2_ACCOUNT_ID", "r2-account")
    monkeypatch.setenv("BPP_R2_ACCESS_KEY_ID", "key")
    monkeypatch.setenv("BPP_R2_SECRET_ACCESS_KEY", "secret")
    monkeypatch.setenv("BPP_R2_BUCKET", "bucket")
    monkeypatch.setenv("BPP_MYSQL_HOST", "127.0.0.1")
    monkeypatch.setenv("BPP_MYSQL_PORT", "3306")
    monkeypatch.setenv("BPP_MYSQL_USER", "tester")
    monkeypatch.setenv("BPP_MYSQL_PASSWORD", "secret")
    monkeypatch.setenv("BPP_MYSQL_DATABASE", "bpp_analytics")

    config = SyncConfig.from_env()

    assert config.mysql.host == "127.0.0.1"
    assert config.mysql.port == 3306
    assert config.mysql.user == "tester"


def test_sync_config_reads_sqlite_provider(monkeypatch):
    monkeypatch.setenv("BPP_SQL_PROVIDER", "sqlite")
    monkeypatch.setenv("BPP_SQLITE_PATH", "/tmp/analytics.db")
    monkeypatch.setenv("BPP_CF_API_TOKEN", "token")
    monkeypatch.setenv("BPP_CF_ACCOUNT_ID", "account")
    monkeypatch.setenv("BPP_CF_D1_DATABASE_ID", "db")
    monkeypatch.setenv("BPP_R2_ACCOUNT_ID", "r2-account")
    monkeypatch.setenv("BPP_R2_ACCESS_KEY_ID", "key")
    monkeypatch.setenv("BPP_R2_SECRET_ACCESS_KEY", "secret")
    monkeypatch.setenv("BPP_R2_BUCKET", "bucket")

    config = SyncConfig.from_env()

    assert config.sql_provider == "sqlite"
    assert str(config.sqlite_path) == "/tmp/analytics.db"
