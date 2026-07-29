import {
  asArray,
  asFiniteNumber,
  asString,
  isRecord,
  pick,
} from "../model/value.ts";

export interface SyncAnchor {
  combatMs: number;
  mediaPtsMs: number;
}

export interface RecordingSyncState {
  status: "NotRequested" | "ReadyExact" | "ReadyUnsynced";
  anchors: SyncAnchor[];
  identityMatches: boolean;
  issues: string[];
}

const MIN_SETTLED_TERMINAL_PLATFORM_MS = 1_000;
const MAX_SETTLED_TERMINAL_PLATFORM_MS = 2_000;
const TERMINAL_SETTLE_OFFSET_MS = 750;

export function getTerminalCombatAnchor(
  anchors: readonly SyncAnchor[],
): SyncAnchor | null {
  const lastAnchor = anchors[anchors.length - 1];
  if (!lastAnchor) return null;

  // Capture can continue after the combat clock stops. Those trailing samples
  // share one combat timestamp but already contain victory/outro animation.
  let index = anchors.length - 1;
  while (
    index > 0
    && anchors[index - 1].combatMs === lastAnchor.combatMs
  ) {
    index -= 1;
  }
  return anchors[index] ?? null;
}

export function getSettledTerminalAnchor(
  anchors: readonly SyncAnchor[],
): SyncAnchor | null {
  const first = getTerminalCombatAnchor(anchors);
  const last = anchors[anchors.length - 1];
  if (!first || !last || last.combatMs !== first.combatMs) return first;

  // Current recordings hold the final board for one second after CombatSim
  // settles. Older reports either stop earlier or retain the native multi-
  // second final-blow slowdown, so only the bounded modern platform opts into
  // the later preview.
  const platformDurationMs = last.mediaPtsMs - first.mediaPtsMs;
  if (
    platformDurationMs < MIN_SETTLED_TERMINAL_PLATFORM_MS
    || platformDurationMs > MAX_SETTLED_TERMINAL_PLATFORM_MS
  ) {
    return first;
  }

  const targetMediaMs = first.mediaPtsMs + TERMINAL_SETTLE_OFFSET_MS;
  for (const anchor of anchors) {
    if (
      anchor.combatMs === first.combatMs
      && anchor.mediaPtsMs >= targetMediaMs
    ) {
      return anchor;
    }
  }
  return first;
}

export function normalizeAnchors(manifest: unknown): SyncAnchor[] {
  const rawAnchors = asArray(pick(manifest, ["syncAnchors", "anchors"], []));
  const anchors: SyncAnchor[] = [];
  let priorCombat = -Infinity;
  let priorMedia = -Infinity;
  for (const raw of rawAnchors) {
    const combatMs = asFiniteNumber(
      pick(raw, ["combatMs", "combatTimeMs"], Number.NaN),
      Number.NaN,
    );
    const mediaPtsMs = asFiniteNumber(
      pick(raw, ["mediaPtsMs", "mediaTimeMs"], Number.NaN),
      Number.NaN,
    );
    if (
      !Number.isFinite(combatMs)
      || !Number.isFinite(mediaPtsMs)
      || combatMs < priorCombat
      || mediaPtsMs < priorMedia
    ) {
      return [];
    }
    anchors.push({ combatMs, mediaPtsMs });
    priorCombat = combatMs;
    priorMedia = mediaPtsMs;
  }
  return anchors;
}

export function normalizeSyncState(
  manifest: unknown,
  battleId: unknown,
): RecordingSyncState {
  if (!isRecord(manifest)) {
    return {
      status: "NotRequested",
      anchors: [],
      identityMatches: true,
      issues: [],
    };
  }

  const issues: string[] = [];
  const expectedBattleId = asString(battleId, "");
  const manifestBattleId = asString(pick(manifest, ["battleId"], ""), "");
  const recordingId = asString(pick(manifest, ["recordingId"], ""), "");
  const identityMatches =
    !expectedBattleId
    || !manifestBattleId
    || expectedBattleId === manifestBattleId;
  if (!identityMatches) {
    issues.push("identityMismatch");
  }

  const requested = asString(
    pick(
      manifest,
      [
        "syncMetadataStatus",
        "syncStatus",
        "videoArtifactStatus",
        "status",
      ],
      "ReadyUnsynced",
    ),
    "ReadyUnsynced",
  ).toLowerCase();
  const anchors = normalizeAnchors(manifest);
  const exactRequested = requested === "readyexact" || requested === "exact";
  const exactValid =
    identityMatches && recordingId.length > 0 && anchors.length >= 2;
  if (exactRequested && !exactValid) {
    issues.push("invalidExactSync");
  }

  return {
    status: exactRequested && exactValid ? "ReadyExact" : "ReadyUnsynced",
    anchors: exactValid ? anchors : [],
    identityMatches,
    issues,
  };
}

export function mapCombatToMedia(
  combatMs: number,
  anchors: readonly SyncAnchor[],
): number | null {
  if (anchors.length < 2) return null;
  if (combatMs <= anchors[0].combatMs) return anchors[0].mediaPtsMs;

  for (let index = 1; index < anchors.length; index += 1) {
    const right = anchors[index];
    const left = anchors[index - 1];
    if (combatMs > right.combatMs) continue;
    const combatSpan = right.combatMs - left.combatMs;
    if (combatSpan <= 0) return right.mediaPtsMs;
    const progress = (combatMs - left.combatMs) / combatSpan;
    return left.mediaPtsMs + progress * (right.mediaPtsMs - left.mediaPtsMs);
  }

  return getSettledTerminalAnchor(anchors)?.mediaPtsMs ?? null;
}

export function mapMediaToCombat(
  mediaMs: number,
  anchors: readonly SyncAnchor[],
): number | null {
  if (anchors.length < 2) return null;
  if (mediaMs <= anchors[0].mediaPtsMs) return anchors[0].combatMs;

  for (let index = 1; index < anchors.length; index += 1) {
    const right = anchors[index];
    const left = anchors[index - 1];
    if (mediaMs > right.mediaPtsMs) continue;
    const mediaSpan = right.mediaPtsMs - left.mediaPtsMs;
    if (mediaSpan <= 0) return right.combatMs;
    const progress = (mediaMs - left.mediaPtsMs) / mediaSpan;
    return left.combatMs + progress * (right.combatMs - left.combatMs);
  }

  return anchors[anchors.length - 1].combatMs;
}
