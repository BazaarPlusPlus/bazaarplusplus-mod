import type { ReportViewModel } from "../model/report.ts";
import type { NormalizedEntity } from "../model/normalize.ts";
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
  TIMELINE_LEFT_GUTTER,
  TIMELINE_RIGHT_INTERACTION_GUTTER,
  timelineXAtCombatMs,
} from "./geometry.ts";
import { cachedTimelineImage } from "./image-cache.ts";
import type { StatusRange, TimelineCluster } from "./clusters.ts";

const MARKER_COLOR_NAMES: Record<string, ThemeColorName> = {
  damage: "damage",
  damageDirect: "damage",
  destroy: "damage",
  defeat: "damage",
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
  attributeHealthMax: "heal",
};

const MARKER_GLYPHS: Record<string, string> = {
  damage: "✦",
  damageDirect: "✦",
  destroy: "×",
  defeat: "☠",
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
  attributeHealthMax: "+",
};

function drawAttributeMarkerGlyph(
  context: CanvasRenderingContext2D,
  markerSize: number,
  color: string,
): void {
  const size = Math.max(1, markerSize);
  const halfWidth = size * 0.36;
  const plusY = -size * 0.22;
  const minusY = size * 0.3;
  const plusHalfHeight = size * 0.22;
  const drawPath = (): void => {
    context.beginPath();
    context.moveTo(-halfWidth, plusY);
    context.lineTo(halfWidth, plusY);
    context.moveTo(0, plusY - plusHalfHeight);
    context.lineTo(0, plusY + plusHalfHeight);
    context.moveTo(-halfWidth, minusY);
    context.lineTo(halfWidth, minusY);
    context.stroke();
  };

  context.save();
  context.lineCap = "round";
  context.lineJoin = "round";
  context.strokeStyle = themeColor("background");
  context.lineWidth = Math.max(3, size * 0.32);
  drawPath();
  context.strokeStyle = color;
  context.lineWidth = Math.max(1.5, size * 0.16);
  drawPath();
  context.restore();
}

function markerColor(token: string): string {
  return themeColor(MARKER_COLOR_NAMES[token] ?? "status");
}

export function markerImageBounds(
  naturalWidth: number,
  naturalHeight: number,
  size: number,
): { x: number; y: number; width: number; height: number } {
  const width = Math.max(1, naturalWidth);
  const height = Math.max(1, naturalHeight);
  const scale = Math.min(size / width, size / height);
  const drawWidth = width * scale;
  const drawHeight = height * scale;
  return {
    x: -drawWidth / 2,
    y: -drawHeight / 2,
    width: drawWidth,
    height: drawHeight,
  };
}

export function markerPoint(
  cluster: TimelineCluster,
): { x: number; y: number } {
  return {
    x: cluster.markerX ?? cluster.x,
    y: cluster.markerY ?? cluster.y,
  };
}

export function criticalIndicatorOffset(
  markerSize: number,
): { x: number; y: number } {
  const size = Math.max(1, markerSize);
  return {
    x: size / 2 + 3,
    y: -size * 0.28,
  };
}

export function drawMarker(
  context: CanvasRenderingContext2D,
  cluster: TimelineCluster,
  selected: boolean,
  requestDraw: () => void,
): void {
  const tier = cluster.tier ?? 2;
  const baseMarkerSize = tier === 1 ? 18 : tier === 3 ? 11 : 14;
  const markerSize = Math.min(
    baseMarkerSize,
    cluster.markerSizeCap ?? baseMarkerSize,
  );
  const color = markerColor(cluster.token);
  const marker = markerPoint(cluster);
  context.save();
  context.translate(marker.x, marker.y);
  const image = cachedTimelineImage(cluster.icon, requestDraw);
  if (image) {
    const bounds = markerImageBounds(
      image.naturalWidth || image.width,
      image.naturalHeight || image.height,
      markerSize,
    );
    if (selected) {
      context.strokeStyle = themeColor("foreground");
      context.lineWidth = 1.5;
      context.strokeRect(
        -markerSize / 2 - 2,
        -markerSize / 2 - 2,
        markerSize + 4,
        markerSize + 4,
      );
    }
    context.drawImage(
      image,
      bounds.x,
      bounds.y,
      bounds.width,
      bounds.height,
    );
  } else if (cluster.token === "attribute") {
    drawAttributeMarkerGlyph(context, markerSize, color);
    if (selected) {
      const half = Math.max(7, markerSize * 0.65);
      context.strokeStyle = themeColor("foreground");
      context.lineWidth = 1.5;
      context.strokeRect(-half, -half, half * 2, half * 2);
    }
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
  if (
    cluster.events.some(
      (event) =>
        event.isCritical
        && event.kind.toLowerCase() === "effect-executed",
    )
  ) {
    const indicator = criticalIndicatorOffset(markerSize);
    const fontSize = Math.max(8, Math.round(markerSize * 0.58));
    context.font = `700 ${fontSize}px ${themeValue("--bpp-font-sans")}`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.lineWidth = 2.5;
    context.lineJoin = "round";
    context.strokeStyle = themeColor("background");
    context.strokeText("!", indicator.x, indicator.y);
    context.fillStyle = themeColor("damage");
    context.fillText("!", indicator.x, indicator.y);
  }
  if (cluster.token === "defeat" && image) {
    const indicator = criticalIndicatorOffset(markerSize);
    const fontSize = Math.max(8, Math.round(markerSize * 0.62));
    context.font = `700 ${fontSize}px ${themeValue("--bpp-font-sans")}`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.lineWidth = 2.5;
    context.lineJoin = "round";
    context.strokeStyle = themeColor("background");
    context.strokeText("×", indicator.x, indicator.y);
    context.fillStyle = themeColor("damage");
    context.fillText("×", indicator.x, indicator.y);
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

export interface HeroHealthAreaGeometry {
  lane: number;
  side: "player" | "opponent";
  peak: number;
  baselineY: number;
  points: Array<{ x: number; y: number }>;
}

export function heroHealthAreaGeometry(
  model: Pick<ReportViewModel, "durationMs" | "metrics">,
  entities: readonly NormalizedEntity[],
  width: number,
  laneHeight: number,
): HeroHealthAreaGeometry[] {
  const areas: HeroHealthAreaGeometry[] = [];
  for (const side of ["player", "opponent"] as const) {
    const lane = entities.findIndex(
      (entity) =>
        entity.side === side && entity.type.toLowerCase() === "hero",
    );
    if (lane < 0) continue;
    const samples = model.metrics
      .filter(
        (sample) => sample.side === side && sample.metric === "health",
      )
      .slice()
      .sort(
        (left, right) =>
          left.combatMs - right.combatMs || left.frame - right.frame,
      );
    if (samples.length === 0) continue;

    const peak = Math.max(
      1,
      ...samples.map((sample) => Math.max(0, sample.value)),
    );
    const top = lane * laneHeight + 4;
    const baselineY = (lane + 1) * laneHeight - 4;
    const verticalRange = Math.max(1, baselineY - top);
    const yAtValue = (value: number): number =>
      baselineY
      - Math.max(0, Math.min(1, value / peak)) * verticalRange;
    const rightX = Math.max(
      TIMELINE_LEFT_GUTTER,
      width - TIMELINE_RIGHT_INTERACTION_GUTTER,
    );
    const leftX = Math.min(
      rightX,
      Math.max(
        TIMELINE_LEFT_GUTTER,
        timelineXAtCombatMs(
          samples[0].combatMs,
          model.durationMs,
          width,
        ),
      ),
    );
    let previousY = yAtValue(samples[0].value);
    const points = [{ x: leftX, y: previousY }];
    for (const sample of samples.slice(1)) {
      const x = Math.max(
        leftX,
        Math.min(
          rightX,
          timelineXAtCombatMs(
            sample.combatMs,
            model.durationMs,
            width,
          ),
        ),
      );
      const nextY = yAtValue(sample.value);
      points.push({ x, y: previousY }, { x, y: nextY });
      previousY = nextY;
    }
    points.push({ x: rightX, y: previousY });
    areas.push({ lane, side, peak, baselineY, points });
  }
  return areas;
}

export function drawHeroHealthAreas(
  context: CanvasRenderingContext2D,
  areas: readonly HeroHealthAreaGeometry[],
): void {
  const color = themeColor("damage");
  for (const area of areas) {
    const first = area.points[0];
    const last = area.points[area.points.length - 1];
    if (!first || !last) continue;
    context.save();
    context.beginPath();
    context.moveTo(first.x, area.baselineY);
    context.lineTo(first.x, first.y);
    for (const point of area.points.slice(1)) {
      context.lineTo(point.x, point.y);
    }
    context.lineTo(last.x, area.baselineY);
    context.closePath();
    context.fillStyle = withAlpha(color, 0.08);
    context.fill();

    context.beginPath();
    context.moveTo(first.x, first.y);
    for (const point of area.points.slice(1)) {
      context.lineTo(point.x, point.y);
    }
    context.strokeStyle = withAlpha(color, 0.52);
    context.lineWidth = 1.25;
    context.setLineDash(area.side === "opponent" ? [5, 3] : []);
    context.stroke();
    context.restore();
  }
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
  context.font = `600 ${themeLengthPx("--bpp-text-compact")}px ${themeValue("--bpp-font-data")}`;
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
