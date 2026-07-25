import { Activity, Crosshair, TimerReset } from "lucide-react";
import { useMemo } from "react";
import { cn } from "../../lib/utils.ts";
import { formatCompactNumber, formatDuration } from "../../i18n/format.ts";
import type { ReportViewModel } from "../../model/report.ts";
import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import {
  buildStatistics,
  type CombatSide,
} from "../../statistics/aggregate.ts";
import {
  buildBattleRecords,
  type BattleRecord,
} from "../../statistics/records.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { Card } from "../ui/card.tsx";
import { ActivityTable } from "./ActivityTable.tsx";
import { StatisticsCharts } from "./StatisticsCharts.tsx";

function recordValue(record: BattleRecord): string {
  if (record.unit === "milliseconds") {
    return formatDuration(record.value);
  }
  return formatCompactNumber(record.value);
}

function RecordCard({
  record,
  t,
}: {
  record: BattleRecord;
  t: (key: string) => string;
}): React.JSX.Element {
  const Icon =
    record.key === "biggest-hit"
      ? Crosshair
      : record.key === "control"
        ? TimerReset
        : Activity;
  return (
    <Card className="flex min-w-0 flex-row items-center gap-3 border-foreground/7 bg-linear-to-b from-surface-raised to-card px-3 py-2.5 shadow-none">
      {record.entity ? (
        <EntityArt entity={record.entity} size="compact" />
      ) : (
        <Icon className="size-5 text-brand-soft" />
      )}
      <div className="min-w-0">
        <p className="text-nano font-semibold uppercase tracking-wider text-muted-foreground">
          {t(record.label)}
        </p>
        <strong className="block font-mono text-headline text-foreground">
          {recordValue(record)}
        </strong>
        <p className="truncate text-micro text-muted-foreground">
          {record.entity?.name ?? "—"}
        </p>
      </div>
    </Card>
  );
}

function SideSummary({
  statistics,
  side,
  t,
}: {
  statistics: ReturnType<typeof buildStatistics>;
  side: CombatSide;
  t: (key: string) => string;
}): React.JSX.Element {
  return (
    <Card
      className={cn(
        "gap-0 border border-foreground/7 bg-linear-to-b from-surface-raised to-card p-3 shadow-none",
        side === "player"
          ? "border-t-player/80"
          : "border-t-opponent/70",
      )}
    >
      <div className="flex items-end gap-3">
        <strong className="text-body text-foreground">{t(side)}</strong>
        <div className="ml-auto text-right">
          <small className="block text-nano uppercase tracking-wider text-muted-foreground">
            {t("damageDealt")}
          </small>
          <strong
            className={cn(
              "font-mono text-metric",
              side === "player" ? "text-player" : "text-opponent",
            )}
          >
            {formatCompactNumber(statistics.damageDealt[side])}
          </strong>
        </div>
      </div>
      <dl className="mt-3 grid grid-cols-3 gap-2 text-micro">
        <div className="rounded-panel border border-border/45 bg-background/30 p-2">
          <dt className="text-muted-foreground">{t("damageReceived")}</dt>
          <dd className="mt-1 font-mono text-heading font-semibold text-foreground">
            {formatCompactNumber(statistics.output[side][1])}
          </dd>
        </div>
        <div className="rounded-panel border border-border/45 bg-background/30 p-2">
          <dt className="text-muted-foreground">{t("healingReceived")}</dt>
          <dd className="mt-1 font-mono text-heading font-semibold text-foreground">
            {formatCompactNumber(statistics.output[side][3])}
          </dd>
        </div>
        <div className="rounded-panel border border-border/45 bg-background/30 p-2">
          <dt className="text-muted-foreground">{t("shieldReceived")}</dt>
          <dd className="mt-1 font-mono text-heading font-semibold text-foreground">
            {formatCompactNumber(statistics.output[side][4])}
          </dd>
        </div>
      </dl>
    </Card>
  );
}

export function StatisticsView({
  model,
  state,
  dispatch,
  t,
}: {
  model: ReportViewModel;
  state: ReportState;
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  const statistics = useMemo(() => buildStatistics(model), [model]);
  const records = useMemo(() => buildBattleRecords(model), [model]);
  if (model.events.length === 0) {
    return (
      <section
        className="grid min-h-0 flex-1 place-items-center text-heading text-muted-foreground"
        data-bpp-test-id="statistics-section"
      >
        {t("statisticsEmpty")}
      </section>
    );
  }
  return (
    <section
      className="min-h-0 flex-1 overflow-y-auto rounded-panel border border-border bg-card/45 p-3"
      data-bpp-test-id="statistics-section"
    >
      <div className="mx-auto flex max-w-[1760px] flex-col gap-3">
        <div className="grid gap-3 lg:grid-cols-2">
          <SideSummary side="player" statistics={statistics} t={t} />
          <SideSummary side="opponent" statistics={statistics} t={t} />
        </div>
        {records.length > 0 && (
          <section className="grid gap-2 sm:grid-cols-3">
            {records.map((record) => (
              <RecordCard key={record.key} record={record} t={t} />
            ))}
          </section>
        )}
        <StatisticsCharts statistics={statistics} t={t} />
        <ActivityTable
          dispatch={dispatch}
          groupBySide={state.activityGroupBySide}
          model={model}
          sort={state.activitySort}
          t={t}
        />
      </div>
    </section>
  );
}
