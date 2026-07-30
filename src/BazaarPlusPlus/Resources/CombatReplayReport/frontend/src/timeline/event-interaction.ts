import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../model/normalize.ts";
import {
  relatedLaneRoles,
  type TimelineCluster,
} from "./clusters.ts";
import { EVENT_HIT_RADIUS } from "./constants.ts";
import {
  markerPoint,
  markerRenderSize,
} from "./event-drawing.ts";
import { timelineXAtCombatMs } from "./geometry.ts";
import { markerHorizontalBounds } from "./marker-layout.ts";

export interface TimelinePoint {
  x: number;
  y: number;
}

export function timelinePointAtPointer({
  event,
  canvas,
  width,
  laneHeight,
  entityCount,
}: {
  event: Pick<PointerEvent, "clientX" | "clientY">;
  canvas: HTMLCanvasElement;
  width: number;
  laneHeight: number;
  entityCount: number;
}): TimelinePoint {
  const bounds = canvas.getBoundingClientRect();
  const logicalWidth = Number.parseFloat(canvas.style.width) || width;
  const logicalHeight =
    Number.parseFloat(canvas.style.height)
    || Math.max(laneHeight, entityCount * laneHeight);
  return {
    x: (event.clientX - bounds.left) * (logicalWidth / bounds.width),
    y: (event.clientY - bounds.top) * (logicalHeight / bounds.height),
  };
}

export function stickyHeroPointAtPointer({
  event,
  canvas,
  width,
  laneHeight,
  lane,
}: {
  event: Pick<PointerEvent, "clientX" | "clientY">;
  canvas: HTMLCanvasElement;
  width: number;
  laneHeight: number;
  lane: number;
}): TimelinePoint {
  const bounds = canvas.getBoundingClientRect();
  const logicalWidth = Number.parseFloat(canvas.style.width) || width;
  const localY =
    (event.clientY - bounds.top) * (laneHeight / bounds.height);
  return {
    x: (event.clientX - bounds.left) * (logicalWidth / bounds.width),
    y: lane * laneHeight + localY,
  };
}

export function hitTestTimelineClusters({
  x,
  y,
  hitIndex,
  visualClusters,
  laneHeight,
}: TimelinePoint & {
  hitIndex: ReadonlyMap<number, readonly TimelineCluster[]>;
  visualClusters: readonly TimelineCluster[];
  laneHeight: number;
}): TimelineCluster | null {
  const bucket = Math.floor(x / 24);
  let best: TimelineCluster | null = null;
  let bestDistance = Number.POSITIVE_INFINITY;
  for (let offset = -1; offset <= 1; offset += 1) {
    for (const cluster of hitIndex.get(bucket + offset) ?? []) {
      const marker = markerPoint(cluster);
      const dx = Math.abs(marker.x - x);
      const dy = Math.abs(marker.y - y);
      const horizontalBounds = markerHorizontalBounds(cluster);
      const markerHalfSize = markerRenderSize(cluster) / 2;
      const visualDx =
        x < horizontalBounds.left
          ? horizontalBounds.left - x
          : x > horizontalBounds.right
            ? x - horizontalBounds.right
            : 0;
      const visualDy = Math.max(
        0,
        Math.abs(marker.y - y) - markerHalfSize,
      );
      const distance = Math.hypot(visualDx, visualDy);
      if (
        dx <= EVENT_HIT_RADIUS
        && dy <= EVENT_HIT_RADIUS
        && distance < bestDistance
      ) {
        best = cluster;
        bestDistance = distance;
      }
    }
  }
  if (best) return best;
  return (
    visualClusters.find(
      (cluster) =>
        cluster.statusRange
        && y >= cluster.lane * laneHeight
        && y < (cluster.lane + 1) * laneHeight
        && x >= cluster.statusRange.x - 5
        && x <= cluster.statusRange.endX + 5,
    ) ?? null
  );
}

export function selectedTimelineVisualClusters({
  visualClusters,
  selectedClusterEventIds,
  selectedFrame,
}: {
  visualClusters: readonly TimelineCluster[];
  selectedClusterEventIds: ReadonlySet<string>;
  selectedFrame: number | null;
}): Set<TimelineCluster> {
  const selected = new Set<TimelineCluster>();
  if (selectedClusterEventIds.size > 0) {
    for (const cluster of visualClusters) {
      const matches =
        cluster.events.some((event) =>
          selectedClusterEventIds.has(event.id),
        )
        || (cluster.relatedEvents ?? []).some((event) =>
          selectedClusterEventIds.has(event.id),
        );
      if (matches) selected.add(cluster);
    }
    return selected;
  }
  if (selectedFrame === null) return selected;
  for (const cluster of visualClusters) {
    if (
      cluster.events.some((event) => event.frame === selectedFrame)
    ) {
      selected.add(cluster);
    }
  }
  return selected;
}

export function applyTimelineRelatedHighlights({
  cluster,
  entities,
  laneLabels,
  stickyHeroLabel,
  pinnedHeroLane,
}: {
  cluster: TimelineCluster | null;
  entities: readonly NormalizedEntity[];
  laneLabels: HTMLElement;
  stickyHeroLabel: HTMLElement;
  pinnedHeroLane: number | null;
}): void {
  const rows = laneLabels.querySelectorAll<HTMLElement>(
    "[data-bpp-lane-index]",
  );
  const clear = (row: HTMLElement): void => {
    row.classList.remove(
      "is-event-related",
      "is-related-source",
      "is-related-trigger",
      "is-related-target",
      "is-related-removed",
    );
  };
  for (const row of rows) clear(row);
  clear(stickyHeroLabel);
  const related = relatedLaneRoles(cluster, entities);
  for (const [lane, roles] of related) {
    const row = rows.item(lane);
    const apply = (element: HTMLElement): void => {
      element.classList.add("is-event-related");
      for (const role of roles) {
        element.classList.add(`is-related-${role}`);
      }
    };
    if (row) apply(row);
    if (lane === pinnedHeroLane) apply(stickyHeroLabel);
  }
}

export function timelineNavigationTarget({
  markerClusters,
  selectedVisualClusters,
  direction,
  playheadMs,
  durationMs,
  width,
}: {
  markerClusters: readonly TimelineCluster[];
  selectedVisualClusters: ReadonlySet<TimelineCluster>;
  direction: -1 | 1;
  playheadMs: number;
  durationMs: number;
  width: number;
}): { cluster: TimelineCluster; event: NormalizedEvent } | null {
  const clusters = markerClusters.filter(
    (cluster) => cluster.events.length > 0,
  );
  if (clusters.length === 0) return null;
  const selectedIndices = clusters.flatMap((cluster, index) =>
    selectedVisualClusters.has(cluster) ? [index] : [],
  );
  let index: number;
  if (selectedIndices.length > 0) {
    const edge =
      direction > 0
        ? Math.max(...selectedIndices)
        : Math.min(...selectedIndices);
    index = Math.max(
      0,
      Math.min(clusters.length - 1, edge + direction),
    );
  } else {
    const playheadX = timelineXAtCombatMs(
      playheadMs,
      durationMs,
      width,
    );
    index =
      direction > 0
        ? clusters.findIndex((cluster) => cluster.x > playheadX)
        : (() => {
            for (
              let candidate = clusters.length - 1;
              candidate >= 0;
              candidate -= 1
            ) {
              if (clusters[candidate].x < playheadX) return candidate;
            }
            return -1;
          })();
    if (index < 0) {
      index = direction > 0 ? 0 : clusters.length - 1;
    }
  }
  const cluster = clusters[index];
  const event = cluster.events.reduce((nearest, candidate) => {
    const candidateX = timelineXAtCombatMs(
      candidate.combatMs,
      durationMs,
      width,
    );
    const nearestX = timelineXAtCombatMs(
      nearest.combatMs,
      durationMs,
      width,
    );
    return Math.abs(candidateX - cluster.x)
        < Math.abs(nearestX - cluster.x)
      ? candidate
      : nearest;
  });
  return { cluster, event };
}

export function eventsAtTimelineFrame(
  events: readonly NormalizedEvent[],
  frame: number | null,
): NormalizedEvent[] {
  if (frame === null) return [];
  return events.filter((event) => event.frame === frame);
}
