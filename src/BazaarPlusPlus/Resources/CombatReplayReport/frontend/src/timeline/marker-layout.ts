import type { TimelineCluster } from "./clusters.ts";
import { EVENT_HIT_RADIUS } from "./constants.ts";

const DENSE_GROUP_MAX_GAP = EVENT_HIT_RADIUS * 2 + 12;
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

function denseMarkerSlots(count: number): MarkerSlot[] {
  if (count === 2) {
    return [
      { y: -15, sizeCap: 18 },
      { y: 15, sizeCap: 18 },
    ];
  }
  if (count === 3) {
    return [
      { y: -17, sizeCap: 14 },
      { y: 0, sizeCap: 14 },
      { y: 17, sizeCap: 14 },
    ];
  }
  if (count === 4) {
    return [-19, -6, 6, 19].map((y) => ({ y, sizeCap: 11 }));
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
    let previousX = 0;
    const flush = (): void => {
      if (group.length > 1) layoutDenseGroup(group);
      group = [];
    };
    for (const cluster of lane) {
      if (group.length === 0) {
        previousX = cluster.x;
        group.push(cluster);
        continue;
      }
      if (cluster.x - previousX <= DENSE_GROUP_MAX_GAP) {
        group.push(cluster);
      } else {
        flush();
        group.push(cluster);
      }
      previousX = cluster.x;
    }
    flush();
  }
  return clusters.slice();
}
