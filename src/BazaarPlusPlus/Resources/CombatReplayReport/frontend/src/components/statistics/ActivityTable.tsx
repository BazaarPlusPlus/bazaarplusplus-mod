import { ArrowDown, ArrowUp } from "lucide-react";
import { useMemo } from "react";
import { cn } from "../../lib/utils.ts";
import {
  formatCompactNumber,
  formatMilliseconds,
  formatNumber,
} from "../../i18n/format.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  ACTIVITY_COLUMNS,
  buildEntityActivity,
  compareActivityRows,
  type ActivityColumn,
  type ActivitySortState,
  type CombatSide,
  type EntityActivityRow,
} from "../../statistics/aggregate.ts";
import type { ReportAction } from "../../app/report-reducer.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { SemanticIcon } from "../semantic/SemanticIcon.tsx";
import { Button } from "../ui/button.tsx";
import { Card } from "../ui/card.tsx";
import { Checkbox } from "../ui/checkbox.tsx";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "../ui/table.tsx";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "../ui/tooltip.tsx";

function columnIconUrl(
  model: ReportViewModel,
  column: ActivityColumn,
): string {
  if (!column.semanticKey) return "";
  return (
    model.events.find(
      (event) =>
        event.iconSemanticKey === column.semanticKey && event.icon,
    )?.icon ?? ""
  );
}

function nextSort(
  current: ActivitySortState,
  column: ActivityColumn | "triggers" | "entity",
): ActivitySortState {
  const key = typeof column === "string" ? column : column.key;
  const metric =
    typeof column === "string" || !column.quantitative
      ? "count"
      : "amount";
  return {
    key,
    metric,
    direction:
      current.key === key && current.direction === "desc" ? "asc" : "desc",
  };
}

function ActivityValue({
  row,
  column,
  t,
}: {
  row: EntityActivityRow;
  column: ActivityColumn;
  t: (key: string) => string;
}): React.JSX.Element {
  const count = row.counts[column.key] ?? 0;
  const amount = row.amounts[column.key] ?? 0;
  const quantified = row.quantifiedCounts[column.key] ?? 0;
  const partial = quantified > 0 && quantified < count;
  const formattedAmount =
    column.unit === "ms"
      ? formatMilliseconds(amount)
      : formatCompactNumber(amount);
  const metricLabel = t(column.label);
  const amountLabel = t("activityAmount");
  const countLabel = t("activityCount");
  const coverage = t("activityQuantifiedCoverage")
    .replace("{known}", formatNumber(quantified))
    .replace("{total}", formatNumber(count));
  const accessibleValue = [
    metricLabel,
    column.quantitative && quantified > 0
      ? `${amountLabel} ${partial ? "≥" : ""}${formattedAmount}`
      : "",
    `${countLabel} ${formatNumber(count)}`,
  ]
    .filter(Boolean)
    .join(" · ");

  let visibleValue: React.JSX.Element;
  if (count === 0) {
    visibleValue = <span className="text-muted-foreground/25">·</span>;
  } else if (column.quantitative && quantified > 0) {
    visibleValue = (
      <span className="inline-flex flex-col items-end leading-none">
        <strong className="font-mono text-compact text-foreground">
          {partial ? "≥" : ""}
          {formattedAmount}
        </strong>
        <small className="mt-1 font-mono text-nano text-muted-foreground">
          ×{formatNumber(count)}
        </small>
      </span>
    );
  } else {
    visibleValue = (
      <strong className="font-mono text-compact text-foreground/85">
        ×{formatNumber(count)}
      </strong>
    );
  }

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span
          aria-label={accessibleValue}
          className="inline-flex min-h-8 w-full cursor-help items-center justify-end outline-none focus-visible:ring-1 focus-visible:ring-brand-soft"
          data-bpp-test-id={`statistics-activity-value-${column.key}`}
          tabIndex={0}
        >
          {visibleValue}
        </span>
      </TooltipTrigger>
      <TooltipContent
        className="min-w-48 overflow-hidden p-0"
        data-bpp-test-id="statistics-activity-cell-tooltip"
        side="top"
        sideOffset={8}
      >
        <div className="flex items-center gap-2 border-b border-border/60 px-3 py-2">
          <SemanticIcon
            className="size-4 shrink-0"
            token={column.token ?? column.key}
          />
          <strong className="text-compact text-foreground">
            {metricLabel}
          </strong>
        </div>
        <dl className="grid grid-cols-[auto_auto] items-baseline gap-x-5 gap-y-1 px-3 py-2">
          {column.quantitative && quantified > 0 && (
            <>
              <dt className="text-micro text-muted-foreground">
                {amountLabel}
              </dt>
              <dd className="text-right font-mono text-compact font-semibold text-foreground">
                {partial ? "≥" : ""}
                {formattedAmount}
              </dd>
            </>
          )}
          <dt className="text-micro text-muted-foreground">{countLabel}</dt>
          <dd className="text-right font-mono text-compact font-semibold text-foreground">
            {formatNumber(count)}
          </dd>
        </dl>
        {column.quantitative && count > 0 && (
          <p className="border-t border-border/50 px-3 py-1.5 text-nano text-muted-foreground">
            {quantified === 0
              ? t("activityAmountUnavailable")
              : partial
                ? `${t("activityPartialAmount")} · ${coverage}`
                : coverage}
          </p>
        )}
      </TooltipContent>
    </Tooltip>
  );
}

function GroupHeader({
  side,
  model,
  rows,
  columnCount,
  t,
}: {
  side: CombatSide;
  model: ReportViewModel;
  rows: EntityActivityRow[];
  columnCount: number;
  t: (key: string) => string;
}): React.JSX.Element {
  const total = rows.reduce((sum, row) => sum + row.triggers, 0);
  return (
    <TableRow className={cn("bpp-activity-group", `bpp-side-${side}`)}>
      <TableCell
        className="sticky left-0 top-9 z-20 border-y border-border/60 bg-card px-3 py-2 shadow-[0_1px_0_var(--color-border)]"
        colSpan={columnCount}
        data-bpp-test-id={`statistics-activity-group-${side}`}
      >
        <div className="flex items-center gap-2">
          <strong className="text-body text-foreground">
            {side === "player" ? model.playerName : model.opponentName}
          </strong>
          <span
            className={cn(
              "text-nano font-bold uppercase tracking-wider",
              side === "player" ? "text-player" : "text-opponent",
            )}
          >
            {t(side)}
          </span>
          <span className="ml-auto font-mono text-micro text-muted-foreground">
            {formatNumber(total)} {t("activityTriggers")}
          </span>
        </div>
      </TableCell>
    </TableRow>
  );
}

export function ActivityTable({
  model,
  sort,
  groupBySide,
  dispatch,
  t,
}: {
  model: ReportViewModel;
  sort: ActivitySortState;
  groupBySide: boolean;
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  const rows = useMemo(() => buildEntityActivity(model), [model]);
  const columnByKey = useMemo(
    () => new Map(ACTIVITY_COLUMNS.map((column) => [column.key, column])),
    [],
  );
  const sortedRows = useMemo(
    () =>
      rows
        .slice()
        .sort((left, right) =>
          compareActivityRows(left, right, sort, columnByKey),
        ),
    [columnByKey, rows, sort],
  );
  const groups = useMemo(() => {
    return (["player", "opponent"] as const)
      .map((side) => ({
        side,
        rows: sortedRows.filter((row) => row.side === side),
      }))
      .filter((group) => group.rows.length > 0);
  }, [sortedRows]);
  const sortButton = (
    column: ActivityColumn | "triggers" | "entity",
    label: string,
  ): React.JSX.Element => {
    const key = typeof column === "string" ? column : column.key;
    const active = sort.key === key;
    const iconUrl =
      typeof column === "string" ? "" : columnIconUrl(model, column);
    return (
      <Button
        aria-label={`${label} · ${
          active && sort.direction === "desc"
            ? t("activitySortAscending")
            : t("activitySortDescending")
        }`}
        className={cn(
          "h-9 w-full justify-end gap-1.5 rounded-none border-0 px-3 text-micro text-muted-foreground hover:text-foreground",
          key === "entity" && "justify-start",
          active
            && "bg-brand/7 text-brand-soft shadow-[inset_0_-2px_0_var(--color-brand-soft)]",
        )}
        data-bpp-test-id={`statistics-activity-sort-${key}`}
        onClick={() =>
          dispatch({
            type: "set-activity-sort",
            sort: nextSort(sort, column),
          })
        }
        size="sm"
        style={{ borderRadius: 0 }}
        type="button"
        variant="ghost"
      >
        {typeof column !== "string" && (
          iconUrl ? (
            <img
              alt=""
              className="size-4 object-contain"
              src={iconUrl}
            />
          ) : (
            <SemanticIcon
              className="size-3.5"
              token={column.token ?? column.key}
            />
          )
        )}
        <span className="whitespace-nowrap">{label}</span>
        <span
          aria-hidden="true"
          className="grid size-3 shrink-0 place-items-center"
        >
          {sort.direction === "desc"
            ? (
              <ArrowDown
                className={cn("size-3", !active && "invisible")}
              />
            )
            : (
              <ArrowUp
                className={cn("size-3", !active && "invisible")}
              />
            )}
        </span>
      </Button>
    );
  };
  const columnCount = ACTIVITY_COLUMNS.length + 2;

  return (
    <Card className="gap-0 border-foreground/7 bg-card/70 shadow-none">
      <header className="border-b border-border/60 px-4 py-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="font-display text-heading font-semibold text-foreground">
            {t("entityActivityTitle")}
          </h2>
          <label className="inline-flex h-7 shrink-0 cursor-pointer items-center gap-2 rounded-panel border border-border/55 bg-background/35 px-2.5 text-micro font-medium text-muted-foreground transition-colors hover:bg-accent/55 hover:text-foreground">
            <Checkbox
              checked={groupBySide}
              data-bpp-test-id="statistics-activity-group-by-side"
              onCheckedChange={(checked) =>
                dispatch({
                  type: "set-activity-group-by-side",
                  value: checked === true,
                })
              }
            />
            <span>{t("activityGroupBySide")}</span>
          </label>
        </div>
        <div className="min-w-0">
          <p className="mt-0.5 text-compact text-muted-foreground">
            {t("entityActivityHint")}
          </p>
        </div>
      </header>
      <div
        className="max-h-[min(58vh,620px)] overflow-auto"
        data-bpp-test-id="statistics-entity-activity-scroll"
      >
        <Table
          className="min-w-[1120px]"
          containerClassName="overflow-visible"
          data-bpp-test-id="statistics-entity-activity"
        >
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead
                aria-sort={
                  sort.key === "entity"
                    ? sort.direction === "desc"
                      ? "descending"
                      : "ascending"
                    : undefined
                }
                className="sticky left-0 top-0 z-30 min-w-[210px] bg-card p-0"
              >
                {sortButton("entity", t("activityEntity"))}
              </TableHead>
              <TableHead
                aria-sort={
                  sort.key === "triggers"
                    ? sort.direction === "desc"
                      ? "descending"
                      : "ascending"
                    : undefined
                }
                className="sticky top-0 z-20 min-w-[96px] bg-card p-0"
              >
                {sortButton("triggers", t("activityTriggers"))}
              </TableHead>
              {ACTIVITY_COLUMNS.map((column) => (
                <TableHead
                  aria-sort={
                    sort.key === column.key
                      ? sort.direction === "desc"
                        ? "descending"
                        : "ascending"
                      : undefined
                  }
                  className="sticky top-0 z-20 min-w-[112px] bg-card p-0"
                  key={column.key}
                >
                  {sortButton(column, t(column.label))}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {groupBySide
              ? groups.map((group) => (
                <GroupRows
                  columnCount={columnCount}
                  dispatch={dispatch}
                  group={group}
                  key={group.side}
                  model={model}
                  t={t}
                />
              ))
              : (
                <ActivityRows
                  dispatch={dispatch}
                  rows={sortedRows}
                  testIdPrefix="all"
                  t={t}
                />
              )}
          </TableBody>
        </Table>
      </div>
    </Card>
  );
}

function GroupRows({
  group,
  model,
  columnCount,
  dispatch,
  t,
}: {
  group: { side: CombatSide; rows: EntityActivityRow[] };
  model: ReportViewModel;
  columnCount: number;
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  return (
    <>
      <GroupHeader
        columnCount={columnCount}
        model={model}
        rows={group.rows}
        side={group.side}
        t={t}
      />
      <ActivityRows
        dispatch={dispatch}
        rows={group.rows}
        testIdPrefix={group.side}
        t={t}
      />
    </>
  );
}

function ActivityRows({
  rows,
  testIdPrefix,
  dispatch,
  t,
}: {
  rows: EntityActivityRow[];
  testIdPrefix: string;
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  const selectEntity = (entityId: string): void => {
    dispatch({
      type: "select-entity",
      entityId,
    });
  };

  return (
    <>
      {rows.map((row, index) => (
        <TableRow
          aria-label={`${row.entity.name} · ${t("activityJumpHint")}`}
          className={cn(
            "group cursor-pointer border-b border-border/40 hover:bg-brand-soft/4",
            `bpp-side-${row.side}`,
          )}
          data-bpp-test-id={`statistics-activity-row-${testIdPrefix}-${index}`}
          data-entity-id={row.entity.id}
          data-entity-type={row.entity.type.toLowerCase()}
          data-side={row.side}
          key={row.entity.id}
          onClick={() => selectEntity(row.entity.id)}
          onKeyDown={(event) => {
            if (
              event.target !== event.currentTarget
              || (event.key !== "Enter" && event.key !== " ")
            ) {
              return;
            }
            event.preventDefault();
            selectEntity(row.entity.id);
          }}
          role="button"
          tabIndex={0}
        >
          <TableCell className="sticky left-0 z-10 bg-card/95 px-3 py-1 group-hover:bg-muted/70">
            <div className="flex min-w-0 items-center gap-2">
              <span className="grid h-11 w-[132px] shrink-0 place-items-center">
                <EntityArt entity={row.entity} size="activity" />
              </span>
              <span className="min-w-0">
                <strong className="block truncate text-compact text-foreground">
                  {row.entity.name}
                </strong>
                <small className="block text-nano capitalize text-muted-foreground">
                  {row.entity.type}
                </small>
              </span>
            </div>
          </TableCell>
          <TableCell className="px-2 py-1.5 text-right font-mono text-compact font-semibold text-foreground">
            {formatNumber(row.triggers)}
          </TableCell>
          {ACTIVITY_COLUMNS.map((column) => (
            <TableCell
              className="px-2 py-1.5 text-right"
              data-amount={row.amounts[column.key] ?? 0}
              data-count={row.counts[column.key] ?? 0}
              key={column.key}
            >
              <ActivityValue column={column} row={row} t={t} />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}
