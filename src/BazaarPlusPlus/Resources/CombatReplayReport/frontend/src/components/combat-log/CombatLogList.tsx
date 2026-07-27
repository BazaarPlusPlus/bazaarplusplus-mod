import { useVirtualizer } from "@tanstack/react-virtual";
import {
  ChevronDown,
  Pause,
  Play,
  RotateCcw,
} from "lucide-react";
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
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
  combatLogGroupKey,
  combatLogTargetDetails,
  groupCombatLogEntries,
  isCombatLogApplicationEvent,
  isCombatLogHealthSettlementEvent,
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
  labelTestId,
}: {
  entity: NormalizedEntity;
  labelTestId: string;
}): React.JSX.Element {
  return (
    <span className="grid w-full min-w-0 grid-cols-[1.5rem_minmax(0,1fr)] items-center gap-1.5">
      <EntityArt entity={entity} size="compact" squareSlot />
      <span
        className="truncate"
        data-bpp-test-id={labelTestId}
        title={entity.name}
      >
        {entity.name}
      </span>
    </span>
  );
}

function MissingEntity({
  label,
  labelTestId,
}: {
  label: string;
  labelTestId: string;
}): React.JSX.Element {
  return (
    <span className="grid w-full min-w-0 grid-cols-[1.5rem_minmax(0,1fr)] items-center gap-1.5">
      <span aria-hidden="true" className="size-6" />
      <span
        aria-label={label}
        className="truncate text-muted-foreground/55"
        data-bpp-test-id={labelTestId}
        title={label}
      >
        —
      </span>
    </span>
  );
}

function CombatLogKindIcon({
  entry,
}: {
  entry: CombatLogEntry;
}): React.JSX.Element {
  return entry.icon ? (
    <img
      alt=""
      className="size-icon-md shrink-0 object-contain"
      data-bpp-test-id="combat-log-kind-native-icon"
      src={entry.icon}
    />
  ) : (
    <SemanticIcon className="size-icon-sm" token={entry.token} />
  );
}

function CombatLogEntryColumns({
  entry,
  entityById,
  frameStart,
  t,
  detailCount,
  expandable,
  expanded,
}: {
  entry: CombatLogEntry;
  entityById: ReadonlyMap<string, NormalizedEntity>;
  frameStart: boolean;
  t: (key: string) => string;
  detailCount: number;
  expandable?: boolean;
  expanded?: boolean;
}): React.JSX.Element {
  const source =
    combatLogEntity(entityById, entry.sourceId)
    ?? combatLogEntity(entityById, entry.triggerSourceId);
  const targets = entry.targetIds
    .map((id) => combatLogEntity(entityById, id))
    .filter((entity): entity is NormalizedEntity => entity !== null);
  const amount = entryAmount(entry);
  return (
    <>
      <span
        className="inline-flex h-full min-w-0 items-center gap-1 font-mono text-micro text-brand-soft"
        data-bpp-test-id="combat-log-time"
      >
        {frameStart
          ? <strong>{formatDuration(entry.combatMs)}</strong>
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
        <CombatLogKindIcon entry={entry} />
        <span className="truncate text-compact font-semibold text-foreground max-[600px]:sr-only">
          {t(entry.token)}
        </span>
      </span>
      <span
        className="block min-w-0 overflow-hidden text-compact text-muted-foreground"
        data-bpp-test-id="combat-log-source"
      >
        {source
          ? (
            <EntityChip
              entity={source}
              labelTestId="combat-log-source-label"
            />
          )
          : (
            <MissingEntity
              label={t("sourceNotRecorded")}
              labelTestId="combat-log-source-label"
            />
          )}
      </span>
      <span
        aria-hidden="true"
        data-bpp-test-id="combat-log-relation"
      />
      <span
        className="grid min-w-0 grid-cols-[minmax(0,1fr)_auto] items-center gap-1 overflow-hidden text-compact text-muted-foreground"
        data-bpp-test-id="combat-log-target"
      >
        {expandable
          ? (
            <span
              className="col-span-2 inline-flex min-w-0 items-center gap-1 text-compact text-muted-foreground"
              data-bpp-test-id="combat-log-entry-expand"
            >
              <span data-bpp-test-id="combat-log-target-summary">
                {t("combatLogTargets")}
              </span>
              <strong className="font-mono text-micro text-foreground">
                ×{detailCount}
              </strong>
              <ChevronDown
                aria-hidden="true"
                className={cn(
                  "size-icon-sm transition-transform",
                  expanded && "rotate-180",
                )}
              />
            </span>
          )
          : (
            <>
              {targets[0]
                ? (
                  <EntityChip
                    entity={targets[0]}
                    labelTestId="combat-log-target-label"
                  />
                )
                : (
                  <MissingEntity
                    label={t("targetNotRecorded")}
                    labelTestId="combat-log-target-label"
                  />
                )}
              {targets.length > 1
                ? (
                  <span className="shrink-0 text-micro text-muted-foreground">
                    +{targets.length - 1}
                  </span>
                )
                : null}
            </>
          )}
      </span>
      <span
        className="flex shrink-0 items-center justify-end gap-1 font-mono text-micro tabular-nums"
        data-bpp-test-id="combat-log-amount"
      >
        {amount && <strong className="text-foreground">{amount}</strong>}
        {entry.count > 1 && (
          <span className="text-muted-foreground">×{entry.count}</span>
        )}
      </span>
    </>
  );
}

function CombatLogTargetDetail({
  detail,
  entityById,
  onSelectEntry,
  parentEntryId,
  t,
}: {
  detail: CombatLogEntry;
  entityById: ReadonlyMap<string, NormalizedEntity>;
  onSelectEntry: (entry: CombatLogEntry) => void;
  parentEntryId: string;
  t: (key: string) => string;
}): React.JSX.Element {
  const target = combatLogEntity(entityById, detail.targetIds[0]);
  const amount = entryAmount(detail);

  return (
    <Button
      aria-label={[
        t(detail.token),
        target?.name ?? t("targetNotRecorded"),
        amount,
      ].filter(Boolean).join(" · ")}
      className="grid h-8 w-full grid-cols-[4rem_4.75rem_minmax(0,1fr)_1rem_minmax(0,1fr)_4.5rem] items-center justify-stretch gap-x-2 gap-y-0 rounded-none border-b border-border/15 bg-surface-raised/25 px-3 text-left font-normal transition-colors hover:bg-accent/35 max-[600px]:grid-cols-[3.5rem_1.25rem_minmax(0,1fr)_0.75rem_minmax(0,1fr)_auto] max-[600px]:gap-x-1.5 max-[600px]:px-2"
      data-bpp-parent-entry-id={parentEntryId}
      data-bpp-target-id={detail.targetIds[0] ?? ""}
      data-bpp-test-id="combat-log-entry-detail"
      onClick={() => onSelectEntry(detail)}
      size="sm"
      type="button"
      variant="tableHeader"
    >
      <span aria-hidden="true" />
      <span aria-hidden="true" />
      <span aria-hidden="true" />
      <span aria-hidden="true" />
      <span
        className="block min-w-0 overflow-hidden pl-2 text-compact text-muted-foreground"
        data-bpp-test-id="combat-log-detail-target"
      >
        {target
          ? (
            <EntityChip
              entity={target}
              labelTestId="combat-log-detail-target-label"
            />
          )
          : (
            <MissingEntity
              label={t("targetNotRecorded")}
              labelTestId="combat-log-detail-target-label"
            />
          )}
      </span>
      <span className="flex shrink-0 items-center justify-end gap-1 font-mono text-micro tabular-nums">
        {amount && <strong className="text-foreground">{amount}</strong>}
        {detail.count > 1 && (
          <span className="text-muted-foreground">×{detail.count}</span>
        )}
      </span>
    </Button>
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
          || isCombatLogApplicationEvent(event)
          || isCombatLogHealthSettlementEvent(event)
        ),
      ).filter(isNarrativeCombatLogEntry),
    [entityById, model.events],
  );
  const entries = useMemo(
    () => groupCombatLogEntries(rawEntries),
    [rawEntries],
  );
  const detailsByEntryId = useMemo(() => {
    const rawByKey = new Map<string, CombatLogEntry[]>();
    for (const entry of rawEntries) {
      const key = combatLogGroupKey(entry);
      rawByKey.set(key, [...(rawByKey.get(key) ?? []), entry]);
    }
    return new Map(
      entries.map((entry) => [
        entry.id,
        combatLogTargetDetails(
          rawByKey.get(combatLogGroupKey(entry)) ?? [entry],
        ),
      ]),
    );
  }, [entries, rawEntries]);
  const viewportRef = useRef<HTMLDivElement>(null);
  const previewMsRef = useRef<number | null>(null);
  const playbackMsRef = useRef(initialPlaybackCombatMs);
  const playbackActiveRef = useRef(initialPlaybackActive);
  const pinnedMsRef = useRef(pinnedCombatMs);
  const pinnedEventIdsRef = useRef(pinnedEventIds);
  const scheduledRef = useRef(0);
  const hasFollowedRef = useRef(false);
  const [activeIndex, setActiveIndex] = useState(() =>
    selectedCombatLogEntryIndex(entries, pinnedEventIds, pinnedCombatMs)
  );
  const [manualPaused, setManualPaused] = useState(false);
  const [expandedEntryIds, setExpandedEntryIds] = useState<Set<string>>(
    () => new Set(),
  );
  const [followSource, setFollowSource] = useState<
    "hover" | "playback" | "pinned"
  >(initialPlaybackActive ? "playback" : "pinned");

  const virtualizer = useVirtualizer({
    count: entries.length,
    estimateSize: (index) => {
      const entry = entries[index];
      const detailCount = entry
        ? detailsByEntryId.get(entry.id)?.length ?? 0
        : 0;
      return 36
        + (expandedEntryIds.has(entry?.id ?? "")
          ? detailCount * 32 + 4
          : 0);
    },
    getItemKey: (index) => entries[index]?.id ?? index,
    getScrollElement: () => viewportRef.current,
    overscan: 8,
  });

  useLayoutEffect(() => {
    virtualizer.measure();
  }, [expandedEntryIds, virtualizer]);

  const updateActive = (): void => {
    scheduledRef.current = 0;
    const source =
      playbackActiveRef.current
        ? "playback"
        : previewMsRef.current !== null
          ? "hover"
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
        if (combatMs !== null) setManualPaused(false);
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
    const reduceMotion = window.matchMedia(
      "(prefers-reduced-motion: reduce)",
    ).matches;
    virtualizer.scrollToIndex(activeIndex, {
      align: "center",
      behavior: hasFollowedRef.current && !reduceMotion ? "smooth" : "auto",
    });
    hasFollowedRef.current = true;
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
        onKeyDown={(event) => {
          if (["ArrowDown", "ArrowUp", "PageDown", "PageUp"].includes(
            event.key,
          )) {
            setManualPaused(true);
          }
        }}
        onPointerDown={(event) => {
          if (event.target === event.currentTarget) {
            setManualPaused(true);
          }
        }}
        onWheel={() => setManualPaused(true)}
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
              const details = detailsByEntryId.get(entry.id) ?? [];
              const expandable = details.length > 1;
              const expanded = expandedEntryIds.has(entry.id);
              const active = virtualRow.index === activeIndex;
              const frameStart =
                virtualRow.index === 0
                || entries[virtualRow.index - 1]?.frame !== entry.frame;
              const sameFrame =
                activeIndex >= 0
                && entries[activeIndex]?.frame === entry.frame;
              return (
                <div
                  className="absolute left-0 top-0 w-full"
                  data-index={virtualRow.index}
                  key={entry.id}
                  ref={virtualizer.measureElement}
                  style={{
                    transform: `translateY(${virtualRow.start}px)`,
                  }}
                >
                  <Button
                    aria-current={active ? "true" : undefined}
                    aria-expanded={expandable ? expanded : undefined}
                    className={cn(
                      "grid h-9 w-full grid-cols-[4rem_4.75rem_minmax(0,1fr)_1rem_minmax(0,1fr)_4.5rem] items-center justify-stretch gap-x-2 gap-y-0 rounded-none border-b border-border/20 px-3 text-left font-normal transition-colors hover:bg-accent/40 max-[600px]:grid-cols-[3.5rem_1.25rem_minmax(0,1fr)_0.75rem_minmax(0,1fr)_auto] max-[600px]:gap-x-1.5 max-[600px]:px-2",
                      frameStart && "border-t border-t-border/55",
                      sameFrame && "bg-brand-soft/[0.07]",
                      active
                        && "bg-brand-soft/[0.13] shadow-[inset_3px_0_0_var(--color-brand-soft)]",
                    )}
                    data-bpp-active={active ? "true" : "false"}
                    data-bpp-combat-ms={entry.combatMs}
                    data-bpp-frame={entry.frame}
                    data-bpp-frame-start={frameStart ? "true" : "false"}
                    data-bpp-same-frame={sameFrame ? "true" : "false"}
                    data-bpp-test-id="combat-log-entry"
                    data-index={virtualRow.index}
                    onClick={() => {
                      onSelectEntry(entry);
                      if (!expandable) return;
                      setExpandedEntryIds((current) => {
                        const next = new Set(current);
                        if (next.has(entry.id)) next.delete(entry.id);
                        else next.add(entry.id);
                        return next;
                      });
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
                    <CombatLogEntryColumns
                      entityById={entityById}
                      entry={entry}
                      detailCount={details.length}
                      expandable={expandable}
                      expanded={expanded}
                      frameStart={frameStart}
                      t={t}
                    />
                  </Button>
                  {expanded && (
                    <div
                      className="pb-1"
                      data-bpp-source-id={
                        entry.sourceId || entry.triggerSourceId
                      }
                      data-bpp-test-id="combat-log-entry-details"
                      role="group"
                    >
                      {details.map((detail) => (
                        <CombatLogTargetDetail
                          detail={detail}
                          entityById={entityById}
                          key={detail.id}
                          onSelectEntry={onSelectEntry}
                          parentEntryId={entry.id}
                          t={t}
                        />
                      ))}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </section>
  );
});
