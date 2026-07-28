export type EntityArtSize =
  | "compact"
  | "default"
  | "activity"
  | "inspector";

type EntityArtGeometry = {
  height: number;
  itemSlotWidth: number;
  skillDiameter: number;
};

const ENTITY_ART_GEOMETRY: Record<EntityArtSize, EntityArtGeometry> = {
  compact: {
    height: 24,
    itemSlotWidth: 24,
    skillDiameter: 20,
  },
  default: {
    height: 44,
    itemSlotWidth: 44,
    skillDiameter: 36,
  },
  activity: {
    height: 44,
    itemSlotWidth: 44,
    skillDiameter: 36,
  },
  inspector: {
    height: 48,
    itemSlotWidth: 48,
    skillDiameter: 48,
  },
};

export const MAX_ENTITY_ART_SPAN = 3;

export function normalizedEntityArtSpan(span: number): number {
  return Math.min(
    MAX_ENTITY_ART_SPAN,
    Math.max(1, Number.isFinite(span) ? Math.trunc(span) : 1),
  );
}

export function entityArtDimensions(
  type: string,
  span: number,
  size: EntityArtSize,
): { width: number; height: number; span: number } {
  const geometry = ENTITY_ART_GEOMETRY[size];
  const normalizedSpan = normalizedEntityArtSpan(span);
  const normalizedType = type.toLowerCase();
  const isSkill = normalizedType === "skill";
  return {
    width:
      normalizedType === "item"
        ? geometry.itemSlotWidth * normalizedSpan
        : isSkill
          ? geometry.skillDiameter
          : geometry.height,
    height: isSkill ? geometry.skillDiameter : geometry.height,
    span: normalizedSpan,
  };
}

export function maximumEntityArtWidth(size: EntityArtSize): number {
  return (
    ENTITY_ART_GEOMETRY[size].itemSlotWidth * MAX_ENTITY_ART_SPAN
  );
}
