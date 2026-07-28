import type { ActivitySortState } from "../statistics/aggregate.ts";
import type { SupportedLocale } from "../model/report.ts";
import {
  DEFAULT_LANE_VISIBILITY,
  type LaneFilterKey,
} from "../timeline/lane-filter.ts";
import type { EventLaneMode } from "../timeline/event-lane-mode.ts";
import type { StateScaleMode } from "../timeline/state-scale.ts";
import {
  DEFAULT_TIME_ZOOM_INDEX,
  TIME_ZOOM_STEPS,
  type InspectorAnchor,
  type ReportState,
  type ReportTab,
} from "./report-state.ts";

export type ReportAction =
  | { type: "select-tab"; tab: ReportTab }
  | { type: "select-locale"; locale: SupportedLocale }
  | { type: "zoom"; direction: -1 | 1 }
  | { type: "reset-zoom" }
  | { type: "select-scale"; scale: StateScaleMode }
  | {
      type: "select-frame";
      frame: number;
      combatMs: number;
      clusterEventIds: string[];
      entityId: string;
      anchor?: InspectorAnchor;
    }
  | { type: "select-time"; combatMs: number }
  | { type: "close-inspector" }
  | { type: "set-event-lane-mode"; mode: EventLaneMode }
  | {
      type: "set-lane-visibility";
      filter: LaneFilterKey;
      visible: boolean;
    }
  | { type: "reset-lane-visibility" }
  | { type: "set-activity-sort"; sort: ActivitySortState }
  | { type: "set-activity-group-by-side"; value: boolean };

export function reportReducer(
  state: ReportState,
  action: ReportAction,
): ReportState {
  switch (action.type) {
    case "select-tab":
      return state.tab === action.tab ? state : { ...state, tab: action.tab };
    case "select-locale":
      return state.locale === action.locale
        ? state
        : { ...state, locale: action.locale };
    case "zoom": {
      const timeZoomIndex = Math.max(
        0,
        Math.min(
          TIME_ZOOM_STEPS.length - 1,
          state.timeZoomIndex + action.direction,
        ),
      );
      return timeZoomIndex === state.timeZoomIndex
        ? state
        : { ...state, timeZoomIndex };
    }
    case "reset-zoom":
      return state.timeZoomIndex === DEFAULT_TIME_ZOOM_INDEX
        ? state
        : { ...state, timeZoomIndex: DEFAULT_TIME_ZOOM_INDEX };
    case "select-scale":
      return state.stateScale === action.scale
        ? state
        : { ...state, stateScale: action.scale };
    case "select-frame":
      return {
        ...state,
        selectedFrame: action.frame,
        selectedClusterEventIds: action.clusterEventIds,
        inspectedEntityId: action.entityId,
        selectedCombatMs: action.combatMs,
        inspectorOpen: action.anchor !== undefined,
        inspectorAnchor: action.anchor ?? null,
      };
    case "select-time":
      return {
        ...state,
        selectedFrame: null,
        selectedClusterEventIds: [],
        inspectedEntityId: "",
        selectedCombatMs: action.combatMs,
        inspectorOpen: false,
        inspectorAnchor: null,
      };
    case "close-inspector":
      return {
        ...state,
        selectedFrame: null,
        selectedClusterEventIds: [],
        inspectedEntityId: "",
        inspectorOpen: false,
        inspectorAnchor: null,
      };
    case "set-event-lane-mode":
      return state.eventLaneMode === action.mode
        ? state
        : {
            ...state,
            eventLaneMode: action.mode,
            selectedFrame: null,
            selectedClusterEventIds: [],
            inspectedEntityId: "",
            inspectorOpen: false,
            inspectorAnchor: null,
          };
    case "set-lane-visibility":
      return state.laneVisibility[action.filter] === action.visible
        ? state
        : {
            ...state,
            laneVisibility: {
              ...state.laneVisibility,
              [action.filter]: action.visible,
            },
          };
    case "reset-lane-visibility":
      return {
        ...state,
        laneVisibility: { ...DEFAULT_LANE_VISIBILITY },
      };
    case "set-activity-sort":
      return { ...state, activitySort: action.sort };
    case "set-activity-group-by-side":
      return state.activityGroupBySide === action.value
        ? state
        : { ...state, activityGroupBySide: action.value };
    default:
      return state;
  }
}
