import {
  formatCompactNumber,
  formatMilliseconds,
  formatNumber,
} from "../../i18n/format.ts";
import type {
  ActivityColumn,
  EntityActivityRow,
} from "../../statistics/aggregate.ts";
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
  const details = row.amountDetails[column.key];
  const quantified = row.quantifiedCounts[column.key] ?? 0;
  const partial = quantified > 0 && quantified < count;
  const signed = column.aggregation === "signed";
  const formattedAmount = formatActivityAmount(column, amount, signed);
  const averageAmount = quantified > 0 ? amount / quantified : 0;
  const formattedAverage = formatActivityAmount(
    column,
    averageAmount,
    signed,
  );
  const hasRecordedRange =
    signed
    && details?.firstRecordedValue !== null
    && details?.firstRecordedValue !== undefined
    && details.lastRecordedValue !== null;
  const recordedRange = hasRecordedRange
    ? `${
      formatActivityAmount(column, details.firstRecordedValue ?? 0, false)
    } → ${
      formatActivityAmount(column, details.lastRecordedValue ?? 0, false)
    }`
    : "";
  const increaseAmount = details?.increaseAmount ?? 0;
  const increaseCount = details?.increaseCount ?? 0;
  const decreaseAmount = details?.decreaseAmount ?? 0;
  const decreaseCount = details?.decreaseCount ?? 0;
  const formattedIncrease = formatActivityAmount(
    column,
    increaseAmount,
    true,
  );
  const formattedDecrease = formatActivityAmount(
    column,
    decreaseAmount,
    true,
  );
  const partialPrefix =
    partial && column.aggregation === "absolute" ? "≥" : "";
  const metricLabel = t(column.label);
  const amountLabel = t(
    column.aggregation === "signed"
      ? "activityNetChange"
      : "activityAmount",
  );
  const countLabel = t("activityCount");
  const averageLabel = t(
    signed ? "activityAverageChange" : "activityAverageAmount",
  );
  const coverage = t("activityQuantifiedCoverage")
    .replace("{known}", formatNumber(quantified))
    .replace("{total}", formatNumber(count));
  const accessibleValue = [
    metricLabel,
    column.quantitative && quantified > 0
      ? `${amountLabel} ${partialPrefix}${formattedAmount}`
      : "",
    hasRecordedRange
      ? `${t("activityRecordedValue")} ${recordedRange}`
      : "",
    increaseCount > 0
      ? `${t("activityIncreases")} ${formattedIncrease} ×${
        formatNumber(increaseCount)
      }`
      : "",
    decreaseCount > 0
      ? `${t("activityDecreases")} ${formattedDecrease} ×${
        formatNumber(decreaseCount)
      }`
      : "",
    quantified > 1 ? `${averageLabel} ${formattedAverage}` : "",
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
          {partialPrefix}
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
          className="inline-flex min-h-control-sm w-full cursor-help items-center justify-end outline-none focus-visible:ring-1 focus-visible:ring-brand-soft"
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
            className="size-icon-md shrink-0"
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
                {partialPrefix}
                {formattedAmount}
              </dd>
            </>
          )}
          {hasRecordedRange && (
            <>
              <dt className="text-micro text-muted-foreground">
                {t("activityRecordedValue")}
              </dt>
              <dd
                className="text-right font-mono text-compact font-semibold tabular-nums text-foreground"
                data-bpp-test-id="statistics-activity-recorded-range"
              >
                {recordedRange}
              </dd>
            </>
          )}
          {increaseCount > 0 && (
            <>
              <dt className="text-micro text-muted-foreground">
                {t("activityIncreases")}
              </dt>
              <dd
                className="inline-flex items-baseline justify-end gap-1.5 text-right font-mono tabular-nums text-foreground"
                data-bpp-test-id="statistics-activity-increase"
              >
                <strong className="text-compact text-success">
                  {formattedIncrease}
                </strong>
                <small className="text-nano text-muted-foreground">
                  ×{formatNumber(increaseCount)}
                </small>
              </dd>
            </>
          )}
          {decreaseCount > 0 && (
            <>
              <dt className="text-micro text-muted-foreground">
                {t("activityDecreases")}
              </dt>
              <dd
                className="inline-flex items-baseline justify-end gap-1.5 text-right font-mono tabular-nums text-foreground"
                data-bpp-test-id="statistics-activity-decrease"
              >
                <strong className="text-compact text-destructive">
                  {formattedDecrease}
                </strong>
                <small className="text-nano text-muted-foreground">
                  ×{formatNumber(decreaseCount)}
                </small>
              </dd>
            </>
          )}
          {quantified > 1 && (
            <>
              <dt className="text-micro text-muted-foreground">
                {averageLabel}
              </dt>
              <dd
                className="text-right font-mono text-compact font-semibold tabular-nums text-foreground"
                data-bpp-test-id="statistics-activity-average"
              >
                {formattedAverage}
              </dd>
            </>
          )}
          <dt className="text-micro text-muted-foreground">{countLabel}</dt>
          <dd className="text-right font-mono text-compact font-semibold tabular-nums text-foreground">
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
