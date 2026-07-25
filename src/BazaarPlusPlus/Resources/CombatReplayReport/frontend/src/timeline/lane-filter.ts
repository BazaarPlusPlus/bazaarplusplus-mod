import {
  normalizeSide,
  type NormalizedEntity,
} from "../model/normalize.ts";

export const LANE_FILTER_SIDES = ["player", "opponent"] as const;
export const LANE_FILTER_TYPES = ["hero", "item", "skill", "effect"] as const;

export type LaneSideFilter = (typeof LANE_FILTER_SIDES)[number];
export type LaneTypeFilter = (typeof LANE_FILTER_TYPES)[number];
export type LaneFilterKey = LaneSideFilter | LaneTypeFilter;
export type LaneVisibility = Record<LaneFilterKey, boolean>;

export const DEFAULT_LANE_VISIBILITY: LaneVisibility = {
  player: true,
  opponent: true,
  hero: true,
  item: true,
  skill: true,
  effect: true,
};

function normalizedEntityType(entity: NormalizedEntity): LaneTypeFilter | null {
  const type = entity.type.toLowerCase();
  return LANE_FILTER_TYPES.find((candidate) => candidate === type) ?? null;
}

export function isTimelineEntityVisible(
  entity: NormalizedEntity,
  visibility: LaneVisibility,
): boolean {
  if (entity.hiddenFromTimeline) return false;
  const side = normalizeSide(entity.side);
  const type = normalizedEntityType(entity);
  return (
    (side === "neutral" || visibility[side])
    && (type === null || visibility[type])
  );
}

export function filterTimelineEntities(
  entities: readonly NormalizedEntity[],
  visibility: LaneVisibility,
): NormalizedEntity[] {
  return entities.filter((entity) =>
    isTimelineEntityVisible(entity, visibility)
  );
}

export function opponentBoundaryLane(
  entities: readonly NormalizedEntity[],
): number | null {
  const boundary = entities.findIndex(
    (entity, index) =>
      index > 0
      && normalizeSide(entity.side) === "opponent"
      && normalizeSide(entities[index - 1].side) !== "opponent",
  );
  return boundary < 0 ? null : boundary;
}
