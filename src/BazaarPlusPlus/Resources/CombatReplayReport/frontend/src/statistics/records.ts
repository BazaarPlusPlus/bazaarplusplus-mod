import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../model/normalize.ts";
import type { ReportViewModel } from "../model/report.ts";
import { asFiniteNumber } from "../model/value.ts";
import {
  buildEntityActivity,
  type EntityActivityRow,
} from "./aggregate.ts";

export interface BattleRecord {
  key: "biggest-hit" | "control" | "active";
  label: string;
  value: number;
  unit: "points" | "milliseconds" | "events";
  entity: NormalizedEntity | null;
  event: NormalizedEvent | null;
}

function sourceEntity(
  entityById: ReadonlyMap<string, NormalizedEntity>,
  event: NormalizedEvent | null,
): NormalizedEntity | null {
  if (!event) return null;
  return (
    entityById.get(event.sourceId)
    ?? entityById.get(event.triggerSourceId)
    ?? null
  );
}

function maxEvent(
  events: readonly NormalizedEvent[],
  predicate: (event: NormalizedEvent) => boolean,
): NormalizedEvent | null {
  let best: NormalizedEvent | null = null;
  let bestValue = Number.NEGATIVE_INFINITY;
  for (const event of events) {
    if (!predicate(event)) continue;
    const value = Math.abs(asFiniteNumber(event.value, Number.NaN));
    if (!Number.isFinite(value) || value <= bestValue) continue;
    best = event;
    bestValue = value;
  }
  return best;
}

function mostActive(rows: readonly EntityActivityRow[]): EntityActivityRow | null {
  return rows.reduce<EntityActivityRow | null>(
    (best, row) =>
      !best || row.triggers > best.triggers ? row : best,
    null,
  );
}

export function buildBattleRecords(model: ReportViewModel): BattleRecord[] {
  const entityById = new Map(
    model.entities.map((entity) => [entity.id, entity]),
  );
  const biggestHit = maxEvent(
    model.events,
    (event) =>
      event.kind.toLowerCase() === "effect-executed"
      && event.action === "PlayerDamage",
  );
  const control = maxEvent(
    model.events,
    (event) =>
      event.kind.toLowerCase() === "effect-executed"
      && (event.action === "CardSlow" || event.action === "CardFreeze"),
  );
  const active = mostActive(buildEntityActivity(model));
  const records: BattleRecord[] = [];
  if (biggestHit) {
    records.push({
      key: "biggest-hit",
      label: "recordBiggestHit",
      value: Math.abs(asFiniteNumber(biggestHit.value, 0)),
      unit: "points",
      entity: sourceEntity(entityById, biggestHit),
      event: biggestHit,
    });
  }
  if (control) {
    records.push({
      key: "control",
      label: "recordLongestControl",
      value: Math.abs(asFiniteNumber(control.value, 0)),
      unit: "milliseconds",
      entity: sourceEntity(entityById, control),
      event: control,
    });
  }
  if (active) {
    records.push({
      key: "active",
      label: "recordMostActive",
      value: active.triggers,
      unit: "events",
      entity: active.entity,
      event: null,
    });
  }
  return records;
}
