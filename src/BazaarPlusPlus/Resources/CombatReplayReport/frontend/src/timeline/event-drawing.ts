import type { ReportViewModel } from "../model/report.ts";
import {
  themeColor,
  themeLengthPx,
  themeValue,
  withAlpha,
  type ThemeColorName,
} from "../styles/theme.ts";
import { beginLogicalDraw, resizeLogicalCanvas } from "./canvas.ts";
import { TIME_RULER_HEIGHT } from "./constants.ts";
import {
  shouldDrawTimelinePreview,
  TIMELINE_RIGHT_INTERACTION_GUTTER,
  timelineXAtCombatMs,
} from "./geometry.ts";
import { cachedTimelineImage } from "./image-cache.ts";
import type { StatusRange, TimelineCluster } from "./clusters.ts";

const MARKER_COLOR_NAMES: Record<string, ThemeColorName> = {
  damage: "damage",
  damageDirect: "damage",
  destroy: "damage",
  burn: "burn",
  poison: "poison",
  heal: "heal",
  shield: "shield",
  charge: "charge",
  haste: "haste",
  slow: "slow",
  freeze: "freeze",
  skill: "skill",
  trigger: "rage",
  status: "status",
  attribute: "attribute",
};

const MARKER_GLYPHS: Record<string, string> = {
  damage: "✦",
  damageDirect: "✦",
  destroy: "×",
  burn: "♨",
  poison: "●",
  heal: "+",
  shield: "◈",
  charge: "ϟ",
  haste: "»",
  slow: "«",
  freeze: "❄",
  skill: "◆",
  trigger: "◇",
  status: "∿",
  attribute: "±",
};

function markerColor(token: string): string {
  return themeColor(MARKER_COLOR_NAMES[token] ?? "status");
}

export function markerOffset(cluster: TimelineCluster): number {
  if (cluster.statusRange) return 0;
  if (cluster.role === "source" || cluster.role === "trigger") return 9;
  if (cluster.role === "target" || cluster.role === "both") {
    if (cluster.token === "damage" || cluster.token === "damageDirect") {
      return -12;
    }
    if (cluster.token === "burn") return 0;
    if (cluster.token === "poison") return 12;
    if (cluster.token === "heal" || cluster.token === "shield") return 9;
  }
  return 0;
}

export function markerPoint(
  cluster: TimelineCluster,
): { x: number; y: number } {
  return {
    x: cluster.markerX ?? cluster.x,
    y: cluster.markerY ?? cluster.y + markerOffset(cluster),
  };
}

export function drawMarker(
  context: CanvasRenderingContext2D,
  cluster: TimelineCluster,
  selected: boolean,
  requestDraw: () => void,
): void {
  const tier = cluster.tier ?? 2;
  const color = markerColor(cluster.token);
  const marker = markerPoint(cluster);
  context.save();
  context.translate(marker.x, marker.y);
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
    context.font = `700 ${fontSize}px ${themeValue("--bpp-font-sans")}`;
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

export function drawStatusRanges(
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
    Math.max(1, width - TIMELINE_RIGHT_INTERACTION_GUTTER) /
    Math.max(0.001, durationMs / 1000);
  const candidates = [
    100, 200, 500, 1_000, 2_000, 5_000, 10_000, 20_000, 30_000,
  ];
  return (
    candidates.find((step) => (step / 1000) * pixelsPerSecond >= 72) ??
    candidates[candidates.length - 1]
  );
}

export function drawTimeGrid(
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
    const x = Math.round(timelineXAtCombatMs(time, model.durationMs, width)) + 0.5;
    context.beginPath();
    context.moveTo(x, 0);
    context.lineTo(x, height);
    context.stroke();
  }
  context.restore();
}

export function drawSideBoundary(
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

export function drawPlayhead(
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

export function drawPreviewGuide(
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

export function drawRuler(
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
  context.font = `600 ${themeLengthPx("--bpp-text-compact")}px ${themeValue("--bpp-font-mono")}`;
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
      Math.min(size.width - TIMELINE_RIGHT_INTERACTION_GUTTER - 18, x),
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
