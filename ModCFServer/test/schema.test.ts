import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

test("migration includes the source_client_id/run_id projection index", () => {
  const migrationPath = path.join(
    import.meta.dirname,
    "..",
    "migrations",
    "0001_initial_schema.sql",
  );
  const sql = readFileSync(migrationPath, "utf8");

  assert.match(
    sql,
    /CREATE INDEX IF NOT EXISTS idx_pvp_battles_source_run\s+ON pvp_battles\(source_client_id, run_id\);/m,
  );
});

test("migration does not store the raw run upload blob in run_uploads", () => {
  const migrationPath = path.join(
    import.meta.dirname,
    "..",
    "migrations",
    "0001_initial_schema.sql",
  );
  const sql = readFileSync(migrationPath, "utf8");

  const runUploadsSection = sql.match(
    /CREATE TABLE IF NOT EXISTS run_uploads \(([\s\S]*?)\);/,
  )?.[1];
  assert.ok(runUploadsSection);
  assert.doesNotMatch(runUploadsSection!, /\bpayload_json\b/);
});
