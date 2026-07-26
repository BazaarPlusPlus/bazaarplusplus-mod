import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../model/normalize.ts";
import { eventKindToken } from "../timeline/clusters.ts";

export interface CombatLogEntry {
  id: string;
  frame: number;
  combatMs: number;
  token: string;
  sourceId: string;
  triggerSourceId: string;
  targetIds: string[];
  eventIds: string[];
  value: unknown;
  unit: string;
  count: number;
}

function valueKey(value: unknown): string {
  if (typeof value === "number") {
    return Object.is(value, -0) ? "-0" : String(value);
  }
  if (value === null) return "null";
  if (typeof value !== "object") return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

function entryKey(event: NormalizedEvent): string {
  return [
    event.frame,
    event.kind,
    eventKindToken(event),
    event.action,
    event.sourceId,
    event.triggerSourceId,
    [...event.targetIds].sort().join(","),
    [...event.removedTargetIds].sort().join(","),
    valueKey(event.value),
    valueKey(event.previousValue),
    valueKey(event.currentValue),
    event.unit,
    event.iconSemanticKey,
    event.icon,
    event.attributionConfidence,
    event.role,
  ].join("\u001f");
}

export function buildCombatLogEntries(
  events: readonly NormalizedEvent[],
): CombatLogEntry[] {
  const merged = new Map<string, CombatLogEntry>();
  for (const event of events) {
    const token = eventKindToken(event);
    const key = entryKey(event);
    const current = merged.get(key);
    if (current) {
      current.count += Math.max(1, event.occurrences);
      current.eventIds.push(event.id);
      continue;
    }
    merged.set(key, {
      id: `${event.id}:${key}`,
      frame: event.frame,
      combatMs: event.combatMs,
      token,
      sourceId: event.sourceId,
      triggerSourceId: event.triggerSourceId,
      targetIds: [...event.targetIds],
      eventIds: [event.id],
      value: event.value,
      unit: event.unit,
      count: Math.max(1, event.occurrences),
    });
  }
  return Array.from(merged.values()).sort(
    (left, right) =>
      left.combatMs - right.combatMs
      || left.frame - right.frame
      || left.id.localeCompare(right.id),
  );
}

export function nearestCombatLogEntryIndex(
  entries: readonly CombatLogEntry[],
  combatMs: number,
): number {
  if (entries.length === 0) return -1;
  let low = 0;
  let high = entries.length;
  while (low < high) {
    const middle = Math.floor((low + high) / 2);
    if (entries[middle].combatMs < combatMs) low = middle + 1;
    else high = middle;
  }
  if (low === 0) return 0;
  if (low >= entries.length) return entries.length - 1;
  const before = entries[low - 1];
  const after = entries[low];
  return combatMs - before.combatMs <= after.combatMs - combatMs
    ? low - 1
    : low;
}

export function selectedCombatLogEntryIndex(
  entries: readonly CombatLogEntry[],
  eventIds: readonly string[],
  combatMs: number,
): number {
  if (eventIds.length > 0) {
    const selectedIds = new Set(eventIds);
    const exactIndex = entries.findIndex((entry) =>
      entry.eventIds.some((id) => selectedIds.has(id))
    );
    if (exactIndex >= 0) return exactIndex;
  }
  return nearestCombatLogEntryIndex(entries, combatMs);
}

export function combatLogEntity(
  entityById: ReadonlyMap<string, NormalizedEntity>,
  id: string,
): NormalizedEntity | null {
  return entityById.get(id) ?? null;
}
