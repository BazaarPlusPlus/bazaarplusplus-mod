import { ListFilter, RotateCcw } from "lucide-react";
import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import {
  LANE_FILTER_SIDES,
  LANE_FILTER_TYPES,
  type LaneFilterKey,
} from "../../timeline/lane-filter.ts";
import { Button } from "../ui/button.tsx";
import { Checkbox } from "../ui/checkbox.tsx";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "../ui/popover.tsx";
import { Separator } from "../ui/separator.tsx";
import { ControlTooltip } from "../ui/tooltip.tsx";

function FilterOption({
  filter,
  label,
  state,
  dispatch,
}: {
  filter: LaneFilterKey;
  label: string;
  state: ReportState;
  dispatch: React.Dispatch<ReportAction>;
}): React.JSX.Element {
  return (
    <label className="flex cursor-pointer items-center gap-2 rounded-panel px-2 py-1.5 text-compact text-foreground/90 transition-colors hover:bg-accent">
      <Checkbox
        checked={state.laneVisibility[filter]}
        data-bpp-test-id={`lane-filter-${filter}`}
        onCheckedChange={(checked) =>
          dispatch({
            type: "set-lane-visibility",
            filter,
            visible: checked === true,
          })
        }
      />
      <span>{label}</span>
    </label>
  );
}

export function LaneFilterPopover({
  dispatch,
  state,
  t,
}: {
  dispatch: React.Dispatch<ReportAction>;
  state: ReportState;
  t: (key: string) => string;
}): React.JSX.Element {
  const hiddenCount = Object.values(state.laneVisibility).filter(
    (visible) => !visible,
  ).length;
  const hasFilters = hiddenCount > 0;

  return (
    <Popover>
      <ControlTooltip label={t("laneFilterHint")}>
        <PopoverTrigger asChild>
          <Button
            aria-label={t("laneFilter")}
            className="h-control-xs gap-1 px-2 normal-case tracking-normal"
            data-bpp-test-id="lane-filter-trigger"
            size="xs"
            variant={hasFilters ? "secondary" : "ghost"}
          >
            <ListFilter />
            <span>{t("laneFilter")}</span>
            {hasFilters && (
              <span
                className="grid min-w-4 place-items-center rounded-full bg-primary px-1 font-data text-nano tabular-nums text-primary-foreground"
                data-bpp-test-id="lane-filter-hidden-count"
              >
                {hiddenCount}
              </span>
            )}
          </Button>
        </PopoverTrigger>
      </ControlTooltip>
      <PopoverContent
        align="start"
        className="w-60 p-0"
        data-bpp-test-id="lane-filter-popover"
      >
        <div className="flex items-center justify-between gap-2 px-3 py-2">
          <strong className="text-compact">{t("laneFilterTitle")}</strong>
          <Button
            aria-label={t("laneFilterReset")}
            className="h-control-xs px-2"
            data-bpp-test-id="lane-filter-reset"
            disabled={!hasFilters}
            onClick={() => dispatch({ type: "reset-lane-visibility" })}
            size="xs"
            variant="ghost"
          >
            <RotateCcw />
            {t("laneFilterReset")}
          </Button>
        </div>
        <Separator />
        <div className="grid grid-cols-2 gap-x-2 p-2">
          <div>
            <div className="px-2 pb-1 pt-0.5 text-micro font-medium uppercase tracking-wide text-muted-foreground">
              {t("laneFilterSides")}
            </div>
            {LANE_FILTER_SIDES.map((filter) => (
              <FilterOption
                dispatch={dispatch}
                filter={filter}
                key={filter}
                label={t(filter)}
                state={state}
              />
            ))}
          </div>
          <div>
            <div className="px-2 pb-1 pt-0.5 text-micro font-medium uppercase tracking-wide text-muted-foreground">
              {t("laneFilterTypes")}
            </div>
            {LANE_FILTER_TYPES.map((filter) => (
              <FilterOption
                dispatch={dispatch}
                filter={filter}
                key={filter}
                label={t(`entity${filter[0].toUpperCase()}${filter.slice(1)}`)}
                state={state}
              />
            ))}
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
