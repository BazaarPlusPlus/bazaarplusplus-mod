import type { NormalizedEvent } from "../../model/normalize.ts";
import { eventDamageKind } from "../../model/damage-semantics.ts";

export interface MergedInspectorEvent {
  event: NormalizedEvent;
  count: number;
  targetIds: string[];
  key: string;
}

export interface DirectDamageGroupSummary {
  amount: number;
  settlementEventIds: string[];
}

function isDirectDamageAction(event: NormalizedEvent): boolean {
  return (
    event.kind.toLowerCase() === "effect-executed"
    && eventDamageKind(event) === "direct"
  );
}

function provenanceKey(event: NormalizedEvent): string {
  const sourceId = event.sourceId || event.triggerSourceId;
  const triggerSourceId =
    event.triggerSourceId && event.triggerSourceId !== sourceId
      ? event.triggerSourceId
      : "";
  return `${sourceId}\u001f${triggerSourceId}`;
}

export function summarizeDirectDamageGroup(
  focusedEvents: readonly NormalizedEvent[],
  frameEvents: readonly NormalizedEvent[],
  implicitTargetId: string,
): DirectDamageGroupSummary | null {
  if (
    focusedEvents.length < 2
    || !focusedEvents.every(
      (event) =>
        isDirectDamageAction(event)
        && event.targetIds.includes(implicitTargetId),
    )
    || new Set(focusedEvents.map(provenanceKey)).size < 2
  ) {
    return null;
  }

  const focusedIds = new Set(focusedEvents.map((event) => event.id));
  const allDirectActions = frameEvents.filter(
    (event) =>
      isDirectDamageAction(event)
      && event.targetIds.includes(implicitTargetId),
  );
  if (
    allDirectActions.length !== focusedEvents.length
    || allDirectActions.some((event) => !focusedIds.has(event.id))
  ) {
    return null;
  }

  const settlements = frameEvents.filter((event) => {
    if (
      event.kind.toLowerCase() !== "health"
      || eventDamageKind(event) !== "direct"
      || !event.targetIds.includes(implicitTargetId)
    ) {
      return false;
    }
    const value =
      typeof event.value === "number" ? event.value : Number(event.value);
    return Number.isFinite(value) && value < 0;
  });
  if (settlements.length === 0) return null;

  return {
    amount: settlements.reduce(
      (total, event) => total + Math.abs(Number(event.value)),
      0,
    ),
    settlementEventIds: settlements.map((event) => event.id),
  };
}

function eventValueKey(value: unknown): string {
  if (typeof value === "number") {
    return `number:${Object.is(value, -0) ? "-0" : String(value)}`;
  }
  if (
    typeof value === "string"
    || typeof value === "boolean"
    || typeof value === "bigint"
    || typeof value === "undefined"
  ) {
    return `${typeof value}:${String(value)}`;
  }
  if (value === null) return "null";
  try {
    return `object:${JSON.stringify(value)}`;
  } catch {
    return `object:${String(value)}`;
  }
}

export function mergeInspectorEvents(
  events: readonly NormalizedEvent[],
): MergedInspectorEvent[] {
  const merged = new Map<string, MergedInspectorEvent>();
  for (const event of events) {
    const key = [
      event.kind,
      event.action,
      event.sourceId,
      event.triggerSourceId,
      eventValueKey(event.value),
      eventValueKey(event.previousValue),
      eventValueKey(event.currentValue),
      event.unit,
      event.iconSemanticKey,
      event.icon,
      event.attributionConfidence,
      event.role,
    ].join("\u001f");
    const existing = merged.get(key);
    if (existing) {
      existing.count += 1;
      for (const id of event.targetIds) {
        if (!existing.targetIds.includes(id)) {
          existing.targetIds.push(id);
        }
      }
      continue;
    }
    merged.set(key, {
      event,
      count: 1,
      targetIds: [...event.targetIds],
      key: `${key}:${event.id}`,
    });
  }
  return Array.from(merged.values());
}
