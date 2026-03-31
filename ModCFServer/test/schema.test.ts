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

test("migration includes the source_client_id/run_id projection index", () => {
  const sql = readMigrationSql();

  assert.match(
    sql,
    /CREATE INDEX IF NOT EXISTS idx_pvp_battles_source_run\s+ON pvp_battles\(source_client_id, run_id\);/m,
  );
  assert.doesNotMatch(sql, /\brequest_nonces\b/);
});

test("migration promotes run_uploads to the production ingestion ledger", () => {
  const sql = readMigrationSql();
  const runUploadsSection = getTableSection(sql, "run_uploads");

  assert.match(runUploadsSection, /\bpayload_object_key\b/);
  assert.match(runUploadsSection, /\bpayload_bytes\b/);
  assert.match(runUploadsSection, /\bprojection_status\b/);
  assert.match(runUploadsSection, /\bprojected_at_utc\b/);
  assert.match(runUploadsSection, /\blast_error_code\b/);
  assert.match(runUploadsSection, /\bcreated_at_utc\b/);
  assert.match(runUploadsSection, /\bupdated_at_utc\b/);
  assert.doesNotMatch(runUploadsSection, /\bprojection_version\b/);
  assert.doesNotMatch(runUploadsSection, /\bprojected_battle_count\b/);
  assert.doesNotMatch(runUploadsSection, /\bpayload_json\b/);
});

test("migration stores the production pvp battle query model", () => {
  const sql = readMigrationSql();
  const pvpBattlesSection = getTableSection(sql, "pvp_battles");

  assert.doesNotMatch(pvpBattlesSection, /\bplayer_hand_json\b/);
  assert.doesNotMatch(pvpBattlesSection, /\bplayer_skills_json\b/);
  assert.doesNotMatch(pvpBattlesSection, /\bopponent_hand_json\b/);
  assert.doesNotMatch(pvpBattlesSection, /\bopponent_skills_json\b/);
  assert.match(pvpBattlesSection, /\breplay_available\b/);
  assert.match(pvpBattlesSection, /\breplay_object_key\b/);
  assert.match(pvpBattlesSection, /\breplay_uploaded_at_utc\b/);
  assert.doesNotMatch(pvpBattlesSection, /\bprojection_version\b/);
  assert.doesNotMatch(pvpBattlesSection, /\bsummary_json\b/);
});
