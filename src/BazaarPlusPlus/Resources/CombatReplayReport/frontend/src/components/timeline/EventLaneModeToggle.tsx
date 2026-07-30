import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import { EVENT_LANE_MODES } from "../../timeline/event-lane-mode.ts";
import { Button } from "../ui/button.tsx";
import { ControlTooltip } from "../ui/tooltip.tsx";

export function EventLaneModeToggle({
  dispatch,
  state,
  t,
}: {
  dispatch: React.Dispatch<ReportAction>;
  state: ReportState;
  t: (key: string) => string;
}): React.JSX.Element {
  return (
    <div
      aria-label={t("eventLaneMode")}
      className="bpp-event-lane-mode"
      data-bpp-event-lane-mode={state.eventLaneMode}
      data-bpp-test-id="event-lane-mode-toggle"
      role="tablist"
    >
      <span
        aria-hidden="true"
        className="bpp-event-lane-mode-pill"
        data-bpp-test-id="event-lane-mode-pill"
      />
      {EVENT_LANE_MODES.map((mode) => (
        <ControlTooltip
          key={mode}
          label={t(
            mode === "source"
              ? "eventLaneSourceHint"
              : "eventLaneTargetHint",
          )}
        >
          <Button
            aria-selected={state.eventLaneMode === mode}
            className="bpp-event-lane-mode-option !h-[1.375rem]"
            data-bpp-event-lane-mode-option={mode}
            data-bpp-test-id={`event-lane-mode-${mode}`}
            onClick={() =>
              dispatch({ type: "set-event-lane-mode", mode })
            }
            role="tab"
            size="xs"
            type="button"
            variant="ghost"
          >
            {t(mode === "source" ? "source" : "target")}
          </Button>
        </ControlTooltip>
      ))}
    </div>
  );
}
