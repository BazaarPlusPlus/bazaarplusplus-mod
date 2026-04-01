import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

function readMigrationSql(): string {
  const migrationPath = path.join(
    import.meta.dirname,
    "..",
    "migrations",
    "0001_initial_schema.sql",
  );
  return readFileSync(migrationPath, "utf8");
}

function getTableSection(sql: string, tableName: string): string {
  const section = sql.match(
    new RegExp(
      `CREATE TABLE IF NOT EXISTS ${tableName} \\(([\\s\\S]*?)\\);`,
    ),
  )?.[1];
  assert.ok(section, `expected CREATE TABLE for ${tableName}`);
  return section!;
}

test("migration defines only the clean-break tables", () => {
  const sql = readMigrationSql();

  for (const tableName of [
    "clients",
    "player_links",
    "runs",
    "battles",
    "replay_tokens",
  ]) {
    assert.ok(getTableSection(sql, tableName));
  }

  assert.doesNotMatch(sql, /\bregistered_clients\b/);
  assert.doesNotMatch(sql, /\bclient_player_account_bindings\b/);
  assert.doesNotMatch(sql, /\bclient_player_account_observations\b/);
  assert.doesNotMatch(sql, /\brun_uploads\b/);
  assert.doesNotMatch(sql, /\bpvp_battles\b/);
});

test("migration stores battle replay metadata without encounter_id", () => {
  const sql = readMigrationSql();
  const battlesSection = getTableSection(sql, "battles");

  assert.match(battlesSection, /\breplay_object_key\b/);
  assert.match(battlesSection, /\breplay_schema_version\b/);
  assert.match(battlesSection, /\breplay_size_bytes\b/);
  assert.match(battlesSection, /\bplayer_account_id\b/);
  assert.match(battlesSection, /\bopponent_account_id\b/);
  assert.doesNotMatch(battlesSection, /\bencounter_id\b/);
  assert.doesNotMatch(battlesSection, /\breplay_available\b/);
});

test("migration defines replay token lifecycle columns", () => {
  const sql = readMigrationSql();
  const replayTokensSection = getTableSection(sql, "replay_tokens");

  assert.match(replayTokensSection, /\btoken\b/);
  assert.match(replayTokensSection, /\bbattle_id\b/);
  assert.match(replayTokensSection, /\brequested_by_player_account_id\b/);
  assert.match(replayTokensSection, /\bexpires_at_utc\b/);
  assert.match(replayTokensSection, /\bused_at_utc\b/);
  assert.match(replayTokensSection, /\brevoked_at_utc\b/);
});
