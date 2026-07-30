import { COPY, type LocaleCopy } from "../i18n/catalog.ts";
import {
  frameZeroMetricSamples,
  normalizeCardStats,
  normalizeEntity,
  normalizeEvent,
  normalizeMetric,
  normalizeSide,
  type NormalizedEntity,
  type NormalizedEvent,
  type NormalizedMetric,
  type NormalizedCardStats,
} from "./normalize.ts";
import { safeAssetUrl, safeVideoUrl } from "./asset-paths.ts";
import { enrichDefeatEvents } from "./defeat-events.ts";
import {
  normalizeSyncState,
  type RecordingSyncState,
} from "../recording/sync.ts";
import { asString } from "./value.ts";
import type {
  ReportDocumentV1,
  ReportEnvelopeV1,
} from "./report-schema.ts";

export type SupportedLocale = "en" | "zh-CN" | "zh-Hant";

export interface ReportViewModel {
  battleId: string;
  playerName: string;
  opponentName: string;
  outcome: string;
  durationMs: number;
  frameDurationMs: number;
  frameCount: number;
  rawRecordCount: number;
  entities: NormalizedEntity[];
  cardStats: ReadonlyMap<string, NormalizedCardStats>;
  events: NormalizedEvent[];
  semanticIcons: ReadonlyMap<string, string>;
  metrics: NormalizedMetric[];
  videoUrl: string;
  scrubVideoUrl: string;
  sync: RecordingSyncState;
}

export function normalizeLocale(value: unknown): SupportedLocale {
  const requested = asString(value, "en");
  const normalized = requested.toLowerCase().replaceAll("_", "-");
  if (
    normalized.startsWith("zh-hant")
    || normalized.startsWith("zh-tw")
    || normalized.startsWith("zh-hk")
    || normalized.startsWith("zh-mo")
  ) {
    return "zh-Hant";
  }
  return normalized.startsWith("zh") ? "zh-CN" : "en";
}

export function selectLocale(envelope: ReportEnvelopeV1): SupportedLocale {
  const queryLocale = new URLSearchParams(window.location.search).get("lang");
  return normalizeLocale(
    queryLocale || envelope.locale || navigator.language || "en",
  );
}

export function translate(copy: LocaleCopy, key: string): string {
  return copy[key] || COPY.en[key] || key;
}

function fillMissingEntityNames(
  entities: NormalizedEntity[],
  copy: LocaleCopy,
): void {
  const counters = new Map<string, number>();
  for (const entity of entities) {
    if (entity.name || entity.hiddenFromTimeline) continue;
    const type = asString(entity.type, "entity").toLowerCase();
    const key = `${normalizeSide(entity.side)}:${type}`;
    const ordinal = (counters.get(key) ?? 0) + 1;
    counters.set(key, ordinal);
    const labelKey =
      type === "hero"
        ? "entityHero"
        : type === "item"
          ? "entityItem"
          : type === "skill"
            ? "entitySkill"
            : type === "effect"
              ? "entityEffect"
              : "entityUnknown";
    entity.name = `${translate(copy, labelKey)} ${ordinal}`;
  }
}

function participantName(
  documentModel: ReportDocumentV1,
  side: "player" | "opponent",
  fallback: string,
): string {
  const summaryName =
    side === "player"
      ? documentModel.summary.playerName
      : documentModel.summary.opponentName;
  if (summaryName) return summaryName;
  const directName = documentModel[side].name;
  return directName || fallback;
}

export function buildViewModel(
  envelope: ReportEnvelopeV1,
  copy: LocaleCopy,
): ReportViewModel {
  const battle = envelope.battleDocument;
  const manifest = envelope.recordingManifest;
  const entities = battle.entities.map(normalizeEntity);
  const cardStats = new Map(
    (battle.cardStats ?? [])
      .map(normalizeCardStats)
      .filter((stats) => stats.entityId)
      .map((stats) => [stats.entityId, stats] as const),
  );
  fillMissingEntityNames(entities, copy);
  let events = battle.events.map(normalizeEvent);
  events.sort(
    (left, right) =>
      left.combatMs - right.combatMs
      || left.frame - right.frame
      || left.sequence - right.sequence,
  );
  const metrics = frameZeroMetricSamples(battle.frameZeroState).concat(
    battle.metrics
      .map(normalizeMetric)
      .filter((sample): sample is NormalizedMetric => sample !== null),
  );
  metrics.sort(
    (left, right) =>
      left.combatMs - right.combatMs || left.frame - right.frame,
  );
  events = enrichDefeatEvents(events, metrics, entities);
  const semanticIcons = new Map<string, string>();
  for (const icon of manifest.semanticIcons ?? []) {
    const url = safeAssetUrl(icon.relativeUrl);
    if (icon.semanticKey && url && !semanticIcons.has(icon.semanticKey)) {
      semanticIcons.set(icon.semanticKey, url);
    }
  }

  const frameDurationMs = Math.max(
    1,
    Math.round(battle.frameDurationMs),
  );
  const frameCount = Math.max(0, Math.round(battle.frameCount));
  const durationMs = battle.durationMs;
  const videoUrl = safeVideoUrl(manifest.videoRelativeUrl);
  const scrubVideoUrl = safeVideoUrl(manifest.scrubVideoRelativeUrl);
  const sync = normalizeSyncState(manifest, battle.battleId);
  if (manifest.videoRelativeUrl && !videoUrl) {
    sync.issues.push("unsafeVideoPath");
  }
  if (manifest.scrubVideoRelativeUrl && !scrubVideoUrl) {
    sync.issues.push("unsafeVideoPath");
  }

  return {
    battleId: battle.battleId,
    playerName: participantName(
      battle,
      "player",
      translate(copy, "unknownPlayer"),
    ),
    opponentName: participantName(
      battle,
      "opponent",
      translate(copy, "unknownOpponent"),
    ),
    outcome: battle.summary.outcome.toLowerCase(),
    durationMs,
    frameDurationMs,
    frameCount,
    rawRecordCount: Math.max(0, Math.round(battle.rawRecordCount)),
    entities,
    cardStats,
    events,
    semanticIcons,
    metrics,
    videoUrl,
    scrubVideoUrl,
    sync,
  };
}
