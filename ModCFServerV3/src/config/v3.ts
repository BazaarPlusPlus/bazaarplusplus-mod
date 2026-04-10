import type { Env } from "../env";

export function getGhostQueryLookbackDays(): number {
  return 3;
}

export function getRunBundleRetentionDays(): number {
  return 5;
}

export function getBattleIngestMinRating(): number {
  return 1600;
}

export function getBattleIngestMinDayIfBelowRating(): number {
  return 10;
}

export function allowUnauthenticatedReplayDownloads(env: Env): boolean {
  const raw = env.ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS?.trim().toLowerCase();
  return raw === "1" || raw === "true" || raw === "yes" || raw === "on";
}

export function allowUnauthenticatedReplayLinks(env: Env): boolean {
  const raw = env.ALLOW_UNAUTHENTICATED_REPLAY_LINKS?.trim().toLowerCase();
  return raw === "1" || raw === "true" || raw === "yes" || raw === "on";
}
