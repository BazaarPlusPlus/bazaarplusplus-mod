import type { NormalizedEntity } from "../model/normalize.ts";
import type { ReportViewModel } from "../model/report.ts";
import {
  buildClusters,
  buildStatusRanges,
  buildVisualClusters,
  createHitIndex,
  isVisibleTimelineEvent,
  type StatusRange,
  type TimelineCluster,
} from "./clusters.ts";
import { resizeLogicalCanvas } from "./canvas.ts";
import {
  applyTimelineRelatedHighlights,
  eventsAtTimelineFrame,
  hitTestTimelineClusters,
  selectedTimelineVisualClusters,
  stickyHeroPointAtPointer,
  timelineNavigationTarget,
  timelinePointAtPointer,
} from "./event-interaction.ts";
import {
  drawStickyHeroRow,
  drawTimelineScene,
} from "./event-scene-renderer.ts";
import { combatMsAtPointer } from "./geometry.ts";

interface ControllerOptions {
  canvas: HTMLCanvasElement;
  ruler: HTMLCanvasElement;
  model: ReportViewModel;
  entities: NormalizedEntity[];
  laneLabels: HTMLElement;
  stickyHeroCanvas: HTMLCanvasElement;
  stickyHeroLabel: HTMLElement;
  onSelect: (
    cluster: TimelineCluster | null,
    combatMs: number,
    clientX?: number,
    clientY?: number,
  ) => void;
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
    const point = stickyHeroPointAtPointer({
      event,
      canvas,
      width: this.width,
      laneHeight: this.laneHeight,
      lane,
    });
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
    if (cluster === this.hoverCluster && this.previewMs === combatMs) {
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
    this.onSelect(cluster, combatMs, event.clientX, event.clientY);
  }

  handleStickyHeroPointerDown(
    event: PointerEvent,
    canvas: HTMLCanvasElement,
    lane: number,
  ): void {
    if (event.button !== 0) return;
    const point = stickyHeroPointAtPointer({
      event,
      canvas,
      width: this.width,
      laneHeight: this.laneHeight,
      lane,
    });
    const cluster = this.hitTest(point.x, point.y);
    const combatMs = combatMsAtPointer(
      canvas,
      event.clientX,
      this.model.durationMs,
    );
    this.onSelect(cluster, combatMs, event.clientX, event.clientY);
  }

  handleRulerPointerDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    const combatMs = combatMsAtPointer(
      this.ruler,
      event.clientX,
      this.model.durationMs,
    );
    this.onSelect(null, combatMs, event.clientX, event.clientY);
  }

  navigate(direction: -1 | 1): void {
    const target = timelineNavigationTarget({
      markerClusters: this.markerClusters,
      selectedVisualClusters: this.selectedVisualClusters,
      direction,
      playheadMs: this.playheadMs,
      durationMs: this.model.durationMs,
      width: this.width,
    });
    if (target) this.onSelect(target.cluster, target.event.combatMs);
  }

  destroy(): void {
    if (this.frameHandle) cancelAnimationFrame(this.frameHandle);
    this.frameHandle = 0;
    this.applyRelatedHighlights(null);
  }

  private logicalPoint(event: PointerEvent): { x: number; y: number } {
    return timelinePointAtPointer({
      event,
      canvas: this.canvas,
      width: this.width,
      laneHeight: this.laneHeight,
      entityCount: this.entities.length,
    });
  }

  private hitTest(x: number, y: number): TimelineCluster | null {
    return hitTestTimelineClusters({
      x,
      y,
      hitIndex: this.hitIndex,
      visualClusters: this.visualClusters,
      laneHeight: this.laneHeight,
    });
  }

  private applyRelatedHighlights(cluster: TimelineCluster | null): void {
    applyTimelineRelatedHighlights({
      cluster,
      entities: this.entities,
      laneLabels: this.laneLabels,
      stickyHeroLabel: this.stickyHeroLabel,
      pinnedHeroLane: this.pinnedHeroLane,
    });
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
    drawTimelineScene({
      canvas: this.canvas,
      ruler: this.ruler,
      stickyHeroCanvas: this.stickyHeroCanvas,
      model: this.model,
      entities: this.entities,
      width: this.width,
      laneHeight: this.laneHeight,
      clusters: this.clusters,
      markerClusters: this.markerClusters,
      statusRanges: this.statusRanges,
      firstVisibleMs: this.firstVisibleMs,
      selectedVisualClusters: this.selectedVisualClusters,
      hoverCluster: this.hoverCluster,
      playheadMs: this.playheadMs,
      previewMs: this.previewMs,
      pinnedHeroLane: this.pinnedHeroLane,
      requestDraw: this.requestDraw,
    });
  }

  private drawStickyHeroRow(): void {
    drawStickyHeroRow({
      canvas: this.canvas,
      stickyHeroCanvas: this.stickyHeroCanvas,
      entities: this.entities,
      laneHeight: this.laneHeight,
      pinnedHeroLane: this.pinnedHeroLane,
    });
  }

  private rebuildSelectedVisualClusters(): void {
    this.selectedVisualClusters = selectedTimelineVisualClusters({
      visualClusters: this.visualClusters,
      selectedClusterEventIds: this.selectedClusterEventIds,
      selectedFrame: this.selectedFrame,
    });
  }
}

export { eventsAtTimelineFrame as eventsAtFrame };
