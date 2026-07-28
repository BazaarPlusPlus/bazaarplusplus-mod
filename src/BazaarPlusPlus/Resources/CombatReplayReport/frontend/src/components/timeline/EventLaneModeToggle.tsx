import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import { cn } from "../../lib/utils.ts";
import { EVENT_LANE_MODES } from "../../timeline/event-lane-mode.ts";
import { Button } from "../ui/button.tsx";

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
      {EVENT_LANE_MODES.map((mode) => (
        <Button
          aria-selected={state.eventLaneMode === mode}
          className={cn(
            "bpp-event-lane-mode-option",
            state.eventLaneMode === mode && "is-active",
          )}
          data-bpp-event-lane-mode-option={mode}
          data-bpp-test-id={`event-lane-mode-${mode}`}
          key={mode}
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
      ))}
    </div>
  );
}
