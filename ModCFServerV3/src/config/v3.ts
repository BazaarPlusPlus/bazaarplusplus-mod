import type { Env } from "../env";

type RequiredNumericConfigKey =
  | "GHOST_QUERY_LOOKBACK_DAYS"
  | "RUN_BUNDLE_RETENTION_DAYS"
  | "BATTLE_INGEST_MIN_RATING"
  | "BATTLE_INGEST_MIN_DAY_IF_BELOW_RATING";

function requireNonNegativeInteger(env: Env, key: RequiredNumericConfigKey): number {
  const raw = env[key]?.trim();
  if (!raw) {
    throw new Error(`Missing required V3 config var: ${key}`);
  }

  const parsed = Number.parseInt(raw, 10);
  if (!Number.isSafeInteger(parsed) || parsed < 0 || String(parsed) !== raw) {
    throw new Error(`Invalid required V3 config var: ${key}=${raw}`);
  }

  return parsed;
}

export function getGhostQueryLookbackDays(env: Env): number {
  return requireNonNegativeInteger(env, "GHOST_QUERY_LOOKBACK_DAYS");
}

export function getRunBundleRetentionDays(env: Env): number {
  return requireNonNegativeInteger(env, "RUN_BUNDLE_RETENTION_DAYS");
}

export function getBattleIngestMinRating(env: Env): number {
  return requireNonNegativeInteger(env, "BATTLE_INGEST_MIN_RATING");
}

export function getBattleIngestMinDayIfBelowRating(env: Env): number {
  return requireNonNegativeInteger(env, "BATTLE_INGEST_MIN_DAY_IF_BELOW_RATING");
}

export function allowUnauthenticatedReplayDownloads(env: Env): boolean {
  const raw = env.ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS?.trim().toLowerCase();
  return raw === "1" || raw === "true" || raw === "yes" || raw === "on";
}

export function allowUnauthenticatedReplayLinks(env: Env): boolean {
  const raw = env.ALLOW_UNAUTHENTICATED_REPLAY_LINKS?.trim().toLowerCase();
  return raw === "1" || raw === "true" || raw === "yes" || raw === "on";
}
