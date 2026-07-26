import { useVirtualizer } from "@tanstack/react-virtual";
import { ChevronRight, Pause, Play, RotateCcw } from "lucide-react";
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  formatCompactNumber,
  formatDuration,
  formatMilliseconds,
} from "../../i18n/format.ts";
import type { NormalizedEntity } from "../../model/normalize.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  buildCombatLogEntries,
  combatLogEntity,
  groupCombatLogEntries,
  isNarrativeCombatLogEntry,
  nearestCombatLogEntryIndex,
  selectedCombatLogEntryIndex,
  type CombatLogEntry,
} from "../../combat-log/entries.ts";
import { cn } from "../../lib/utils.ts";
import { isVisibleTimelineEvent } from "../../timeline/clusters.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { SemanticIcon } from "../semantic/SemanticIcon.tsx";
import { Button } from "../ui/button.tsx";

export interface CombatLogHandle {
  setPlaybackActive: (active: boolean) => void;
  setPlaybackCombatMs: (combatMs: number) => void;
  setPreviewCombatMs: (combatMs: number | null) => void;
}

function entryAmount(entry: CombatLogEntry): string {
  if (
    entry.value === null
    || entry.value === undefined
    || entry.value === ""
  ) {
    return "";
  }
  const unit = entry.unit.toLowerCase();
  return unit === "ms" || unit.includes("millisecond")
    ? formatMilliseconds(entry.value)
    : formatCompactNumber(entry.value);
}

function EntityChip({
  entity,
}: {
  entity: NormalizedEntity;
}): React.JSX.Element {
  return (
    <span className="inline-flex min-w-0 items-center gap-1.5">
      <EntityArt entity={entity} size="compact" />
      <span className="truncate" title={entity.name}>{entity.name}</span>
    </span>
  );
}

function MissingEntity({
  label,
}: {
  label: string;
}): React.JSX.Element {
  return (
    <span
      aria-label={label}
      className="truncate text-muted-foreground/55"
      title={label}
    >
      —
    </span>
  );
}

export const CombatLogList = forwardRef<
  CombatLogHandle,
  {
    initialPlaybackActive: boolean;
    initialPlaybackCombatMs: number;
    model: ReportViewModel;
    pinnedCombatMs: number;
    pinnedEventIds: readonly string[];
    onSelectEntry: (entry: CombatLogEntry) => void;
    t: (key: string) => string;
  }
>(function CombatLogList(
  {
    initialPlaybackActive,
    initialPlaybackCombatMs,
    model,
    pinnedCombatMs,
    pinnedEventIds,
    onSelectEntry,
    t,
  },
  forwardedRef,
): React.JSX.Element {
  const entityById = useMemo(
    () => new Map(model.entities.map((entity) => [entity.id, entity])),
    [model.entities],
  );
  const rawEntries = useMemo(
    () =>
      buildCombatLogEntries(
        model.events.filter((event) =>
          isVisibleTimelineEvent(event, entityById)
        ),
      ).filter(isNarrativeCombatLogEntry),
    [entityById, model.events],
  );
  const entries = useMemo(
    () => groupCombatLogEntries(rawEntries),
    [rawEntries],
  );
  const viewportRef = useRef<HTMLDivElement>(null);
  const previewMsRef = useRef<number | null>(null);
  const playbackMsRef = useRef(initialPlaybackCombatMs);
  const playbackActiveRef = useRef(initialPlaybackActive);
  const pinnedMsRef = useRef(pinnedCombatMs);
  const pinnedEventIdsRef = useRef(pinnedEventIds);
  const scheduledRef = useRef(0);
  const programmaticScrollRef = useRef(false);
  const [activeIndex, setActiveIndex] = useState(() =>
    selectedCombatLogEntryIndex(entries, pinnedEventIds, pinnedCombatMs)
  );
  const [manualPaused, setManualPaused] = useState(false);
  const [followSource, setFollowSource] = useState<
    "hover" | "playback" | "pinned"
  >(initialPlaybackActive ? "playback" : "pinned");

  const virtualizer = useVirtualizer({
    count: entries.length,
    estimateSize: () => 36,
    getScrollElement: () => viewportRef.current,
    overscan: 8,
  });

  const updateActive = (): void => {
    scheduledRef.current = 0;
    const source =
      previewMsRef.current !== null
        ? "hover"
        : playbackActiveRef.current
          ? "playback"
          : "pinned";
    const combatMs =
      source === "hover"
        ? previewMsRef.current ?? pinnedMsRef.current
        : source === "playback"
          ? playbackMsRef.current
          : pinnedMsRef.current;
    setFollowSource(source);
    setActiveIndex(
      source === "pinned"
        ? selectedCombatLogEntryIndex(
          entries,
          pinnedEventIdsRef.current,
          combatMs,
        )
        : nearestCombatLogEntryIndex(entries, combatMs),
    );
  };

  const scheduleActive = (): void => {
    if (scheduledRef.current) return;
    scheduledRef.current = window.requestAnimationFrame(updateActive);
  };

  useImperativeHandle(
    forwardedRef,
    () => ({
      setPlaybackActive(active: boolean): void {
        playbackActiveRef.current = active;
        scheduleActive();
      },
      setPlaybackCombatMs(combatMs: number): void {
        playbackMsRef.current = combatMs;
        scheduleActive();
      },
      setPreviewCombatMs(combatMs: number | null): void {
        previewMsRef.current = combatMs;
        scheduleActive();
      },
    }),
  );

  useEffect(
    () => () => window.cancelAnimationFrame(scheduledRef.current),
    [],
  );

  useEffect(() => {
    pinnedMsRef.current = pinnedCombatMs;
    pinnedEventIdsRef.current = pinnedEventIds;
    scheduleActive();
  }, [pinnedCombatMs, pinnedEventIds]);

  useEffect(() => {
    if (manualPaused || activeIndex < 0) return;
    programmaticScrollRef.current = true;
    virtualizer.scrollToIndex(activeIndex, {
      align: "center",
      behavior: "auto",
    });
    const frame = window.requestAnimationFrame(() => {
      programmaticScrollRef.current = false;
    });
    return () => window.cancelAnimationFrame(frame);
  }, [activeIndex, manualPaused, virtualizer]);

  const followLabel =
    followSource === "hover"
      ? t("combatLogFollowingHover")
      : followSource === "playback"
        ? t("combatLogFollowingPlayback")
        : t("combatLogFollowingPinned");

  return (
    <section
      className="flex min-h-0 min-w-0 flex-1 flex-col"
      data-bpp-follow-source={manualPaused ? "paused" : followSource}
      data-bpp-row-count={entries.length}
      data-bpp-total-count={rawEntries.length}
      data-bpp-test-id="combat-log"
    >
      <header className="flex h-control-sm shrink-0 items-center gap-2 border-b border-border/60 bg-surface-raised/70 px-3">
        <strong className="text-compact">{t("combatLogTitle")}</strong>
        {manualPaused
          ? (
          <Button
            className="ml-auto"
            data-bpp-test-id="combat-log-resume"
            onClick={() => setManualPaused(false)}
            size="xs"
            type="button"
            variant="ghost"
          >
            <RotateCcw className="size-icon-sm" />
            {t("combatLogResume")}
          </Button>
          )
          : (
            <span className="ml-auto inline-flex items-center gap-1.5 text-micro text-muted-foreground">
              {followSource === "playback"
                ? <Play className="size-icon-sm" />
                : <Pause className="size-icon-sm opacity-60" />}
              <span className="max-[600px]:sr-only">{followLabel}</span>
            </span>
          )}
      </header>
      <div
        className="min-h-0 flex-1 overflow-y-auto overscroll-contain"
        data-bpp-test-id="combat-log-viewport"
        onFocusCapture={() => {
          setManualPaused(true);
        }}
        onKeyDown={(event) => {
          if (["ArrowDown", "ArrowUp", "PageDown", "PageUp"].includes(
            event.key,
          )) {
            setManualPaused(true);
          }
        }}
        onPointerDown={() => {
          if (!programmaticScrollRef.current) setManualPaused(true);
        }}
        onWheel={() => {
          if (!programmaticScrollRef.current) setManualPaused(true);
        }}
        ref={viewportRef}
        tabIndex={0}
      >
        {entries.length === 0 ? (
          <div className="grid h-full place-items-center px-4 text-center text-compact text-muted-foreground">
            {t("combatLogEmpty")}
          </div>
        ) : (
          <div
            className="relative w-full"
            style={{ height: virtualizer.getTotalSize() }}
          >
            {virtualizer.getVirtualItems().map((virtualRow) => {
              const entry = entries[virtualRow.index];
              const source =
                combatLogEntity(entityById, entry.sourceId)
                ?? combatLogEntity(entityById, entry.triggerSourceId);
              const targets = entry.targetIds
                .map((id) => combatLogEntity(entityById, id))
                .filter((entity): entity is NormalizedEntity => entity !== null);
              const amount = entryAmount(entry);
              const active = virtualRow.index === activeIndex;
              const frameStart =
                virtualRow.index === 0
                || entries[virtualRow.index - 1]?.frame !== entry.frame;
              const sameFrame =
                activeIndex >= 0
                && entries[activeIndex]?.frame === entry.frame;
              return (
                <Button
                  aria-current={active ? "true" : undefined}
                  className={cn(
                    "absolute left-0 top-0 grid h-9 w-full grid-cols-[4rem_8.5rem_minmax(0,1fr)_4.5rem] items-center gap-x-2 border-b border-border/20 px-3 text-left font-normal transition-colors hover:bg-accent/40 max-[600px]:grid-cols-[3.5rem_1.25rem_minmax(0,1fr)_auto] max-[600px]:gap-x-1.5 max-[600px]:px-2",
                    frameStart && "border-t border-t-border/55",
                    sameFrame && "bg-brand-soft/[0.07]",
                    active && "bg-brand-soft/[0.13] shadow-[inset_3px_0_0_var(--color-brand-soft)]",
                  )}
                  data-bpp-active={active ? "true" : "false"}
                  data-bpp-combat-ms={entry.combatMs}
                  data-bpp-frame={entry.frame}
                  data-bpp-frame-start={frameStart ? "true" : "false"}
                  data-bpp-same-frame={sameFrame ? "true" : "false"}
                  data-bpp-test-id="combat-log-entry"
                  data-index={virtualRow.index}
                  key={entry.id}
                  onClick={() => onSelectEntry(entry)}
                  ref={virtualizer.measureElement}
                  style={{
                    transform: `translateY(${virtualRow.start}px)`,
                  }}
                  type="button"
                  variant="tableHeader"
                >
                  {sameFrame && (
                    <span className="sr-only">
                      {t(
                        active
                          ? "combatLogSelectedEvent"
                          : "combatLogSameFrameEvent",
                      )}
                    </span>
                  )}
                  <span
                    className="inline-flex h-full min-w-0 items-center gap-1 font-mono text-micro text-brand-soft"
                    data-bpp-test-id="combat-log-time"
                  >
                    {frameStart
                      ? (
                        <strong>{formatDuration(entry.combatMs)}</strong>
                      )
                      : (
                        <span
                          aria-hidden="true"
                          className="ml-1 h-full border-l border-border/35"
                        />
                      )}
                  </span>
                  <span
                    className="flex min-w-0 items-center gap-1.5 overflow-hidden"
                    data-bpp-test-id="combat-log-kind"
                  >
                    <SemanticIcon className="size-icon-sm" token={entry.token} />
                    <span className="shrink-0 text-compact font-semibold text-foreground max-[600px]:sr-only">
                      {t(entry.token)}
                    </span>
                  </span>
                  <span
                    className="grid min-w-0 grid-cols-[minmax(0,1fr)_1rem_minmax(0,1fr)] items-center gap-x-1 overflow-hidden text-compact text-muted-foreground"
                    data-bpp-test-id="combat-log-route"
                  >
                    <span
                      className="min-w-0 overflow-hidden"
                      data-bpp-test-id="combat-log-source"
                    >
                      {source
                        ? <EntityChip entity={source} />
                        : <MissingEntity label={t("sourceNotRecorded")} />}
                    </span>
                    <ChevronRight
                      aria-hidden="true"
                      className="size-icon-sm shrink-0 justify-self-center opacity-45"
                      data-bpp-test-id="combat-log-arrow"
                    />
                    <span
                      className="inline-flex min-w-0 items-center gap-1 overflow-hidden"
                      data-bpp-test-id="combat-log-target"
                    >
                      {targets[0]
                        ? <EntityChip entity={targets[0]} />
                        : <MissingEntity label={t("targetNotRecorded")} />}
                      {targets.length > 1 && (
                        <span className="shrink-0 text-micro text-muted-foreground">
                          +{targets.length - 1}
                        </span>
                      )}
                    </span>
                  </span>
                  <span
                    className="flex shrink-0 items-center justify-end gap-1 font-mono text-micro tabular-nums"
                    data-bpp-test-id="combat-log-amount"
                  >
                    {amount && (
                      <strong className="text-foreground">{amount}</strong>
                    )}
                    {entry.count > 1 && (
                      <span className="text-muted-foreground">×{entry.count}</span>
                    )}
                  </span>
                </Button>
              );
            })}
          </div>
        )}
      </div>
    </section>
  );
});
