import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

function readMigration(fileName: string): string {
  const migrationPath = path.join(
    import.meta.dirname,
    "..",
    "migrations",
    fileName,
  );
  return readFileSync(migrationPath, "utf8");
}

function getTableSection(sql: string, tableName: string): string {
  const section = sql.match(
    new RegExp(`CREATE TABLE(?: IF NOT EXISTS)? ${tableName} \\(([\\s\\S]*?)\\);`),
  )?.[1];
  assert.ok(section, `expected CREATE TABLE for ${tableName}`);
  return section!;
}

test("initial migration defines the V3 projection tables", () => {
  const sql = readMigration("0001_initial_schema.sql");

  for (const tableName of [
    "users",
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

test("initial migration stores V3 user identity columns", () => {
  const sql = readMigration("0001_initial_schema.sql");
  const usersSection = getTableSection(sql, "users");

  assert.match(usersSection, /\bplayer_account_id TEXT PRIMARY KEY\b/);
  assert.match(usersSection, /\bplayer_username TEXT NOT NULL UNIQUE\b/);
  assert.match(usersSection, /\bpassword_hash TEXT NOT NULL\b/);
});

test("auth simplification migration creates tokens and drops installation tables", () => {
  const sql = readMigration("0002_auth_simplification.sql");
  const tokensSection = getTableSection(sql, "tokens");

  assert.match(tokensSection, /\btoken\s+TEXT\s+PRIMARY KEY\b/);
  assert.match(tokensSection, /\bplayer_account_id\s+TEXT\s+NOT NULL\b/);
  assert.match(tokensSection, /\bissued_at_utc\s+TEXT\s+NOT NULL\b/);
  assert.match(tokensSection, /\brevoked_at_utc\s+TEXT\s+NULL\b/);
  assert.match(tokensSection, /\blast_used_at_utc\s+TEXT\s+NULL\b/);

  assert.match(sql, /DROP TABLE IF EXISTS installation_sessions;/);
  assert.match(sql, /DROP TABLE IF EXISTS installation_observations;/);
  assert.match(sql, /DROP TABLE IF EXISTS installations;/);
});
