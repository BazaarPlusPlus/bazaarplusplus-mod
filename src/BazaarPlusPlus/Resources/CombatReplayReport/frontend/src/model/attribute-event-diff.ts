import {
  formatCompactNumber,
  formatMilliseconds,
} from "../i18n/format.ts";
import {
  eventAttributePolicy,
  eventAttributeSemantic,
} from "./event-semantics.ts";
import type { NormalizedEvent } from "./normalize.ts";
import { asFiniteNumber } from "./value.ts";

export interface AttributeEventDiff {
  deltaText: string;
  polarity: "decrease" | "increase";
  transitionText: string;
}

function formatAttributeValue(
  event: Pick<NormalizedEvent, "kind" | "action" | "unit">,
  value: number,
): string {
  const semantic = eventAttributeSemantic(event);
  if (semantic?.valueKind === "percent") {
    return `${formatCompactNumber(value)}%`;
  }
  const unit = event.unit.toLowerCase();
  if (unit.includes("millisecond") || unit === "ms") {
    return formatMilliseconds(value);
  }
  return formatCompactNumber(value);
}

export function attributeEventDiff(
  event: NormalizedEvent,
): AttributeEventDiff | null {
  if (
    !eventAttributePolicy(event).inspectorVisible
    || !eventAttributeSemantic(event)
  ) {
    return null;
  }
  const previous = asFiniteNumber(event.previousValue, Number.NaN);
  const current = asFiniteNumber(event.currentValue, Number.NaN);
  const delta = asFiniteNumber(
    event.value,
    Number.isFinite(previous) && Number.isFinite(current)
      ? current - previous
      : Number.NaN,
  );
  if (
    !Number.isFinite(previous)
    || !Number.isFinite(current)
    || !Number.isFinite(delta)
    || delta === 0
  ) {
    return null;
  }
  return {
    deltaText:
      `${delta > 0 ? "+" : "−"}${
        formatAttributeValue(event, Math.abs(delta))
      }`,
    polarity: delta > 0 ? "increase" : "decrease",
    transitionText:
      `${formatAttributeValue(event, previous)} → ${
        formatAttributeValue(event, current)
      }`,
  };
}
