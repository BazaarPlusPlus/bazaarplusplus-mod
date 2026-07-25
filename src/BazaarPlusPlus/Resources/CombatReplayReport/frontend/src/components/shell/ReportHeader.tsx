import {
  HelpCircle,
  Languages,
} from "lucide-react";
import { useMemo } from "react";
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
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "../ui/tooltip.tsx";

const LOCALE_LABELS: Record<SupportedLocale, string> = {
  en: "EN",
  "zh-CN": "中",
  "zh-Hant": "繁",
};
const LOCALE_ORDER: SupportedLocale[] = ["zh-CN", "zh-Hant", "en"];

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

export function ReportHeader({
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
  const playerHero = useMemo(() => heroForSide(model, "player"), [model]);
  const opponentHero = useMemo(
    () => heroForSide(model, "opponent"),
    [model],
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
  const nextLocale = (): SupportedLocale => {
    const index = LOCALE_ORDER.indexOf(state.locale);
    return LOCALE_ORDER[(index + 1) % LOCALE_ORDER.length];
  };
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
          <span className="mx-1.5 font-normal text-muted-foreground">
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
        <span className="hidden shrink-0 font-mono text-compact text-muted-foreground xl:inline">
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
          className="h-8 w-full rounded-none border-0 border-b border-border/70 bg-transparent p-0"
        >
          {(["timeline", "statistics"] as const).map((tab) => (
            <TabsTrigger
              className="h-8 w-1/2 flex-none rounded-none border-b-2 border-transparent bg-transparent px-2 text-compact shadow-none hover:bg-foreground/4 hover:text-foreground data-[state=active]:border-brand-soft data-[state=active]:bg-transparent data-[state=active]:text-foreground data-[state=active]:shadow-none"
              data-bpp-test-id={`report-tab-${tab}`}
              key={tab}
              style={{ borderRadius: 0 }}
              value={tab}
            >
              {t(tab === "timeline" ? "timelineTab" : "statisticsTab")}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      <div className="hidden items-center gap-2 text-compact text-muted-foreground lg:flex">
        <span>{formatNumber(model.events.length)} {t("event")}</span>
      </div>

      <ButtonGroup
        className="shrink-0"
        data-bpp-test-id="header-utility-toolbar"
      >
        <Tooltip>
          <TooltipTrigger asChild>
            <Button
              aria-label={t("language")}
              className="gap-1 px-2"
              data-bpp-test-id="locale-switch"
              onClick={() =>
                dispatch({ type: "select-locale", locale: nextLocale() })
              }
              size="sm"
              type="button"
              variant="outline"
            >
              <Languages className="hidden size-3.5 sm:block" />
              <span className="text-compact font-bold">
                {LOCALE_LABELS[state.locale]}
              </span>
            </Button>
          </TooltipTrigger>
          <TooltipContent>{t("language")}</TooltipContent>
        </Tooltip>
        <Popover>
          <Tooltip>
            <TooltipTrigger asChild>
              <PopoverTrigger asChild>
                <Button
                  aria-label={t("help")}
                  size="icon-sm"
                  type="button"
                  variant="outline"
                >
                  <HelpCircle className="size-4" />
                </Button>
              </PopoverTrigger>
            </TooltipTrigger>
            <TooltipContent>{t("help")}</TooltipContent>
          </Tooltip>
          <PopoverContent>
            <p className="text-foreground">{t("helpTimeline")}</p>
            <p className="mt-2">{t("helpEvents")}</p>
            {model.videoUrl && <p className="mt-2">{t("helpVideo")}</p>}
          </PopoverContent>
        </Popover>
      </ButtonGroup>
    </header>
  );
}
