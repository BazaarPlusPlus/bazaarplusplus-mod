import type { NormalizedEntity } from "../model/normalize.ts";
import type { ReportViewModel } from "../model/report.ts";
import { themeColor, withAlpha } from "../styles/theme.ts";
import { beginLogicalDraw } from "./canvas.ts";
import {
  relatedLaneRoles,
  type StatusRange,
  type TimelineCluster,
} from "./clusters.ts";
import {
  drawMarker,
  drawPlayhead,
  drawPreviewGuide,
  drawRuler,
  drawSideBoundary,
  drawStatusRanges,
  drawTimeGrid,
} from "./event-drawing.ts";
import {
  shouldDrawTimelinePreview,
  timelineXAtCombatMs,
} from "./geometry.ts";
import { opponentBoundaryLane } from "./lane-filter.ts";

export interface TimelineSceneOptions {
  canvas: HTMLCanvasElement;
  ruler: HTMLCanvasElement;
  stickyHeroCanvas: HTMLCanvasElement;
  model: ReportViewModel;
  entities: readonly NormalizedEntity[];
  width: number;
  laneHeight: number;
  clusters: readonly TimelineCluster[];
  markerClusters: readonly TimelineCluster[];
  statusRanges: readonly StatusRange[];
  firstVisibleMs: number;
  selectedVisualClusters: ReadonlySet<TimelineCluster>;
  hoverCluster: TimelineCluster | null;
  playheadMs: number;
  previewMs: number | null;
  pinnedHeroLane: number | null;
  requestDraw: () => void;
}

export function drawTimelineScene(options: TimelineSceneOptions): void {
  const {
    canvas,
    ruler,
    model,
    entities,
    width,
    laneHeight,
    clusters,
    markerClusters,
    statusRanges,
    firstVisibleMs,
    selectedVisualClusters,
    hoverCluster,
    playheadMs,
    previewMs,
    requestDraw,
  } = options;
  const context = canvas.getContext("2d");
  if (!context) return;
  const size = beginLogicalDraw(canvas, context);
  context.clearRect(0, 0, size.width, size.height);
  context.fillStyle = themeColor("background");
  context.fillRect(0, 0, size.width, size.height);
  drawTimeGrid(context, model, size.width, size.height);

  const selectedCluster =
    selectedVisualClusters.values().next().value ?? null;
  const highlighted = relatedLaneRoles(
    hoverCluster ?? selectedCluster,
    entities,
  );
  for (const [lane, roles] of highlighted) {
    const isSource = roles.has("source") || roles.has("trigger");
    context.fillStyle = withAlpha(
      themeColor(isSource ? "brand-soft" : "player"),
      isSource ? 0.12 : 0.07,
    );
    context.fillRect(
      0,
      lane * laneHeight,
      size.width,
      laneHeight,
    );
    context.fillStyle = withAlpha(
      themeColor(isSource ? "brand-soft" : "success"),
      0.8,
    );
    context.fillRect(0, lane * laneHeight, 2, laneHeight);
  }

  context.strokeStyle = withAlpha(
    themeColor("muted-foreground"),
    0.16,
  );
  context.lineWidth = 1;
  for (let lane = 0; lane <= entities.length; lane += 1) {
    const y = Math.round(lane * laneHeight) + 0.5;
    context.beginPath();
    context.moveTo(0, y);
    context.lineTo(size.width, y);
    context.stroke();
  }
  if (clusters.length > 0 && firstVisibleMs > 400) {
    const dimEnd = timelineXAtCombatMs(
      firstVisibleMs,
      model.durationMs,
      size.width,
    );
    context.fillStyle = withAlpha(themeColor("background"), 0.55);
    context.fillRect(0, 0, dimEnd, size.height);
    context.strokeStyle = withAlpha(
      themeColor("muted-foreground"),
      0.3,
    );
    context.setLineDash([3, 5]);
    context.beginPath();
    context.moveTo(dimEnd + 0.5, 0);
    context.lineTo(dimEnd + 0.5, size.height);
    context.stroke();
    context.setLineDash([]);
  }
  drawStatusRanges(context, statusRanges, laneHeight);
  drawSideBoundary(
    context,
    opponentBoundaryLane(entities),
    laneHeight,
    size.width,
  );
  for (const cluster of markerClusters) {
    drawMarker(
      context,
      cluster,
      cluster === hoverCluster || selectedVisualClusters.has(cluster),
      requestDraw,
    );
  }
  if (
    shouldDrawTimelinePreview(
      playheadMs,
      previewMs,
      model.durationMs,
      size.width,
    )
  ) {
    drawPreviewGuide(
      context,
      previewMs,
      model.durationMs,
      size.width,
      size.height,
    );
  }
  drawPlayhead(
    context,
    playheadMs,
    model.durationMs,
    size.width,
    size.height,
  );
  drawRuler(ruler, model, width, playheadMs, previewMs);
  drawStickyHeroRow(options);
}

export function drawStickyHeroRow({
  canvas,
  stickyHeroCanvas,
  entities,
  laneHeight,
  pinnedHeroLane,
}: Pick<
  TimelineSceneOptions,
  | "canvas"
  | "stickyHeroCanvas"
  | "entities"
  | "laneHeight"
  | "pinnedHeroLane"
>): void {
  const context = stickyHeroCanvas.getContext("2d");
  if (!context) return;
  const size = beginLogicalDraw(stickyHeroCanvas, context);
  context.clearRect(0, 0, size.width, size.height);
  if (pinnedHeroLane === null) return;
  const logicalHeight =
    Number.parseFloat(canvas.style.height)
    || Math.max(laneHeight, entities.length * laneHeight);
  const sourceScaleY = canvas.height / logicalHeight;
  context.drawImage(
    canvas,
    0,
    pinnedHeroLane * laneHeight * sourceScaleY,
    canvas.width,
    laneHeight * sourceScaleY,
    0,
    0,
    size.width,
    size.height,
  );
}
