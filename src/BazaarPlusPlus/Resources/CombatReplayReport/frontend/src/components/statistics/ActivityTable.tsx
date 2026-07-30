import { ArrowDown, ArrowUp } from "lucide-react";
import { useMemo } from "react";
import { cn } from "../../lib/utils.ts";
import type { ReportViewModel } from "../../model/report.ts";
import { indexSemanticIcons } from "../../model/semantic-icons.ts";
import {
  ACTIVITY_COLUMNS,
  buildEntityActivity,
  compareActivityRows,
  type ActivityColumn,
  type ActivitySortState,
} from "../../statistics/aggregate.ts";
import type { ReportAction } from "../../app/report-reducer.ts";
import { NativeOrSemanticIcon } from "../semantic/SemanticIcon.tsx";
import { Button } from "../ui/button.tsx";
import { Card } from "../ui/card.tsx";
import { Checkbox } from "../ui/checkbox.tsx";
import {
  Table,
  TableBody,
  TableHead,
  TableHeader,
  TableRow,
} from "../ui/table.tsx";
import { ActivityRows, GroupRows } from "./ActivityRows.tsx";
import {
  groupActivityRows,
  nextActivitySort,
} from "./activity-table-model.ts";

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
  const iconBySemanticKey = useMemo(() => {
    return indexSemanticIcons(model.events, model.semanticIcons);
  }, [model.events, model.semanticIcons]);
  const sortedRows = useMemo(
    () =>
      rows
        .slice()
        .sort((left, right) =>
          compareActivityRows(left, right, sort, columnByKey),
        ),
    [columnByKey, rows, sort],
  );
  const groups = useMemo(
    () => groupActivityRows(sortedRows),
    [sortedRows],
  );
  const sortButton = (
    column: ActivityColumn | "triggers" | "entity",
    label: string,
  ): React.JSX.Element => {
    const key = typeof column === "string" ? column : column.key;
    const active = sort.key === key;
    const iconUrl =
      typeof column === "string" || !column.semanticKey
        ? ""
        : iconBySemanticKey.get(column.semanticKey) ?? "";
    return (
      <Button
        aria-label={`${label} · ${
          active && sort.direction === "desc"
            ? t("activitySortAscending")
            : t("activitySortDescending")
        }`}
        className={cn(
          "w-full justify-end gap-1.5 px-3",
          key === "entity" && "justify-start",
          active
            && "bg-brand/7 text-brand-soft shadow-[inset_0_-2px_0_var(--color-brand-soft)]",
        )}
        data-bpp-test-id={`statistics-activity-sort-${key}`}
        onClick={() =>
          dispatch({
            type: "set-activity-sort",
            sort: nextActivitySort(sort, column),
          })
        }
        size="default"
        type="button"
        variant="tableHeader"
      >
        {typeof column !== "string" && (
          <NativeOrSemanticIcon
            nativeClassName="size-icon-md"
            nativeUrl={iconUrl}
            semanticClassName="size-icon-sm"
            testId={`statistics-activity-sort-native-icon-${key}`}
            token={column.token ?? column.key}
          />
        )}
        <span className="whitespace-nowrap text-micro">{label}</span>
        <span
          aria-hidden="true"
          className="grid size-icon-xs shrink-0 place-items-center"
        >
          {sort.direction === "desc"
            ? (
              <ArrowDown
                className={cn("size-icon-xs", !active && "invisible")}
              />
            )
            : (
              <ArrowUp
                className={cn("size-icon-xs", !active && "invisible")}
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
          <label className="inline-flex h-control-xs shrink-0 cursor-pointer items-center gap-2 rounded-panel border border-border/55 bg-background/35 px-2.5 text-micro font-medium text-muted-foreground transition-colors hover:bg-accent/55 hover:text-foreground">
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
          className="min-w-[2320px]"
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
                  group={group}
                  iconBySemanticKey={iconBySemanticKey}
                  key={group.side}
                  model={model}
                  t={t}
                />
              ))
              : (
                <ActivityRows
                  iconBySemanticKey={iconBySemanticKey}
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
