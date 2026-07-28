import { safeAssetUrl } from "./asset-paths.ts";
import {
  asFiniteNumber,
  asString,
} from "./value.ts";
import { METRIC_ORDER, type StateMetric } from "../timeline/state-scale.ts";

export interface ReportEntityV1 {
  entityId: string;
  templateId?: string;
  owner: string;
  type: string;
  name: string;
  size?: string;
  slot?: number;
  span?: number;
  tier?: string;
  enchant?: string;
  contentKey?: string;
  assetRelativeUrl?: string;
  order: number;
}

export interface ReportRawReferenceV1 {
  category: string;
  type: string;
  index: number;
}

export interface ReportEventV1 {
  eventId: string;
  frame: number;
  frameSequence: number;
  combatTimeMs: number;
  kind: string;
  action: string;
  effectId?: string;
  executionContextId?: string;
  sourceEntityId?: string;
  triggerSourceEntityId?: string;
  targetEntityIds: string[];
  removedTargetEntityIds: string[];
  value?: number;
  previousValue?: number;
  currentValue?: number;
  unit?: string;
  isCritical?: boolean;
  role: string;
  attributionConfidence: string;
  iconSemanticKey?: string;
  iconContentKey?: string;
  iconAssetRelativeUrl?: string;
  rawReference: ReportRawReferenceV1;
}

export interface ReportMetricSampleV1 {
  frame: number;
  combatTimeMs: number;
  combatant: string;
  metric: string;
  value: number;
  unit: string;
}

export interface ReportCardStatsV1 {
  entityId: string;
  damageDone: number;
  shieldAdded: number;
  healAdded: number;
  joyAdded: number;
  poisonAdded: number;
  burnAdded: number;
  hastedCardsCount: number;
  slowedCardsCount: number;
  frozenCardsCount: number;
  useCount: number;
  regenAdded: number;
  rageAdded: number;
}

export interface NormalizedCardStats extends Omit<ReportCardStatsV1, "entityId"> {
  entityId: string;
}

export interface ReportCombatantStateV1 {
  health?: number;
  rage?: number;
  healthRegen?: number;
  shield?: number;
  burn?: number;
  poison?: number;
}

export interface ReportFrameZeroStateV1 {
  player: ReportCombatantStateV1;
  opponent: ReportCombatantStateV1;
}

export interface NormalizedEntity {
  id: string;
  name: string;
  type: string;
  side: "player" | "opponent" | "neutral";
  span: number;
  asset: string;
  hiddenFromTimeline: boolean;
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
  if (value === "player") return "player";
  if (value === "opponent") return "opponent";
  return "neutral";
}

export function normalizeEntity(
  raw: ReportEntityV1,
  index: number,
): NormalizedEntity {
  const fallbackId = `entity-${index}`;
  const id = asString(raw.entityId, fallbackId);
  const capturedName = asString(raw.name, "");
  const type = asString(raw.type, "entity");
  const hiddenFromTimeline =
    type.toLowerCase() === "effect"
    && /^\[[^\]]+\]\s+Socket Effect$/iu.test(capturedName);
  const name = capturedName === id || hiddenFromTimeline ? "" : capturedName;
  const side = normalizeSide(raw.owner);
  const span = Math.max(
    1,
    Math.min(
      3,
      Math.round(asFiniteNumber(raw.span, 1)),
    ),
  );
  const asset = safeAssetUrl(raw.assetRelativeUrl ?? "");
  return {
    id,
    name,
    type,
    side,
    span,
    asset,
    hiddenFromTimeline,
  };
}

export function eventTimeMs(raw: ReportEventV1): number {
  return Math.max(0, asFiniteNumber(raw.combatTimeMs, 0));
}

export function normalizeEvent(
  raw: ReportEventV1,
  index: number,
): NormalizedEvent {
  const fallbackId = `event-${index}`;
  return {
    id: asString(raw.eventId, fallbackId),
    frame: Math.max(0, Math.round(asFiniteNumber(raw.frame, 0))),
    sequence: Math.max(
      0,
      Math.round(asFiniteNumber(raw.frameSequence, index)),
    ),
    combatMs: eventTimeMs(raw),
    kind: asString(raw.kind, "status"),
    action: asString(raw.action, ""),
    value: raw.value ?? null,
    previousValue: raw.previousValue ?? null,
    currentValue: raw.currentValue ?? null,
    unit: asString(raw.unit, ""),
    sourceId: asString(raw.sourceEntityId, ""),
    triggerSourceId: asString(raw.triggerSourceEntityId, ""),
    targetIds: raw.targetEntityIds.slice(),
    removedTargetIds: raw.removedTargetEntityIds.slice(),
    role: asString(raw.role, ""),
    attributionConfidence: asString(
      raw.attributionConfidence,
      "unknown",
    ).toLowerCase(),
    iconSemanticKey: asString(raw.iconSemanticKey, ""),
    icon: safeAssetUrl(raw.iconAssetRelativeUrl ?? ""),
    occurrences: 1,
  };
}

export function normalizeMetricName(value: unknown): StateMetric | "" {
  const normalized = asString(value, "")
    .replace(/[\s_-]/gu, "")
    .toLowerCase();
  if (normalized === "health") return "health";
  if (normalized === "rage") return "rage";
  if (normalized === "healthregen") return "healthRegen";
  if (normalized === "shield") return "shield";
  if (normalized === "burn") return "burn";
  if (normalized === "poison") return "poison";
  return "";
}

export function normalizeMetric(
  raw: ReportMetricSampleV1,
): NormalizedMetric | null {
  const metric = normalizeMetricName(raw.metric);
  const side = normalizeSide(raw.combatant);
  const value = asFiniteNumber(raw.value, Number.NaN);
  if (!metric || side === "neutral" || !Number.isFinite(value)) {
    return null;
  }
  return {
    frame: Math.max(0, Math.round(asFiniteNumber(raw.frame, 0))),
    combatMs: Math.max(0, asFiniteNumber(raw.combatTimeMs, 0)),
    side,
    metric,
    value,
    unit: asString(raw.unit, "points"),
  };
}

export function normalizeCardStats(
  raw: ReportCardStatsV1,
): NormalizedCardStats {
  const nonNegativeInteger = (value: unknown): number =>
    Math.max(0, Math.round(asFiniteNumber(value, 0)));
  return {
    entityId: asString(raw.entityId, ""),
    damageDone: nonNegativeInteger(raw.damageDone),
    shieldAdded: nonNegativeInteger(raw.shieldAdded),
    healAdded: nonNegativeInteger(raw.healAdded),
    joyAdded: nonNegativeInteger(raw.joyAdded),
    poisonAdded: nonNegativeInteger(raw.poisonAdded),
    burnAdded: nonNegativeInteger(raw.burnAdded),
    hastedCardsCount: nonNegativeInteger(raw.hastedCardsCount),
    slowedCardsCount: nonNegativeInteger(raw.slowedCardsCount),
    frozenCardsCount: nonNegativeInteger(raw.frozenCardsCount),
    useCount: nonNegativeInteger(raw.useCount),
    regenAdded: nonNegativeInteger(raw.regenAdded),
    rageAdded: nonNegativeInteger(raw.rageAdded),
  };
}

export function frameZeroMetricSamples(
  frameZero: ReportFrameZeroStateV1,
): NormalizedMetric[] {
  const samples: NormalizedMetric[] = [];
  for (const side of ["player", "opponent"] as const) {
    const state = frameZero[side];
    for (const metric of METRIC_ORDER) {
      const value = asFiniteNumber(state[metric], Number.NaN);
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
