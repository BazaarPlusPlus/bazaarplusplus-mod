import { useEffect, useRef, type RefObject } from "react";
import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  normalizeSide,
  type NormalizedEntity,
} from "../../model/normalize.ts";
import { cn } from "../../lib/utils.ts";
import {
  LANE_HEIGHT,
  LANE_LABEL_WIDTH,
  STATE_BAND_HEIGHT,
  TIME_RULER_HEIGHT,
} from "../../timeline/constants.ts";
import { TimelineCanvasController } from "../../timeline/event-renderer.ts";
import { combatMsAtPointer } from "../../timeline/geometry.ts";
import { LaneFilterPopover } from "./LaneFilterPopover.tsx";
import {
  LaneLabelContents,
  StateLabels,
} from "./TimelineLabels.tsx";

export interface TimelineViewportRefs {
  scroll: RefObject<HTMLDivElement | null>;
  grid: RefObject<HTMLDivElement | null>;
  labels: RefObject<HTMLDivElement | null>;
  stateCanvas: RefObject<HTMLCanvasElement | null>;
  ruler: RefObject<HTMLCanvasElement | null>;
  eventCanvas: RefObject<HTMLCanvasElement | null>;
  stickyHeroCanvas: RefObject<HTMLCanvasElement | null>;
  stickyHeroLabel: RefObject<HTMLDivElement | null>;
  tooltipHost: RefObject<HTMLDivElement | null>;
  tooltipTime: RefObject<HTMLSpanElement | null>;
  tooltipLabel: RefObject<HTMLElement | null>;
  tooltipCount: RefObject<HTMLSpanElement | null>;
  controller: RefObject<TimelineCanvasController | null>;
  preview: RefObject<number | null>;
}

export interface TimelineTooltipRefs {
  host: HTMLDivElement;
  time: HTMLSpanElement;
  label: HTMLElement;
  count: HTMLSpanElement;
}

export function useTimelineViewportRefs(): TimelineViewportRefs {
  return {
    scroll: useRef<HTMLDivElement>(null),
    grid: useRef<HTMLDivElement>(null),
    labels: useRef<HTMLDivElement>(null),
    stateCanvas: useRef<HTMLCanvasElement>(null),
    ruler: useRef<HTMLCanvasElement>(null),
    eventCanvas: useRef<HTMLCanvasElement>(null),
    stickyHeroCanvas: useRef<HTMLCanvasElement>(null),
    stickyHeroLabel: useRef<HTMLDivElement>(null),
    tooltipHost: useRef<HTMLDivElement>(null),
    tooltipTime: useRef<HTMLSpanElement>(null),
    tooltipLabel: useRef<HTMLElement>(null),
    tooltipCount: useRef<HTMLSpanElement>(null),
    controller: useRef<TimelineCanvasController>(null),
    preview: useRef<number | null>(null),
  };
}

export function TimelineViewport({
  model,
  state,
  dispatch,
  onPreviewCombatMs,
  t,
  refs,
  entities,
  timelineWidth,
  laneCanvasHeight,
  sideBoundaryLane,
  pinnedHeroLane,
  pinnedHero,
  drawState,
  clearPreview,
}: {
  model: ReportViewModel;
  state: ReportState;
  dispatch: React.Dispatch<ReportAction>;
  onPreviewCombatMs: (combatMs: number | null) => void;
  t: (key: string) => string;
  refs: TimelineViewportRefs;
  entities: NormalizedEntity[];
  timelineWidth: number;
  laneCanvasHeight: number;
  sideBoundaryLane: number | null;
  pinnedHeroLane: number | null;
  pinnedHero: NormalizedEntity | null;
  drawState: () => void;
  clearPreview: () => void;
}): React.JSX.Element {
  const pinnedHeroSide = pinnedHero
    ? normalizeSide(pinnedHero.side)
    : "neutral";

  useEffect(() => {
    const scroll = refs.scroll.current;
    if (!scroll) return;

    const handleWheel = (event: WheelEvent): void => {
      if (!event.shiftKey) return;
      const delta =
        Math.abs(event.deltaX) > Math.abs(event.deltaY)
          ? event.deltaX
          : event.deltaY;
      if (!Number.isFinite(delta) || delta === 0) return;
      event.preventDefault();
      const maximum = Math.max(
        0,
        scroll.scrollWidth - scroll.clientWidth,
      );
      const next = Math.max(
        0,
        Math.min(maximum, scroll.scrollLeft + delta),
      );
      if (Math.abs(next - scroll.scrollLeft) < 0.5) return;
      scroll.scrollLeft = next;
    };

    scroll.addEventListener("wheel", handleWheel, { passive: false });
    return () => scroll.removeEventListener("wheel", handleWheel);
  }, [refs.scroll]);

  const previewAtPointer = (
    canvas: HTMLCanvasElement,
    clientX: number,
  ): void => {
    if (state.inspectorOpen) return;
    const combatMs = combatMsAtPointer(
      canvas,
      clientX,
      model.durationMs,
    );
    refs.preview.current = combatMs;
    refs.controller.current?.setPreview(combatMs);
    drawState();
    onPreviewCombatMs(combatMs);
  };

  return (
    <div className="relative min-h-0 min-w-0 flex-1">
      <div
        className="bpp-timeline-scroll"
        data-bpp-test-id="timeline-scroll"
        ref={refs.scroll}
      >
        <div
          className="bpp-timeline-grid"
          ref={refs.grid}
          style={{
            "--bpp-lane-height": `${LANE_HEIGHT}px`,
            "--bpp-state-band-height": `${STATE_BAND_HEIGHT}px`,
            "--bpp-time-ruler-height": `${TIME_RULER_HEIGHT}px`,
            gridTemplateColumns: `${LANE_LABEL_WIDTH}px ${timelineWidth}px`,
            gridTemplateRows:
              `${STATE_BAND_HEIGHT}px ${TIME_RULER_HEIGHT}px ${laneCanvasHeight}px`,
            width: LANE_LABEL_WIDTH + timelineWidth,
          } as React.CSSProperties}
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
            onPointerDown={(event) => {
              if (event.button !== 0 || !refs.stateCanvas.current) return;
              dispatch({
                type: "select-time",
                combatMs: combatMsAtPointer(
                  refs.stateCanvas.current,
                  event.clientX,
                  model.durationMs,
                ),
              });
            }}
            onPointerLeave={clearPreview}
            onPointerMove={(event) => {
              if (state.inspectorOpen || !refs.stateCanvas.current) return;
              previewAtPointer(refs.stateCanvas.current, event.clientX);
            }}
            ref={refs.stateCanvas}
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
              refs.controller.current?.handleRulerPointerDown(
                event.nativeEvent,
              )
            }
            onPointerLeave={clearPreview}
            onPointerMove={(event) => {
              if (state.inspectorOpen || !refs.ruler.current) return;
              previewAtPointer(refs.ruler.current, event.clientX);
            }}
            ref={refs.ruler}
            role="img"
          />
          <div
            className="bpp-lane-labels"
            data-bpp-test-id="timeline-lane-labels"
            ref={refs.labels}
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
                  {index !== pinnedHeroLane && (
                    <LaneLabelContents entity={entity} index={index} t={t} />
                  )}
                </div>
              ))}
          </div>
          <canvas
            aria-label={t("timelineTitle")}
            className="bpp-event-canvas"
            data-bpp-side-boundary-lane={sideBoundaryLane ?? ""}
            data-bpp-test-id="timeline-canvas"
            onPointerDown={(event) =>
              refs.controller.current?.handlePointerDown(event.nativeEvent)
            }
            onPointerLeave={clearPreview}
            onPointerMove={(event) => {
              if (state.inspectorOpen) return;
              refs.controller.current?.handlePointerMove(event.nativeEvent);
            }}
            ref={refs.eventCanvas}
            role="img"
          />
          <div
            className={cn(
              "bpp-lane-label bpp-sticky-hero-label",
              pinnedHero && `bpp-side-${pinnedHeroSide}`,
              pinnedHeroLane !== null
                && pinnedHeroLane === sideBoundaryLane
                && "is-side-boundary",
            )}
            aria-hidden="true"
            data-bpp-side-boundary={
              pinnedHeroLane !== null && pinnedHeroLane === sideBoundaryLane
                ? "opponent"
                : undefined
            }
            data-bpp-sticky-hero-lane={pinnedHeroLane ?? ""}
            data-bpp-sticky-hero-entity-id={pinnedHero?.id ?? ""}
            data-bpp-test-id="timeline-sticky-hero-label"
            hidden={!pinnedHero}
            ref={refs.stickyHeroLabel}
            style={{ height: LANE_HEIGHT }}
          >
            {pinnedHero && (
              <LaneLabelContents
                entity={pinnedHero}
                index={pinnedHeroLane ?? 0}
                sticky
                t={t}
              />
            )}
          </div>
          <canvas
            aria-hidden="true"
            className={cn(
              "bpp-sticky-hero-canvas",
              pinnedHero && `bpp-side-${pinnedHeroSide}`,
            )}
            data-bpp-sticky-side={pinnedHero ? pinnedHeroSide : ""}
            data-bpp-sticky-hero-lane={pinnedHeroLane ?? ""}
            data-bpp-test-id="timeline-sticky-hero-events"
            hidden={!pinnedHero}
            onPointerDown={(event) => {
              if (pinnedHeroLane === null) return;
              refs.controller.current?.handleStickyHeroPointerDown(
                event.nativeEvent,
                event.currentTarget,
                pinnedHeroLane,
              );
            }}
            onPointerLeave={clearPreview}
            onPointerMove={(event) => {
              if (state.inspectorOpen || pinnedHeroLane === null) return;
              refs.controller.current?.handleStickyHeroPointerMove(
                event.nativeEvent,
                event.currentTarget,
                pinnedHeroLane,
              );
            }}
            ref={refs.stickyHeroCanvas}
          />
        </div>
      </div>
      <div
        className="pointer-events-none absolute left-0 top-0 z-50 min-w-40 rounded-panel border border-brand-soft/25 bg-popover/98 px-2.5 py-2 text-micro shadow-float"
        data-bpp-test-id="timeline-tooltip"
        hidden
        ref={refs.tooltipHost}
      >
        <div className="flex items-center gap-2">
          <strong
            className="text-foreground"
            data-bpp-test-id="timeline-tooltip-label"
            ref={refs.tooltipLabel}
          />
          <span
            data-bpp-test-id="timeline-tooltip-time"
            className="ml-auto font-mono text-brand-soft"
            ref={refs.tooltipTime}
          />
        </div>
        <span
          className="mt-0.5 block text-muted-foreground"
          data-bpp-test-id="timeline-tooltip-count"
          ref={refs.tooltipCount}
        />
      </div>
    </div>
  );
}
