import { Check, ChevronDown, Languages } from "lucide-react";
import { useMemo, useState } from "react";
import type { SupportedLocale, ReportViewModel } from "../../model/report.ts";
import type { NormalizedEntity } from "../../model/normalize.ts";
import type { ReportAction } from "../../app/report-reducer.ts";
import type { ReportState } from "../../app/report-state.ts";
import { formatDuration, formatNumber } from "../../i18n/format.ts";
import { EntityArt } from "../semantic/EntityArt.tsx";
import { Badge } from "../ui/badge.tsx";
import { Button } from "../ui/button.tsx";
import { ButtonGroup } from "../ui/button-group.tsx";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "../ui/popover.tsx";
import { Tabs, TabsList, TabsTrigger } from "../ui/tabs.tsx";
import { ControlTooltip } from "../ui/tooltip.tsx";

const LOCALE_OPTIONS: readonly {
  locale: SupportedLocale;
  compactLabel: string;
  label: string;
}[] = [
  { locale: "zh-CN", compactLabel: "中", label: "简体中文" },
  { locale: "zh-Hant", compactLabel: "繁", label: "繁體中文" },
  { locale: "en", compactLabel: "EN", label: "English" },
];

function heroForSide(
  model: ReportViewModel,
  side: "player" | "opponent",
): NormalizedEntity | null {
  return (
    model.entities.find(
      (entity) =>
        entity.type.toLowerCase() === "hero"
        && entity.side.toLowerCase().includes(side),
    ) ?? null
  );
}

function compactProductGenerator(productGenerator: string): string {
  const match = /^BazaarPlusPlus\s+(\d+\.\d+\.\d+)/u.exec(productGenerator);
  return match ? `BPP ${match[1]}` : productGenerator;
}

export function ReportHeader({
  model,
  productGenerator,
  state,
  dispatch,
  t,
}: {
  model: ReportViewModel;
  productGenerator: string;
  state: ReportState;
  dispatch: React.Dispatch<ReportAction>;
  t: (key: string) => string;
}): React.JSX.Element {
  const [localeOpen, setLocaleOpen] = useState(false);
  const playerHero = useMemo(() => heroForSide(model, "player"), [model]);
  const opponentHero = useMemo(
    () => heroForSide(model, "opponent"),
    [model],
  );
  const productLabel = useMemo(
    () => compactProductGenerator(productGenerator),
    [productGenerator],
  );
  const outcomeLabel =
    model.outcome === "win" || model.outcome === "victory"
      ? t("win")
      : model.outcome === "loss" || model.outcome === "defeat"
        ? t("loss")
        : model.outcome === "draw"
          ? t("draw")
          : t("unknownOutcome");
  const isWin = model.outcome === "win" || model.outcome === "victory";
  const selectedLocale =
    LOCALE_OPTIONS.find(({ locale }) => locale === state.locale)
    ?? LOCALE_OPTIONS[0];
  return (
    <header className="relative z-30 flex h-12 shrink-0 items-center gap-1 rounded-panel border border-border border-b-foreground/10 bg-linear-to-b from-surface-raised to-surface px-2 shadow-panel sm:gap-2 sm:px-3 lg:gap-3">
      <div className="hidden min-w-0 flex-1 items-center gap-2 sm:flex">
        {playerHero && <EntityArt entity={playerHero} size="compact" />}
        <h1
          className="min-w-0 truncate font-display text-headline font-semibold tracking-tight text-foreground lg:text-title"
          data-bpp-test-id="match-title"
          title={`${model.playerName} vs ${model.opponentName}`}
        >
          {model.playerName}
          <span className="mx-1.5 font-sans font-normal text-muted-foreground">
            {" vs "}
          </span>
          {model.opponentName}
        </h1>
        {opponentHero && <EntityArt entity={opponentHero} size="compact" />}
        <Badge
          className="hidden lg:inline-flex"
          data-bpp-test-id="match-outcome"
          variant={isWin ? "success" : "destructive"}
        >
          {outcomeLabel}
        </Badge>
        <span className="hidden shrink-0 font-data text-compact tabular-nums text-muted-foreground xl:inline">
          {formatDuration(model.durationMs)}
        </span>
      </div>

      <Tabs
        className="w-44 shrink-0 sm:ml-auto"
        onValueChange={(value) =>
          dispatch({
            type: "select-tab",
            tab: value as ReportState["tab"],
          })
        }
        value={state.tab}
      >
        <TabsList
          aria-label={t("timelineTitle")}
          className="w-full"
          variant="underline"
        >
          {(["timeline", "statistics"] as const).map((tab) => (
            <TabsTrigger
              className="w-1/2 flex-none px-2"
              data-bpp-test-id={`report-tab-${tab}`}
              key={tab}
              value={tab}
              variant="underline"
            >
              {t(tab === "timeline" ? "timelineTab" : "statisticsTab")}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      <div className="hidden items-center gap-2 text-compact text-muted-foreground lg:flex">
        <span className="font-data tabular-nums">
          {formatNumber(model.events.length)} {t("event")}
        </span>
      </div>

      <span
        aria-label={productGenerator}
        className="max-w-24 shrink-0 truncate font-mono text-nano text-muted-foreground/70"
        data-bpp-test-id="report-product-info"
        title={productGenerator}
      >
        {productLabel}
      </span>

      <ButtonGroup
        className="shrink-0"
        data-bpp-test-id="header-utility-toolbar"
      >
        <Popover onOpenChange={setLocaleOpen} open={localeOpen}>
          <ControlTooltip label={t("language")}>
            <PopoverTrigger asChild>
              <Button
                aria-label={t("language")}
                className="min-w-16 gap-1 px-2"
                data-bpp-test-id="locale-switch"
                size="sm"
                type="button"
                variant="outline"
              >
                <Languages className="hidden size-icon-sm sm:block" />
                <span className="text-compact font-medium">
                  {selectedLocale.compactLabel}
                </span>
                <ChevronDown className="size-icon-xs text-muted-foreground" />
              </Button>
            </PopoverTrigger>
          </ControlTooltip>
          <PopoverContent
            align="end"
            className="w-36 p-1"
            data-bpp-test-id="locale-popover"
            side="bottom"
          >
            <div aria-label={t("language")} role="radiogroup">
              {LOCALE_OPTIONS.map(({ locale, label }) => {
                const selected = locale === state.locale;
                return (
                  <Button
                    aria-checked={selected}
                    className="w-full justify-between px-2"
                    data-bpp-test-id={`locale-option-${locale}`}
                    key={locale}
                    onClick={() => {
                      dispatch({ type: "select-locale", locale });
                      setLocaleOpen(false);
                    }}
                    role="radio"
                    size="sm"
                    type="button"
                    variant={selected ? "secondary" : "ghost"}
                  >
                    <span lang={locale}>{label}</span>
                    <Check
                      className={selected
                        ? "size-icon-xs"
                        : "size-icon-xs opacity-0"}
                    />
                  </Button>
                );
              })}
            </div>
          </PopoverContent>
        </Popover>
      </ButtonGroup>
    </header>
  );
}
