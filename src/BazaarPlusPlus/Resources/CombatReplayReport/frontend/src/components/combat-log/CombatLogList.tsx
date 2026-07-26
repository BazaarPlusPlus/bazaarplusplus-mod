import { useVirtualizer } from "@tanstack/react-virtual";
import { Pause, Play, RotateCcw } from "lucide-react";
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
  nearestCombatLogEntryIndex,
  selectedCombatLogEntryIndex,
  type CombatLogEntry,
} from "../../combat-log/entries.ts";
import { cn } from "../../lib/utils.ts";
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
  fallback,
}: {
  entity: NormalizedEntity | null;
  fallback: string;
}): React.JSX.Element {
  if (!entity) {
    return (
      <span className="min-w-0 truncate text-muted-foreground">
        {fallback}
      </span>
    );
  }
  return (
    <span className="inline-flex min-w-0 items-center gap-1.5">
      <EntityArt entity={entity} size="compact" />
      <span className="truncate" title={entity.name}>{entity.name}</span>
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
  const entries = useMemo(
    () => buildCombatLogEntries(model.events),
    [model.events],
  );
  const entityById = useMemo(
    () => new Map(model.entities.map((entity) => [entity.id, entity])),
    [model.entities],
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
    estimateSize: () => 58,
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
      data-bpp-total-count={entries.length}
      data-bpp-test-id="combat-log"
    >
      <header className="flex h-control-sm shrink-0 items-center gap-2 border-b border-border/60 px-3">
        <strong className="text-compact">{t("combatLogTitle")}</strong>
        <span className="font-mono text-micro text-muted-foreground">
          {entries.length} {t("event")}
        </span>
        <span className="ml-auto inline-flex items-center gap-1.5 text-micro text-muted-foreground">
          {followSource === "playback"
            ? <Play className="size-icon-sm" />
            : manualPaused
              ? <Pause className="size-icon-sm" />
              : null}
          {manualPaused ? t("combatLogPaused") : followLabel}
        </span>
        {manualPaused && (
          <Button
            data-bpp-test-id="combat-log-resume"
            onClick={() => setManualPaused(false)}
            size="xs"
            type="button"
            variant="ghost"
          >
            <RotateCcw className="size-icon-sm" />
            {t("combatLogResume")}
          </Button>
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
              const sameFrame =
                activeIndex >= 0
                && entries[activeIndex]?.frame === entry.frame;
              return (
                <Button
                  aria-current={active ? "true" : undefined}
                  className={cn(
                    "absolute left-0 top-0 grid h-[58px] w-full grid-cols-[4.25rem_7.25rem_minmax(0,1fr)_auto] items-center gap-2 border-b border-border/40 px-3 text-left transition-colors hover:bg-accent/45 max-[720px]:grid-cols-[3.75rem_minmax(0,1fr)_auto]",
                    sameFrame && "bg-accent/25",
                    active && "bg-brand-soft/10 shadow-[inset_3px_0_0_var(--color-brand-soft)]",
                  )}
                  data-bpp-active={active ? "true" : "false"}
                  data-bpp-combat-ms={entry.combatMs}
                  data-bpp-frame={entry.frame}
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
                  <span className="font-mono text-micro text-brand-soft">
                    {formatDuration(entry.combatMs)}
                  </span>
                  <span className="flex min-w-0 items-center gap-1.5">
                    <SemanticIcon className="size-icon-sm" token={entry.token} />
                    <strong className="truncate text-compact">
                      {t(entry.token)}
                    </strong>
                  </span>
                  <span
                    className="grid min-w-0 grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-1.5 text-compact max-[720px]:hidden"
                    data-bpp-test-id="combat-log-route"
                  >
                    <EntityChip
                      entity={source}
                      fallback={t("sourceNotRecorded")}
                    />
                    <span aria-hidden="true" className="text-muted-foreground">
                      →
                    </span>
                    <span className="inline-flex min-w-0 items-center gap-1">
                      <EntityChip
                        entity={targets[0] ?? null}
                        fallback={t("targetNotRecorded")}
                      />
                      {targets.length > 1 && (
                        <span className="shrink-0 text-micro text-muted-foreground">
                          +{targets.length - 1}
                        </span>
                      )}
                    </span>
                  </span>
                  <span className="flex shrink-0 items-center gap-1.5 font-mono text-micro">
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
