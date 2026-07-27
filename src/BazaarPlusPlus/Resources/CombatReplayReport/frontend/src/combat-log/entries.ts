import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../model/normalize.ts";
import { eventKindToken } from "../timeline/clusters.ts";

const DIRECT_STATUS_APPLICATION_ACTIONS = new Set([
  "CardHaste",
  "CardSlow",
  "CardFreeze",
]);

export interface CombatLogEntry {
  id: string;
  frame: number;
  combatMs: number;
  action: string;
  token: string;
  sourceId: string;
  triggerSourceId: string;
  targetIds: string[];
  eventIds: string[];
  value: unknown;
  unit: string;
  count: number;
}

export function combatLogGroupKey(
  entry: Pick<
    CombatLogEntry,
    | "frame"
    | "token"
    | "action"
    | "sourceId"
    | "triggerSourceId"
    | "unit"
    | "value"
  >,
): string {
  return [
    entry.frame,
    entry.token,
    entry.action,
    entry.sourceId,
    entry.triggerSourceId,
    entry.unit,
    valueKey(entry.value),
  ].join("\u001f");
}

/**
 * Direct tempo/control applications are represented by status ranges in the
 * timeline, so their source markers are intentionally hidden there. They are
 * still meaningful narrative actions and must remain in the combat log.
 */
export function isDirectStatusApplicationEvent(
  event: Pick<NormalizedEvent, "kind" | "action">,
): boolean {
  return (
    event.kind.toLowerCase() === "effect-executed"
    && DIRECT_STATUS_APPLICATION_ACTIONS.has(event.action)
  );
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
      action: event.action,
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

/**
 * Collapses a dense frame into readable action summaries without losing the
 * event ids required by the timeline inspector. Actions, values, and sources
 * stay in the key so semantically different outcomes never share a row.
 */
export function groupCombatLogEntries(
  entries: readonly CombatLogEntry[],
): CombatLogEntry[] {
  const grouped = new Map<string, CombatLogEntry>();
  for (const entry of entries) {
    const key = combatLogGroupKey(entry);
    const current = grouped.get(key);
    if (!current) {
      grouped.set(key, {
        ...entry,
        id: `group:${entry.id}`,
        eventIds: [...entry.eventIds],
        targetIds: [...entry.targetIds],
      });
      continue;
    }

    current.count += entry.count;
    current.eventIds.push(...entry.eventIds);
    const targetIds = new Set(current.targetIds);
    for (const targetId of entry.targetIds) targetIds.add(targetId);
    current.targetIds = Array.from(targetIds);
  }
  return Array.from(grouped.values()).sort(
    (left, right) =>
      left.combatMs - right.combatMs
      || left.frame - right.frame
      || left.id.localeCompare(right.id),
  );
}

/**
 * The combat log is a player-facing narrative, not a raw event inspector.
 * Generic attribute mutations and setup markers remain available in the
 * timeline/report payload but would overwhelm the transcript. A death marker
 * is the sole generic-status exception because it closes the battle story.
 */
export function isNarrativeCombatLogEntry(
  entry: Pick<CombatLogEntry, "action" | "token">,
): boolean {
  return entry.token !== "status" || entry.action === "Died";
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
