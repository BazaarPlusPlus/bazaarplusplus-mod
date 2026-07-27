import type { TimelineCluster } from "./clusters.ts";
import { markerOffset } from "./event-drawing.ts";

const DENSE_GROUP_WIDTH = 20;
const MARKER_COLUMN_GAP = 20;
const MARKER_ROW_OFFSET = 11;
const TOKEN_PRIORITY: Readonly<Record<string, number>> = {
  damageDirect: 0,
  damage: 1,
  burn: 2,
  poison: 3,
  heal: 4,
  shield: 5,
  destroy: 6,
  attribute: 7,
  charge: 8,
  haste: 9,
  slow: 10,
  freeze: 11,
  status: 12,
};

interface MarkerSlot {
  x: number;
  y: number;
}

function denseMarkerSlots(count: number): MarkerSlot[] {
  if (count === 2) {
    return [
      { x: -7, y: -10 },
      { x: 7, y: 10 },
    ];
  }
  if (count === 3) {
    return [
      { x: 0, y: -13 },
      { x: -10, y: 9 },
      { x: 10, y: 9 },
    ];
  }

  const upperCount = Math.ceil(count / 2);
  const lowerCount = count - upperCount;
  const row = (rowCount: number, y: number): MarkerSlot[] =>
    Array.from({ length: rowCount }, (_, index) => ({
      x: (index - (rowCount - 1) / 2) * MARKER_COLUMN_GAP,
      y,
    }));
  return [
    ...row(upperCount, -MARKER_ROW_OFFSET),
    ...row(lowerCount, MARKER_ROW_OFFSET),
  ];
}

function layoutDenseGroup(
  group: readonly TimelineCluster[],
): void {
  const anchorX =
    group.reduce((sum, cluster) => sum + cluster.x, 0) / group.length;
  const anchorY = group[0]?.y ?? 0;
  const slots = denseMarkerSlots(group.length);
  group.forEach((cluster, index) => {
    cluster.markerX = anchorX + slots[index].x;
    cluster.markerY = anchorY + slots[index].y;
  });
}

export function layoutTimelineMarkers(
  clusters: readonly TimelineCluster[],
): TimelineCluster[] {
  const laneGroups = new Map<number, TimelineCluster[]>();
  for (const cluster of clusters) {
    cluster.markerX = cluster.x;
    cluster.markerY = cluster.y + markerOffset(cluster);
    if (cluster.statusRange) continue;
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
    let group: TimelineCluster[] = [];
    let firstX = 0;
    const flush = (): void => {
      if (group.length > 1) layoutDenseGroup(group);
      group = [];
    };
    for (const cluster of lane) {
      if (group.length === 0) {
        firstX = cluster.x;
        group.push(cluster);
        continue;
      }
      if (cluster.x - firstX <= DENSE_GROUP_WIDTH) {
        group.push(cluster);
      } else {
        flush();
        firstX = cluster.x;
        group.push(cluster);
      }
    }
    flush();
  }
  return clusters.slice();
}
