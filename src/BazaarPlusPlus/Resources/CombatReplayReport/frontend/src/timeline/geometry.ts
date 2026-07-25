export const TIMELINE_LEFT_GUTTER = 14;
export const TIMELINE_RIGHT_INTERACTION_GUTTER = 64;

export function timelineUsableWidth(width: number): number {
  return Math.max(
    1,
    width - TIMELINE_LEFT_GUTTER - TIMELINE_RIGHT_INTERACTION_GUTTER,
  );
}

export function timelineRatioAtX(x: number, width: number): number {
  return Math.max(
    0,
    Math.min(
      1,
      (x - TIMELINE_LEFT_GUTTER) / timelineUsableWidth(width),
    ),
  );
}

export function timelineXAtCombatMs(
  combatMs: number,
  durationMs: number,
  width: number,
): number {
  return (
    TIMELINE_LEFT_GUTTER
    + Math.max(0, Math.min(1, combatMs / Math.max(1, durationMs)))
      * timelineUsableWidth(width)
  );
}

export function shouldDrawTimelinePreview(
  playheadMs: number,
  previewMs: number | null,
  durationMs: number,
  width: number,
  minimumDistancePx = 3,
): boolean {
  if (previewMs === null || !Number.isFinite(previewMs)) return false;
  if (!Number.isFinite(playheadMs)) return true;
  return (
    Math.abs(
      timelineXAtCombatMs(previewMs, durationMs, width)
      - timelineXAtCombatMs(playheadMs, durationMs, width),
    ) >= Math.max(0, minimumDistancePx)
  );
}

export function combatMsAtPointer(
  canvas: HTMLCanvasElement,
  clientX: number,
  durationMs: number,
): number {
  const bounds = canvas.getBoundingClientRect();
  if (bounds.width <= 0) return 0;
  const logicalWidth = Number.parseFloat(canvas.style.width) || canvas.width;
  const canvasX = (clientX - bounds.left) * (logicalWidth / bounds.width);
  const ratio = timelineRatioAtX(canvasX, logicalWidth);
  return ratio * Math.max(0, durationMs);
}
