import { attributeEventDiff } from "./attribute-event-diff.ts";
import {
  eventAttributePolicy,
  eventAttributeSemantic,
} from "./event-semantics.ts";
import type { NormalizedEvent } from "./normalize.ts";
import { asFiniteNumber } from "./value.ts";

const RESOLVABLE_ACTIONS = new Set([
  "CardModifyAttribute",
  "CardReload",
]);

function exactTargetId(event: NormalizedEvent): string {
  return event.targetIds.length === 1 && event.removedTargetIds.length === 0
    ? event.targetIds[0]
    : "";
}

function isConcreteTransition(event: NormalizedEvent): boolean {
  return event.kind.toLowerCase() === "card-attribute"
    && exactTargetId(event) !== ""
    && eventAttributePolicy(event).inspectorVisible
    && eventAttributeSemantic(event) !== null
    && attributeEventDiff(event) !== null;
}

function isPositiveAmmoTransition(event: NormalizedEvent): boolean {
  return event.action === "Ammo"
    && asFiniteNumber(event.value, 0) > 0
    && isConcreteTransition(event);
}

function hasAttributedReloadDiff(event: NormalizedEvent): boolean {
  if (event.action !== "CardReload") return false;
  const previous = asFiniteNumber(event.previousValue, Number.NaN);
  const current = asFiniteNumber(event.currentValue, Number.NaN);
  const delta = asFiniteNumber(event.value, Number.NaN);
  return Number.isFinite(previous)
    && Number.isFinite(current)
    && Number.isFinite(delta)
    && delta > 0
    && current - previous === delta;
}

function enrichExecution(
  execution: NormalizedEvent,
  attributeAction: string,
  transition?: NormalizedEvent,
): NormalizedEvent {
  return {
    ...execution,
    resolvedAttributeAction: attributeAction,
    value: transition?.value ?? execution.value,
    previousValue: transition?.previousValue ?? execution.previousValue,
    currentValue: transition?.currentValue ?? execution.currentValue,
    unit: transition?.unit || execution.unit,
    iconSemanticKey:
      transition?.iconSemanticKey
      || eventAttributeSemantic({
        kind: "card-attribute",
        action: attributeAction,
      })?.nativeSemanticKey
      || execution.iconSemanticKey,
    icon: transition?.icon || execution.icon,
  };
}

/**
 * Legacy reports record exact source/target provenance on effect executions and
 * exact before/after values on target-side attribute transitions. Source mode
 * may join those records only when the pairing is unique within one frame and
 * one exact target. Ambiguous records deliberately remain unresolved.
 */
export function resolveSourceModeAttributeEvents(
  events: readonly NormalizedEvent[],
): NormalizedEvent[] {
  const frames = new Map<number, NormalizedEvent[]>();
  for (const event of events) {
    const frame = frames.get(event.frame);
    if (frame) frame.push(event);
    else frames.set(event.frame, [event]);
  }

  const resolvedById = new Map<string, NormalizedEvent>();
  for (const frameEvents of frames.values()) {
    const targetIds = new Set(
      frameEvents
        .map(exactTargetId)
        .filter((targetId) => targetId !== ""),
    );
    for (const targetId of targetIds) {
      const transitions = frameEvents.filter(
        (event) =>
          exactTargetId(event) === targetId
          && isConcreteTransition(event),
      );
      const executions = frameEvents.filter(
        (event) =>
          event.kind.toLowerCase() === "effect-executed"
          && RESOLVABLE_ACTIONS.has(event.action)
          && event.sourceId !== ""
          && exactTargetId(event) === targetId,
      );
      const reserved = new Set<string>();
      const reloads = executions.filter(
        (event) => event.action === "CardReload",
      );
      const ammoTransitions = transitions.filter(isPositiveAmmoTransition);
      if (reloads.length === 1) {
        const reload = reloads[0];
        if (hasAttributedReloadDiff(reload)) {
          const matchingTransition = ammoTransitions.length === 1
            ? ammoTransitions[0]
            : undefined;
          if (matchingTransition) reserved.add(matchingTransition.id);
          resolvedById.set(
            reload.id,
            enrichExecution(reload, "Ammo", matchingTransition),
          );
        } else if (ammoTransitions.length === 1) {
          reserved.add(ammoTransitions[0].id);
          resolvedById.set(
            reload.id,
            enrichExecution(reload, "Ammo", ammoTransitions[0]),
          );
        }
      }

      const modifiers = executions.filter(
        (event) => event.action === "CardModifyAttribute",
      );
      const remainingTransitions = transitions.filter(
        (event) => !reserved.has(event.id),
      );
      if (modifiers.length === 1 && remainingTransitions.length === 1) {
        const transition = remainingTransitions[0];
        resolvedById.set(
          modifiers[0].id,
          enrichExecution(modifiers[0], transition.action, transition),
        );
      }
    }
  }

  return events.map((event) => resolvedById.get(event.id) ?? event);
}
