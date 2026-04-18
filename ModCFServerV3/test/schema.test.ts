import { expect, test } from "vitest";

import authSimplificationSql from "../migrations/0002_auth_simplification.sql?raw";
import runsEndedAtIndexSql from "../migrations/0005_runs_ended_at_index.sql?raw";
import initialSchemaSql from "../migrations/0001_initial_schema.sql?raw";

function getTableSection(sql: string, tableName: string): string {
  const section = sql.match(
    new RegExp(`CREATE TABLE(?: IF NOT EXISTS)? ${tableName} \\(([\\s\\S]*?)\\);`),
  )?.[1];
  expect(section).toBeTruthy();
  return section!;
}

test("initial migration defines the V3 projection tables", () => {
  const sql = initialSchemaSql;

  for (const tableName of [
    "users",
    "run_bundles",
    "runs",
    "battles",
    "replay_tokens",
  ]) {
    expect(getTableSection(sql, tableName)).toBeTruthy();
  }

  expect(sql).not.toMatch(/\bclients\b/);
  expect(sql).not.toMatch(/\bplayer_links\b/);
});

test("initial migration stores V3 user identity columns", () => {
  const sql = initialSchemaSql;
  const usersSection = getTableSection(sql, "users");

  expect(usersSection).toMatch(/\bplayer_account_id TEXT PRIMARY KEY\b/);
  expect(usersSection).toMatch(/\bplayer_username TEXT NOT NULL UNIQUE\b/);
  expect(usersSection).toMatch(/\bpassword_hash TEXT NOT NULL\b/);
});

test("auth simplification migration creates tokens and drops installation tables", () => {
  const sql = authSimplificationSql;
  const tokensSection = getTableSection(sql, "tokens");

  expect(tokensSection).toMatch(/\btoken\s+TEXT\s+PRIMARY KEY\b/);
  expect(tokensSection).toMatch(/\bplayer_account_id\s+TEXT\s+NOT NULL\b/);
  expect(tokensSection).toMatch(/\bissued_at_utc\s+TEXT\s+NOT NULL\b/);
  expect(tokensSection).toMatch(/\brevoked_at_utc\s+TEXT\s+NULL\b/);
  expect(tokensSection).toMatch(/\blast_used_at_utc\s+TEXT\s+NULL\b/);

  expect(sql).toMatch(/DROP TABLE IF EXISTS installation_sessions;/);
  expect(sql).toMatch(/DROP TABLE IF EXISTS installation_observations;/);
  expect(sql).toMatch(/DROP TABLE IF EXISTS installations;/);
});

test("runs ended_at index migration adds the mirror sync index", () => {
  expect(runsEndedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_runs_ended_at\s+ON runs \(ended_at_utc, run_id\);/,
  );
});
