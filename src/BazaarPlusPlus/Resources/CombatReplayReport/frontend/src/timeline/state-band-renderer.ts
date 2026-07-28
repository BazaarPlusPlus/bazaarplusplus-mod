import type {
  NormalizedMetric,
} from "../model/normalize.ts";
import type { ReportViewModel } from "../model/report.ts";
import {
  beginLogicalDraw,
  resizeLogicalCanvas,
} from "./canvas.ts";
import {
  shouldDrawTimelinePreview,
  TIMELINE_LEFT_GUTTER,
  TIMELINE_RIGHT_INTERACTION_GUTTER,
  timelineXAtCombatMs,
} from "./geometry.ts";
import {
  METRIC_ORDER,
  sharedStateDomain,
  sharedStateMappedY,
  sharedStateY,
  stateAxisTicks,
  type StateAxisTick,
  type StateDomain,
  type StateMetric,
  type StateScaleMode,
} from "./state-scale.ts";
import {
  themeColor,
  withAlpha,
} from "../styles/theme.ts";
import { METRIC_THEME_COLORS } from "./metric-theme.ts";

export type GroupedMetricSamples = Map<string, NormalizedMetric[]>;

export function groupMetricSamples(
  model: ReportViewModel,
): GroupedMetricSamples {
  const grouped: GroupedMetricSamples = new Map();
  for (const sample of model.metrics) {
    const key = `${sample.side}:${sample.metric}`;
    const values = grouped.get(key) ?? [];
    values.push(sample);
    grouped.set(key, values);
  }
  for (const values of grouped.values()) {
    values.sort(
      (left, right) =>
        left.combatMs - right.combatMs || left.frame - right.frame,
    );
  }
  return grouped;
}

export function metricValueAt(
  samples: readonly NormalizedMetric[] | undefined,
  combatMs: number,
): number | null {
  if (!samples || samples.length === 0) return null;
  let value: number | null = null;
  for (const sample of samples) {
    if (sample.combatMs > combatMs) break;
    value = sample.value;
  }
  return value;
}

function drawGrid(
  context: CanvasRenderingContext2D,
  domain: StateDomain,
  width: number,
  height: number,
  ticks: readonly StateAxisTick[],
): void {
  context.save();
  context.strokeStyle = withAlpha(themeColor("muted-foreground"), 0.15);
  context.lineWidth = 1;
  for (const tick of ticks) {
    const y =
      Math.round(sharedStateMappedY(tick.mapped, domain, height)) + 0.5;
    context.beginPath();
    context.moveTo(0, y);
    context.lineTo(width, y);
    context.stroke();
  }
  if (domain.minimum <= 0 && domain.maximum >= 0) {
    const zeroY =
      Math.round(sharedStateMappedY(0, domain, height)) + 0.5;
    context.strokeStyle = withAlpha(themeColor("foreground"), 0.22);
    context.beginPath();
    context.moveTo(0, zeroY);
    context.lineTo(width, zeroY);
    context.stroke();
  }
  context.restore();
}

function drawStepLine(
  context: CanvasRenderingContext2D,
  samples: readonly NormalizedMetric[] | undefined,
  model: ReportViewModel,
  width: number,
  height: number,
  domain: StateDomain,
  scaleMode: StateScaleMode,
  side: "player" | "opponent",
  color: string,
  emphasis: "normal" | "muted" | "highlighted",
): void {
  if (!samples || samples.length === 0) return;
  context.save();
  context.strokeStyle = color;
  const baseAlpha = side === "player" ? 1 : 0.92;
  context.globalAlpha = emphasis === "muted" ? baseAlpha * 0.16 : baseAlpha;
  context.lineWidth = emphasis === "highlighted"
    ? side === "player" ? 3.25 : 2.8
    : side === "player" ? 2.15 : 1.8;
  context.setLineDash(side === "opponent" ? [6, 4] : []);
  context.lineJoin = "round";
  context.lineCap = "round";
  context.beginPath();
  let previousY = sharedStateY(
    samples[0].value,
    domain,
    height,
    scaleMode,
  );
  const firstX = timelineXAtCombatMs(
    samples[0].combatMs,
    model.durationMs,
    width,
  );
  context.moveTo(Math.max(TIMELINE_LEFT_GUTTER, firstX), previousY);
  for (const sample of samples) {
    const x = timelineXAtCombatMs(
      sample.combatMs,
      model.durationMs,
      width,
    );
    const nextY = sharedStateY(
      sample.value,
      domain,
      height,
      scaleMode,
    );
    context.lineTo(x, previousY);
    context.lineTo(x, nextY);
    previousY = nextY;
  }
  context.lineTo(
    width - TIMELINE_RIGHT_INTERACTION_GUTTER,
    previousY,
  );
  context.stroke();
  context.restore();
}

function drawVerticalTime(
  context: CanvasRenderingContext2D,
  combatMs: number | null,
  durationMs: number,
  width: number,
  height: number,
  preview: boolean,
): void {
  if (combatMs === null || !Number.isFinite(combatMs)) return;
  const x = timelineXAtCombatMs(combatMs, durationMs, width);
  context.save();
  context.strokeStyle = preview
    ? withAlpha(themeColor("success"), 0.82)
    : themeColor("rage");
  context.lineWidth = preview ? 1.2 : 1.8;
  if (preview) context.setLineDash([4, 4]);
  context.beginPath();
  context.moveTo(x + 0.5, 0);
  context.lineTo(x + 0.5, height);
  context.stroke();
  context.restore();
}

export interface StateBandRenderInput {
  canvas: HTMLCanvasElement;
  model: ReportViewModel;
  grouped: GroupedMetricSamples;
  width: number;
  height: number;
  scaleMode: StateScaleMode;
  playheadMs: number;
  previewMs: number | null;
  visibleMetrics?: ReadonlySet<StateMetric>;
  highlightedMetric?: StateMetric | null;
}

interface StateBandStaticCache {
  canvas: HTMLCanvasElement;
  model: ReportViewModel;
  grouped: GroupedMetricSamples;
  width: number;
  height: number;
  scaleMode: StateScaleMode;
  visibleKey: string;
  highlightedMetric: StateMetric | null;
  domain: StateDomain | null;
  ticks: StateAxisTick[];
}

const staticCacheByCanvas = new WeakMap<
  HTMLCanvasElement,
  StateBandStaticCache
>();

export function drawStateBand(input: StateBandRenderInput): {
  domain: StateDomain | null;
  ticks: StateAxisTick[];
} {
  resizeLogicalCanvas(input.canvas, input.width, input.height);
  const context = input.canvas.getContext("2d");
  if (!context) return { domain: null, ticks: [] };
  const size = beginLogicalDraw(input.canvas, context);
  const visibleMetrics = input.visibleMetrics ?? new Set(METRIC_ORDER);
  const visibleOrder = METRIC_ORDER.filter((metric) =>
    visibleMetrics.has(metric)
  );
  const highlightedMetric =
    input.highlightedMetric
      && visibleMetrics.has(input.highlightedMetric)
      ? input.highlightedMetric
      : null;
  input.canvas.dataset.bppVisibleMetrics = visibleOrder.join(",");
  input.canvas.dataset.bppHighlightedMetric = highlightedMetric ?? "";
  const visibleKey = visibleOrder.join(",");
  let cache = staticCacheByCanvas.get(input.canvas);
  if (
    !cache
    || cache.model !== input.model
    || cache.grouped !== input.grouped
    || cache.width !== input.width
    || cache.height !== input.height
    || cache.scaleMode !== input.scaleMode
    || cache.visibleKey !== visibleKey
    || cache.highlightedMetric !== highlightedMetric
  ) {
    const staticCanvas = cache?.canvas ?? document.createElement("canvas");
    resizeLogicalCanvas(staticCanvas, input.width, input.height);
    const staticContext = staticCanvas.getContext("2d");
    const domain = sharedStateDomain(
      input.grouped,
      input.scaleMode,
      visibleOrder,
    );
    const ticks = stateAxisTicks(domain, input.scaleMode);
    if (staticContext) {
      const staticSize = beginLogicalDraw(staticCanvas, staticContext);
      staticContext.clearRect(0, 0, staticSize.width, staticSize.height);
      staticContext.fillStyle = themeColor("surface");
      staticContext.fillRect(0, 0, staticSize.width, staticSize.height);
      if (domain) {
        drawGrid(
          staticContext,
          domain,
          staticSize.width,
          staticSize.height,
          ticks,
        );
        const drawOrder = highlightedMetric
          ? [
              ...visibleOrder.filter((metric) => metric !== highlightedMetric),
              highlightedMetric,
            ]
          : visibleOrder;
        for (const metric of drawOrder) {
          const emphasis = highlightedMetric === null
            ? "normal"
            : metric === highlightedMetric
              ? "highlighted"
              : "muted";
          drawStepLine(
            staticContext,
            input.grouped.get(`player:${metric}`),
            input.model,
            staticSize.width,
            staticSize.height,
            domain,
            input.scaleMode,
            "player",
            themeColor(METRIC_THEME_COLORS[metric]),
            emphasis,
          );
          drawStepLine(
            staticContext,
            input.grouped.get(`opponent:${metric}`),
            input.model,
            staticSize.width,
            staticSize.height,
            domain,
            input.scaleMode,
            "opponent",
            themeColor(METRIC_THEME_COLORS[metric]),
            emphasis,
          );
        }
      }
    }
    cache = {
      canvas: staticCanvas,
      model: input.model,
      grouped: input.grouped,
      width: input.width,
      height: input.height,
      scaleMode: input.scaleMode,
      visibleKey,
      highlightedMetric,
      domain,
      ticks,
    };
    staticCacheByCanvas.set(input.canvas, cache);
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.stateStaticDrawCount += 1;
    }
  }
  context.clearRect(0, 0, size.width, size.height);
  context.drawImage(
    cache.canvas,
    0,
    0,
    cache.canvas.width,
    cache.canvas.height,
    0,
    0,
    size.width,
    size.height,
  );
  if (
    shouldDrawTimelinePreview(
      input.playheadMs,
      input.previewMs,
      input.model.durationMs,
      size.width,
    )
  ) {
    drawVerticalTime(
      context,
      input.previewMs,
      input.model.durationMs,
      size.width,
      size.height,
      true,
    );
  }
  drawVerticalTime(
    context,
    input.playheadMs,
    input.model.durationMs,
    size.width,
    size.height,
    false,
  );
  return { domain: cache.domain, ticks: cache.ticks };
}
