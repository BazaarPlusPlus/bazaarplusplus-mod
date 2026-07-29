import type { TimelineCluster } from "./clusters.ts";

const DENSE_GROUP_WIDTH = 20;
const MARKER_ROW_OFFSET = 11;
const TOKEN_PRIORITY: Readonly<Record<string, number>> = {
  damageDirect: 0,
  damage: 1,
  burn: 2,
  poison: 3,
  heal: 4,
  attributeHealthMax: 5,
  shield: 6,
  destroy: 7,
  attribute: 8,
  charge: 9,
  haste: 10,
  slow: 11,
  freeze: 12,
  status: 13,
};

interface MarkerSlot {
  y: number;
  sizeCap: number;
}

function denseMarkerSlots(count: number): MarkerSlot[] {
  if (count === 2) {
    return [
      { y: -MARKER_ROW_OFFSET, sizeCap: 18 },
      { y: MARKER_ROW_OFFSET, sizeCap: 18 },
    ];
  }
  if (count === 3) {
    return [
      { y: -16, sizeCap: 14 },
      { y: 0, sizeCap: 14 },
      { y: 16, sizeCap: 14 },
    ];
  }
  if (count === 4) {
    return [-18, -6, 6, 18].map((y) => ({ y, sizeCap: 11 }));
  }
  if (count === 5) {
    return [-20, -10, 0, 10, 20].map((y) => ({ y, sizeCap: 9 }));
  }

  const stackSpan = 42;
  const step = stackSpan / Math.max(1, count - 1);
  const sizeCap = Math.max(7, Math.min(9, Math.floor(step - 1)));
  return Array.from({ length: count }, (_, index) => ({
    y: -stackSpan / 2 + index * step,
    sizeCap,
  }));
}

function layoutDenseGroup(
  group: readonly TimelineCluster[],
): void {
  const anchorY = group[0]?.y ?? 0;
  const slots = denseMarkerSlots(group.length);
  group.forEach((cluster, index) => {
    cluster.markerX = cluster.x;
    cluster.markerY = anchorY + slots[index].y;
    cluster.markerSizeCap = slots[index].sizeCap;
  });
}

export function layoutTimelineMarkers(
  clusters: readonly TimelineCluster[],
): TimelineCluster[] {
  const laneGroups = new Map<number, TimelineCluster[]>();
  for (const cluster of clusters) {
    cluster.markerX = cluster.x;
    cluster.markerY = cluster.y;
    cluster.markerSizeCap = undefined;
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
