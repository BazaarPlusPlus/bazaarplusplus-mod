import { X } from "lucide-react";
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
  formatNumber,
} from "../../i18n/format.ts";
import { cn } from "../../lib/utils.ts";
import {
  eventAttributeSemantic,
  eventPresentation,
  type EventPresentation,
} from "../../model/event-semantics.ts";
import { attributeEventDiff } from "../../model/attribute-event-diff.ts";
import { resolveSourceModeAttributeEvents } from "../../model/effect-attribute-details.ts";
import type {
  NormalizedEntity,
  NormalizedEvent,
} from "../../model/normalize.ts";
import type { ReportViewModel } from "../../model/report.ts";
import { eventsAtFrame } from "../../timeline/event-renderer.ts";
import type { EventLaneMode } from "../../timeline/event-lane-mode.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import {
  entityArtDimensions,
  intrinsicItemArtGeometry,
} from "../semantic/entity-art-geometry.ts";
import {
  NativeOrSemanticIcon,
  SemanticIcon,
} from "../semantic/SemanticIcon.tsx";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "../ui/accordion.tsx";
import { Badge } from "../ui/badge.tsx";
import { Button } from "../ui/button.tsx";
import { ScrollArea } from "../ui/scroll-area.tsx";
import { ControlTooltip } from "../ui/tooltip.tsx";
import {
  mergeInspectorEvents,
  summarizeDirectDamageGroup,
} from "./frame-event-groups.ts";

const PAGE_SIZE = 80;

function EntityReference({
  artColumnWidth,
  entityById,
  entityId,
  fallback,
  testId,
}: {
  artColumnWidth: number;
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
      <span
        className="flex h-11 shrink-0 items-center justify-start"
        data-bpp-test-id="frame-event-entity-art-slot"
        style={{ width: artColumnWidth }}
      >
        <EntityArt
          contentAlign="start"
          entity={entity}
          itemFit="intrinsic"
          size="default"
        />
      </span>
      <span className="truncate text-foreground/90" title={entity.name}>
        {entity.name}
      </span>
    </span>
  );
}

interface RelationNode {
  ariaLabel: string;
  entityId: string;
  fallback: string;
  relationLabel: string;
  testId: string;
}

function EventRelationTree({
  entityById,
  eventLaneMode,
  nodes,
}: {
  entityById: ReadonlyMap<string, NormalizedEntity>;
  eventLaneMode: EventLaneMode;
  nodes: readonly RelationNode[];
}): React.JSX.Element | null {
  if (nodes.length === 0) return null;
  const artColumnWidth = Math.max(
    ...nodes.map((node) => {
      const entity = entityById.get(node.entityId);
      if (!entity) return 0;
      return entity.type.toLowerCase() === "item"
        ? intrinsicItemArtGeometry(entity.span, "default").width
        : entityArtDimensions(entity.type, entity.span, "default").width;
    }),
  );
  return (
    <div
      className="relative ml-2.5 mt-1.5 flex min-w-0 flex-col gap-1 pl-4"
      data-bpp-test-id="frame-event-relation"
    >
      <span
        aria-hidden="true"
        className={cn(
          "absolute -top-[1.125rem] bottom-[1.375rem] left-0 border-l",
          eventLaneMode === "source"
            ? "border-success/45"
            : "border-brand-soft/35",
        )}
        data-bpp-test-id="frame-event-relation-line"
      />
      {nodes.map((node, index) => (
        <div
          aria-label={node.ariaLabel}
          className="relative flex min-h-11 min-w-0 items-center"
          key={`${node.testId}:${node.entityId}:${index}`}
          role="group"
        >
          <span
            aria-hidden="true"
            className={cn(
              "absolute -left-4 top-1/2 w-3 border-t",
              eventLaneMode === "source"
                ? "border-success/45"
                : "border-brand-soft/35",
            )}
            data-bpp-test-id="frame-event-relation-branch"
          />
          <EntityReference
            artColumnWidth={artColumnWidth}
            entityById={entityById}
            entityId={node.entityId}
            fallback={node.fallback}
            testId={node.testId}
          />
          <span
            className="pointer-events-none absolute top-0.5 truncate text-nano font-medium uppercase tracking-wide text-muted-foreground"
            data-bpp-test-id="frame-event-relation-role"
            style={{ left: artColumnWidth + 8 }}
          >
            {node.relationLabel}
          </span>
        </div>
      ))}
    </div>
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
  eventLaneMode,
  fallbackIcon,
  inspectedEntityId,
  mergedCount = 1,
  t,
}: {
  event: NormalizedEvent;
  entityById: ReadonlyMap<string, NormalizedEntity>;
  eventLaneMode: EventLaneMode;
  fallbackIcon?: string;
  inspectedEntityId: string;
  mergedCount?: number;
  t: (key: string) => string;
}): React.JSX.Element {
  const presentation = eventPresentation(event);
  const diff = attributeEventDiff(event);
  const amount = diff?.deltaText ?? eventAmount(event);
  return (
    <article
      className={cn(
        "border-b border-border/45 bg-surface-raised/45 px-2.5 py-2 last:border-b-0",
        diff?.polarity === "increase"
          && "border-l-2 border-l-success/70 bg-success/5",
        diff?.polarity === "decrease"
          && "border-l-2 border-l-destructive/70 bg-destructive/5",
      )}
      data-bpp-diff-polarity={diff?.polarity}
      data-bpp-event-id={event.id}
      data-bpp-test-id="focused-cluster-event"
    >
      <div className="flex min-h-control-xs items-center gap-1.5">
        <NativeOrSemanticIcon
          nativeClassName="size-icon-lg"
          nativeUrl={event.icon || fallbackIcon || ""}
          semanticClassName="size-icon-lg"
          testId="frame-event-native-icon"
          token={presentation.token}
        />
        <strong
          className="truncate text-compact text-foreground"
          data-bpp-test-id="frame-event-kind"
        >
          {t(presentation.labelKey)}
        </strong>
        {mergedCount > 1 && (
          <Badge className="px-1.5 font-mono" variant="secondary">
            ×{mergedCount}
          </Badge>
        )}
        {event.isCritical && (
          <Badge
            className="!h-4 !rounded-tag border-damage/35 bg-damage/10 !px-1 !py-0 !text-nano leading-none text-damage"
            data-bpp-test-id="frame-event-critical"
            variant="outline"
          >
            {t("critical")}
          </Badge>
        )}
        {amount && (
          <strong
            className={cn(
              "ml-auto shrink-0 rounded-panel border px-1.5 py-0.5 font-mono text-compact",
              diff?.polarity === "increase"
                ? "border-success/30 bg-success/10 text-success"
                : diff?.polarity === "decrease"
                  ? "border-destructive/30 bg-destructive/10 text-destructive"
                  : "border-transparent text-brand-soft",
            )}
            data-bpp-test-id="frame-event-amount"
          >
            {amount}
          </strong>
        )}
      </div>
      {diff && (
        <div
          className="ml-7 mt-0.5 font-mono text-micro tabular-nums text-muted-foreground"
          data-bpp-test-id="frame-event-transition"
        >
          {diff.transitionText}
        </div>
      )}
      <EventRelationTree
        entityById={entityById}
        eventLaneMode={eventLaneMode}
        nodes={relationTreeNodes(
          [event],
          t,
          inspectedEntityId,
          eventLaneMode,
        )}
      />
    </article>
  );
}

function relationTreeNodes(
  events: readonly NormalizedEvent[],
  t: (key: string) => string,
  inspectedEntityId: string,
  eventLaneMode: EventLaneMode,
): RelationNode[] {
  const nodes: RelationNode[] = [];
  const seen = new Set<string>();
  function append(
    ariaLabel: string,
    entityId: string,
    fallback: string,
    testId: string,
    role: string,
    allowInspectedEntity = false,
  ): void {
    if (
      !entityId
      || (!allowInspectedEntity && entityId === inspectedEntityId)
    ) {
      return;
    }
    const key = `${role}:${entityId}`;
    if (seen.has(key)) return;
    seen.add(key);
    nodes.push({
      ariaLabel,
      entityId,
      fallback,
      relationLabel: ariaLabel,
      testId,
    });
  }
  for (const event of events) {
    const sourceId = event.sourceId || event.triggerSourceId;
    const inspectedSource =
      inspectedEntityId === sourceId
      || inspectedEntityId === event.triggerSourceId;
    if (inspectedSource || eventLaneMode === "source") {
      for (const targetId of event.targetIds) {
        append(
          t("target"),
          targetId,
          t("targetNotRecorded"),
          "event-target-entity",
          "target",
          eventLaneMode === "source",
        );
      }
      for (const removedTargetId of event.removedTargetIds) {
        append(
          t("removedTarget"),
          removedTargetId,
          t("targetNotRecorded"),
          "event-removed-target-entity",
          "removed",
          eventLaneMode === "source",
        );
      }
      continue;
    }
    append(
      t("source"),
      sourceId,
      t("sourceNotRecorded"),
      "event-source-entity",
      "source",
    );
    if (
      event.triggerSourceId
      && event.triggerSourceId !== sourceId
    ) {
      append(
        t("triggerSource"),
        event.triggerSourceId,
        t("sourceNotRecorded"),
        "event-trigger-source-entity",
        "trigger",
      );
    }
  }
  return nodes;
}

function DirectDamageGroupRow({
  amount,
  entityById,
  eventLaneMode,
  events,
  inspectedEntityId,
  t,
}: {
  amount: number;
  entityById: ReadonlyMap<string, NormalizedEntity>;
  eventLaneMode: EventLaneMode;
  events: readonly NormalizedEvent[];
  inspectedEntityId: string;
  t: (key: string) => string;
}): React.JSX.Element {
  return (
    <article
      className="border-b border-border/45 bg-surface-raised/45 px-2.5 py-2 last:border-b-0"
      data-bpp-event-id={events[0]?.id}
      data-bpp-event-ids={events.map((event) => event.id).join(" ")}
      data-bpp-test-id="focused-cluster-event"
    >
      <div className="flex min-h-control-xs items-center gap-1.5">
        <NativeOrSemanticIcon
          nativeClassName="size-icon-lg"
          nativeUrl={events.find((event) => Boolean(event.icon))?.icon ?? ""}
          semanticClassName="size-icon-lg"
          testId="frame-event-native-icon"
          token="damage"
        />
        <strong
          className="truncate text-compact text-foreground"
          data-bpp-test-id="frame-event-kind"
        >
          {t("damageDirect")}
        </strong>
        {events.some((event) => event.isCritical) && (
          <Badge
            className="!h-4 !rounded-tag border-damage/35 bg-damage/10 !px-1 !py-0 !text-nano leading-none text-damage"
            data-bpp-test-id="frame-event-critical"
            variant="outline"
          >
            {t("critical")}
          </Badge>
        )}
        <strong
          className="ml-auto shrink-0 font-mono text-compact text-brand-soft"
          data-bpp-test-id="frame-event-amount"
        >
          {formatNumber(amount)}
        </strong>
      </div>
      <EventRelationTree
        entityById={entityById}
        eventLaneMode={eventLaneMode}
        nodes={relationTreeNodes(
          events,
          t,
          inspectedEntityId,
          eventLaneMode,
        )}
      />
    </article>
  );
}

export function FrameInspector({
  model,
  entityId,
  eventLaneMode,
  frame,
  focusedEventIds,
  onClose,
  t,
  variant = "popover",
}: {
  model: ReportViewModel;
  entityId: string;
  eventLaneMode: EventLaneMode;
  frame: number | null;
  focusedEventIds: readonly string[];
  onClose?: () => void;
  t: (key: string) => string;
  variant?: "popover" | "hover";
}): React.JSX.Element {
  const inspectorRef = useRef<HTMLDivElement>(null);
  const [limit, setLimit] = useState(PAGE_SIZE);
  const focusKey = focusedEventIds.join("\u001f");
  useEffect(() => setLimit(PAGE_SIZE), [entityId, focusKey, frame]);
  const entityById = useMemo(
    () => new Map(model.entities.map((entity) => [entity.id, entity])),
    [model.entities],
  );
  const iconBySemanticKey = useMemo(() => {
    const icons = new Map<string, string>();
    for (const event of model.events) {
      if (
        event.iconSemanticKey
        && event.icon
        && !icons.has(event.iconSemanticKey)
      ) {
        icons.set(event.iconSemanticKey, event.icon);
      }
    }
    return icons;
  }, [model.events]);
  const inspectedEntity = entityById.get(entityId);
  const focusedIdSet = useMemo(
    () => new Set(focusedEventIds),
    [focusedEventIds],
  );
  const rawFrameEvents = useMemo(
    () => eventsAtFrame(model.events, frame),
    [frame, model.events],
  );
  const frameEvents = useMemo(
    () =>
      eventLaneMode === "source"
        ? resolveSourceModeAttributeEvents(rawFrameEvents)
        : rawFrameEvents,
    [eventLaneMode, rawFrameEvents],
  );
  const events = useMemo(
    () =>
      frameEvents.filter((event) =>
        focusedIdSet.has(event.id),
      ),
    [focusedIdSet, frameEvents],
  );
  const groups = useMemo(() => {
    const result = new Map<
      string,
      EventPresentation & { events: NormalizedEvent[] }
    >();
    for (const event of events) {
      const presentation = eventPresentation(event);
      const group = result.get(presentation.groupKey);
      if (group) group.events.push(event);
      else result.set(presentation.groupKey, {
        ...presentation,
        events: [event],
      });
    }
    return Array.from(result.values())
      .sort((left, right) => right.events.length - left.events.length);
  }, [events]);
  useLayoutEffect(() => {
    const viewport = inspectorRef.current?.querySelector<HTMLElement>(
      "[data-slot='scroll-area-viewport']",
    );
    if (viewport) viewport.scrollTop = 0;
  }, [entityId, focusKey, frame]);
  const first = events[0];
  const eventCountLabel = `${events.length} ${
    events.length === 1 ? t("eventSingular") : t("event")
  }`;
  const renderGroupEvents = (
    group: (typeof groups)[number],
  ): React.JSX.Element | React.JSX.Element[] => {
    const visibleEvents = group.events.slice(0, limit);
    const directDamageSummary =
      visibleEvents.length === group.events.length
        ? summarizeDirectDamageGroup(
          group.events,
          frameEvents,
          entityId,
        )
        : null;
    if (directDamageSummary) {
      return (
        <DirectDamageGroupRow
          amount={directDamageSummary.amount}
          entityById={entityById}
          eventLaneMode={eventLaneMode}
          events={group.events}
          inspectedEntityId={entityId}
          t={t}
        />
      );
    }
    return mergeInspectorEvents(visibleEvents).map((merged) => (
      <EventRow
        entityById={entityById}
        event={merged.event}
        eventLaneMode={eventLaneMode}
        fallbackIcon={iconBySemanticKey.get(
          eventAttributeSemantic(merged.event)?.nativeSemanticKey ?? "",
        )}
        inspectedEntityId={entityId}
        key={merged.key}
        mergedCount={merged.count}
        t={t}
      />
    ));
  };
  const singleGroup = groups.length === 1 ? groups[0] : undefined;
  const useGroupedAccordion = groups.length > 1 && events.length > 12;

  return (
    <div
      className={cn(
        "flex min-h-0 w-full flex-col overflow-hidden bg-popover",
        variant === "hover"
          ? "max-h-[min(26rem,calc(100vh-1rem))]"
          : "max-h-[min(32rem,calc(100vh-2rem))]",
      )}
      data-bpp-inspector-variant={variant}
      data-bpp-test-id="frame-inspector"
      ref={inspectorRef}
    >
      <header
        className="shrink-0 border-b border-border/70"
        data-bpp-test-id="frame-inspector-header"
      >
        <div
          className={cn(
            "flex min-h-16 items-center gap-2 pr-2.5",
            !inspectedEntity && "pl-2.5",
          )}
        >
          {inspectedEntity && (
            <EntityArt
              contentAlign="start"
              entity={inspectedEntity}
              itemFit="intrinsic"
              size="inspector"
              testId="frame-inspector-entity-art"
            />
          )}
          <div className="min-w-0 flex-1">
            <h2
              className="truncate text-body font-semibold text-foreground"
              data-bpp-test-id="frame-inspector-entity"
            >
              {inspectedEntity?.name || t("frameEvents")}
            </h2>
            <div
              className="mt-0.5 flex min-w-0 items-center gap-1.5 text-micro text-muted-foreground"
              data-bpp-test-id="frame-inspector-meta"
            >
              {inspectedEntity && (
                <>
                  <span
                    className="truncate"
                    data-bpp-test-id="frame-inspector-entity-type"
                  >
                    {entityTypeLabel(inspectedEntity.type, t)}
                  </span>
                  <span aria-hidden="true">·</span>
                </>
              )}
              <span data-bpp-test-id="frame-inspector-time">
                {first ? formatDuration(first.combatMs) : "—"}
              </span>
              <span aria-hidden="true">·</span>
              <span data-bpp-test-id="frame-inspector-frame">
                {frame === null ? "—" : `${t("frame")} ${frame}`}
              </span>
              {events.length > 1 && (
                <>
                  <span aria-hidden="true">·</span>
                  <span
                    className="truncate text-foreground/85"
                    data-bpp-test-id="frame-event-total"
                  >
                    {eventCountLabel}
                  </span>
                </>
              )}
              {events.length === 1 && (
                <span
                  className="sr-only"
                  data-bpp-test-id="frame-event-total"
                >
                  {eventCountLabel}
                </span>
              )}
            </div>
          </div>
          {variant === "popover" && onClose && (
            <ControlTooltip label={t("close")}>
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
            </ControlTooltip>
          )}
        </div>
      </header>
      <ScrollArea
        className={cn(
          "min-h-0",
          variant === "hover"
            ? "max-h-[min(21rem,calc(100vh-5rem))]"
            : "max-h-[min(27rem,calc(100vh-6rem))]",
        )}
        data-bpp-test-id="frame-event-list"
      >
        <div className="bg-background/20">
          {singleGroup ? (
            <section
              data-bpp-event-token={singleGroup.groupKey}
              data-bpp-test-id="focused-cluster-group"
            >
              {renderGroupEvents(singleGroup)}
            </section>
          ) : !useGroupedAccordion ? (
            groups.map((group) => (
              <section
                data-bpp-event-token={group.groupKey}
                data-bpp-test-id="focused-cluster-group"
                key={group.groupKey}
              >
                {renderGroupEvents(group)}
              </section>
            ))
          ) : (
            <Accordion
              className="divide-y divide-border/45"
              defaultValue={groups.map((group) => group.groupKey)}
              key={`${entityId}:${frame ?? "none"}:${focusKey}`}
              type="multiple"
            >
              {groups.map((group) => (
                <AccordionItem
                  className="rounded-none border-x-0 border-y-0 bg-transparent"
                  data-bpp-event-token={group.groupKey}
                  data-bpp-test-id="focused-cluster-group"
                  key={group.groupKey}
                  value={group.groupKey}
                >
                  <AccordionTrigger className="min-h-control-sm px-2.5 py-1.5 text-compact hover:bg-accent/45">
                    <SemanticIcon token={group.token} />
                    <span className="min-w-0 truncate">
                      {t(group.labelKey)}
                    </span>
                    <Badge
                      className="px-1.5 font-mono"
                      variant="secondary"
                    >
                      ×{group.events.length}
                    </Badge>
                  </AccordionTrigger>
                  <AccordionContent className="space-y-0 border-t border-border/45 p-0">
                    {renderGroupEvents(group)}
                  </AccordionContent>
                </AccordionItem>
              ))}
            </Accordion>
          )}
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
    </div>
  );
}
