import {
  formatCompactNumber,
  formatMilliseconds,
  formatNumber,
} from "../../i18n/format.ts";
import { cn } from "../../lib/utils.ts";
import type {
  ActivityColumn,
  ActivityTargetDetail,
  EntityActivityRow,
} from "../../statistics/aggregate.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { SemanticIcon } from "../semantic/SemanticIcon.tsx";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "../ui/tooltip.tsx";

function formatActivityAmount(
  column: ActivityColumn,
  value: number,
  signed: boolean,
): string {
  const formatted =
    column.unit === "ms"
      ? formatMilliseconds(value)
      : column.unit === "percent"
        ? `${formatCompactNumber(value)}%`
        : formatCompactNumber(value);
  return signed && value > 0 ? `+${formatted}` : formatted;
}

function targetTransition(
  column: ActivityColumn,
  detail: ActivityTargetDetail,
): string {
  if (
    column.aggregation !== "signed"
    || detail.firstPreviousValue === null
    || detail.lastCurrentValue === null
  ) {
    return "";
  }
  return `${
    formatActivityAmount(column, detail.firstPreviousValue, false)
  } → ${formatActivityAmount(column, detail.lastCurrentValue, false)}`;
}

function targetAmount(
  column: ActivityColumn,
  detail: ActivityTargetDetail,
): string {
  if (!column.quantitative || detail.quantifiedCount === 0) return "";
  const partialPrefix =
    detail.quantifiedCount < detail.count
    && column.aggregation === "absolute"
      ? "≥"
      : "";
  return `${partialPrefix}${
    formatActivityAmount(
      column,
      detail.amount,
      column.aggregation === "signed",
    )
  }`;
}

function TargetDetail({
  column,
  detail,
  t,
}: {
  column: ActivityColumn;
  detail: ActivityTargetDetail;
  t: (key: string) => string;
}): React.JSX.Element {
  const transition = targetTransition(column, detail);
  const amount = targetAmount(column, detail);
  const targetName = detail.target?.name ?? t("targetNotRecorded");
  const polarity =
    column.aggregation === "signed"
      ? detail.amount > 0
        ? "increase"
        : detail.amount < 0
          ? "decrease"
          : "neutral"
      : "neutral";

  return (
    <div
      className="grid min-w-0 grid-cols-[minmax(0,1fr)_auto] items-center gap-4 px-3 py-2"
      data-bpp-target-id={detail.targetId || undefined}
      data-bpp-test-id="statistics-activity-target-detail"
    >
      <span className="inline-flex min-w-0 items-center gap-2">
        {detail.target ? (
          <EntityArt entity={detail.target} size="compact" squareSlot />
        ) : (
          <span
            aria-hidden="true"
            className="grid size-6 shrink-0 place-items-center rounded-art border border-border/45 bg-muted/25 text-micro text-muted-foreground"
          >
            —
          </span>
        )}
        <span
          className="truncate text-compact text-foreground"
          data-bpp-test-id="statistics-activity-target-name"
          title={targetName}
        >
          {targetName}
        </span>
      </span>
      <span className="flex min-w-20 flex-col items-end font-mono tabular-nums">
        {transition && (
          <strong
            className="text-compact text-foreground"
            data-bpp-test-id="statistics-activity-target-transition"
          >
            {transition}
          </strong>
        )}
        <span className="inline-flex items-baseline gap-1.5">
          {amount && (
            <strong
              className={cn(
                "text-compact",
                polarity === "increase"
                  ? "text-success"
                  : polarity === "decrease"
                    ? "text-destructive"
                    : "text-foreground",
              )}
              data-bpp-test-id="statistics-activity-target-amount"
            >
              {amount}
            </strong>
          )}
          <small
            className="text-nano text-muted-foreground"
            data-bpp-test-id="statistics-activity-target-count"
          >
            ×{formatNumber(detail.count)}
          </small>
        </span>
      </span>
    </div>
  );
}

export function ActivityValue({
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
  const details = row.targetDetails[column.key] ?? [];
  const quantified = row.quantifiedCounts[column.key] ?? 0;
  const authoritative =
    Object.prototype.hasOwnProperty.call(row.authoritativeValues, column.key);
  const authoritativeValue = row.authoritativeValues[column.key] ?? 0;
  const partial = quantified > 0 && quantified < count;
  const signed = column.aggregation === "signed";
  const formattedAmount = formatActivityAmount(column, amount, signed);
  const partialPrefix =
    partial && column.aggregation === "absolute" ? "≥" : "";
  const tooltipTotal = authoritative
    ? formatCompactNumber(authoritativeValue)
    : count === 0
      ? "—"
      : column.quantitative && quantified > 0
        ? `${partialPrefix}${formattedAmount}`
        : `×${formatNumber(count)}`;
  const metricLabel = t(column.label);
  const targetSummary = details
    .map((detail) => {
      const targetName = detail.target?.name ?? t("targetNotRecorded");
      return [
        targetName,
        targetTransition(column, detail),
        targetAmount(column, detail),
        `×${formatNumber(detail.count)}`,
      ]
        .filter(Boolean)
        .join(" · ");
    })
    .join("; ");
  const coverage = t("activityQuantifiedCoverage")
    .replace("{known}", formatNumber(quantified))
    .replace("{total}", formatNumber(count));
  const accessibleValue = [
    metricLabel,
    authoritative
      ? `${t("activityPostCombatTotal")} ${tooltipTotal}`
      : "",
    details.length > 0
      ? `${t("activityTargetEffects")} ${targetSummary}`
      : t("activityNoTargetEffects"),
  ].filter(Boolean).join(" · ");

  const visibleValue =
    authoritative ? (
      authoritativeValue === 0 ? (
        <span className="text-muted-foreground/25">·</span>
      ) : (
        <strong
          className="font-mono text-compact text-foreground"
          data-bpp-authoritative="native-card-stats"
        >
          {formatCompactNumber(authoritativeValue)}
        </strong>
      )
    ) : count === 0 ? (
      <span className="text-muted-foreground/25">·</span>
    ) : column.quantitative && quantified > 0 ? (
      <span className="inline-flex flex-col items-end leading-none">
        <strong className="font-mono text-compact text-foreground">
          {partialPrefix}
          {formattedAmount}
        </strong>
        <small className="mt-1 font-mono text-nano text-muted-foreground">
          ×{formatNumber(count)}
        </small>
      </span>
    ) : (
      <strong className="font-mono text-compact text-foreground/85">
        ×{formatNumber(count)}
      </strong>
    );

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span
          aria-label={accessibleValue}
          className="inline-flex min-h-control-sm w-full cursor-help items-center justify-end outline-none focus-visible:ring-1 focus-visible:ring-brand-soft"
          data-bpp-test-id={`statistics-activity-value-${column.key}`}
          tabIndex={0}
        >
          {visibleValue}
        </span>
      </TooltipTrigger>
      <TooltipContent
        className="w-80 max-w-[calc(100vw-2rem)] overflow-hidden p-0"
        data-bpp-test-id="statistics-activity-cell-tooltip"
        side="top"
        sideOffset={8}
      >
        <div className="flex items-center gap-2 border-b border-border/60 px-3 py-2">
          <SemanticIcon
            className="size-icon-md shrink-0"
            token={column.token ?? column.key}
          />
          <strong className="text-compact text-foreground">
            {metricLabel}
          </strong>
        </div>
        <div className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-4 border-b border-border/50 bg-muted/15 px-3 py-2">
          <span
            className="text-micro font-semibold uppercase tracking-wide text-muted-foreground"
            data-bpp-test-id="statistics-activity-tooltip-total-label"
          >
            {authoritative ? t("activityPostCombatTotal") : t("activityAmount")}
          </span>
          <span className="flex flex-col items-end font-mono tabular-nums">
            <strong
              className="text-compact text-foreground"
              data-bpp-test-id="statistics-activity-tooltip-total"
            >
              {tooltipTotal}
            </strong>
            {!authoritative && column.quantitative && quantified > 0 && (
              <small
                className="text-nano text-muted-foreground"
                data-bpp-test-id="statistics-activity-tooltip-total-count"
              >
                ×{formatNumber(count)}
              </small>
            )}
          </span>
        </div>
        <div className="flex items-center justify-between bg-muted/25 px-3 py-1.5">
          <span className="text-micro font-semibold uppercase tracking-wide text-muted-foreground">
            {t("activityTargetEffects")}
          </span>
          <span className="font-mono text-nano tabular-nums text-muted-foreground">
            {formatNumber(details.length)}
          </span>
        </div>
        <div
          className="max-h-64 divide-y divide-border/45 overflow-y-auto"
          data-bpp-test-id="statistics-activity-target-list"
        >
          {details.length > 0 ? (
            details.map((detail) => (
              <TargetDetail
                column={column}
                detail={detail}
                key={detail.targetId || "target-not-recorded"}
                t={t}
              />
            ))
          ) : (
            <p className="px-3 py-2 text-micro text-muted-foreground">
              {t("activityNoTargetEffects")}
            </p>
          )}
        </div>
        {authoritative ? (
          <p className="border-t border-border/50 px-3 py-1.5 text-nano text-muted-foreground">
            {t("activityNativeTotal")}
          </p>
        ) : column.quantitative && count > 0 && (
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
