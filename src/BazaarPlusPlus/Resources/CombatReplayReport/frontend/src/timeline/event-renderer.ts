import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../model/normalize.ts";
import type { ReportViewModel } from "../model/report.ts";
import {
  buildClusters,
  buildStatusRanges,
  buildVisualClusters,
  createHitIndex,
  isVisibleTimelineEvent,
  relatedLaneRoles,
  type StatusRange,
  type TimelineCluster,
} from "./clusters.ts";
import {
  beginLogicalDraw,
  resizeLogicalCanvas,
} from "./canvas.ts";
import {
  EVENT_HIT_RADIUS,
  TIME_RULER_HEIGHT,
} from "./constants.ts";
import {
  combatMsAtPointer,
  shouldDrawTimelinePreview,
  TIMELINE_LEFT_GUTTER,
  TIMELINE_RIGHT_INTERACTION_GUTTER,
  timelineXAtCombatMs,
} from "./geometry.ts";
import { cachedTimelineImage } from "./image-cache.ts";
import { opponentBoundaryLane } from "./lane-filter.ts";
import {
  themeColor,
  themeLengthPx,
  themeValue,
  withAlpha,
  type ThemeColorName,
} from "../styles/theme.ts";

const MARKER_COLOR_NAMES: Record<string, ThemeColorName> = {
  damage: "damage",
  heal: "heal",
  shield: "shield",
  charge: "charge",
  haste: "haste",
  slow: "slow",
  freeze: "freeze",
  skill: "skill",
  trigger: "rage",
  status: "status",
};

const MARKER_GLYPHS: Record<string, string> = {
  damage: "✦",
  heal: "+",
  shield: "◈",
  charge: "ϟ",
  haste: "»",
  slow: "«",
  freeze: "❄",
  skill: "◆",
  trigger: "◇",
  status: "∿",
};

function markerColor(token: string): string {
  return themeColor(MARKER_COLOR_NAMES[token] ?? "status");
}

function markerOffset(cluster: TimelineCluster): number {
  if (cluster.statusRange) return 0;
  if (cluster.role === "source" || cluster.role === "trigger") return 9;
  if (cluster.role === "target" || cluster.role === "both") {
    if (cluster.token === "damage") return -9;
    if (cluster.token === "heal" || cluster.token === "shield") return 9;
  }
  return 0;
}

function drawMarker(
  context: CanvasRenderingContext2D,
  cluster: TimelineCluster,
  selected: boolean,
  requestDraw: () => void,
): void {
  const tier = cluster.tier ?? 2;
  const color = markerColor(cluster.token);
  const y = cluster.y + markerOffset(cluster);
  context.save();
  context.translate(cluster.x, y);
  const image = cachedTimelineImage(cluster.icon, requestDraw);
  if (image) {
    const size = tier === 1 ? 18 : tier === 3 ? 11 : 14;
    if (selected) {
      context.strokeStyle = themeColor("foreground");
      context.lineWidth = 1.5;
      context.strokeRect(
        -size / 2 - 2,
        -size / 2 - 2,
        size + 4,
        size + 4,
      );
    }
    context.drawImage(image, -size / 2, -size / 2, size, size);
  } else {
    const fontSize =
      tier === 1
        ? themeLengthPx("--bpp-text-body")
        : tier === 3
          ? themeLengthPx("--bpp-text-nano")
          : themeLengthPx("--bpp-text-compact");
    context.font =
      `700 ${fontSize}px ${themeValue("--bpp-font-sans")}`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillStyle = color;
    context.fillText(MARKER_GLYPHS[cluster.token] ?? "∿", 0, 0.5);
    if (selected) {
      const half = Math.max(7, fontSize * 0.65);
      context.strokeStyle = themeColor("foreground");
      context.lineWidth = 1.5;
      context.strokeRect(-half, -half, half * 2, half * 2);
    }
  }
  context.restore();
}

function statusRangeColor(action: string): string {
  if (action === "Haste") return themeColor("haste");
  if (action === "Slow") return themeColor("slow");
  return themeColor("freeze");
}

function drawStatusRanges(
  context: CanvasRenderingContext2D,
  ranges: readonly StatusRange[],
  laneHeight: number,
): void {
  for (const range of ranges) {
    const color = statusRangeColor(range.action);
    const y = range.lane * laneHeight + Math.max(5, laneHeight * 0.16);
    const height = Math.max(14, laneHeight * 0.68);
    const width = Math.max(2, range.endX - range.x);
    context.fillStyle = withAlpha(color, 0.12);
    context.fillRect(range.x, y, width, height);
    context.strokeStyle = withAlpha(color, 0.55);
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(range.x, y + 0.5);
    context.lineTo(range.x + width, y + 0.5);
    context.moveTo(range.x, y + height - 0.5);
    context.lineTo(range.x + width, y + height - 0.5);
    context.stroke();
  }
}

function chooseTickStep(durationMs: number, width: number): number {
  const pixelsPerSecond =
    Math.max(1, width - TIMELINE_RIGHT_INTERACTION_GUTTER)
    / Math.max(0.001, durationMs / 1000);
  const candidates = [
    100, 200, 500, 1_000, 2_000, 5_000, 10_000, 20_000, 30_000,
  ];
  return (
    candidates.find((step) => (step / 1000) * pixelsPerSecond >= 72)
    ?? candidates[candidates.length - 1]
  );
}

function drawTimeGrid(
  context: CanvasRenderingContext2D,
  model: ReportViewModel,
  width: number,
  height: number,
): void {
  const step = chooseTickStep(model.durationMs, width);
  context.save();
  context.strokeStyle = withAlpha(themeColor("muted-foreground"), 0.15);
  context.lineWidth = 1;
  for (let time = 0; time <= model.durationMs; time += step) {
    const x = Math.round(
      timelineXAtCombatMs(time, model.durationMs, width),
    ) + 0.5;
    context.beginPath();
    context.moveTo(x, 0);
    context.lineTo(x, height);
    context.stroke();
  }
  context.restore();
}

function drawSideBoundary(
  context: CanvasRenderingContext2D,
  boundaryLane: number | null,
  laneHeight: number,
  width: number,
): void {
  if (boundaryLane === null) return;
  const y = Math.round(boundaryLane * laneHeight);
  context.save();
  context.fillStyle = withAlpha(themeColor("opponent"), 0.78);
  context.fillRect(0, y, width, 2);
  context.restore();
}

function drawPlayhead(
  context: CanvasRenderingContext2D,
  combatMs: number | null,
  durationMs: number,
  width: number,
  height: number,
): void {
  if (combatMs === null || !Number.isFinite(combatMs)) return;
  const x = timelineXAtCombatMs(combatMs, durationMs, width);
  context.save();
  context.strokeStyle = themeColor("rage");
  context.fillStyle = themeColor("rage");
  context.lineWidth = 1.8;
  context.beginPath();
  context.moveTo(x + 0.5, 0);
  context.lineTo(x + 0.5, height);
  context.stroke();
  context.setLineDash([]);
  context.beginPath();
  context.moveTo(x - 5, 0);
  context.lineTo(x + 5, 0);
  context.lineTo(x, 7);
  context.closePath();
  context.fill();
  context.restore();
}

function drawPreviewGuide(
  context: CanvasRenderingContext2D,
  combatMs: number | null,
  durationMs: number,
  width: number,
  height: number,
): void {
  if (combatMs === null || !Number.isFinite(combatMs)) return;
  const x = timelineXAtCombatMs(combatMs, durationMs, width);
  context.save();
  context.strokeStyle = withAlpha(themeColor("success"), 0.82);
  context.lineWidth = 1.2;
  context.setLineDash([4, 4]);
  context.beginPath();
  context.moveTo(x + 0.5, 0);
  context.lineTo(x + 0.5, height);
  context.stroke();
  context.restore();
}

function drawRuler(
  canvas: HTMLCanvasElement,
  model: ReportViewModel,
  width: number,
  playheadMs: number,
  previewMs: number | null,
): void {
  resizeLogicalCanvas(canvas, width, TIME_RULER_HEIGHT);
  const context = canvas.getContext("2d");
  if (!context) return;
  const size = beginLogicalDraw(canvas, context);
  context.clearRect(0, 0, size.width, size.height);
  context.fillStyle = themeColor("surface");
  context.fillRect(0, 0, size.width, size.height);
  const step = chooseTickStep(model.durationMs, width);
  context.font =
    `600 ${themeLengthPx("--bpp-text-compact")}px ${themeValue("--bpp-font-mono")}`;
  context.fillStyle = themeColor("muted-foreground");
  context.textAlign = "center";
  context.textBaseline = "top";
  context.strokeStyle = withAlpha(themeColor("muted-foreground"), 0.28);
  for (let time = 0; time <= model.durationMs; time += step) {
    const x = timelineXAtCombatMs(time, model.durationMs, width);
    context.beginPath();
    context.moveTo(x + 0.5, size.height - 8);
    context.lineTo(x + 0.5, size.height);
    context.stroke();
    const seconds = time / 1000;
    const label =
      seconds < 10 && step < 1000
        ? `${seconds.toFixed(1)}s`
        : `${Number(seconds.toFixed(1))}s`;
    const alignX = Math.max(
      18,
      Math.min(
        size.width - TIMELINE_RIGHT_INTERACTION_GUTTER - 18,
        x,
      ),
    );
    context.fillText(label, alignX, 6);
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
}

interface ControllerOptions {
  canvas: HTMLCanvasElement;
  ruler: HTMLCanvasElement;
  model: ReportViewModel;
  entities: NormalizedEntity[];
  laneLabels: HTMLElement;
  stickyHeroCanvas: HTMLCanvasElement;
  stickyHeroLabel: HTMLElement;
  onSelect: (cluster: TimelineCluster | null, combatMs: number) => void;
  onPreview: (
    cluster: TimelineCluster | null,
    combatMs: number | null,
    clientX: number,
    clientY: number,
  ) => void;
}

export class TimelineCanvasController {
  private readonly canvas: HTMLCanvasElement;
  private readonly ruler: HTMLCanvasElement;
  private readonly model: ReportViewModel;
  private readonly entities: NormalizedEntity[];
  private readonly laneLabels: HTMLElement;
  private readonly stickyHeroCanvas: HTMLCanvasElement;
  private readonly stickyHeroLabel: HTMLElement;
  private readonly onSelect: ControllerOptions["onSelect"];
  private readonly onPreview: ControllerOptions["onPreview"];
  private width = 1;
  private laneHeight = 52;
  private clusters: TimelineCluster[] = [];
  private visualClusters: TimelineCluster[] = [];
  private markerClusters: TimelineCluster[] = [];
  private firstVisibleMs = 0;
  private statusRanges: StatusRange[] = [];
  private hitIndex = new Map<number, TimelineCluster[]>();
  private selectedFrame: number | null = null;
  private selectedClusterEventIds = new Set<string>();
  private selectedVisualClusters = new Set<TimelineCluster>();
  private playheadMs = 0;
  private previewMs: number | null = null;
  private hoverCluster: TimelineCluster | null = null;
  private pinnedHeroLane: number | null = null;
  private frameHandle = 0;

  constructor(options: ControllerOptions) {
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.timelineControllerCount += 1;
    }
    this.canvas = options.canvas;
    this.ruler = options.ruler;
    this.model = options.model;
    this.entities = options.entities;
    this.laneLabels = options.laneLabels;
    this.stickyHeroCanvas = options.stickyHeroCanvas;
    this.stickyHeroLabel = options.stickyHeroLabel;
    this.onSelect = options.onSelect;
    this.onPreview = options.onPreview;
  }

  setLayout(width: number, laneHeight: number): void {
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.timelinePreprocessCount += 1;
    }
    this.width = Math.max(1, width);
    this.laneHeight = Math.max(32, laneHeight);
    const entityById = new Map(
      this.entities.map((entity) => [entity.id, entity]),
    );
    const visible = this.model.events.filter((event) =>
      isVisibleTimelineEvent(event, entityById),
    );
    this.statusRanges = buildStatusRanges(
      this.model,
      this.entities,
      this.width,
      this.laneHeight,
    );
    this.clusters = buildClusters(
      this.model,
      visible,
      this.entities,
      this.width,
      this.laneHeight,
    ).concat(
      this.statusRanges
        .map((range) => range.cluster)
        .filter((cluster): cluster is TimelineCluster => cluster !== null),
    );
    this.visualClusters = buildVisualClusters(this.clusters);
    this.markerClusters = this.visualClusters;
    this.firstVisibleMs = this.clusters.reduce(
      (min, cluster) =>
        Math.min(
          min,
          ...cluster.events.map((event) => event.combatMs),
        ),
      this.model.durationMs,
    );
    this.hitIndex = createHitIndex(this.markerClusters);
    this.rebuildSelectedVisualClusters();
    resizeLogicalCanvas(
      this.canvas,
      this.width,
      Math.max(this.laneHeight, this.entities.length * this.laneHeight),
    );
    resizeLogicalCanvas(
      this.stickyHeroCanvas,
      this.width,
      this.laneHeight,
    );
    this.syncRelatedHighlights();
    this.requestDraw();
  }

  setPinnedHeroLane(lane: number | null): void {
    const next =
      lane !== null
        && Number.isInteger(lane)
        && lane >= 0
        && lane < this.entities.length
        ? lane
        : null;
    if (next === this.pinnedHeroLane) return;
    this.pinnedHeroLane = next;
    this.syncRelatedHighlights();
    this.drawStickyHeroRow();
    this.requestDraw();
  }

  setSelection(
    frame: number | null,
    clusterEventIds: readonly string[],
  ): void {
    this.selectedFrame = frame;
    this.selectedClusterEventIds = new Set(clusterEventIds);
    this.rebuildSelectedVisualClusters();
    this.syncRelatedHighlights();
    this.requestDraw();
  }

  setPlayhead(combatMs: number): void {
    this.playheadMs = Math.max(
      0,
      Math.min(this.model.durationMs, combatMs),
    );
    this.requestDraw();
  }

  setPreview(combatMs: number | null): void {
    this.previewMs =
      combatMs === null
        ? null
        : Math.max(0, Math.min(this.model.durationMs, combatMs));
    this.requestDraw();
  }

  handlePointerMove(event: PointerEvent): void {
    const point = this.logicalPoint(event);
    const cluster = this.hitTest(point.x, point.y);
    const combatMs = combatMsAtPointer(
      this.canvas,
      event.clientX,
      this.model.durationMs,
    );
    this.updateHover(cluster, combatMs, event.clientX, event.clientY);
  }

  handleStickyHeroPointerMove(
    event: PointerEvent,
    canvas: HTMLCanvasElement,
    lane: number,
  ): void {
    const bounds = canvas.getBoundingClientRect();
    const logicalWidth =
      Number.parseFloat(canvas.style.width) || this.width;
    const localY =
      (event.clientY - bounds.top) * (this.laneHeight / bounds.height);
    const point = {
      x: (event.clientX - bounds.left) * (logicalWidth / bounds.width),
      y: lane * this.laneHeight + localY,
    };
    const cluster = this.hitTest(point.x, point.y);
    const combatMs = combatMsAtPointer(
      canvas,
      event.clientX,
      this.model.durationMs,
    );
    this.updateHover(cluster, combatMs, event.clientX, event.clientY);
  }

  private updateHover(
    cluster: TimelineCluster | null,
    combatMs: number,
    clientX: number,
    clientY: number,
  ): void {
    if (
      cluster === this.hoverCluster
      && Math.abs((this.previewMs ?? 0) - combatMs) < 8
    ) {
      return;
    }
    this.hoverCluster = cluster;
    this.previewMs = combatMs;
    this.syncRelatedHighlights();
    this.onPreview(cluster, combatMs, clientX, clientY);
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.hoverDrawCount += 1;
    }
    this.requestDraw();
  }

  handlePointerLeave(): void {
    this.hoverCluster = null;
    this.previewMs = null;
    this.syncRelatedHighlights();
    this.onPreview(null, null, 0, 0);
    this.requestDraw();
  }

  handlePointerDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    const point = this.logicalPoint(event);
    const cluster = this.hitTest(point.x, point.y);
    const combatMs = combatMsAtPointer(
      this.canvas,
      event.clientX,
      this.model.durationMs,
    );
    this.onSelect(cluster, combatMs);
  }

  handleStickyHeroPointerDown(
    event: PointerEvent,
    canvas: HTMLCanvasElement,
    lane: number,
  ): void {
    if (event.button !== 0) return;
    const bounds = canvas.getBoundingClientRect();
    const logicalWidth =
      Number.parseFloat(canvas.style.width) || this.width;
    const localY =
      (event.clientY - bounds.top) * (this.laneHeight / bounds.height);
    const cluster = this.hitTest(
      (event.clientX - bounds.left) * (logicalWidth / bounds.width),
      lane * this.laneHeight + localY,
    );
    const combatMs = combatMsAtPointer(
      canvas,
      event.clientX,
      this.model.durationMs,
    );
    this.onSelect(cluster, combatMs);
  }

  handleRulerPointerDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    const combatMs = combatMsAtPointer(
      this.ruler,
      event.clientX,
      this.model.durationMs,
    );
    this.onSelect(null, combatMs);
  }

  navigate(direction: -1 | 1): void {
    const clusters = this.markerClusters.filter(
      (cluster) => cluster.events.length > 0,
    );
    if (clusters.length === 0) return;
    const selectedIndices = clusters.flatMap((cluster, index) =>
      this.selectedVisualClusters.has(cluster) ? [index] : [],
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
        this.playheadMs,
        this.model.durationMs,
        this.width,
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
        this.model.durationMs,
        this.width,
      );
      const nearestX = timelineXAtCombatMs(
        nearest.combatMs,
        this.model.durationMs,
        this.width,
      );
      return Math.abs(candidateX - cluster.x)
          < Math.abs(nearestX - cluster.x)
        ? candidate
        : nearest;
    });
    this.onSelect(cluster, event.combatMs);
  }

  destroy(): void {
    if (this.frameHandle) cancelAnimationFrame(this.frameHandle);
    this.frameHandle = 0;
    this.applyRelatedHighlights(null);
  }

  private logicalPoint(event: PointerEvent): { x: number; y: number } {
    const bounds = this.canvas.getBoundingClientRect();
    const logicalWidth =
      Number.parseFloat(this.canvas.style.width) || this.width;
    const logicalHeight =
      Number.parseFloat(this.canvas.style.height)
      || Math.max(this.laneHeight, this.entities.length * this.laneHeight);
    return {
      x: (event.clientX - bounds.left) * (logicalWidth / bounds.width),
      y: (event.clientY - bounds.top) * (logicalHeight / bounds.height),
    };
  }

  private hitTest(x: number, y: number): TimelineCluster | null {
    const bucket = Math.floor(x / 24);
    let best: TimelineCluster | null = null;
    let bestDistance = Number.POSITIVE_INFINITY;
    for (let offset = -1; offset <= 1; offset += 1) {
      for (const cluster of this.hitIndex.get(bucket + offset) ?? []) {
        const markerY = cluster.y + markerOffset(cluster);
        const dx = Math.abs(cluster.x - x);
        const dy = Math.abs(markerY - y);
        const distance = Math.hypot(dx, dy);
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
      this.visualClusters.find(
        (cluster) =>
          cluster.statusRange
          && y >= cluster.lane * this.laneHeight
          && y < (cluster.lane + 1) * this.laneHeight
          && x >= cluster.statusRange.x - 5
          && x <= cluster.statusRange.endX + 5,
      ) ?? null
    );
  }

  private applyRelatedHighlights(cluster: TimelineCluster | null): void {
    const rows = this.laneLabels.querySelectorAll<HTMLElement>(
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
    clear(this.stickyHeroLabel);
    const related = relatedLaneRoles(cluster, this.entities);
    for (const [lane, roles] of related) {
      const row = rows.item(lane);
      const apply = (element: HTMLElement): void => {
        element.classList.add("is-event-related");
        for (const role of roles) {
          element.classList.add(`is-related-${role}`);
        }
      };
      if (row) apply(row);
      if (lane === this.pinnedHeroLane) apply(this.stickyHeroLabel);
    }
  }

  private syncRelatedHighlights(): void {
    this.applyRelatedHighlights(
      this.hoverCluster
      ?? this.selectedVisualClusters.values().next().value
      ?? null,
    );
  }

  private requestDraw = (): void => {
    if (this.frameHandle) return;
    this.frameHandle = requestAnimationFrame(() => {
      this.frameHandle = 0;
      this.draw();
    });
  };

  private draw(): void {
    const context = this.canvas.getContext("2d");
    if (!context) return;
    const size = beginLogicalDraw(this.canvas, context);
    context.clearRect(0, 0, size.width, size.height);
    context.fillStyle = themeColor("background");
    context.fillRect(0, 0, size.width, size.height);
    drawTimeGrid(context, this.model, size.width, size.height);

    const selectedCluster =
      this.selectedVisualClusters.values().next().value ?? null;
    const highlighted = relatedLaneRoles(
      this.hoverCluster ?? selectedCluster,
      this.entities,
    );
    for (const [lane, roles] of highlighted) {
      const isSource =
        roles.has("source") || roles.has("trigger");
      context.fillStyle = withAlpha(
        themeColor(isSource ? "brand-soft" : "player"),
        isSource ? 0.12 : 0.07,
      );
      context.fillRect(
        0,
        lane * this.laneHeight,
        size.width,
        this.laneHeight,
      );
      context.fillStyle = withAlpha(
        themeColor(isSource ? "brand-soft" : "success"),
        0.8,
      );
      context.fillRect(
        0,
        lane * this.laneHeight,
        2,
        this.laneHeight,
      );
    }

    context.strokeStyle = withAlpha(
      themeColor("muted-foreground"),
      0.16,
    );
    context.lineWidth = 1;
    for (let lane = 0; lane <= this.entities.length; lane += 1) {
      const y = Math.round(lane * this.laneHeight) + 0.5;
      context.beginPath();
      context.moveTo(0, y);
      context.lineTo(size.width, y);
      context.stroke();
    }
    if (this.clusters.length > 0 && this.firstVisibleMs > 400) {
      const dimEnd = timelineXAtCombatMs(
        this.firstVisibleMs,
        this.model.durationMs,
        size.width,
      );
      context.fillStyle = withAlpha(themeColor("background"), 0.55);
      context.fillRect(0, 0, dimEnd, size.height);
      context.strokeStyle = withAlpha(themeColor("muted-foreground"), 0.3);
      context.setLineDash([3, 5]);
      context.beginPath();
      context.moveTo(dimEnd + 0.5, 0);
      context.lineTo(dimEnd + 0.5, size.height);
      context.stroke();
      context.setLineDash([]);
    }
    drawStatusRanges(context, this.statusRanges, this.laneHeight);
    // Keep the side divider in the overlay layer. Status ranges may span the
    // first opponent lane and must never obscure the player/opponent boundary.
    drawSideBoundary(
      context,
      opponentBoundaryLane(this.entities),
      this.laneHeight,
      size.width,
    );
    for (const cluster of this.markerClusters) {
      drawMarker(
        context,
        cluster,
        cluster === this.hoverCluster
          || this.selectedVisualClusters.has(cluster),
        this.requestDraw,
      );
    }
    if (
      shouldDrawTimelinePreview(
        this.playheadMs,
        this.previewMs,
        this.model.durationMs,
        size.width,
      )
    ) {
      drawPreviewGuide(
        context,
        this.previewMs,
        this.model.durationMs,
        size.width,
        size.height,
      );
    }
    drawPlayhead(
      context,
      this.playheadMs,
      this.model.durationMs,
      size.width,
      size.height,
    );
    drawRuler(
      this.ruler,
      this.model,
      this.width,
      this.playheadMs,
      this.previewMs,
    );
    this.drawStickyHeroRow();
  }

  private drawStickyHeroRow(): void {
    const context = this.stickyHeroCanvas.getContext("2d");
    if (!context) return;
    const size = beginLogicalDraw(this.stickyHeroCanvas, context);
    context.clearRect(0, 0, size.width, size.height);
    if (this.pinnedHeroLane === null) return;
    const logicalWidth =
      Number.parseFloat(this.canvas.style.width) || this.width;
    const logicalHeight =
      Number.parseFloat(this.canvas.style.height)
      || Math.max(
        this.laneHeight,
        this.entities.length * this.laneHeight,
      );
    const sourceScaleY = this.canvas.height / logicalHeight;
    context.drawImage(
      this.canvas,
      0,
      this.pinnedHeroLane * this.laneHeight * sourceScaleY,
      this.canvas.width,
      this.laneHeight * sourceScaleY,
      0,
      0,
      size.width,
      size.height,
    );
  }

  private rebuildSelectedVisualClusters(): void {
    this.selectedVisualClusters.clear();
    if (this.selectedClusterEventIds.size > 0) {
      for (const cluster of this.visualClusters) {
        const matches =
          cluster.events.some((event) =>
            this.selectedClusterEventIds.has(event.id),
          )
          || (cluster.relatedEvents ?? []).some((event) =>
            this.selectedClusterEventIds.has(event.id),
          );
        if (matches) this.selectedVisualClusters.add(cluster);
      }
      return;
    }
    if (this.selectedFrame === null) return;
    for (const cluster of this.visualClusters) {
      if (
        cluster.events.some(
          (event) => event.frame === this.selectedFrame,
        )
      ) {
        this.selectedVisualClusters.add(cluster);
      }
    }
  }
}

export function eventsAtFrame(
  events: readonly NormalizedEvent[],
  frame: number | null,
): NormalizedEvent[] {
  if (frame === null) return [];
  return events.filter((event) => event.frame === frame);
}
