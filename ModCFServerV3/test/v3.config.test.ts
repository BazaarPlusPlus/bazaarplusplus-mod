import { expect, test } from "vitest";

import type { Env } from "../src/env";
import { getGhostQueryLookbackDays, getRunBundleRetentionDays } from "../src/config/v3";

function buildConfigEnv(overrides: Partial<Env> = {}): Env {
  return {
    DB: {} as D1Database,
    RUN_BUNDLE_BUCKET: {} as R2Bucket,
    REPLAY_DOWNLOAD_SECRET: "test-replay-download-secret",
    GHOST_QUERY_LOOKBACK_DAYS: "3",
    RUN_BUNDLE_RETENTION_DAYS: "5",
    ...overrides,
  };
}

test("v3 config reads numeric values from env vars", () => {
  const env = buildConfigEnv({
    GHOST_QUERY_LOOKBACK_DAYS: "6",
    RUN_BUNDLE_RETENTION_DAYS: "9",
  });

  expect(getGhostQueryLookbackDays(env)).toBe(6);
  expect(getRunBundleRetentionDays(env)).toBe(9);
});

test("v3 config rejects missing numeric env vars", () => {
  const env = buildConfigEnv({
    GHOST_QUERY_LOOKBACK_DAYS: undefined as never,
  });

  expect(() => getGhostQueryLookbackDays(env)).toThrow(
    /GHOST_QUERY_LOOKBACK_DAYS/,
  );
});

test("v3 config rejects invalid numeric env vars", () => {
  const env = buildConfigEnv({
    RUN_BUNDLE_RETENTION_DAYS: "abc",
  });

  expect(() => getRunBundleRetentionDays(env)).toThrow(
    /RUN_BUNDLE_RETENTION_DAYS/,
  );
});
