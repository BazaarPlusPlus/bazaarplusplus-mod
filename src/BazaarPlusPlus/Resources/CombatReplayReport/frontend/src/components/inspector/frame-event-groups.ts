import type { NormalizedEvent } from "../../model/normalize.ts";

export interface MergedInspectorEvent {
  event: NormalizedEvent;
  count: number;
  targetIds: string[];
  key: string;
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
