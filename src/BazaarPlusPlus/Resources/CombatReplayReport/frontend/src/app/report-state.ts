import type { ActivitySortState } from "../statistics/aggregate.ts";
import type {
  StateScaleMode,
} from "../timeline/state-scale.ts";
import {
  DEFAULT_LANE_VISIBILITY,
  type LaneVisibility,
} from "../timeline/lane-filter.ts";
import type { SupportedLocale } from "../model/report.ts";

export type ReportTab = "timeline" | "statistics";

export interface InspectorAnchor {
  x: number;
  y: number;
}

export interface ReportState {
  tab: ReportTab;
  locale: SupportedLocale;
  timeZoomIndex: number;
  stateScale: StateScaleMode;
  selectedFrame: number | null;
  selectedClusterEventIds: string[];
  inspectedEntityId: string;
  selectedCombatMs: number;
  selectedEntityId: string;
  inspectorOpen: boolean;
  inspectorAnchor: InspectorAnchor | null;
  laneVisibility: LaneVisibility;
  activitySort: ActivitySortState;
  activityGroupBySide: boolean;
}

export const TIME_ZOOM_STEPS = [0.5, 0.75, 1, 1.5, 2, 3, 4] as const;
export const DEFAULT_TIME_ZOOM_INDEX = 2;

export function createInitialReportState(
  locale: SupportedLocale,
): ReportState {
  return {
    tab: "timeline",
    locale,
    timeZoomIndex: DEFAULT_TIME_ZOOM_INDEX,
    stateScale: "linear",
    selectedFrame: null,
    selectedClusterEventIds: [],
    inspectedEntityId: "",
    selectedCombatMs: 0,
    selectedEntityId: "",
    inspectorOpen: false,
    inspectorAnchor: null,
    laneVisibility: { ...DEFAULT_LANE_VISIBILITY },
    activitySort: {
      key: "damage",
      metric: "amount",
      direction: "desc",
    },
    activityGroupBySide: true,
  };
}
