import {
  eventDamageKind,
  type DamageKind,
} from "./damage-semantics.ts";
import type {
  NormalizedEntity,
  NormalizedEvent,
  NormalizedMetric,
} from "./normalize.ts";

interface DefeatCause {
  amount: number;
  kind: DamageKind;
  settlement: NormalizedEvent;
}

function finiteValue(event: Pick<NormalizedEvent, "value">): number {
  const value =
    typeof event.value === "number" ? event.value : Number(event.value);
  return Number.isFinite(value) ? value : 0;
}

function eventTargets(
  event: Pick<NormalizedEvent, "targetIds">,
  entityId: string,
): boolean {
  return event.targetIds.includes(entityId);
}

function healthBeforeDefeat(
  metrics: readonly NormalizedMetric[],
  side: NormalizedEntity["side"],
  defeat: Pick<NormalizedEvent, "frame" | "combatMs">,
): number | null {
  let latest: NormalizedMetric | null = null;
  for (const metric of metrics) {
    if (
      metric.side !== side
      || metric.metric !== "health"
      || (
        metric.frame > defeat.frame
        || (
          metric.frame === defeat.frame
          && metric.combatMs >= defeat.combatMs
        )
      )
    ) {
      continue;
    }
    if (
      !latest
      || metric.frame > latest.frame
      || (
        metric.frame === latest.frame
        && metric.combatMs > latest.combatMs
      )
    ) {
      latest = metric;
    }
  }
  return latest?.value ?? null;
}

function defeatCause(
  frameEvents: readonly NormalizedEvent[],
  targetId: string,
  previousHealth: number | null,
): DefeatCause | null {
  const healthEvents = frameEvents
    .filter(
      (event) =>
        event.kind.toLowerCase() === "health"
        && event.action.toLowerCase().startsWith("health:")
        && eventTargets(event, targetId)
        && finiteValue(event) !== 0,
    )
    .sort((left, right) => left.sequence - right.sequence);

  if (previousHealth !== null && Number.isFinite(previousHealth)) {
    let health = previousHealth;
    for (const event of healthEvents) {
      const delta = finiteValue(event);
      const nextHealth = health + delta;
      const kind = eventDamageKind(event);
      if (health > 0 && nextHealth <= 0 && delta < 0 && kind) {
        return {
          amount: Math.abs(delta),
          kind,
          settlement: event,
        };
      }
      health = nextHealth;
    }
  }

  const damaging = healthEvents.filter(
    (event) => finiteValue(event) < 0 && eventDamageKind(event),
  );
  if (damaging.length === 0) return null;
  const kinds = new Set(damaging.map((event) => eventDamageKind(event)));
  if (kinds.size !== 1) return null;
  return {
    amount: damaging.reduce(
      (total, event) => total + Math.abs(finiteValue(event)),
      0,
    ),
    kind: eventDamageKind(damaging[0]) ?? "other",
    settlement: damaging[0],
  };
}

function exactDirectSources(
  frameEvents: readonly NormalizedEvent[],
  targetId: string,
): NormalizedEvent[] {
  const candidates = frameEvents.filter(
    (event) =>
      event.kind.toLowerCase() === "effect-executed"
      && eventDamageKind(event) === "direct"
      && eventTargets(event, targetId)
      && Boolean(event.sourceId || event.triggerSourceId),
  );
  const provenance = new Set(
    candidates.map(
      (event) =>
        `${event.sourceId || event.triggerSourceId}\u001f`
        + `${event.triggerSourceId}`,
    ),
  );
  return provenance.size === 1 ? candidates : [];
}

function defeatAction(kind: DamageKind): string {
  if (kind === "direct") return "Died:Direct";
  if (kind === "burn") return "Died:Burn";
  if (kind === "poison") return "Died:Poison";
  return "Died:Other";
}

/**
 * CombatantDied identifies only the recipient. Reconstruct the lethal damage
 * kind from the ordered same-frame health settlements and the previous health
 * sample. Direct damage receives a source only when that frame has one unique
 * exact damage provenance; accumulated Burn/Poison never borrow the source of
 * an unrelated same-frame application.
 */
export function enrichDefeatEvents(
  events: readonly NormalizedEvent[],
  metrics: readonly NormalizedMetric[],
  entities: readonly NormalizedEntity[],
): NormalizedEvent[] {
  const entityById = new Map(
    entities.map((entity) => [entity.id, entity] as const),
  );
  const eventsByFrame = new Map<number, NormalizedEvent[]>();
  for (const event of events) {
    const frameEvents = eventsByFrame.get(event.frame);
    if (frameEvents) frameEvents.push(event);
    else eventsByFrame.set(event.frame, [event]);
  }

  const replacedTimelineEventIds = new Set<string>();
  const enriched = events.map((event) => {
    if (event.kind.toLowerCase() !== "combatant-died") return event;
    const targetId = event.targetIds[0] ?? "";
    const target = entityById.get(targetId);
    if (!target || target.type.toLowerCase() !== "hero") return event;
    const frameEvents = eventsByFrame.get(event.frame) ?? [];
    const cause = defeatCause(
      frameEvents,
      targetId,
      healthBeforeDefeat(metrics, target.side, event),
    );
    if (!cause) return event;

    const directSources =
      cause.kind === "direct"
        ? exactDirectSources(frameEvents, targetId)
        : [];
    const directSource = directSources[0];
    for (const sourceEvent of directSources) {
      replacedTimelineEventIds.add(sourceEvent.id);
    }
    return {
      ...event,
      action: defeatAction(cause.kind),
      value: cause.amount,
      unit: cause.settlement.unit,
      sourceId: directSource?.sourceId || directSource?.triggerSourceId || "",
      triggerSourceId: directSource?.triggerSourceId || "",
      attributionConfidence: directSource
        ? "derived-exact-same-frame-lethal-transition"
        : "derived-lethal-health-transition",
      iconSemanticKey:
        cause.settlement.iconSemanticKey
        || directSource?.iconSemanticKey
        || "",
      icon: cause.settlement.icon || directSource?.icon || "",
    };
  });
  return enriched.map((event) =>
    replacedTimelineEventIds.has(event.id)
      ? { ...event, timelineReplacedByDefeat: true }
      : event
  );
}
