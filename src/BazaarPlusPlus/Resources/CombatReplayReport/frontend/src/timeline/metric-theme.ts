import type { ThemeColorName } from "../styles/theme.ts";
import type { StateMetric } from "./state-scale.ts";

export const METRIC_THEME_COLORS: Readonly<
  Record<StateMetric, ThemeColorName>
> = {
  health: "damage",
  rage: "rage",
  healthRegen: "regen",
  shield: "shield",
  burn: "burn",
  poison: "poison",
};

export const METRIC_DOT_CLASSES: Readonly<Record<StateMetric, string>> = {
  health: "bg-damage",
  rage: "bg-rage",
  healthRegen: "bg-regen",
  shield: "bg-shield",
  burn: "bg-burn",
  poison: "bg-poison",
};
