import { asFiniteNumber } from "../model/value.ts";
import {
  normalizeMetricName,
  type NormalizedEntity,
  type NormalizedEvent,
} from "../model/normalize.ts";
import { timelineXAtCombatMs } from "./geometry.ts";

export const STATUS_ACTIONS = new Set(["Haste", "Slow", "Freeze"]);

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
  icon: string;
  statusRange?: StatusRange;
  members?: TimelineCluster[];
  tier?: EventTier;
  impact?: number;
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
  if (eventKindToken(event) === "status") return 3;
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
  const normalized = String(kind ?? "").toLowerCase();
  if (
    normalized.includes("damage")
    || normalized.includes("burn")
    || normalized.includes("poison")
  ) {
    return "damage";
  }
  if (
    normalized.includes("heal")
    || normalized.includes("regen")
    || normalized.includes("restore")
  ) {
    return "heal";
  }
  if (normalized.includes("shield")) return "shield";
  if (normalized.includes("charge")) return "charge";
  if (normalized.includes("haste") || normalized.includes("speedup")) {
    return "haste";
  }
  if (normalized.includes("slow")) return "slow";
  if (normalized.includes("freeze")) return "freeze";
  if (normalized.includes("skill")) return "skill";
  if (normalized.includes("trigger")) return "trigger";
  return "status";
}

export function eventKindToken(
  event: Pick<NormalizedEvent, "kind" | "action">,
): string {
  return kindToken(`${event.kind} ${event.action}`);
}

function isMetricOnlyEvent(event: NormalizedEvent): boolean {
  return (
    event.kind.toLowerCase() === "player-attribute"
    && Boolean(normalizeMetricName(event.action))
  );
}

export function isVisibleTimelineEvent(
  event: NormalizedEvent,
  entityById: ReadonlyMap<string, TimelineEntity>,
): boolean {
  if (isMetricOnlyEvent(event)) return false;
  if (event.kind.toLowerCase() === "health") return false;
  if (event.kind.toLowerCase() === "card-attribute") return false;
  if (event.kind.toLowerCase() !== "effect-executed") return true;
  if (event.action === "CardModifyAttribute") return false;
  if (
    event.action === "CardHaste"
    || event.action === "CardSlow"
    || event.action === "CardFreeze"
  ) {
    return false;
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
    const relatedEvents = model.events.filter(
      (event) =>
        event.frame === range.startEvent.frame
        && event.kind.toLowerCase() === "effect-executed"
        && event.action === applyAction
        && event.targetIds.includes(range.targetId),
    );
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
  for (const targetId of event.targetIds) append(targetId, "target");
  for (const removedTargetId of event.removedTargetIds) {
    append(removedTargetId, "removed");
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
): TimelineCluster[] {
  const entityIndex = new Map(
    entities.map((entity, index) => [entity.id, index] as const),
  );
  const duration = Math.max(1, model.durationMs);
  const clusterMap = new Map<string, TimelineCluster>();

  for (const event of events) {
    const x = timelineXAtCombatMs(event.combatMs, duration, timelineWidth);
    const token = eventKindToken(event);
    for (const endpoint of eventLaneEndpoints(event, entityIndex)) {
      const pixel = Math.round(x);
      const key =
        `${event.frame}:${endpoint.lane}:${pixel}:${endpoint.role}:${token}`;
      let cluster = clusterMap.get(key);
      if (!cluster) {
        cluster = {
          lane: endpoint.lane,
          x,
          y: endpoint.lane * laneHeight + laneHeight / 2,
          role: endpoint.role,
          events: [],
          token,
          icon: event.icon,
        };
        clusterMap.set(key, cluster);
      }
      cluster.events.push(event);
      if (!cluster.icon && event.icon) cluster.icon = event.icon;
    }
  }

  const built = Array.from(clusterMap.values());
  for (const cluster of built) {
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
    const key = `${cluster.lane}:${cluster.role}:${cluster.token}`;
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
    const key = Math.floor(cluster.x / 24);
    if (!index.has(key)) {
      index.set(key, []);
    }
    index.get(key)?.push(cluster);
  }
  return index;
}
