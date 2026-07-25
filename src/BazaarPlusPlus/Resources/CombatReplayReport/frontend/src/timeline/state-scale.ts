import {
  formatCompactNumber,
  orderAxisLabel,
  signedOrder,
} from "../i18n/format.ts";
import { asFiniteNumber } from "../model/value.ts";

export const METRIC_ORDER = [
  "health",
  "rage",
  "healthRegen",
  "shield",
  "burn",
  "poison",
] as const;

export type StateMetric = (typeof METRIC_ORDER)[number];
export type StateScaleMode = "linear" | "magnitude";

export interface StateSample {
  value: unknown;
}

export interface StateDomain {
  minimum: number;
  maximum: number;
}

export interface StateAxisTick {
  mapped: number;
  label: string;
}

type GroupedStateSamples = ReadonlyMap<string, readonly StateSample[]>;

export function stateScaleValue(
  value: unknown,
  scaleMode: StateScaleMode,
): number {
  const numeric = asFiniteNumber(value, 0);
  return scaleMode === "magnitude" ? signedOrder(numeric) : numeric;
}

export function sharedStateDomain(
  grouped: GroupedStateSamples,
  scaleMode: StateScaleMode,
): StateDomain | null {
  let minimum = 0;
  let maximum = 0;
  let hasSamples = false;
  for (const metric of METRIC_ORDER) {
    for (const side of ["player", "opponent"]) {
      const samples = grouped.get(`${side}:${metric}`) ?? [];
      for (const sample of samples) {
        const value = asFiniteNumber(sample.value, Number.NaN);
        if (!Number.isFinite(value)) continue;
        const mapped = stateScaleValue(value, scaleMode);
        minimum = Math.min(minimum, mapped);
        maximum = Math.max(maximum, mapped);
        hasSamples = true;
      }
    }
  }
  if (!hasSamples) return null;
  if (minimum === maximum) {
    return { minimum: minimum - 1, maximum: maximum + 1 };
  }
  const padding = (maximum - minimum) * 0.08;
  if (minimum === 0 && maximum > 0) {
    return { minimum: 0, maximum: maximum + padding };
  }
  if (maximum === 0 && minimum < 0) {
    return { minimum: minimum - padding, maximum: 0 };
  }
  return {
    minimum: minimum - padding,
    maximum: maximum + padding,
  };
}

export function sharedStateMappedY(
  mapped: number,
  domain: StateDomain,
  height: number,
): number {
  const range = Math.max(Number.EPSILON, domain.maximum - domain.minimum);
  const normalized = Math.max(
    0,
    Math.min(1, (mapped - domain.minimum) / range),
  );
  return height - 8 - normalized * Math.max(1, height - 16);
}

export function sharedStateY(
  value: unknown,
  domain: StateDomain,
  height: number,
  scaleMode: StateScaleMode,
): number {
  return sharedStateMappedY(
    stateScaleValue(value, scaleMode),
    domain,
    height,
  );
}

export function niceLinearTicks(
  minimum: number,
  maximum: number,
  targetCount: number,
): number[] {
  const span = maximum - minimum;
  if (!(span > 0) || !Number.isFinite(span)) return [];
  const roughStep = span / Math.max(1, targetCount);
  const exponent = Math.floor(Math.log10(roughStep));
  const magnitude = Math.pow(10, exponent);
  const residual = roughStep / magnitude;
  const factor =
    residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 5 ? 5 : 10;
  const step = factor * magnitude;
  const start = Math.ceil(minimum / step) * step;
  const end = Math.floor(maximum / step) * step;
  const ticks: number[] = [];
  for (
    let value = start;
    value <= end + step * 0.001 && ticks.length < 8;
    value += step
  ) {
    const normalized = Math.abs(value) < step * 1e-9 ? 0 : value;
    ticks.push(normalized);
  }
  return ticks;
}

export function stateAxisTicks(
  domain: StateDomain | null,
  scaleMode: StateScaleMode,
): StateAxisTick[] {
  if (!domain) return [];
  let mappedTicks = niceLinearTicks(domain.minimum, domain.maximum, 4);
  if (scaleMode === "magnitude") {
    const integerMinimum = Math.ceil(domain.minimum);
    const integerMaximum = Math.floor(domain.maximum);
    if (integerMaximum - integerMinimum <= 6) {
      mappedTicks = [];
      for (
        let mapped = integerMinimum;
        mapped <= integerMaximum;
        mapped += 1
      ) {
        mappedTicks.push(mapped);
      }
    }
  }
  return mappedTicks.map((mapped) => ({
    mapped,
    label:
      scaleMode === "magnitude"
        ? orderAxisLabel(mapped)
        : formatCompactNumber(mapped),
  }));
}
