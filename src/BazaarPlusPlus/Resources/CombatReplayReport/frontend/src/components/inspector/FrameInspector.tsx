import { ArrowRight, X } from "lucide-react";
import {
  useEffect,
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
import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../../model/normalize.ts";
import type { ReportViewModel } from "../../model/report.ts";
import { eventKindToken } from "../../timeline/clusters.ts";
import { eventsAtFrame } from "../../timeline/event-renderer.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { SemanticIcon } from "../semantic/SemanticIcon.tsx";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "../ui/accordion.tsx";
import { Badge } from "../ui/badge.tsx";
import { Button } from "../ui/button.tsx";
import { ScrollArea } from "../ui/scroll-area.tsx";
import { mergeInspectorEvents } from "./frame-event-groups.ts";

const PAGE_SIZE = 80;

function EntityReference({
  entityById,
  entityId,
  fallback,
  testId,
}: {
  entityById: ReadonlyMap<string, NormalizedEntity>;
  entityId: string;
  fallback: string;
  testId: string;
}): React.JSX.Element {
  const entity = entityById.get(entityId);
  if (!entity) {
    return (
      <span
        className="truncate text-compact text-muted-foreground"
        data-bpp-test-id={testId}
      >
        {fallback}
      </span>
    );
  }
  return (
    <span
      className="inline-flex min-w-0 items-center gap-2 text-compact"
      data-bpp-entity-id={entity.id}
      data-bpp-test-id={testId}
    >
      <EntityArt entity={entity} size="compact" />
      <span className="truncate text-foreground/90" title={entity.name}>
        {entity.name}
      </span>
    </span>
  );
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

function eventAmount(event: NormalizedEvent): string {
  if (
    event.value === null
    || event.value === undefined
    || event.value === ""
  ) {
    return "";
  }
  const isMilliseconds =
    event.unit.toLowerCase().includes("millisecond")
    || event.unit.toLowerCase() === "ms";
  return isMilliseconds
    ? formatMilliseconds(event.value)
    : formatCompactNumber(event.value);
}

function EventRow({
  event,
  entityById,
  mergedCount = 1,
  mergedTargets,
  t,
}: {
  event: NormalizedEvent;
  entityById: ReadonlyMap<string, NormalizedEntity>;
  mergedCount?: number;
  mergedTargets?: readonly string[];
  t: (key: string) => string;
}): React.JSX.Element {
  const token = eventKindToken(event);
  const sourceId = event.sourceId || event.triggerSourceId;
  const targetIds = mergedTargets ?? event.targetIds;
  const amount = eventAmount(event);
  return (
    <article
      className="border-b border-border/45 bg-brand-soft/7 px-3 py-2 shadow-[inset_2px_0_0_var(--color-brand-soft)] last:border-b-0"
      data-bpp-event-id={event.id}
      data-bpp-test-id="focused-cluster-event"
    >
      <div className="flex min-h-control-xs items-center gap-2">
        {event.icon ? (
          <img alt="" className="size-icon-md object-contain" src={event.icon} />
        ) : (
          <SemanticIcon token={token} />
        )}
        <strong className="truncate text-compact text-foreground">
          {t(token)}
        </strong>
        {mergedCount > 1 && (
          <Badge className="px-1.5 font-mono" variant="secondary">
            ×{mergedCount}
          </Badge>
        )}
        {amount && (
          <strong className="ml-auto shrink-0 font-mono text-compact text-brand-soft">
            {amount}
          </strong>
        )}
      </div>
      <div
        className="mt-1.5 grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-2"
        data-bpp-test-id="frame-event-relation"
      >
        <div className="min-w-0">
          <span className="mb-0.5 block text-nano font-medium uppercase tracking-wide text-muted-foreground/80">
            {t("source")}
          </span>
          <EntityReference
            entityById={entityById}
            entityId={sourceId}
            fallback={t("sourceNotRecorded")}
            testId="event-source-entity"
          />
        </div>
        <ArrowRight
          aria-hidden="true"
          className="mt-4 size-icon-sm shrink-0 text-brand-soft/70"
        />
        <div className="min-w-0">
          <span className="mb-0.5 block text-nano font-medium uppercase tracking-wide text-muted-foreground/80">
            {t("target")}
          </span>
          <span className="flex min-w-0 flex-wrap gap-x-2 gap-y-1">
            {targetIds.length > 0 ? (
              targetIds.map((targetId) => (
                <EntityReference
                  entityById={entityById}
                  entityId={targetId}
                  fallback={t("targetNotRecorded")}
                  key={targetId}
                  testId="event-target-entity"
                />
              ))
            ) : (
              <EntityReference
                entityById={entityById}
                entityId=""
                fallback={t("targetNotRecorded")}
                testId="event-target-entity"
              />
            )}
          </span>
        </div>
      </div>
      {event.triggerSourceId
        && event.triggerSourceId !== event.sourceId && (
          <div className="mt-1 flex min-w-0 items-center gap-1.5 pl-0.5 text-nano text-muted-foreground">
            <span className="shrink-0">{t("triggerSource")}</span>
            <EntityReference
              entityById={entityById}
              entityId={event.triggerSourceId}
              fallback={t("sourceNotRecorded")}
              testId="event-trigger-source-entity"
            />
          </div>
        )}
    </article>
  );
}

export function FrameInspector({
  model,
  entityId,
  frame,
  focusedEventIds,
  onClose,
  t,
}: {
  model: ReportViewModel;
  entityId: string;
  frame: number | null;
  focusedEventIds: readonly string[];
  onClose: () => void;
  t: (key: string) => string;
}): React.JSX.Element {
  const inspectorRef = useRef<HTMLElement>(null);
  const [limit, setLimit] = useState(PAGE_SIZE);
  const focusKey = focusedEventIds.join("\u001f");
  useEffect(() => setLimit(PAGE_SIZE), [entityId, focusKey, frame]);
  const entityById = useMemo(
    () => new Map(model.entities.map((entity) => [entity.id, entity])),
    [model.entities],
  );
  const inspectedEntity = entityById.get(entityId);
  const focusedIdSet = useMemo(
    () => new Set(focusedEventIds),
    [focusedEventIds],
  );
  const events = useMemo(
    () =>
      eventsAtFrame(model.events, frame).filter((event) =>
        focusedIdSet.has(event.id),
      ),
    [focusedIdSet, frame, model.events],
  );
  const groups = useMemo(() => {
    const result = new Map<string, NormalizedEvent[]>();
    for (const event of events) {
      const token = eventKindToken(event);
      const values = result.get(token) ?? [];
      values.push(event);
      result.set(token, values);
    }
    return Array.from(result.entries())
      .map(([token, group]) => ({ token, events: group }))
      .sort((left, right) => right.events.length - left.events.length);
  }, [events]);
  useLayoutEffect(() => {
    const viewport = inspectorRef.current?.querySelector<HTMLElement>(
      "[data-slot='scroll-area-viewport']",
    );
    if (viewport) viewport.scrollTop = 0;
  }, [entityId, focusKey, frame]);
  const first = events[0];

  return (
    <aside
      className="flex h-full min-h-0 w-[clamp(320px,30vw,400px)] shrink-0 flex-col border-l border-border/70 bg-surface shadow-sticky"
      data-bpp-test-id="frame-inspector"
      ref={inspectorRef}
    >
      <header
        className="shrink-0 border-b border-border/70 px-3 py-2"
        data-bpp-test-id="frame-inspector-header"
      >
        <div className="flex items-start gap-2">
          {inspectedEntity && (
            <EntityArt entity={inspectedEntity} size="compact" />
          )}
          <div className="min-w-0 flex-1">
            <div className="flex min-w-0 items-center gap-2">
              <h2
                className="truncate text-body font-semibold text-foreground"
                data-bpp-test-id="frame-inspector-entity"
              >
                {inspectedEntity?.name || t("frameEvents")}
              </h2>
              {inspectedEntity && (
                <Badge
                  className="px-1.5 font-normal"
                  data-bpp-test-id="frame-inspector-entity-type"
                  variant="outline"
                >
                  {entityTypeLabel(inspectedEntity.type, t)}
                </Badge>
              )}
            </div>
            <div
              className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 font-mono text-micro text-muted-foreground"
              data-bpp-test-id="frame-inspector-meta"
            >
              <span data-bpp-test-id="frame-inspector-time">
                {first ? formatDuration(first.combatMs) : "—"}
              </span>
              <span aria-hidden="true">·</span>
              <span data-bpp-test-id="frame-inspector-frame">
                {frame === null ? "—" : `${t("frame")} ${frame}`}
              </span>
              <span aria-hidden="true">·</span>
              <span
                className="text-foreground/85"
                data-bpp-test-id="frame-event-total"
              >
                {events.length} {t("event")}
              </span>
            </div>
          </div>
          <Button
            aria-label={t("close")}
            className="shrink-0"
            data-bpp-test-id="frame-inspector-close"
            onClick={onClose}
            size="icon-xs"
            type="button"
            variant="ghost"
          >
            <X className="size-icon-sm" />
          </Button>
        </div>
      </header>
      <ScrollArea
        className="min-h-0 flex-1"
        data-bpp-test-id="frame-event-list"
      >
        <div>
          <Accordion
            className="divide-y divide-border/45"
            defaultValue={groups.map((group) => group.token)}
            key={`${entityId}:${frame ?? "none"}:${focusKey}`}
            type="multiple"
          >
            {groups.map((group) => (
              <AccordionItem
                className="rounded-none border-x-0 border-y-0 bg-brand-soft/4 shadow-[inset_2px_0_0_var(--color-brand-soft)]"
                data-bpp-event-token={group.token}
                data-bpp-test-id="focused-cluster-group"
                key={group.token}
                value={group.token}
              >
                <AccordionTrigger className="min-h-control-md px-3 py-2 text-body hover:bg-accent/45">
                  <SemanticIcon token={group.token} />
                  <span className="min-w-0 truncate">{t(group.token)}</span>
                  <Badge
                    className="px-1.5 font-mono"
                    variant="secondary"
                  >
                    ×{group.events.length}
                  </Badge>
                </AccordionTrigger>
                <AccordionContent className="space-y-0 border-t border-border/45 p-0">
                  {mergeInspectorEvents(
                    group.events.slice(0, limit),
                  ).map((merged) => (
                    <EventRow
                      entityById={entityById}
                      event={merged.event}
                      key={merged.key}
                      mergedCount={merged.count}
                      mergedTargets={merged.targetIds}
                      t={t}
                    />
                  ))}
                </AccordionContent>
              </AccordionItem>
            ))}
          </Accordion>
          {events.length > limit && (
            <Button
              className="m-2 w-[calc(100%_-_1rem)]"
              data-bpp-test-id="frame-events-more"
              onClick={() =>
                setLimit((current) =>
                  Math.min(events.length, current + PAGE_SIZE),
                )
              }
              size="sm"
              type="button"
              variant="outline"
            >
              {t("moreEvents")} ({events.length - limit})
            </Button>
          )}
        </div>
      </ScrollArea>
    </aside>
  );
}
