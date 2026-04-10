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
    new RegExp(`CREATE TABLE IF NOT EXISTS ${tableName} \\(([\\s\\S]*?)\\);`),
  )?.[1];
  assert.ok(section, `expected CREATE TABLE for ${tableName}`);
  return section!;
}

test("migration defines only the V3 tables", () => {
  const sql = readMigrationSql();

  for (const tableName of [
    "users",
    "installations",
    "installation_sessions",
    "installation_observations",
    "run_bundles",
    "runs",
    "battles",
    "replay_tokens",
  ]) {
    assert.ok(getTableSection(sql, tableName));
  }

  assert.doesNotMatch(sql, /\bclients\b/);
  assert.doesNotMatch(sql, /\bplayer_links\b/);
});

test("migration stores V3 user and installation identity columns", () => {
  const sql = readMigrationSql();
  const usersSection = getTableSection(sql, "users");
  const installationsSection = getTableSection(sql, "installations");
  const sessionsSection = getTableSection(sql, "installation_sessions");

  assert.match(usersSection, /\bplayer_account_id TEXT PRIMARY KEY\b/);
  assert.match(usersSection, /\bplayer_username TEXT NOT NULL UNIQUE\b/);
  assert.match(usersSection, /\bpassword_hash TEXT NOT NULL\b/);

  assert.match(installationsSection, /\binstallation_id TEXT PRIMARY KEY\b/);
  assert.match(installationsSection, /\bplayer_account_id TEXT NOT NULL\b/);
  assert.match(installationsSection, /\bpublic_key TEXT NOT NULL\b/);
  assert.match(installationsSection, /\bstatus TEXT NOT NULL\b/);

  assert.match(sessionsSection, /\bsession_id TEXT PRIMARY KEY\b/);
  assert.match(sessionsSection, /\bplayer_account_id TEXT NOT NULL\b/);
  assert.match(sessionsSection, /\bexpires_at_utc TEXT NOT NULL\b/);
});

