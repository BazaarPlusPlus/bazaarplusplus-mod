import { expect, test } from "vitest";

import dropAuthTablesSql from "../migrations/0009_drop_auth_tables.sql?raw";
import dropInstallationIdSql from "../migrations/0010_drop_installation_id.sql?raw";
import createSeenPlayerAccountsSql from "../migrations/0011_create_seen_player_accounts.sql?raw";
import runsEndedAtIndexSql from "../migrations/0005_runs_ended_at_index.sql?raw";
import runBundlesCreatedAtIndexSql from "../migrations/0006_run_bundles_created_at_index.sql?raw";
import runsUpdatedAtIndexSql from "../migrations/0007_runs_updated_at_index.sql?raw";
import battlesBundleFinalFlagSql from "../migrations/0008_battles_bundle_final_flag.sql?raw";
import initialSchemaSql from "../migrations/0001_initial_schema.sql?raw";

function getTableSection(sql: string, tableName: string): string {
  const section = sql.match(
    new RegExp(`CREATE TABLE(?: IF NOT EXISTS)? ${tableName} \\(([\\s\\S]*?)\\);`),
  )?.[1];
  expect(section).toBeTruthy();
  return section!;
}

test("initial migration defines the V3 projection tables", () => {
  for (const tableName of ["run_bundles", "runs", "battles", "replay_tokens"]) {
    expect(getTableSection(initialSchemaSql, tableName)).toBeTruthy();
  }
});

test("runs ended_at index migration adds the mirror sync index", () => {
  expect(runsEndedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_runs_ended_at\s+ON runs \(ended_at_utc, run_id\);/,
  );
});

test("run_bundles created_at index migration adds the server-clock mirror index", () => {
  expect(runBundlesCreatedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_run_bundles_created_at\s+ON run_bundles \(created_at_utc, bundle_id\);/,
  );
});

test("runs updated_at index migration adds the server-clock mirror index", () => {
  expect(runsUpdatedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_runs_updated_at\s+ON runs \(updated_at_utc, run_id\);/,
  );
});

test("battles bundle-final migration adds the ghost display flag to the covering index", () => {
  expect(battlesBundleFinalFlagSql).toContain(
    "ADD COLUMN is_bundle_final_battle INTEGER NOT NULL DEFAULT 0",
  );
  expect(battlesBundleFinalFlagSql).toContain("idx_battles_opponent_recorded_covering");
});

test("0009 drops auth tables in dependency order", () => {
  expect(dropAuthTablesSql).toMatch(/DROP TABLE IF EXISTS tokens;/);
  expect(dropAuthTablesSql).toMatch(/DROP TABLE IF EXISTS users;/);
  expect(dropAuthTablesSql.indexOf("DROP TABLE IF EXISTS tokens")).toBeLessThan(
    dropAuthTablesSql.indexOf("DROP TABLE IF EXISTS users"),
  );
});

test("0010 drops installation_id and rebuilds run_bundles without UNIQUE", () => {
  expect(dropInstallationIdSql).toMatch(/ALTER TABLE runs\s+DROP COLUMN installation_id;/);
  expect(dropInstallationIdSql).toMatch(/ALTER TABLE battles\s+DROP COLUMN installation_id;/);
  expect(dropInstallationIdSql).toMatch(/CREATE TABLE run_bundles_new \(/);
  expect(dropInstallationIdSql).toMatch(/ALTER TABLE run_bundles_new RENAME TO run_bundles;/);
  expect(dropInstallationIdSql).not.toMatch(/UNIQUE \(installation_id/);
});

test("0011 creates seen_player_accounts and backfills uploaders", () => {
  expect(createSeenPlayerAccountsSql).toMatch(
    /CREATE TABLE seen_player_accounts \(/,
  );
  expect(createSeenPlayerAccountsSql).toMatch(
    /INSERT OR IGNORE INTO seen_player_accounts/,
  );
  expect(createSeenPlayerAccountsSql).toMatch(/FROM run_bundles/);
});
