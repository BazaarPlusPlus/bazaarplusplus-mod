import { safeAssetUrl } from "./asset-paths.ts";
import {
  asArray,
  asFiniteNumber,
  asString,
  isRecord,
  pick,
} from "./value.ts";
import { METRIC_ORDER, type StateMetric } from "../timeline/state-scale.ts";

export interface NormalizedEntity {
  id: string;
  name: string;
  type: string;
  side: string;
  span: number;
  asset: string;
  hiddenFromTimeline: boolean;
  raw: unknown;
}

export interface NormalizedEvent {
  id: string;
  frame: number;
  sequence: number;
  combatMs: number;
  kind: string;
  action: string;
  value: unknown;
  previousValue: unknown;
  currentValue: unknown;
  unit: string;
  sourceId: string;
  triggerSourceId: string;
  targetIds: string[];
  removedTargetIds: string[];
  role: string;
  attributionConfidence: string;
  iconSemanticKey: string;
  icon: string;
  occurrences: number;
  raw: unknown;
}

export interface NormalizedMetric {
  frame: number;
  combatMs: number;
  side: "player" | "opponent";
  metric: StateMetric;
  value: number;
  unit: string;
}

export function normalizeSide(
  side: unknown,
): "player" | "opponent" | "neutral" {
  const value = asString(side, "neutral").toLowerCase();
  if (
    value.includes("player")
    || value.includes("friendly")
    || value === "self"
  ) {
    return "player";
  }
  if (value.includes("opponent") || value.includes("enemy")) {
    return "opponent";
  }
  return "neutral";
}

export function normalizeEntity(
  raw: unknown,
  index: number,
): NormalizedEntity {
  const visualValue = pick(raw, ["visual"], null);
  const visual = isRecord(visualValue) ? visualValue : {};
  const fallbackId = `entity-${index}`;
  const id = asString(
    pick(raw, ["entityId", "instanceId", "id"], fallbackId),
    fallbackId,
  );
  const capturedName = asString(
    pick(raw, ["capturedName", "name", "displayName", "templateName"], ""),
    "",
  );
  const type = asString(
    pick(raw, ["type", "entityType", "semanticType"], "entity"),
    "entity",
  );
  const hiddenFromTimeline =
    type.toLowerCase() === "effect"
    && /^\[[^\]]+\]\s+Socket Effect$/iu.test(capturedName);
  const name = capturedName === id || hiddenFromTimeline ? "" : capturedName;
  const side = asString(
    pick(raw, ["owner", "side", "team"], "neutral"),
    "neutral",
  ).toLowerCase();
  const span = Math.max(
    1,
    Math.min(
      3,
      Math.round(
        asFiniteNumber(pick(raw, ["span", "slotSpan", "size"], 1), 1),
      ),
    ),
  );
  const asset = asString(
    pick(
      raw,
      ["assetRelativeUrl", "iconRelativeUrl"],
      pick(visual, ["assetRelativeUrl", "relativeUrl"], ""),
    ),
    "",
  );
  return {
    id,
    name,
    type,
    side,
    span,
    asset,
    hiddenFromTimeline,
    raw,
  };
}

function nestedEntityId(value: unknown): string {
  if (typeof value === "string" || typeof value === "number") {
    return String(value);
  }
  if (isRecord(value)) {
    return asString(pick(value, ["entityId", "instanceId", "id"], ""), "");
  }
  return "";
}

export function eventTimeMs(raw: unknown): number {
  const direct = asFiniteNumber(
    pick(
      raw,
      ["combatMs", "combatTimeMs", "timeMs", "timestampMs"],
      Number.NaN,
    ),
    Number.NaN,
  );
  if (Number.isFinite(direct)) {
    return Math.max(0, direct);
  }
  const seconds = asFiniteNumber(
    pick(raw, ["combatTimeSeconds", "timeSeconds"], Number.NaN),
    Number.NaN,
  );
  if (Number.isFinite(seconds)) {
    return Math.max(0, seconds * 1_000);
  }
  const frame = asFiniteNumber(
    pick(raw, ["frame", "combatFrame"], Number.NaN),
    Number.NaN,
  );
  return Number.isFinite(frame) ? Math.max(0, frame * 50) : 0;
}

export function normalizeEvent(
  raw: unknown,
  index: number,
): NormalizedEvent {
  const sourceValue = pick(raw, ["sourceEntityId", "source"], "");
  const triggerSourceValue = pick(
    raw,
    ["triggerSourceEntityId", "triggerSource"],
    "",
  );
  const targetValues = pick(
    raw,
    ["targetEntityIds", "targets", "targetEntities"],
    [],
  );
  const removedTargetValues = pick(
    raw,
    ["removedTargetEntityIds", "removedTargets"],
    [],
  );
  const singularTarget = pick(raw, ["targetEntityId", "target"], null);
  const targets = asArray(targetValues).map(nestedEntityId).filter(Boolean);
  const removedTargets = asArray(removedTargetValues)
    .map(nestedEntityId)
    .filter(Boolean);
  const singularTargetId = nestedEntityId(singularTarget);
  if (singularTargetId && !targets.includes(singularTargetId)) {
    targets.push(singularTargetId);
  }

  const fallbackId = `event-${index}`;
  return {
    id: asString(pick(raw, ["eventId", "id"], fallbackId), fallbackId),
    frame: Math.max(
      0,
      Math.round(
        asFiniteNumber(pick(raw, ["frame", "combatFrame"], 0), 0),
      ),
    ),
    sequence: Math.max(
      0,
      Math.round(
        asFiniteNumber(
          pick(raw, ["frameSequence", "sequence", "order"], index),
          index,
        ),
      ),
    ),
    combatMs: eventTimeMs(raw),
    kind: asString(
      pick(raw, ["semanticKind", "kind", "type"], "status"),
      "status",
    ),
    action: asString(pick(raw, ["action", "effectAction"], ""), ""),
    value: pick(raw, ["value", "amount"], null),
    previousValue: pick(raw, ["previousValue"], null),
    currentValue: pick(raw, ["currentValue"], null),
    unit: asString(pick(raw, ["unit", "valueUnit"], ""), ""),
    sourceId: nestedEntityId(sourceValue),
    triggerSourceId: nestedEntityId(triggerSourceValue),
    targetIds: targets,
    removedTargetIds: removedTargets,
    role: asString(pick(raw, ["role"], ""), ""),
    attributionConfidence: asString(
      pick(raw, ["attributionConfidence", "attribution"], "unknown"),
      "unknown",
    ).toLowerCase(),
    iconSemanticKey: asString(
      pick(raw, ["iconSemanticKey", "statusIconSemanticKey"], ""),
      "",
    ),
    icon: safeAssetUrl(
      asString(
        pick(raw, ["iconAssetRelativeUrl", "iconRelativeUrl"], ""),
        "",
      ),
    ),
    occurrences: Math.max(
      1,
      Math.round(
        asFiniteNumber(pick(raw, ["occurrences", "count"], 1), 1),
      ),
    ),
    raw,
  };
}

export function normalizeMetricName(value: unknown): StateMetric | "" {
  const normalized = asString(value, "")
    .replace(/[\s_-]/gu, "")
    .toLowerCase();
  if (normalized === "health" || normalized === "hp") return "health";
  if (normalized === "rage") return "rage";
  if (
    normalized === "healthregen"
    || normalized === "regen"
    || normalized === "regeneration"
  ) {
    return "healthRegen";
  }
  if (normalized === "shield" || normalized === "armor") return "shield";
  if (normalized === "burn" || normalized === "fire") return "burn";
  if (normalized === "poison" || normalized === "toxic") return "poison";
  return "";
}

export function normalizeMetric(raw: unknown): NormalizedMetric | null {
  const metric = normalizeMetricName(pick(raw, ["metric", "name"], ""));
  const side = normalizeSide(
    asString(
      pick(raw, ["combatant", "side", "owner"], "neutral"),
      "neutral",
    ).toLowerCase(),
  );
  const value = asFiniteNumber(
    pick(raw, ["value", "currentValue"], Number.NaN),
    Number.NaN,
  );
  if (!metric || side === "neutral" || !Number.isFinite(value)) {
    return null;
  }
  return {
    frame: Math.max(
      0,
      Math.round(
        asFiniteNumber(pick(raw, ["frame", "combatFrame"], 0), 0),
      ),
    ),
    combatMs: Math.max(
      0,
      asFiniteNumber(
        pick(raw, ["combatTimeMs", "combatMs", "timeMs"], 0),
        0,
      ),
    ),
    side,
    metric,
    value,
    unit: asString(pick(raw, ["unit"], "points"), "points"),
  };
}

export function frameZeroMetricSamples(
  battle: unknown,
): NormalizedMetric[] {
  const frameZero = pick(battle, ["frameZeroState"], null);
  if (!isRecord(frameZero)) return [];
  const samples: NormalizedMetric[] = [];
  for (const side of ["player", "opponent"] as const) {
    const state = pick(frameZero, [side], null);
    if (!isRecord(state)) continue;
    for (const metric of METRIC_ORDER) {
      const value = asFiniteNumber(
        pick(state, [metric], Number.NaN),
        Number.NaN,
      );
      if (!Number.isFinite(value)) continue;
      samples.push({
        frame: 0,
        combatMs: 0,
        side,
        metric,
        value,
        unit: "points",
      });
    }
  }
  return samples;
}
