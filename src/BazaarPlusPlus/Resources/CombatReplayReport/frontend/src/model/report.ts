import { COPY, type LocaleCopy } from "../i18n/catalog.ts";
import {
  frameZeroMetricSamples,
  normalizeEntity,
  normalizeEvent,
  normalizeMetric,
  normalizeSide,
  type NormalizedEntity,
  type NormalizedEvent,
  type NormalizedMetric,
} from "./normalize.ts";
import { safeVideoUrl } from "./asset-paths.ts";
import {
  normalizeSyncState,
  type RecordingSyncState,
} from "../recording/sync.ts";
import {
  asArray,
  asFiniteNumber,
  asString,
  isRecord,
  pick,
  type UnknownRecord,
} from "./value.ts";

const VIEWER_SCHEMA_VERSION = 1;

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
  events: NormalizedEvent[];
  metrics: NormalizedMetric[];
  videoUrl: string;
  sync: RecordingSyncState;
}

export class ReportError extends Error {
  readonly code: string;
  readonly details: string;

  constructor(code: string, details = "") {
    super(code);
    this.name = "ReportError";
    this.code = code;
    this.details = details;
  }
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

export function selectLocale(envelope: UnknownRecord): SupportedLocale {
  const battle = isRecord(envelope.battleDocument)
    ? envelope.battleDocument
    : {};
  const queryLocale = new URLSearchParams(window.location.search).get("lang");
  return normalizeLocale(
    queryLocale
      || pick(
        envelope,
        ["locale", "language"],
        pick(
          battle,
          ["locale", "language"],
          navigator.language || "en",
        ),
      ),
  );
}

export function translate(copy: LocaleCopy, key: string): string {
  return copy[key] || COPY.en[key] || key;
}

export function readEnvelope(): UnknownRecord {
  const payloadNodes = document.querySelectorAll("#bpp-report-data");
  if (payloadNodes.length !== 1) {
    throw new ReportError("duplicateData", String(payloadNodes.length));
  }
  const payload = payloadNodes[0];
  if (
    payload.tagName !== "SCRIPT"
    || payload.getAttribute("type") !== "application/json"
  ) {
    throw new ReportError("invalidData", "payload-element");
  }
  const raw = payload.textContent;
  if (!raw || raw.trim().length === 0) {
    throw new ReportError("invalidData", "empty-payload");
  }
  let envelope: unknown;
  try {
    envelope = JSON.parse(raw);
  } catch (error) {
    throw new ReportError(
      "invalidData",
      error instanceof Error ? error.message : "json",
    );
  }
  if (!isRecord(envelope)) {
    throw new ReportError("invalidData", "envelope");
  }
  const schemaVersion = asFiniteNumber(envelope.schemaVersion, Number.NaN);
  if (schemaVersion !== VIEWER_SCHEMA_VERSION) {
    throw new ReportError(
      "unsupportedSchema",
      String(envelope.schemaVersion),
    );
  }
  if (!isRecord(envelope.battleDocument)) {
    throw new ReportError("invalidData", "battleDocument");
  }
  return envelope;
}

function fillMissingEntityNames(
  entities: NormalizedEntity[],
  copy: LocaleCopy,
): void {
  const counters = new Map<string, number>();
  for (const entity of entities) {
    if (entity.name) continue;
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
  documentModel: UnknownRecord,
  side: "player" | "opponent",
  fallback: string,
): string {
  const summary = isRecord(documentModel.summary)
    ? documentModel.summary
    : {};
  const summaryKey = side === "player" ? "playerName" : "opponentName";
  const summaryName = asString(summary[summaryKey], "");
  if (summaryName) return summaryName;

  const direct = isRecord(documentModel[side])
    ? documentModel[side]
    : {};
  const directName = asString(
    pick(direct, ["name", "displayName"], ""),
    "",
  );
  if (directName) return directName;

  const participant = asArray(documentModel.participants).find(
    (entry) =>
      isRecord(entry)
      && asString(
        pick(entry, ["side", "owner", "team"], ""),
        "",
      ).toLowerCase() === side,
  );
  return participant
    ? asString(
      pick(participant, ["name", "displayName"], fallback),
      fallback,
    )
    : fallback;
}

export function buildViewModel(
  envelope: UnknownRecord,
  copy: LocaleCopy,
): ReportViewModel {
  const battle = envelope.battleDocument;
  if (!isRecord(battle)) {
    throw new ReportError("invalidData", "battleDocument");
  }
  const manifest = isRecord(envelope.recordingManifest)
    ? envelope.recordingManifest
    : null;
  const battleId = asString(pick(battle, ["battleId"], ""), "");
  const entities = asArray(
    pick(battle, ["entities", "entityTable"], []),
  )
    .filter(isRecord)
    .map(normalizeEntity);
  fillMissingEntityNames(entities, copy);
  const events = asArray(
    pick(battle, ["events", "combatEvents"], []),
  )
    .filter(isRecord)
    .map(normalizeEvent);
  events.sort(
    (left, right) =>
      left.combatMs - right.combatMs
      || left.frame - right.frame
      || left.sequence - right.sequence,
  );
  const metrics = frameZeroMetricSamples(battle).concat(
    asArray(pick(battle, ["metrics", "metricSamples"], []))
      .filter(isRecord)
      .map(normalizeMetric)
      .filter((sample): sample is NormalizedMetric => sample !== null),
  );
  metrics.sort(
    (left, right) =>
      left.combatMs - right.combatMs || left.frame - right.frame,
  );

  const eventDuration = events.reduce(
    (maximum, event) => Math.max(maximum, event.combatMs),
    0,
  );
  const metricDuration = metrics.reduce(
    (maximum, metric) => Math.max(maximum, metric.combatMs),
    0,
  );
  const inferredDuration = Math.max(eventDuration, metricDuration);
  const durationMs = Math.max(
    inferredDuration,
    asFiniteNumber(
      pick(
        battle,
        ["durationMs", "combatDurationMs"],
        inferredDuration,
      ),
      inferredDuration,
    ),
  );
  const summary = isRecord(battle.summary) ? battle.summary : {};
  const outcome = asString(
    pick(
      summary,
      ["outcome", "result"],
      pick(battle, ["outcome", "result"], ""),
    ),
    "",
  ).toLowerCase();
  const nestedVideo = manifest && isRecord(manifest.video)
    ? pick(manifest.video, ["relativeUrl", "relativePath"], "")
    : "";
  const videoValue = manifest
    ? pick(
      manifest,
      ["videoRelativeUrl", "videoRelativePath"],
      nestedVideo,
    )
    : "";
  const videoUrl = safeVideoUrl(videoValue);
  const sync = normalizeSyncState(manifest, battleId);
  if (videoValue && !videoUrl) sync.issues.push("unsafeVideoPath");

  return {
    battleId,
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
    outcome,
    durationMs,
    frameDurationMs: Math.max(
      1,
      Math.round(asFiniteNumber(battle.frameDurationMs, 50)),
    ),
    frameCount: Math.max(
      0,
      Math.round(asFiniteNumber(battle.frameCount, 0)),
    ),
    rawRecordCount: Math.max(
      0,
      Math.round(asFiniteNumber(battle.rawRecordCount, events.length)),
    ),
    entities,
    events,
    metrics,
    videoUrl,
    sync,
  };
}
