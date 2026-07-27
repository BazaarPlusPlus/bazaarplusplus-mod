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
  const quantified = row.quantifiedCounts[column.key] ?? 0;
  const partial = quantified > 0 && quantified < count;
  const unsignedAmount =
    column.unit === "ms"
      ? formatMilliseconds(amount)
      : column.unit === "percent"
        ? `${formatCompactNumber(amount)}%`
        : formatCompactNumber(amount);
  const formattedAmount =
    column.aggregation === "signed" && amount > 0
      ? `+${unsignedAmount}`
      : unsignedAmount;
  const partialPrefix =
    partial && column.aggregation === "absolute" ? "≥" : "";
  const metricLabel = t(column.label);
  const amountLabel = t(
    column.aggregation === "signed"
      ? "activityNetChange"
      : "activityAmount",
  );
  const countLabel = t("activityCount");
  const coverage = t("activityQuantifiedCoverage")
    .replace("{known}", formatNumber(quantified))
    .replace("{total}", formatNumber(count));
  const accessibleValue = [
    metricLabel,
    column.quantitative && quantified > 0
      ? `${amountLabel} ${partialPrefix}${formattedAmount}`
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
