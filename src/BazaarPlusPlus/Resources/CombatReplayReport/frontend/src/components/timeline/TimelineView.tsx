import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { cn } from "../../lib/utils.ts";
import {
  combatMsAtPointer,
} from "../../timeline/geometry.ts";
import type { ReportAction } from "../../app/report-reducer.ts";
import {
  TIME_ZOOM_STEPS,
  type ReportState,
} from "../../app/report-state.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  LANE_HEIGHT,
  LANE_LABEL_WIDTH,
  MIN_TIMELINE_WIDTH,
  STATE_BAND_HEIGHT,
  TIME_RULER_HEIGHT,
  timelineWidthAtZoom,
} from "../../timeline/constants.ts";
import {
  TimelineCanvasController,
} from "../../timeline/event-renderer.ts";
import {
  drawStateBand,
  groupMetricSamples,
  metricValueAt,
} from "../../timeline/state-band-renderer.ts";
import { METRIC_DOT_CLASSES } from "../../timeline/metric-theme.ts";
import {
  METRIC_ORDER,
  stateAxisTicks,
  sharedStateDomain,
} from "../../timeline/state-scale.ts";
import { formatCompactNumber, formatDuration } from "../../i18n/format.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { FrameInspector } from "../inspector/FrameInspector.tsx";
import {
  timelineClusterEventIds,
  type TimelineCluster,
} from "../../timeline/clusters.ts";
import {
  filterTimelineEntities,
  opponentBoundaryLane,
} from "../../timeline/lane-filter.ts";
import {
  ToggleGroup,
  ToggleGroupItem,
} from "../ui/toggle-group.tsx";
import { LaneFilterPopover } from "./LaneFilterPopover.tsx";

export interface TimelineHandle {
  setExternalPlayhead: (combatMs: number) => void;
  navigate: (direction: -1 | 1) => void;
}

interface TooltipRefs {
  host: HTMLDivElement;
  time: HTMLSpanElement;
  label: HTMLElement;
  count: HTMLSpanElement;
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

function StateLabels({
  model,
  combatMs,
  scaleMode,
  dispatch,
  t,
}: {
  model: ReportViewModel;
  combatMs: number;
  scaleMode: ReportState["stateScale"];
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  const grouped = useMemo(() => groupMetricSamples(model), [model]);
  const domain = sharedStateDomain(grouped, scaleMode);
  const ticks = stateAxisTicks(domain, scaleMode);
  return (
    <div
      className="bpp-state-labels"
      data-bpp-test-id="state-band-labels"
    >
      <div className="flex h-control-xs items-center gap-1 border-b border-border/60 px-2">
        <span className="min-w-0 truncate font-mono text-micro text-muted-foreground">
          {t("stateValues")} · {formatDuration(combatMs)}
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
          const player = metricValueAt(
            grouped.get(`player:${metric}`),
            combatMs,
          );
          const opponent = metricValueAt(
            grouped.get(`opponent:${metric}`),
            combatMs,
          );
          return (
            <div
              className="grid grid-cols-[8px_1fr_auto_auto] items-center gap-1 px-2 text-micro"
              data-bpp-test-id={`state-label-${metric}`}
              key={metric}
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
              <span className="font-mono text-player">
                {player === null ? "—" : formatCompactNumber(player)}
              </span>
              <span className="font-mono text-opponent">
                {opponent === null ? "—" : formatCompactNumber(opponent)}
              </span>
            </div>
          );
        })}
      </div>
      <div className="sr-only" data-bpp-state-axis-ticks>
        {ticks.map((tick) => tick.label).join(", ")}
      </div>
    </div>
  );
}

export const TimelineView = forwardRef<
  TimelineHandle,
  {
    model: ReportViewModel;
    state: ReportState;
    dispatch: React.Dispatch<ReportAction>;
    onPreviewCombatMs: (combatMs: number | null) => void;
    t: (key: string) => string;
  }
>(function TimelineView(
  { model, state, dispatch, onPreviewCombatMs, t },
  forwardedRef,
): React.JSX.Element {
  const scrollRef = useRef<HTMLDivElement>(null);
  const gridRef = useRef<HTMLDivElement>(null);
  const labelsRef = useRef<HTMLDivElement>(null);
  const stateCanvasRef = useRef<HTMLCanvasElement>(null);
  const rulerRef = useRef<HTMLCanvasElement>(null);
  const eventCanvasRef = useRef<HTMLCanvasElement>(null);
  const tooltipHostRef = useRef<HTMLDivElement>(null);
  const tooltipTimeRef = useRef<HTMLSpanElement>(null);
  const tooltipLabelRef = useRef<HTMLElement>(null);
  const tooltipCountRef = useRef<HTMLSpanElement>(null);
  const controllerRef = useRef<TimelineCanvasController | null>(null);
  const playheadRef = useRef(state.selectedCombatMs);
  const previewRef = useRef<number | null>(null);
  const selectionRef = useRef({
    frame: state.selectedFrame,
    eventIds: state.selectedClusterEventIds,
  });
  selectionRef.current = {
    frame: state.selectedFrame,
    eventIds: state.selectedClusterEventIds,
  };
  const [baseWidth, setBaseWidth] = useState(MIN_TIMELINE_WIDTH);
  const entities = useMemo(
    () => filterTimelineEntities(model.entities, state.laneVisibility),
    [model.entities, state.laneVisibility],
  );
  const sideBoundaryLane = useMemo(
    () => opponentBoundaryLane(entities),
    [entities],
  );
  const groupedMetrics = useMemo(() => groupMetricSamples(model), [model]);
  const timelineWidth = timelineWidthAtZoom(
    baseWidth,
    TIME_ZOOM_STEPS[state.timeZoomIndex],
  );
  const laneCanvasHeight = Math.max(
    LANE_HEIGHT,
    entities.length * LANE_HEIGHT,
  );

  const drawState = useCallback(() => {
    if (!stateCanvasRef.current) return;
    drawStateBand({
      canvas: stateCanvasRef.current,
      model,
      grouped: groupedMetrics,
      width: timelineWidth,
      height: STATE_BAND_HEIGHT,
      scaleMode: state.stateScale,
      playheadMs: playheadRef.current,
      previewMs: previewRef.current,
    });
  }, [
    groupedMetrics,
    model,
    state.stateScale,
    timelineWidth,
  ]);
  const drawStateRef = useRef(drawState);
  const previewCallbackRef = useRef(onPreviewCombatMs);
  const translateRef = useRef(t);
  drawStateRef.current = drawState;
  previewCallbackRef.current = onPreviewCombatMs;
  translateRef.current = t;

  useLayoutEffect(() => {
    const scroll = scrollRef.current;
    if (!scroll) return;
    const update = (): void => {
      setBaseWidth(
        Math.max(
          MIN_TIMELINE_WIDTH,
          scroll.clientWidth - LANE_LABEL_WIDTH,
        ),
      );
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(scroll);
    return () => observer.disconnect();
  }, []);

  useLayoutEffect(() => {
    const canvas = eventCanvasRef.current;
    const ruler = rulerRef.current;
    const labels = labelsRef.current;
    if (!canvas || !ruler || !labels) return;
    const tooltip = (): TooltipRefs | null => {
      if (
        !tooltipHostRef.current
        || !tooltipTimeRef.current
        || !tooltipLabelRef.current
        || !tooltipCountRef.current
      ) {
        return null;
      }
      return {
        host: tooltipHostRef.current,
        time: tooltipTimeRef.current,
        label: tooltipLabelRef.current,
        count: tooltipCountRef.current,
      };
    };
    const controller = new TimelineCanvasController({
      canvas,
      ruler,
      model,
      entities,
      laneLabels: labels,
      onSelect: (cluster, combatMs) => {
        if (cluster?.events[0]) {
          const event = cluster.events.reduce((nearest, candidate) =>
            Math.abs(candidate.combatMs - combatMs)
              < Math.abs(nearest.combatMs - combatMs)
              ? candidate
              : nearest,
          );
          dispatch({
            type: "select-frame",
            frame: event.frame,
            combatMs: event.combatMs,
            clusterEventIds: timelineClusterEventIds(cluster),
            entityId: entities[cluster.lane]?.id ?? "",
          });
        } else {
          dispatch({ type: "select-time", combatMs });
        }
      },
      onPreview: (cluster, combatMs, clientX, clientY) => {
        previewRef.current = combatMs;
        drawStateRef.current();
        previewCallbackRef.current(combatMs);
        const refs = tooltip();
        if (!refs) return;
        if (combatMs === null) {
          refs.host.hidden = true;
          return;
        }
        refs.host.hidden = false;
        refs.time.textContent = formatDuration(combatMs);
        refs.label.textContent = cluster
          ? translateRef.current(cluster.token)
          : translateRef.current("time");
        refs.count.textContent = cluster
          ? `${cluster.events.length} ${translateRef.current("event")}`
          : "";
        const bounds = scrollRef.current?.getBoundingClientRect();
        if (bounds) {
          const left = Math.max(
            8,
            Math.min(bounds.width - 184, clientX - bounds.left + 12),
          );
          const top = Math.max(
            8,
            Math.min(bounds.height - 54, clientY - bounds.top + 12),
          );
          refs.host.style.transform = `translate(${left}px, ${top}px)`;
        }
      },
    });
    controllerRef.current = controller;
    controller.setSelection(
      selectionRef.current.frame,
      selectionRef.current.eventIds,
    );
    controller.setPlayhead(playheadRef.current);
    return () => {
      controller.destroy();
      controllerRef.current = null;
    };
  }, [dispatch, entities, model]);

  useLayoutEffect(() => {
    controllerRef.current?.setLayout(timelineWidth, LANE_HEIGHT);
  }, [entities, model, timelineWidth]);

  useLayoutEffect(() => {
    drawState();
  }, [drawState]);

  useEffect(() => {
    controllerRef.current?.setSelection(
      state.selectedFrame,
      state.selectedClusterEventIds,
    );
  }, [state.selectedClusterEventIds, state.selectedFrame]);

  useEffect(() => {
    if (
      state.inspectorOpen
      && state.inspectedEntityId
      && !entities.some((entity) => entity.id === state.inspectedEntityId)
    ) {
      dispatch({ type: "close-inspector" });
    }
  }, [
    dispatch,
    entities,
    state.inspectedEntityId,
    state.inspectorOpen,
  ]);

  useEffect(() => {
    playheadRef.current = state.selectedCombatMs;
    controllerRef.current?.setPlayhead(state.selectedCombatMs);
    drawState();
  }, [drawState, state.selectedCombatMs]);

  useEffect(() => {
    if (!state.selectedEntityId || !labelsRef.current || !scrollRef.current) {
      return;
    }
    const row = Array.from(
      labelsRef.current.querySelectorAll<HTMLElement>(
        "[data-bpp-entity-id]",
      ),
    ).find(
      (candidate) =>
        candidate.dataset.bppEntityId === state.selectedEntityId,
    );
    if (!row) return;
    scrollRef.current.scrollTo({
      top: Math.max(0, row.offsetTop - STATE_BAND_HEIGHT - 80),
      behavior: "smooth",
    });
    row.classList.add("is-jump-target");
    const timeout = window.setTimeout(
      () => row.classList.remove("is-jump-target"),
      1_200,
    );
    return () => window.clearTimeout(timeout);
  }, [state.selectedEntityId]);

  useImperativeHandle(
    forwardedRef,
    () => ({
      setExternalPlayhead(combatMs: number): void {
        playheadRef.current = combatMs;
        controllerRef.current?.setPlayhead(combatMs);
        drawState();
      },
      navigate(direction: -1 | 1): void {
        controllerRef.current?.navigate(direction);
      },
    }),
    [drawState],
  );

  const handleStatePointerMove = (
    event: React.PointerEvent<HTMLCanvasElement>,
  ): void => {
    if (!stateCanvasRef.current) return;
    const combatMs = combatMsAtPointer(
      stateCanvasRef.current,
      event.clientX,
      model.durationMs,
    );
    previewRef.current = combatMs;
    controllerRef.current?.setPreview(combatMs);
    drawState();
    onPreviewCombatMs(combatMs);
  };
  const clearPreview = (): void => {
    previewRef.current = null;
    controllerRef.current?.handlePointerLeave();
    drawState();
    onPreviewCombatMs(null);
  };
  const selectStateTime = (
    event: React.PointerEvent<HTMLCanvasElement>,
  ): void => {
    if (event.button !== 0 || !stateCanvasRef.current) return;
    dispatch({
      type: "select-time",
      combatMs: combatMsAtPointer(
        stateCanvasRef.current,
        event.clientX,
        model.durationMs,
      ),
    });
  };

  return (
    <section
      className="flex min-h-0 flex-1 overflow-hidden border border-border bg-surface shadow-panel"
      data-bpp-test-id="timeline-section"
    >
      <div className="relative min-h-0 min-w-0 flex-1">
          <div
            className="bpp-timeline-scroll"
            data-bpp-test-id="timeline-scroll"
            ref={scrollRef}
          >
            <div
              className="bpp-timeline-grid"
              ref={gridRef}
              style={{
                gridTemplateColumns: `${LANE_LABEL_WIDTH}px ${timelineWidth}px`,
                gridTemplateRows:
                  `${STATE_BAND_HEIGHT}px ${TIME_RULER_HEIGHT}px ${laneCanvasHeight}px`,
                width: LANE_LABEL_WIDTH + timelineWidth,
              }}
            >
              <StateLabels
                combatMs={state.selectedCombatMs}
                dispatch={dispatch}
                model={model}
                scaleMode={state.stateScale}
                t={t}
              />
              <canvas
                aria-label={t("metricsTitle")}
                className="bpp-state-canvas"
                data-bpp-test-id="state-band-canvas"
                onPointerDown={selectStateTime}
                onPointerLeave={clearPreview}
                onPointerMove={handleStatePointerMove}
                ref={stateCanvasRef}
                role="img"
              />
              <div className="bpp-lane-label-header">
                <span className="min-w-0 truncate">{t("entity")}</span>
                <LaneFilterPopover
                  dispatch={dispatch}
                  state={state}
                  t={t}
                />
              </div>
              <canvas
                aria-label={t("time")}
                className="bpp-time-ruler-canvas"
                data-bpp-test-id="timeline-ruler-canvas"
                onPointerDown={(event) =>
                  controllerRef.current?.handleRulerPointerDown(
                    event.nativeEvent,
                  )
                }
                onPointerLeave={clearPreview}
                onPointerMove={(event) => {
                  if (!rulerRef.current) return;
                  const combatMs = combatMsAtPointer(
                    rulerRef.current,
                    event.clientX,
                    model.durationMs,
                  );
                  previewRef.current = combatMs;
                  controllerRef.current?.setPreview(combatMs);
                  drawState();
                  onPreviewCombatMs(combatMs);
                }}
                ref={rulerRef}
                role="img"
              />
              <div
                className="bpp-lane-labels"
                data-bpp-test-id="timeline-lane-labels"
                ref={labelsRef}
              >
                {entities.length === 0
                  ? (
                    <div
                      className="grid place-items-center px-3 text-center text-compact text-muted-foreground"
                      data-bpp-test-id="timeline-lane-filter-empty"
                      style={{ height: LANE_HEIGHT }}
                    >
                      {t("laneFilterEmpty")}
                    </div>
                  )
                  : entities.map((entity, index) => (
                    <div
                      className={cn(
                        "bpp-lane-label",
                        `bpp-side-${entity.side}`,
                        index === sideBoundaryLane && "is-side-boundary",
                      )}
                      data-bpp-entity-id={entity.id}
                      data-bpp-lane-index={index}
                      data-bpp-side-boundary={
                        index === sideBoundaryLane ? "opponent" : undefined
                      }
                      data-bpp-test-id={`timeline-lane-${index}`}
                      key={entity.id}
                      style={{ height: LANE_HEIGHT }}
                    >
                      <span
                        className="bpp-lane-art-slot"
                        data-bpp-test-id={`timeline-lane-art-slot-${index}`}
                      >
                        <EntityArt
                          entity={entity}
                          testId={`timeline-lane-icon-${index}`}
                        />
                      </span>
                      <span className="bpp-lane-copy">
                        <strong className="block truncate text-body text-foreground">
                          {entity.name}
                        </strong>
                        <small className="block truncate text-micro text-muted-foreground">
                          {entityTypeLabel(entity.type, t)}
                        </small>
                      </span>
                      <span
                        className={cn(
                          "ml-auto h-7 w-0.5 rounded-full",
                          entity.side.toLowerCase().includes("opponent")
                            ? "bg-opponent/80"
                            : entity.side.toLowerCase().includes("player")
                              ? "bg-player/85"
                              : "bg-faint/70",
                        )}
                      />
                    </div>
                  ))}
              </div>
              <canvas
                aria-label={t("timelineTitle")}
                className="bpp-event-canvas"
                data-bpp-side-boundary-lane={sideBoundaryLane ?? ""}
                data-bpp-test-id="timeline-canvas"
                onPointerDown={(event) =>
                  controllerRef.current?.handlePointerDown(event.nativeEvent)
                }
                onPointerLeave={clearPreview}
                onPointerMove={(event) =>
                  controllerRef.current?.handlePointerMove(event.nativeEvent)
                }
                ref={eventCanvasRef}
                role="img"
              />
            </div>
          </div>
          <div
            className="pointer-events-none absolute left-0 top-0 z-50 min-w-40 rounded-panel border border-brand-soft/25 bg-popover/98 px-2.5 py-2 text-micro shadow-float"
            data-bpp-test-id="timeline-tooltip"
            hidden
            ref={tooltipHostRef}
          >
            <div className="flex items-center gap-2">
              <strong
                className="text-foreground"
                data-bpp-test-id="timeline-tooltip-label"
                ref={tooltipLabelRef}
              />
              <span
                data-bpp-test-id="timeline-tooltip-time"
                className="ml-auto font-mono text-brand-soft"
                ref={tooltipTimeRef}
              />
            </div>
            <span
              className="mt-0.5 block text-muted-foreground"
              data-bpp-test-id="timeline-tooltip-count"
              ref={tooltipCountRef}
            />
          </div>
      </div>
      {state.inspectorOpen && (
        <FrameInspector
          entityId={state.inspectedEntityId}
          frame={state.selectedFrame}
          focusedEventIds={state.selectedClusterEventIds}
          model={model}
          onClose={() => dispatch({ type: "close-inspector" })}
          t={t}
        />
      )}
    </section>
  );
});
