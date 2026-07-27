import { useMemo, type RefObject } from "react";
import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import { formatCompactNumber, formatDuration } from "../../i18n/format.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  normalizeSide,
  type NormalizedEntity,
} from "../../model/normalize.ts";
import { groupMetricSamples, metricValueAt } from "../../timeline/state-band-renderer.ts";
import { METRIC_DOT_CLASSES } from "../../timeline/metric-theme.ts";
import {
  METRIC_ORDER,
  sharedStateDomain,
  stateAxisTicks,
  type StateMetric,
} from "../../timeline/state-scale.ts";
import { LANE_HEIGHT } from "../../timeline/constants.ts";
import { cn } from "../../lib/utils.ts";
import { Badge } from "../ui/badge.tsx";
import { Button } from "../ui/button.tsx";
import { ToggleGroup, ToggleGroupItem } from "../ui/toggle-group.tsx";
import { EntityArt } from "../semantic/EntityArt.tsx";

export function heroLaneAtScroll(
  heroLaneIndexes: readonly number[],
  scrollTop: number,
): number | null {
  let activeLane: number | null = null;
  for (const lane of heroLaneIndexes) {
    if (scrollTop < (lane + 1) * LANE_HEIGHT) break;
    activeLane = lane;
  }
  return activeLane;
}

function entityTypeLabel(
  type: string,
  t: (key: string) => string,
): string {
  const normalized = type.toLowerCase();
  return t(
    normalized === "hero"
      ? "entityHero"
      : normalized === "item"
        ? "entityItem"
        : normalized === "skill"
          ? "entitySkill"
          : normalized === "effect"
            ? "entityEffect"
            : "entityUnknown",
  );
}

export function LaneLabelContents({
  entity,
  index,
  sticky,
  t,
}: {
  entity: NormalizedEntity;
  index: number;
  sticky?: boolean;
  t: (key: string) => string;
}): React.JSX.Element {
  const side = normalizeSide(entity.side);
  const artSlotTestId = sticky
    ? "timeline-sticky-hero-art-slot"
    : `timeline-lane-art-slot-${index}`;
  const iconTestId = sticky
    ? "timeline-sticky-hero-icon"
    : `timeline-lane-icon-${index}`;

  return (
    <>
      <span className="bpp-lane-art-slot" data-bpp-test-id={artSlotTestId}>
        <EntityArt entity={entity} testId={iconTestId} />
      </span>
      <span className="bpp-lane-copy">
        {sticky
          ? (
            <span className="bpp-sticky-hero-copy">
              <strong className="min-w-0 truncate text-body text-foreground">
                {entity.name}
              </strong>
              {side !== "neutral" && (
                <Badge
                  className="bpp-sticky-hero-side text-nano"
                  data-bpp-test-id="timeline-sticky-hero-side"
                  variant="outline"
                >
                  {t(side)}
                </Badge>
              )}
            </span>
          )
          : (
            <>
              <strong className="block truncate text-body text-foreground">
                {entity.name}
              </strong>
              <small className="block truncate text-micro text-muted-foreground">
                {entityTypeLabel(entity.type, t)}
              </small>
            </>
          )}
      </span>
      <span
        className={cn(
          "ml-auto h-7 w-0.5 rounded-full",
          side === "opponent"
            ? "bg-opponent/80"
            : side === "player"
              ? "bg-player/85"
              : "bg-faint/70",
        )}
      />
    </>
  );
}

export function StateLabels({
  model,
  combatMs,
  scaleMode,
  rootRef,
  visibleMetrics,
  onMetricHighlight,
  onMetricToggle,
  dispatch,
  t,
}: {
  model: ReportViewModel;
  combatMs: number;
  scaleMode: ReportState["stateScale"];
  rootRef: RefObject<HTMLDivElement | null>;
  visibleMetrics: ReadonlySet<StateMetric>;
  onMetricHighlight: (metric: StateMetric | null) => void;
  onMetricToggle: (metric: StateMetric) => void;
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  const grouped = useMemo(() => groupMetricSamples(model), [model]);
  const visibleOrder = METRIC_ORDER.filter((metric) =>
    visibleMetrics.has(metric)
  );
  const domain = sharedStateDomain(grouped, scaleMode, visibleOrder);
  const ticks = stateAxisTicks(domain, scaleMode);

  return (
    <div
      className="bpp-state-labels"
      data-bpp-combat-ms={combatMs}
      data-bpp-test-id="state-band-labels"
      ref={rootRef}
    >
      <div className="flex h-control-xs items-center gap-1 border-b border-border/60 px-2">
        <span className="min-w-0 truncate font-mono text-micro text-muted-foreground">
          {t("stateValues")} ·{" "}
          <span data-bpp-state-time>{formatDuration(combatMs)}</span>
        </span>
        <ToggleGroup
          aria-label={t("metricsTitle")}
          className="ml-auto border border-border/70 bg-background/60"
          onValueChange={(value) => {
            if (value === "linear" || value === "magnitude") {
              dispatch({ type: "select-scale", scale: value });
            }
          }}
          size="xs"
          type="single"
          value={scaleMode}
        >
          {(["linear", "magnitude"] as const).map((scale) => (
            <ToggleGroupItem className="px-1.5" key={scale} value={scale}>
              {t(scale === "linear" ? "linearScale" : "magnitudeScale")}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      </div>
      <div className="grid h-[93px] grid-rows-6">
        {METRIC_ORDER.map((metric) => {
          const visible = visibleMetrics.has(metric);
          const player = metricValueAt(
            grouped.get(`player:${metric}`),
            combatMs,
          );
          const opponent = metricValueAt(
            grouped.get(`opponent:${metric}`),
            combatMs,
          );
          return (
            <Button
              aria-pressed={visible}
              className={cn(
                "grid h-full w-full grid-cols-[8px_minmax(0,1fr)_4.5rem_4.5rem] items-center gap-1 rounded-none px-2 text-left text-micro transition-none hover:bg-accent/25 focus-visible:bg-accent/35 focus-visible:outline-none focus-visible:ring-0",
                !visible && "opacity-45",
              )}
              data-bpp-state-metric={metric}
              data-bpp-test-id={`state-label-${metric}`}
              key={metric}
              onBlur={() => onMetricHighlight(null)}
              onClick={() => onMetricToggle(metric)}
              onFocus={() => onMetricHighlight(metric)}
              onPointerEnter={() => onMetricHighlight(metric)}
              onPointerLeave={(event) => {
                if (event.currentTarget !== document.activeElement) {
                  onMetricHighlight(null);
                }
              }}
              size="xs"
              type="button"
              variant="tableHeader"
            >
              <span
                className={cn(
                  "size-1.5 rounded-full",
                  METRIC_DOT_CLASSES[metric],
                )}
              />
              <strong className="truncate text-foreground/85">
                {t(metric)}
              </strong>
              <span
                className="text-right font-mono tabular-nums text-player"
                data-bpp-state-metric={metric}
                data-bpp-state-side="player"
              >
                {player === null ? "—" : formatCompactNumber(player)}
              </span>
              <span
                className="text-right font-mono tabular-nums text-opponent"
                data-bpp-state-metric={metric}
                data-bpp-state-side="opponent"
              >
                {opponent === null ? "—" : formatCompactNumber(opponent)}
              </span>
            </Button>
          );
        })}
      </div>
      <div className="sr-only" data-bpp-state-axis-ticks>
        {ticks.map((tick) => tick.label).join(", ")}
      </div>
    </div>
  );
}
