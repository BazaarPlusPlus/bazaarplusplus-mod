import assert from "node:assert/strict";
import test from "node:test";

import type { Env } from "../src/env";
import {
  getBattleIngestMinDayIfBelowRating,
  getBattleIngestMinRating,
  getGhostQueryLookbackDays,
  getRunBundleRetentionDays,
} from "../src/config/v3";

function buildConfigEnv(overrides: Partial<Env> = {}): Env {
  return {
    DB: {} as D1Database,
    RUN_BUNDLE_BUCKET: {} as R2Bucket,
    REPLAY_DOWNLOAD_SECRET: "test-replay-download-secret",
    GHOST_QUERY_LOOKBACK_DAYS: "3",
    RUN_BUNDLE_RETENTION_DAYS: "5",
    BATTLE_INGEST_MIN_RATING: "700",
    BATTLE_INGEST_MIN_DAY_IF_BELOW_RATING: "7",
    ALLOW_UNAUTHENTICATED_REPLAY_LINKS: "false",
    ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS: "false",
    ...overrides,
  };
}

test("v3 config reads numeric values from env vars", () => {
  const env = buildConfigEnv({
    GHOST_QUERY_LOOKBACK_DAYS: "6",
    RUN_BUNDLE_RETENTION_DAYS: "9",
    BATTLE_INGEST_MIN_RATING: "1800",
    BATTLE_INGEST_MIN_DAY_IF_BELOW_RATING: "11",
  });

  assert.equal(getGhostQueryLookbackDays(env), 6);
  assert.equal(getRunBundleRetentionDays(env), 9);
  assert.equal(getBattleIngestMinRating(env), 1800);
  assert.equal(getBattleIngestMinDayIfBelowRating(env), 11);
});

test("v3 config rejects missing numeric env vars", () => {
  const env = buildConfigEnv({
    GHOST_QUERY_LOOKBACK_DAYS: undefined as never,
  });

  assert.throws(
    () => getGhostQueryLookbackDays(env),
    /GHOST_QUERY_LOOKBACK_DAYS/,
  );
});

test("v3 config rejects invalid numeric env vars", () => {
  const env = buildConfigEnv({
    RUN_BUNDLE_RETENTION_DAYS: "abc",
  });

  assert.throws(
    () => getRunBundleRetentionDays(env),
    /RUN_BUNDLE_RETENTION_DAYS/,
  );
});
