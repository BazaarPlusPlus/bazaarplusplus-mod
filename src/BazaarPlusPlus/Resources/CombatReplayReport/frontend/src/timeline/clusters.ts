import { asFiniteNumber } from "../model/value.ts";
import {
  baseEventKindToken,
  eventAttributeSemantic,
  eventAttributePolicy,
  eventPresentation,
  timelinePresentationToken,
} from "../model/event-semantics.ts";
import {
  type NormalizedEntity,
  type NormalizedEvent,
} from "../model/normalize.ts";
import type { EventLaneMode } from "./event-lane-mode.ts";
import { timelineXAtCombatMs } from "./geometry.ts";

export const STATUS_ACTIONS = new Set(["Haste", "Slow", "Freeze"]);
const STATUS_APPLY_ACTIONS = new Set([
  "CardHaste",
  "CardSlow",
  "CardFreeze",
]);

const COMBATANT_TARGET_ACTIONS = new Set([
  "PlayerDamage",
  "PlayerHeal",
  "PlayerBurnApply",
  "PlayerBurnRemove",
  "PlayerPoisonApply",
  "PlayerPoisonRemove",
  "PlayerRegenApply",
  "PlayerRegenRemove",
  "PlayerShieldApply",
  "PlayerShieldRemove",
]);
export type LaneRole =
  | "source"
  | "trigger"
  | "target"
  | "removed"
  | "both"
  | "neutral";

export interface TimelineEntity
  extends Pick<NormalizedEntity, "id" | "type"> {}

export interface TimelineModel {
  durationMs: number;
  events: NormalizedEvent[];
}

export interface LaneEndpoint {
  lane: number;
  role: LaneRole;
}

export interface TimelineCluster {
  lane: number;
  x: number;
  y: number;
  role: LaneRole;
  events: NormalizedEvent[];
  relatedEvents?: NormalizedEvent[];
  token: string;
  groupKey?: string;
  labelKey?: string;
  iconSemanticKey?: string;
  icon: string;
  statusRange?: StatusRange;
  members?: TimelineCluster[];
  tier?: EventTier;
  impact?: number;
  markerX?: number;
  markerY?: number;
  markerSizeCap?: number;
}

/**
 * 1 = terminal or high-impact explicit events — loudest.
 * 2 = actions (procs, tempo applications, skills) — standard markers.
 * 3 = low-priority status changes — quiet glyphs.
 */
export type EventTier = 1 | 2 | 3;

export function eventTier(
  event: Pick<NormalizedEvent, "kind" | "action">,
): EventTier {
  const kind = event.kind.toLowerCase();
  if (kind === "health" || kind === "combatant-died") return 1;
  if (kind === "card-status-range") return 3;
  const token = eventKindToken(event);
  if (token === "destroy") return 1;
  if (
    kind === "card-attribute"
    || kind === "player-attribute"
    || token === "status"
  ) {
    return 3;
  }
  return 2;
}

export function clusterEventTier(
  events: readonly Pick<NormalizedEvent, "kind" | "action">[],
): EventTier {
  let tier: EventTier = 3;
  for (const event of events) {
    const candidate = eventTier(event);
    if (candidate < tier) tier = candidate;
    if (tier === 1) break;
  }
  return tier;
}

export function clusterImpact(
  events: readonly Pick<NormalizedEvent, "kind" | "value">[],
): number {
  let impact = 0;
  for (const event of events) {
    if (event.kind.toLowerCase() !== "health") continue;
    impact += Math.abs(asFiniteNumber(event.value, 0));
  }
  return impact;
}

export interface StatusRange {
  action: string;
  targetId: string;
  lane: number;
  startMs: number;
  endMs: number;
  startEvent: NormalizedEvent;
  lastEvent: NormalizedEvent;
  endEvent?: NormalizedEvent;
  x: number;
  endX: number;
  laneHeight: number;
  cluster: TimelineCluster | null;
}

export function timelineClusterEventIds(
  cluster: TimelineCluster,
): string[] {
  return Array.from(
    new Set(cluster.events.map((event) => event.id)),
  );
}

export function kindToken(kind: unknown): string {
  return baseEventKindToken(kind);
}

export function eventKindToken(
  event: Pick<
    NormalizedEvent,
    "kind" | "action" | "resolvedAttributeAction"
  >,
): string {
  return eventPresentation(event).token;
}

export function timelineEventToken(
  event: Pick<
    NormalizedEvent,
    "kind" | "action" | "resolvedAttributeAction"
  >,
): string {
  if (event.resolvedAttributeAction) return "attribute";
  const kind = event.kind.toLowerCase();
  if (kind === "card-attribute" || kind === "player-attribute") {
    return "attribute";
  }
  return timelinePresentationToken(event);
}

export function isVisibleTimelineEvent(
  event: NormalizedEvent,
  entityById: ReadonlyMap<string, TimelineEntity>,
  eventLaneMode: EventLaneMode = "target",
): boolean {
  if (event.timelineReplacedByDefeat) return false;
  const kind = event.kind.toLowerCase();
  if (event.resolvedAttributeAction) {
    return eventAttributePolicy(event).timeline === "marker";
  }
  if (kind === "player-attribute" || kind === "card-attribute") {
    return eventAttributePolicy(event).timeline === "marker";
  }
  // Aura records are execution/provenance bookkeeping. They do not identify the
  // concrete attribute that changed, while the same frame's card/player attribute
  // transition carries the exact subtype and before/after values. Rendering both
  // creates a generic "Status change" marker that obscures the useful diff.
  if (kind === "aura") return false;
  if (kind === "card-status-range") return false;
  if (event.kind.toLowerCase() === "health") return false;
  if (event.kind.toLowerCase() !== "effect-executed") return true;
  if (
    event.action === "CardModifyAttribute"
    || event.action === "CardReload"
    || event.action === "PlayerModifyAttribute"
  ) {
    return false;
  }
  if (
    event.action === "CardHaste"
    || event.action === "CardSlow"
    || event.action === "CardFreeze"
  ) {
    return eventLaneMode === "source";
  }
  if (COMBATANT_TARGET_ACTIONS.has(event.action)) {
    return event.targetIds.some(
      (id) => entityById.get(id)?.type.toLowerCase() === "hero",
    );
  }
  return event.action !== "PlayerRageApply";
}

function statusApplyAction(action: string): string {
  if (action === "Haste") return "CardHaste";
  if (action === "Slow") return "CardSlow";
  if (action === "Freeze") return "CardFreeze";
  return "";
}

function statusApplicationKey(
  frame: number,
  action: string,
  targetId: string,
): string {
  return `${frame}\u001f${action}\u001f${targetId}`;
}

export function buildStatusRanges(
  model: TimelineModel,
  entities: readonly TimelineEntity[],
  timelineWidth: number,
  laneHeight: number,
): StatusRange[] {
  const entityIndex = new Map(
    entities.map((entity, index) => [entity.id, index] as const),
  );
  const active = new Map<string, StatusRange>();
  const ranges: StatusRange[] = [];
  const applicationEvents = new Map<string, NormalizedEvent[]>();

  for (const event of model.events) {
    if (
      event.kind.toLowerCase() !== "effect-executed"
      || !STATUS_APPLY_ACTIONS.has(event.action)
    ) {
      continue;
    }
    const targetIds = new Set([
      ...event.targetIds,
      ...event.removedTargetIds,
    ]);
    for (const targetId of targetIds) {
      if (!entityIndex.has(targetId)) continue;
      const key = statusApplicationKey(
        event.frame,
        event.action,
        targetId,
      );
      const events = applicationEvents.get(key);
      if (events) events.push(event);
      else applicationEvents.set(key, [event]);
    }
  }

  function closeRange(
    range: StatusRange,
    endMs: number,
    endEvent: NormalizedEvent | undefined,
  ): void {
    range.endMs = Math.max(
      range.startMs + 1,
      Math.min(model.durationMs, endMs),
    );
    range.endEvent = endEvent || range.lastEvent;
    ranges.push(range);
  }

  const explicitRangeKeys = new Set<string>();
  for (const event of model.events) {
    if (
      event.kind.toLowerCase() !== "card-status-range"
      || !STATUS_ACTIONS.has(event.action)
    ) {
      continue;
    }
    const targetId = event.targetIds.find((id) => entityIndex.has(id));
    if (!targetId) continue;
    const lane = entityIndex.get(targetId);
    if (lane === undefined) continue;
    const key = `${targetId}:${event.action}`;
    explicitRangeKeys.add(key);
    const range: StatusRange = {
      action: event.action,
      targetId,
      lane,
      startMs: event.combatMs,
      endMs: model.durationMs,
      startEvent: event,
      lastEvent: event,
      x: 0,
      endX: 0,
      laneHeight,
      cluster: null,
    };
    closeRange(
      range,
      event.combatMs + Math.max(1, asFiniteNumber(event.value, 1)),
      event,
    );
  }

  for (const event of model.events) {
    if (
      event.kind.toLowerCase() !== "card-attribute"
      || !STATUS_ACTIONS.has(event.action)
    ) {
      continue;
    }
    const targetId = event.targetIds.find((id) => entityIndex.has(id));
    if (!targetId) continue;
    const lane = entityIndex.get(targetId);
    if (lane === undefined) continue;
    const previous = asFiniteNumber(event.previousValue, 0);
    const current = asFiniteNumber(event.currentValue, 0);
    const key = `${targetId}:${event.action}`;
    if (explicitRangeKeys.has(key)) continue;
    let range = active.get(key);

    if (previous <= 0 && current > 0) {
      if (range) closeRange(range, event.combatMs, event);
      range = {
        action: event.action,
        targetId,
        lane,
        startMs: event.combatMs,
        endMs: model.durationMs,
        startEvent: event,
        lastEvent: event,
        x: 0,
        endX: 0,
        laneHeight,
        cluster: null,
      };
      active.set(key, range);
    } else if (!range && current > 0) {
      range = {
        action: event.action,
        targetId,
        lane,
        startMs: event.combatMs,
        endMs: model.durationMs,
        startEvent: event,
        lastEvent: event,
        x: 0,
        endX: 0,
        laneHeight,
        cluster: null,
      };
      active.set(key, range);
    } else if (range) {
      range.lastEvent = event;
    }

    if (range && current <= 0) {
      closeRange(range, event.combatMs, event);
      active.delete(key);
    }
  }

  for (const range of active.values()) {
    const remainingMs = Math.max(
      0,
      asFiniteNumber(range.lastEvent.currentValue, 0),
    );
    closeRange(
      range,
      Math.min(model.durationMs, range.lastEvent.combatMs + remainingMs),
      range.lastEvent,
    );
  }

  const duration = Math.max(1, model.durationMs);
  for (const range of ranges) {
    const applyAction = statusApplyAction(range.action);
    const relatedEvents = applicationEvents.get(
      statusApplicationKey(
        range.startEvent.frame,
        applyAction,
        range.targetId,
      ),
    ) ?? [];
    range.x = timelineXAtCombatMs(
      range.startMs,
      duration,
      timelineWidth,
    );
    range.endX = timelineXAtCombatMs(
      range.endMs,
      duration,
      timelineWidth,
    );
    range.laneHeight = laneHeight;
    range.cluster = {
      lane: range.lane,
      x: range.x,
      y: range.lane * laneHeight + laneHeight / 2,
      role: "target",
      events:
        relatedEvents.length > 0
          ? relatedEvents
          : [range.startEvent],
      relatedEvents:
        relatedEvents.length > 0
          ? [range.startEvent]
          : undefined,
      token: range.action.toLowerCase(),
      icon: relatedEvents[0]?.icon || range.startEvent.icon,
      statusRange: range,
    };
  }
  return ranges.sort(
    (left, right) => left.x - right.x || left.lane - right.lane,
  );
}

export function eventLaneEndpoints(
  event: NormalizedEvent,
  entityIndex: ReadonlyMap<string, number>,
  eventLaneMode: EventLaneMode = "target",
): LaneEndpoint[] {
  const endpoints: LaneEndpoint[] = [];
  function append(entityId: string, role: LaneRole): void {
    if (!entityId || !entityIndex.has(entityId)) return;
    const lane = entityIndex.get(entityId);
    if (lane === undefined) return;
    const existing = endpoints.find((endpoint) => endpoint.lane === lane);
    if (!existing) endpoints.push({ lane, role });
    else if (existing.role !== role) existing.role = "both";
  }
  if (eventLaneMode === "source") {
    if (event.sourceId) append(event.sourceId, "source");
    else append(event.triggerSourceId, "trigger");
  } else {
    for (const targetId of event.targetIds) append(targetId, "target");
    for (const removedTargetId of event.removedTargetIds) {
      append(removedTargetId, "removed");
    }
  }
  return endpoints;
}

export function relatedLaneRoles(
  cluster: TimelineCluster | null,
  entities: readonly TimelineEntity[],
): Map<number, Set<string>> {
  const entityIndex = new Map(
    entities.map((entity, index) => [entity.id, index] as const),
  );
  const related = new Map<number, Set<string>>();
  if (!cluster) return related;
  function append(entityId: string, role: string): void {
    if (!entityId || !entityIndex.has(entityId)) return;
    const lane = entityIndex.get(entityId);
    if (lane === undefined) return;
    if (!related.has(lane)) related.set(lane, new Set());
    related.get(lane)?.add(role);
  }
  const relatedEvents = cluster.events.concat(
    Array.isArray(cluster.relatedEvents) ? cluster.relatedEvents : [],
  );
  for (const event of relatedEvents) {
    append(event.sourceId, "source");
    append(event.triggerSourceId, "trigger");
    for (const targetId of event.targetIds) append(targetId, "target");
    for (const removedTargetId of event.removedTargetIds) {
      append(removedTargetId, "removed");
    }
  }
  return related;
}

export function buildClusters(
  model: Pick<TimelineModel, "durationMs">,
  events: readonly NormalizedEvent[],
  entities: readonly TimelineEntity[],
  timelineWidth: number,
  laneHeight: number,
  eventLaneMode: EventLaneMode = "target",
): TimelineCluster[] {
  const entityIndex = new Map(
    entities.map((entity, index) => [entity.id, index] as const),
  );
  const duration = Math.max(1, model.durationMs);
  const clusterMap = new Map<string, TimelineCluster>();
  const iconBySemanticKey = new Map<string, string>();
  for (const event of events) {
    const semanticKey =
      event.iconSemanticKey
      || eventAttributeSemantic(event)?.nativeSemanticKey
      || "";
    if (semanticKey && event.icon && !iconBySemanticKey.has(semanticKey)) {
      iconBySemanticKey.set(semanticKey, event.icon);
    }
  }

  for (const event of events) {
    const x = timelineXAtCombatMs(event.combatMs, duration, timelineWidth);
    const kind = event.kind.toLowerCase();
    const isAttribute =
      kind === "card-attribute"
      || kind === "player-attribute"
      || Boolean(event.resolvedAttributeAction);
    const token = timelineEventToken(event);
    const presentation = eventPresentation(event);
    const groupKey = isAttribute ? "attribute" : presentation.groupKey;
    const labelKey = isAttribute ? "attribute" : presentation.labelKey;
    const iconSemanticKey = isAttribute
      ? ""
      : event.iconSemanticKey
        || eventAttributeSemantic(event)?.nativeSemanticKey
        || "";
    const icon = isAttribute
      ? ""
      : event.icon
        || iconBySemanticKey.get(iconSemanticKey)
        || "";
    for (
      const endpoint of eventLaneEndpoints(
        event,
        entityIndex,
        eventLaneMode,
      )
    ) {
      const pixel = Math.round(x);
      const key =
        `${event.frame}:${endpoint.lane}:${pixel}:${endpoint.role}:`
        + `${token}:${groupKey}:${iconSemanticKey}`;
      let cluster = clusterMap.get(key);
      if (!cluster) {
        cluster = {
          lane: endpoint.lane,
          x,
          y: endpoint.lane * laneHeight + laneHeight / 2,
          role: endpoint.role,
          events: [],
          token,
          groupKey,
          labelKey,
          iconSemanticKey,
          icon,
        };
        clusterMap.set(key, cluster);
      }
      cluster.events.push(event);
      if (!cluster.icon && icon) cluster.icon = icon;
    }
  }

  const built = Array.from(clusterMap.values());
  for (const cluster of built) {
    if (cluster.token === "attribute") {
      cluster.icon = "";
      cluster.iconSemanticKey = "";
    } else {
      const icons = new Set(
        cluster.events.map((event) => event.icon).filter(Boolean),
      );
      if (icons.size === 1) cluster.icon = Array.from(icons)[0];
      else if (icons.size > 1) cluster.icon = "";
    }
    cluster.tier = clusterEventTier(cluster.events);
    cluster.impact = clusterImpact(cluster.events);
  }
  return built.sort(
    (left, right) => left.x - right.x || left.lane - right.lane,
  );
}

export function buildVisualClusters(
  clusters: readonly TimelineCluster[],
): TimelineCluster[] {
  const grouped = new Map<string, TimelineCluster[]>();
  const visual: TimelineCluster[] = [];
  for (const cluster of clusters) {
    if (cluster.statusRange) {
      visual.push(cluster);
      continue;
    }
    const key =
      `${cluster.lane}:${cluster.role}:${cluster.token}:`
      + `${cluster.groupKey ?? cluster.labelKey ?? ""}:`
      + `${cluster.iconSemanticKey ?? ""}`;
    if (!grouped.has(key)) grouped.set(key, []);
    grouped.get(key)?.push(cluster);
  }
  for (const row of grouped.values()) {
    row.sort((left, right) => left.x - right.x);
    let members: TimelineCluster[] = [];
    let firstX = 0;
    function flush(): void {
      if (members.length === 0) return;
      if (members.length === 1) {
        visual.push(members[0]);
      } else {
        const representative = members[0];
        const icons = new Set(
          members.map((member) => member.icon).filter(Boolean),
        );
        const mergedEvents = members.flatMap((member) => member.events);
        visual.push({
          lane: representative.lane,
          x:
            members.reduce((sum, member) => sum + member.x, 0)
            / members.length,
          y: representative.y,
          role: representative.role,
          events: mergedEvents,
          token: representative.token,
          groupKey: representative.groupKey,
          labelKey: representative.labelKey,
          iconSemanticKey: representative.iconSemanticKey,
          icon: icons.size === 1 ? Array.from(icons)[0] : "",
          members: members.slice(),
          tier: clusterEventTier(mergedEvents),
          impact: clusterImpact(mergedEvents),
        });
      }
      members = [];
    }
    for (const cluster of row) {
      if (members.length === 0) {
        firstX = cluster.x;
        members.push(cluster);
        continue;
      }
      if (cluster.x - firstX <= 30) {
        members.push(cluster);
      } else {
        flush();
        firstX = cluster.x;
        members.push(cluster);
      }
    }
    flush();
  }
  return visual.sort(
    (left, right) => left.x - right.x || left.lane - right.lane,
  );
}

export function createHitIndex(
  clusters: readonly TimelineCluster[],
): Map<number, TimelineCluster[]> {
  const index = new Map<number, TimelineCluster[]>();
  for (const cluster of clusters) {
    const key = Math.floor((cluster.markerX ?? cluster.x) / 24);
    if (!index.has(key)) {
      index.set(key, []);
    }
    index.get(key)?.push(cluster);
  }
  return index;
}
