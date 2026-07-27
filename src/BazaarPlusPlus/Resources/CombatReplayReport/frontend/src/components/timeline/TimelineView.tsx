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
import type { ReportAction } from "../../app/report-reducer.ts";
import { TIME_ZOOM_STEPS, type ReportState } from "../../app/report-state.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  LANE_HEIGHT,
  LANE_LABEL_WIDTH,
  MIN_TIMELINE_WIDTH,
  STATE_BAND_HEIGHT,
  timelineWidthAtZoom,
} from "../../timeline/constants.ts";
import { timelineXAtCombatMs } from "../../timeline/geometry.ts";
import { TimelineCanvasController } from "../../timeline/event-renderer.ts";
import {
  drawStateBand,
  groupMetricSamples,
  metricValueAt,
} from "../../timeline/state-band-renderer.ts";
import {
  METRIC_ORDER,
  type StateMetric,
} from "../../timeline/state-scale.ts";
import {
  formatCompactNumber,
  formatDuration,
} from "../../i18n/format.ts";
import { FrameInspector } from "../inspector/FrameInspector.tsx";
import {
  Popover,
  PopoverAnchor,
  PopoverContent,
} from "../ui/popover.tsx";
import { timelineClusterEventIds } from "../../timeline/clusters.ts";
import {
  filterTimelineEntities,
  opponentBoundaryLane,
} from "../../timeline/lane-filter.ts";
import { heroLaneAtScroll } from "./TimelineLabels.tsx";
import {
  TimelineViewport,
  type TimelineTooltipRefs,
  useTimelineViewportRefs,
} from "./TimelineViewport.tsx";

export interface TimelineHandle {
  setExternalPlayhead: (combatMs: number) => void;
  focusCombatEntry: (combatMs: number, entityId: string) => void;
  navigate: (direction: -1 | 1) => void;
}

interface TimelineFocusRequest {
  combatMs: number;
  entityId: string;
  nonce: number;
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
  const viewportRefs = useTimelineViewportRefs();
  const playheadRef = useRef(state.selectedCombatMs);
  const selectionRef = useRef({
    frame: state.selectedFrame,
    eventIds: state.selectedClusterEventIds,
  });
  selectionRef.current = {
    frame: state.selectedFrame,
    eventIds: state.selectedClusterEventIds,
  };
  const [baseWidth, setBaseWidth] = useState(MIN_TIMELINE_WIDTH);
  const focusNonceRef = useRef(0);
  const [focusRequest, setFocusRequest] =
    useState<TimelineFocusRequest | null>(null);
  const entities = useMemo(
    () => filterTimelineEntities(model.entities, state.laneVisibility),
    [model.entities, state.laneVisibility],
  );
  const heroLaneIndexes = useMemo(
    () =>
      entities.flatMap((entity, index) =>
        entity.type.toLowerCase() === "hero" ? [index] : []
      ),
    [entities],
  );
  const [pinnedHeroLane, setPinnedHeroLane] = useState<number | null>(
    null,
  );
  const pinnedHero =
    pinnedHeroLane === null ? null : entities[pinnedHeroLane] ?? null;
  const sideBoundaryLane = useMemo(
    () => opponentBoundaryLane(entities),
    [entities],
  );
  const groupedMetrics = useMemo(() => groupMetricSamples(model), [model]);
  const [visibleMetrics, setVisibleMetrics] = useState<
    ReadonlySet<StateMetric>
  >(() => new Set(METRIC_ORDER));
  const visibleMetricsRef = useRef(visibleMetrics);
  const highlightedMetricRef = useRef<StateMetric | null>(null);
  const timelineWidth = timelineWidthAtZoom(
    baseWidth,
    TIME_ZOOM_STEPS[state.timeZoomIndex],
  );
  const laneCanvasHeight = Math.max(
    LANE_HEIGHT,
    entities.length * LANE_HEIGHT,
  );

  const updateStateLabels = useCallback((combatMs: number): void => {
    const root = viewportRefs.stateLabels.current;
    if (!root) return;
    root.dataset.bppCombatMs = String(combatMs);
    const time = root.querySelector<HTMLElement>("[data-bpp-state-time]");
    if (time) time.textContent = formatDuration(combatMs);
    for (const value of root.querySelectorAll<HTMLElement>(
      "[data-bpp-state-side][data-bpp-state-metric]",
    )) {
      const metric = value.dataset.bppStateMetric as StateMetric | undefined;
      const side = value.dataset.bppStateSide;
      if (
        !metric
        || !METRIC_ORDER.includes(metric)
        || (side !== "player" && side !== "opponent")
      ) {
        continue;
      }
      const current = metricValueAt(
        groupedMetrics.get(`${side}:${metric}`),
        combatMs,
      );
      value.textContent =
        current === null ? "—" : formatCompactNumber(current);
    }
  }, [groupedMetrics]);

  const drawState = useCallback(() => {
    updateStateLabels(
      viewportRefs.preview.current ?? playheadRef.current,
    );
    if (!viewportRefs.stateCanvas.current) return;
    drawStateBand({
      canvas: viewportRefs.stateCanvas.current,
      model,
      grouped: groupedMetrics,
      width: timelineWidth,
      height: STATE_BAND_HEIGHT,
      scaleMode: state.stateScale,
      playheadMs: playheadRef.current,
      previewMs: viewportRefs.preview.current,
      visibleMetrics,
      highlightedMetric: highlightedMetricRef.current,
    });
  }, [
    groupedMetrics,
    model,
    state.stateScale,
    timelineWidth,
    updateStateLabels,
    visibleMetrics,
  ]);
  const drawStateRef = useRef(drawState);
  const previewCallbackRef = useRef(onPreviewCombatMs);
  const translateRef = useRef(t);
  drawStateRef.current = drawState;
  visibleMetricsRef.current = visibleMetrics;
  previewCallbackRef.current = onPreviewCombatMs;
  translateRef.current = t;

  const handleMetricHighlight = useCallback(
    (metric: StateMetric | null): void => {
      if (highlightedMetricRef.current === metric) return;
      highlightedMetricRef.current = metric;
      drawStateRef.current();
    },
    [],
  );
  const handleMetricToggle = useCallback((metric: StateMetric): void => {
    setVisibleMetrics((current) => {
      const next = new Set(current);
      if (next.has(metric)) {
        next.delete(metric);
      } else {
        next.add(metric);
      }
      return next;
    });
  }, []);

  useLayoutEffect(() => {
    const scroll = viewportRefs.scroll.current;
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
    const scroll = viewportRefs.scroll.current;
    if (!scroll) return;
    let frameHandle = 0;
    const update = (): void => {
      frameHandle = 0;
      const activeLane = heroLaneAtScroll(
        heroLaneIndexes,
        scroll.scrollTop,
      );
      viewportRefs.controller.current?.setPinnedHeroLane(activeLane);
      setPinnedHeroLane((current) =>
        current === activeLane ? current : activeLane
      );
    };
    const handleScroll = (): void => {
      if (frameHandle) return;
      frameHandle = requestAnimationFrame(update);
    };
    update();
    scroll.addEventListener("scroll", handleScroll, { passive: true });
    return () => {
      scroll.removeEventListener("scroll", handleScroll);
      if (frameHandle) cancelAnimationFrame(frameHandle);
    };
  }, [heroLaneIndexes]);

  useLayoutEffect(() => {
    const canvas = viewportRefs.eventCanvas.current;
    const ruler = viewportRefs.ruler.current;
    const labels = viewportRefs.labels.current;
    const stickyHeroCanvas = viewportRefs.stickyHeroCanvas.current;
    const stickyHeroLabel = viewportRefs.stickyHeroLabel.current;
    if (
      !canvas
      || !ruler
      || !labels
      || !stickyHeroCanvas
      || !stickyHeroLabel
    ) {
      return;
    }
    const tooltip = (): TimelineTooltipRefs | null => {
      if (
        !viewportRefs.tooltipHost.current
        || !viewportRefs.tooltipTime.current
        || !viewportRefs.tooltipLabel.current
        || !viewportRefs.tooltipCount.current
      ) {
        return null;
      }
      return {
        host: viewportRefs.tooltipHost.current,
        time: viewportRefs.tooltipTime.current,
        label: viewportRefs.tooltipLabel.current,
        count: viewportRefs.tooltipCount.current,
      };
    };
    const controller = new TimelineCanvasController({
      canvas,
      ruler,
      model,
      entities,
      laneLabels: labels,
      stickyHeroCanvas,
      stickyHeroLabel,
      onSelect: (cluster, combatMs, clientX, clientY) => {
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
            anchor: clientX === undefined || clientY === undefined
              ? undefined
              : { x: clientX, y: clientY },
          });
        } else {
          dispatch({ type: "select-time", combatMs });
        }
      },
      onPreview: (cluster, combatMs, clientX, clientY) => {
        viewportRefs.preview.current = combatMs;
        drawStateRef.current();
        previewCallbackRef.current(combatMs);
        const refs = tooltip();
        if (!refs) return;
        refs.host.dataset.bppCombatMs =
          combatMs === null ? "" : String(combatMs);
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
          ? `${cluster.events.length} ${translateRef.current(
            cluster.events.length === 1 ? "eventSingular" : "event",
          )}`
          : "";
        const bounds = viewportRefs.scroll.current?.getBoundingClientRect();
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
    viewportRefs.controller.current = controller;
    controller.setPinnedHeroLane(
      heroLaneAtScroll(
        heroLaneIndexes,
        viewportRefs.scroll.current?.scrollTop ?? 0,
      ),
    );
    controller.setHeroHealthVisible(
      visibleMetricsRef.current.has("health"),
    );
    controller.setSelection(
      selectionRef.current.frame,
      selectionRef.current.eventIds,
    );
    controller.setPlayhead(playheadRef.current);
    return () => {
      controller.destroy();
      viewportRefs.controller.current = null;
    };
  }, [dispatch, entities, heroLaneIndexes, model]);

  useLayoutEffect(() => {
    viewportRefs.controller.current?.setLayout(timelineWidth, LANE_HEIGHT);
  }, [entities, model, timelineWidth]);

  useLayoutEffect(() => {
    viewportRefs.controller.current?.setPinnedHeroLane(pinnedHeroLane);
  }, [pinnedHeroLane]);

  useLayoutEffect(() => {
    viewportRefs.controller.current?.setHeroHealthVisible(
      visibleMetrics.has("health"),
    );
  }, [visibleMetrics]);

  useLayoutEffect(() => {
    drawState();
  }, [drawState]);

  useEffect(() => {
    viewportRefs.controller.current?.setSelection(
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
    viewportRefs.controller.current?.setPlayhead(state.selectedCombatMs);
    drawState();
  }, [drawState, state.selectedCombatMs]);

  useEffect(() => {
    if (
      !state.selectedEntityId
      || !viewportRefs.labels.current
      || !viewportRefs.scroll.current
    ) {
      return;
    }
    const row = Array.from(
      viewportRefs.labels.current.querySelectorAll<HTMLElement>(
        "[data-bpp-entity-id]",
      ),
    ).find(
      (candidate) =>
        candidate.dataset.bppEntityId === state.selectedEntityId,
    );
    if (!row) return;
    const scroll = viewportRefs.scroll.current;
    scroll.scrollTo({
      left: scroll.scrollLeft,
      top: Math.max(0, row.offsetTop - LANE_HEIGHT),
      behavior: "smooth",
    });
    row.classList.add("is-jump-target");
    const timeout = window.setTimeout(
      () => row.classList.remove("is-jump-target"),
      1_200,
    );
    return () => window.clearTimeout(timeout);
  }, [entities, state.selectedEntityId]);

  useLayoutEffect(() => {
    const scroll = viewportRefs.scroll.current;
    if (!scroll || !focusRequest) return;

    const row = Array.from(
      viewportRefs.labels.current?.querySelectorAll<HTMLElement>(
        "[data-bpp-entity-id]",
      ) ?? [],
    ).find(
      (candidate) =>
        candidate.dataset.bppEntityId === focusRequest.entityId,
    );
    const eventViewportWidth = Math.max(
      1,
      scroll.clientWidth - LANE_LABEL_WIDTH,
    );
    const eventX = timelineXAtCombatMs(
      focusRequest.combatMs,
      model.durationMs,
      timelineWidth,
    );
    const maxLeft = Math.max(0, scroll.scrollWidth - scroll.clientWidth);
    scroll.scrollTo({
      left: Math.min(
        maxLeft,
        Math.max(0, eventX - eventViewportWidth * 0.45),
      ),
      top: row
        ? Math.max(0, row.offsetTop - LANE_HEIGHT)
        : scroll.scrollTop,
      behavior: "auto",
    });

    if (!row) return;
    row.classList.add("is-jump-target");
    const timeout = window.setTimeout(
      () => row.classList.remove("is-jump-target"),
      1_200,
    );
    return () => {
      window.clearTimeout(timeout);
      row.classList.remove("is-jump-target");
    };
  }, [
    entities,
    focusRequest,
    model.durationMs,
    timelineWidth,
  ]);

  useImperativeHandle(
    forwardedRef,
    () => ({
      setExternalPlayhead(combatMs: number): void {
        playheadRef.current = combatMs;
        viewportRefs.controller.current?.setPlayhead(combatMs);
        drawState();
      },
      focusCombatEntry(combatMs: number, entityId: string): void {
        focusNonceRef.current += 1;
        setFocusRequest({
          combatMs,
          entityId,
          nonce: focusNonceRef.current,
        });
      },
      navigate(direction: -1 | 1): void {
        viewportRefs.controller.current?.navigate(direction);
      },
    }),
    [drawState, model.durationMs, timelineWidth],
  );

  const clearPreview = (): void => {
    viewportRefs.preview.current = null;
    viewportRefs.controller.current?.handlePointerLeave();
    drawState();
    onPreviewCombatMs(null);
  };

  useEffect(() => {
    if (!state.inspectorOpen) return;
    clearPreview();
  }, [state.inspectorOpen]);

  return (
    <section
      className="flex min-h-0 flex-1 overflow-hidden border border-border bg-surface shadow-panel"
      data-bpp-test-id="timeline-section"
    >
      <TimelineViewport
        clearPreview={clearPreview}
        dispatch={dispatch}
        drawState={drawState}
        entities={entities}
        laneCanvasHeight={laneCanvasHeight}
        model={model}
        onMetricHighlight={handleMetricHighlight}
        onMetricToggle={handleMetricToggle}
        onPreviewCombatMs={onPreviewCombatMs}
        pinnedHero={pinnedHero}
        pinnedHeroLane={pinnedHeroLane}
        refs={viewportRefs}
        sideBoundaryLane={sideBoundaryLane}
        state={state}
        stateLabelCombatMs={
          viewportRefs.preview.current ?? playheadRef.current
        }
        t={t}
        timelineWidth={timelineWidth}
        visibleMetrics={visibleMetrics}
      />
      <Popover
        onOpenChange={(open) => {
          if (!open) dispatch({ type: "close-inspector" });
        }}
        open={state.inspectorOpen}
      >
        <PopoverAnchor asChild>
          <span
            aria-hidden="true"
            className="pointer-events-none fixed size-px"
            data-bpp-test-id="frame-inspector-anchor"
            style={{
              left:
                state.inspectorAnchor?.x
                ?? (typeof window === "undefined" ? 0 : window.innerWidth / 2),
              top:
                state.inspectorAnchor?.y
                ?? (typeof window === "undefined" ? 0 : window.innerHeight / 2),
            }}
          />
        </PopoverAnchor>
        <PopoverContent
          align="start"
          className="w-[min(360px,calc(100vw-1rem))] overflow-hidden p-0"
          collisionPadding={8}
          data-bpp-test-id="frame-inspector-popover"
          onOpenAutoFocus={(event) => event.preventDefault()}
          onInteractOutside={(event) => {
            const target = event.target;
            if (
              target instanceof Element
              && target.closest(
                [
                  '[data-bpp-test-id="timeline-canvas"]',
                  '[data-bpp-test-id="timeline-sticky-hero-events"]',
                  '[data-bpp-test-id="combat-log-entry"]',
                  '[data-bpp-test-id="combat-log-entry-detail"]',
                ].join(","),
              )
            ) {
              event.preventDefault();
            }
          }}
          side="right"
          sideOffset={10}
        >
          <FrameInspector
            entityId={state.inspectedEntityId}
            frame={state.selectedFrame}
            focusedEventIds={state.selectedClusterEventIds}
            model={model}
            onClose={() => dispatch({ type: "close-inspector" })}
            t={t}
          />
        </PopoverContent>
      </Popover>
    </section>
  );
});
