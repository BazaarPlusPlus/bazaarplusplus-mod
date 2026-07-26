import { BarChart } from "echarts/charts";
import {
  GridComponent,
  TooltipComponent,
} from "echarts/components";
import * as echarts from "echarts/core";
import { CanvasRenderer } from "echarts/renderers";
import { useEffect, useMemo, useRef, useState } from "react";
import { formatCompactNumber } from "../../i18n/format.ts";
import {
  themeColors,
  themeLengthPx,
  themeValue,
  withAlpha,
} from "../../styles/theme.ts";
import type { CombatStatistics } from "../../statistics/aggregate.ts";
import { Card } from "../ui/card.tsx";

type ChartOption = Record<string, unknown>;

interface ChartInstance {
  setOption: (option: ChartOption, settings?: { notMerge?: boolean }) => void;
  resize: () => void;
  dispose: () => void;
}

interface EChartsRuntime {
  init: (
    host: HTMLDivElement,
    theme?: string,
    settings?: { renderer?: "canvas"; useDirtyRect?: boolean },
  ) => ChartInstance;
}

interface ComparisonChartProps {
  categories: string[];
  opponent: number[];
  player: number[];
  t: (key: string) => string;
  wholeNumbers?: boolean;
}

echarts.use([BarChart, GridComponent, TooltipComponent, CanvasRenderer]);

function numericValue(value: unknown): number {
  const candidate =
    Array.isArray(value) && value.length > 0
      ? value[value.length - 1]
      : value;
  const number = Number(candidate);
  return Number.isFinite(number) ? number : 0;
}

function useEChart(option: ChartOption): React.RefObject<HTMLDivElement | null> {
  const hostRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<ChartInstance | null>(null);
  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;
    const runtime = echarts as EChartsRuntime;
    const chart = runtime.init(host, undefined, {
      renderer: "canvas",
      useDirtyRect: true,
    });
    chartRef.current = chart;
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.echartsInitCount += 1;
    }
    const observer = new ResizeObserver(() => chart.resize());
    observer.observe(host);
    return () => {
      observer.disconnect();
      chartRef.current = null;
      chart.dispose();
      if (window.__BPP_VIEWER_TEST__) {
        window.__BPP_VIEWER_TEST__.echartsDisposeCount += 1;
      }
    };
  }, []);
  useEffect(() => {
    chartRef.current?.setOption(option, { notMerge: true });
  }, [option]);
  return hostRef;
}

const chartGrid = {
  bottom: 18,
  left: 72,
  right: 44,
  top: 6,
} as const;

function comparisonChartOption({
  categories,
  opponent,
  player,
  t,
  wholeNumbers = false,
}: ComparisonChartProps): ChartOption {
  const colors = themeColors();
  const radius = themeLengthPx("--radius-panel");
  const microText = themeLengthPx("--text-micro");
  const nanoText = themeLengthPx("--text-nano");
  const fontFamily = themeValue("--bpp-font-sans");
  const monoFamily = themeValue("--bpp-font-mono");
  const barStyle = (color: string) => ({
    backgroundStyle: {
      borderRadius: [0, radius, radius, 0],
      color: withAlpha(color, 0.055),
    },
    emphasis: {
      focus: "series",
      itemStyle: {
        shadowBlur: 9,
        shadowColor: withAlpha(color, 0.24),
      },
    },
    itemStyle: {
      borderRadius: [0, radius, radius, 0],
      color,
    },
    label: {
      color,
      distance: 6,
      fontFamily: monoFamily,
      fontSize: nanoText,
      fontWeight: 700,
      formatter: ({ value }: { value?: unknown }) => {
        const number = numericValue(value);
        return number === 0 ? "" : formatCompactNumber(number);
      },
      position: "right",
      show: true,
    },
    showBackground: true,
  });

  return {
    animation: false,
    aria: { enabled: true },
    backgroundColor: "transparent",
    grid: { ...chartGrid, containLabel: false },
    textStyle: { color: colors.foreground, fontFamily },
    xAxis: {
      axisLabel: {
        color: colors.faint,
        fontFamily: monoFamily,
        fontSize: nanoText,
        formatter: (value: unknown) =>
          formatCompactNumber(numericValue(value)),
        margin: 8,
      },
      axisLine: { show: false },
      axisTick: { show: false },
      minInterval: wholeNumbers ? 1 : undefined,
      splitLine: {
        lineStyle: {
          color: withAlpha(colors["muted-foreground"], 0.1),
          type: "dashed",
        },
      },
      type: "value",
    },
    yAxis: {
      axisLabel: {
        color: colors["muted-foreground"],
        fontFamily,
        fontSize: microText,
        fontWeight: 600,
        margin: 12,
      },
      axisLine: { show: false },
      axisTick: { show: false },
      data: categories,
      type: "category",
    },
    series: [
      {
        ...barStyle(colors.player),
        barCategoryGap: "36%",
        barGap: "20%",
        barMaxWidth: 12,
        data: player,
        name: t("player"),
        type: "bar",
      },
      {
        ...barStyle(colors.opponent),
        barCategoryGap: "36%",
        barGap: "20%",
        barMaxWidth: 12,
        data: opponent,
        name: t("opponent"),
        type: "bar",
      },
    ],
  };
}

function ComparisonLegend({
  t,
}: {
  t: (key: string) => string;
}): React.JSX.Element {
  return (
    <div
      className="flex shrink-0 items-center gap-3 text-micro text-muted-foreground"
      data-bpp-test-id="statistics-chart-legend"
    >
      <span className="inline-flex items-center gap-1.5">
        <i className="h-0.5 w-3 rounded-full bg-player" />
        {t("player")}
      </span>
      <span className="inline-flex items-center gap-1.5">
        <i className="h-0.5 w-3 rounded-full bg-opponent" />
        {t("opponent")}
      </span>
    </div>
  );
}

function ComparisonChart({
  categories,
  opponent,
  option,
  player,
  testId,
  t,
}: {
  categories: string[];
  opponent: number[];
  option: ChartOption;
  player: number[];
  testId: string;
  t: (key: string) => string;
}): React.JSX.Element {
  const ref = useEChart(option);
  const frameRef = useRef<HTMLDivElement>(null);
  const [hover, setHover] = useState<{
    alignBottom: boolean;
    alignRight: boolean;
    index: number;
    x: number;
    y: number;
  } | null>(null);

  const updateHover = (
    event: React.PointerEvent<HTMLDivElement>,
    index: number,
  ): void => {
    const bounds = frameRef.current?.getBoundingClientRect();
    if (!bounds) return;
    setHover({
      alignBottom: event.clientY > bounds.top + bounds.height / 2,
      alignRight: event.clientX > bounds.left + bounds.width / 2,
      index,
      x: event.clientX - bounds.left,
      y: event.clientY - bounds.top,
    });
  };

  return (
    <div
      className="relative h-32 min-w-0 px-1 pb-1"
      data-bpp-test-id={testId}
      ref={frameRef}
    >
      <div className="size-full" ref={ref} />
      <div
        aria-hidden="true"
        className="absolute grid"
        style={{
          bottom: chartGrid.bottom,
          gridTemplateRows: `repeat(${categories.length}, minmax(0, 1fr))`,
          left: chartGrid.left,
          right: chartGrid.right,
          top: chartGrid.top,
        }}
      >
        {categories.map((category, index) => (
          <div
            data-bpp-test-id={`statistics-chart-hit-${index}`}
            key={category}
            onPointerEnter={(event) => updateHover(event, index)}
            onPointerLeave={() => setHover(null)}
          />
        ))}
      </div>
      {hover ? (
        <div
          className="pointer-events-none absolute z-20 animate-in fade-in-0 zoom-in-95 motion-reduce:animate-none"
          style={{
            bottom: hover.alignBottom
              ? `calc(100% - ${hover.y}px + 0.5rem)`
              : undefined,
            left: hover.alignRight ? undefined : hover.x + 8,
            right: hover.alignRight
              ? `calc(100% - ${hover.x}px + 0.5rem)`
              : undefined,
            top: hover.alignBottom ? undefined : hover.y + 8,
          }}
        >
          <Card className="gap-0 border-brand-soft/35 bg-popover p-0 shadow-float">
            <div
              className="bpp-chart-tooltip"
              data-bpp-test-id="statistics-chart-tooltip"
            >
              <div className="bpp-chart-tooltip-title">
                {categories[hover.index]}
              </div>
              <div className="bpp-chart-tooltip-rows">
                <div className="bpp-chart-tooltip-row">
                  <span className="bpp-chart-tooltip-swatch bpp-chart-tooltip-swatch-player" />
                  <span className="bpp-chart-tooltip-label">
                    {t("player")}
                  </span>
                  <strong className="bpp-chart-tooltip-value">
                    {formatCompactNumber(player[hover.index])}
                  </strong>
                </div>
                <div className="bpp-chart-tooltip-row">
                  <span className="bpp-chart-tooltip-swatch bpp-chart-tooltip-swatch-opponent" />
                  <span className="bpp-chart-tooltip-label">
                    {t("opponent")}
                  </span>
                  <strong className="bpp-chart-tooltip-value">
                    {formatCompactNumber(opponent[hover.index])}
                  </strong>
                </div>
              </div>
            </div>
          </Card>
        </div>
      ) : null}
    </div>
  );
}

function ChartCard({
  children,
  t,
  title,
}: {
  children: React.ReactNode;
  t: (key: string) => string;
  title: string;
}): React.JSX.Element {
  return (
    <Card className="gap-0 overflow-hidden border-foreground/7 bg-card/70 shadow-none">
      <header className="flex min-h-control-md items-center justify-between gap-3 border-b border-border/55 px-3 py-1.5">
        <h3 className="min-w-0 truncate font-display text-body font-semibold text-foreground">
          {title}
        </h3>
        <ComparisonLegend t={t} />
      </header>
      {children}
    </Card>
  );
}

export function StatisticsCharts({
  statistics,
  t,
}: {
  statistics: CombatStatistics;
  t: (key: string) => string;
}): React.JSX.Element {
  const outputData = useMemo(
    () => ({
      categories: [t("damage"), t("heal"), t("shield")],
      opponent: [
        statistics.damageDealt.opponent,
        statistics.output.opponent[3],
        statistics.output.opponent[4],
      ],
      player: [
        statistics.damageDealt.player,
        statistics.output.player[3],
        statistics.output.player[4],
      ],
    }),
    [statistics, t],
  );
  const effectsData = useMemo(
    () => ({
      categories: [t("charge"), t("haste"), t("slow"), t("freeze")],
      opponent: statistics.effects.opponent,
      player: statistics.effects.player,
    }),
    [statistics, t],
  );
  const outputOption = useMemo(
    () => comparisonChartOption({ ...outputData, t }),
    [outputData, t],
  );
  const effectsOption = useMemo(
    () => comparisonChartOption({
      ...effectsData,
      t,
      wholeNumbers: true,
    }),
    [effectsData, t],
  );

  return (
    <section
      className="grid gap-2 md:grid-cols-2"
      data-bpp-test-id="statistics-chart-grid"
    >
      <ChartCard t={t} title={t("outputTotals")}>
        <ComparisonChart
          categories={outputData.categories}
          opponent={outputData.opponent}
          option={outputOption}
          player={outputData.player}
          testId="statistics-echarts-output"
          t={t}
        />
      </ChartCard>
      <ChartCard t={t} title={t("statusCounts")}>
        <ComparisonChart
          categories={effectsData.categories}
          opponent={effectsData.opponent}
          option={effectsOption}
          player={effectsData.player}
          testId="statistics-echarts-effects"
          t={t}
        />
      </ChartCard>
    </section>
  );
}
