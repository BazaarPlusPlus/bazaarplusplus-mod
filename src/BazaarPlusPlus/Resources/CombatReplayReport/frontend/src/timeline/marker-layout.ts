import type { TimelineCluster } from "./clusters.ts";
import {
  LANE_HEIGHT,
  TIMELINE_MARKER_SIZE,
} from "./constants.ts";

const MARKER_HALF_SIZE = TIMELINE_MARKER_SIZE / 2;
// Critical and defeat markers draw a small trailing `!` / `×` outside the
// native icon box. Reserve its full visual footprint when reusing a row.
const MARKER_DECORATION_RIGHT_EXTENT = 16;
const MARKER_LANE_PADDING = 4;
const MAX_NORMAL_SIZE_ROWS = 4;
const TOKEN_PRIORITY: Readonly<Record<string, number>> = {
  damageDirect: 0,
  damage: 1,
  burn: 2,
  poison: 3,
  heal: 4,
  attributeHealthMax: 5,
  shield: 6,
  destroy: 7,
  defeat: 8,
  attribute: 9,
  charge: 10,
  haste: 11,
  slow: 12,
  freeze: 13,
  status: 14,
};

interface MarkerSlot {
  y: number;
  sizeCap: number;
}

interface MarkerPlacement {
  cluster: TimelineCluster;
  row: number;
}

export interface MarkerHorizontalBounds {
  left: number;
  right: number;
}

function hasTrailingDecoration(cluster: TimelineCluster): boolean {
  return cluster.token === "defeat"
    || (cluster.events?.some(
      (event) =>
        event.isCritical
        && event.kind.toLowerCase() === "effect-executed",
    ) ?? false);
}

export function markerHorizontalBounds(
  cluster: TimelineCluster,
): MarkerHorizontalBounds {
  return {
    left: cluster.x - MARKER_HALF_SIZE,
    right: cluster.x
      + (hasTrailingDecoration(cluster)
        ? MARKER_DECORATION_RIGHT_EXTENT
        : MARKER_HALF_SIZE),
  };
}

function markerSlots(
  count: number,
  laneHeight: number,
): MarkerSlot[] {
  const preserveNormalSize = count <= MAX_NORMAL_SIZE_ROWS;
  // Four 14px boxes need the full 52px lane. Their visual content overlaps by
  // about one pixel, which is less disruptive than shrinking an isolated burst.
  const lanePadding =
    preserveNormalSize && count === MAX_NORMAL_SIZE_ROWS
      ? 0
      : MARKER_LANE_PADDING;
  const usableHeight = Math.max(
    1,
    laneHeight - lanePadding * 2,
  );
  const sizeCap = preserveNormalSize
    ? TIMELINE_MARKER_SIZE
    : Math.max(
      1,
      Math.min(
        TIMELINE_MARKER_SIZE,
        Math.floor(usableHeight / Math.max(1, count)),
      ),
    );
  if (count <= 1) return [{ y: 0, sizeCap }];

  const span = Math.max(0, usableHeight - sizeCap);
  const step = span / (count - 1);
  return Array.from({ length: count }, (_, index) => ({
    y: -span / 2 + index * step,
    sizeCap,
  }));
}

function layoutCollisionComponent(
  component: readonly TimelineCluster[],
  laneHeight: number,
): void {
  const placements: MarkerPlacement[] = [];
  const rowRightEdges: number[] = [];

  for (const cluster of component) {
    const bounds = markerHorizontalBounds(cluster);
    let row = rowRightEdges.findIndex(
      (rightEdge) => bounds.left >= rightEdge,
    );
    if (row < 0) row = rowRightEdges.length;
    rowRightEdges[row] = bounds.right;
    placements.push({ cluster, row });
  }

  const anchorY = component[0]?.y ?? 0;
  const slots = markerSlots(rowRightEdges.length, laneHeight);
  for (const { cluster, row } of placements) {
    cluster.markerX = cluster.x;
    cluster.markerY = anchorY + slots[row].y;
    cluster.markerSizeCap = slots[row].sizeCap;
  }
}

export function layoutTimelineMarkers(
  clusters: readonly TimelineCluster[],
  laneHeight = LANE_HEIGHT,
): TimelineCluster[] {
  const laneGroups = new Map<number, TimelineCluster[]>();
  for (const cluster of clusters) {
    cluster.markerX = cluster.x;
    cluster.markerY = cluster.y;
    cluster.markerSizeCap = undefined;
    const lane = laneGroups.get(cluster.lane);
    if (lane) lane.push(cluster);
    else laneGroups.set(cluster.lane, [cluster]);
  }

  for (const lane of laneGroups.values()) {
    lane.sort(
      (left, right) =>
        left.x - right.x
        || (left.tier ?? 2) - (right.tier ?? 2)
        || (TOKEN_PRIORITY[left.token] ?? 99)
          - (TOKEN_PRIORITY[right.token] ?? 99)
        || left.token.localeCompare(right.token),
    );
    let component: TimelineCluster[] = [];
    let componentRightEdge = 0;
    const flush = (): void => {
      if (component.length > 0) {
        layoutCollisionComponent(component, laneHeight);
      }
      component = [];
    };
    for (const cluster of lane) {
      const bounds = markerHorizontalBounds(cluster);
      if (component.length === 0) {
        componentRightEdge = bounds.right;
        component.push(cluster);
        continue;
      }
      if (bounds.left < componentRightEdge) {
        component.push(cluster);
      } else {
        flush();
        component.push(cluster);
      }
      componentRightEdge = Math.max(
        componentRightEdge,
        bounds.right,
      );
    }
    flush();
  }
  return clusters.slice();
}
