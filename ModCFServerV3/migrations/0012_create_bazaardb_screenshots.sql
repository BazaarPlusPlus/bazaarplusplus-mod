-- Migration 0012: BazaarDB screenshot store.
-- Mod posts screenshots here via POST /bazaardb-screenshots.
-- BazaarDB pulls daily via GET /bazaardb/manifest?date=YYYY-MM-DD.

CREATE TABLE bazaardb_screenshots (
  screenshot_id        TEXT PRIMARY KEY,
  player_account_id    TEXT NOT NULL,
  run_id               TEXT,
  hero_name            TEXT,
  final_days           INTEGER,
  final_victories      INTEGER,
  player_name          TEXT,
  player_rank          TEXT,
  player_rating        INTEGER,
  player_position      INTEGER,
  captured_at_utc      TEXT NOT NULL,
  captured_date_utc    TEXT NOT NULL,
  image_format         TEXT NOT NULL,
  image_sha256         TEXT NOT NULL,
  image_bytes          INTEGER NOT NULL,
  r2_key               TEXT NOT NULL,
  uploaded_at_utc      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  schema_version       INTEGER NOT NULL
);

CREATE INDEX idx_bazaardb_screenshots_date
  ON bazaardb_screenshots(captured_date_utc, uploaded_at_utc);
