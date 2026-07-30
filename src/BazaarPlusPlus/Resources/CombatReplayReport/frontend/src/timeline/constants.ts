export const STATE_BAND_HEIGHT = 120;
export const TIME_RULER_HEIGHT = 34;
export const LANE_HEIGHT = 52;
export const LANE_LABEL_WIDTH = 300;
export const MIN_TIMELINE_WIDTH = 840;
export const MAX_TIMELINE_WIDTH = 32_000;
export const TIMELINE_BASE_DENSITY = 2;
export const EVENT_HIT_RADIUS = 20;
export const TIMELINE_MARKER_SIZE = 14;

export function timelineWidthAtZoom(
  baseWidth: number,
  zoom: number,
): number {
  return Math.min(
    MAX_TIMELINE_WIDTH,
    Math.max(
      MIN_TIMELINE_WIDTH,
      Math.round(baseWidth * TIMELINE_BASE_DENSITY * zoom),
    ),
  );
}
