export const EVENT_LANE_MODES = ["source", "target"] as const;

export type EventLaneMode = (typeof EVENT_LANE_MODES)[number];

export const DEFAULT_EVENT_LANE_MODE: EventLaneMode = "target";
