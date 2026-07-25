import type { ReportAction } from "../../app/report-reducer.ts";
import { formatNumber } from "../../i18n/format.ts";
import { cn } from "../../lib/utils.ts";
import type { ReportViewModel } from "../../model/report.ts";
import {
  ACTIVITY_COLUMNS,
  type CombatSide,
  type EntityActivityRow,
} from "../../statistics/aggregate.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import {
  TableCell,
  TableRow,
} from "../ui/table.tsx";
import { ActivityValue } from "./ActivityValue.tsx";

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

export function ActivityRows({
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

export function GroupRows({
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
