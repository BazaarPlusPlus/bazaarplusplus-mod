const THEME_COLOR_NAMES = [
  "background",
  "foreground",
  "card",
  "popover",
  "muted",
  "muted-foreground",
  "border",
  "surface",
  "surface-soft",
  "surface-raised",
  "faint",
  "brand",
  "brand-soft",
  "player",
  "opponent",
  "success",
  "danger",
  "damage",
  "burn",
  "poison",
  "heal",
  "regen",
  "shield",
  "rage",
  "charge",
  "haste",
  "slow",
  "freeze",
  "skill",
  "attribute",
  "status",
] as const;

export type ThemeColorName = (typeof THEME_COLOR_NAMES)[number];

let cachedColors: Readonly<Record<ThemeColorName, string>> | null = null;

function rootStyles(): CSSStyleDeclaration {
  return getComputedStyle(document.documentElement);
}

function resolveCustomProperties(
  value: string,
  styles: CSSStyleDeclaration,
  seen = new Set<string>(),
): string {
  return value.replace(
    /var\(\s*(--[\w-]+)(?:\s*,\s*([^)]+))?\s*\)/gu,
    (_match, variable: string, fallback: string | undefined) => {
      if (seen.has(variable)) return fallback?.trim() ?? "";
      const next = styles.getPropertyValue(variable).trim();
      if (!next) return fallback?.trim() ?? "";
      const nestedSeen = new Set(seen);
      nestedSeen.add(variable);
      return resolveCustomProperties(next, styles, nestedSeen);
    },
  );
}

export function themeValue(variable: string): string {
  const styles = rootStyles();
  return resolveCustomProperties(
    styles.getPropertyValue(variable).trim(),
    styles,
    new Set([variable]),
  );
}

export function themeColors(): Readonly<Record<ThemeColorName, string>> {
  if (cachedColors) return cachedColors;
  const styles = rootStyles();
  cachedColors = Object.fromEntries(
    THEME_COLOR_NAMES.map((name) => [
      name,
      resolveCustomProperties(
        styles.getPropertyValue(`--${name}`).trim(),
        styles,
        new Set([`--${name}`]),
      ),
    ]),
  ) as Record<ThemeColorName, string>;
  return cachedColors;
}

export function themeColor(name: ThemeColorName): string {
  return themeColors()[name];
}

export function withAlpha(color: string, alpha: number): string {
  const match = /^#([\da-f]{2})([\da-f]{2})([\da-f]{2})$/iu.exec(color);
  if (!match) return color;
  return `rgb(${Number.parseInt(match[1], 16)} ${Number.parseInt(match[2], 16)} ${Number.parseInt(match[3], 16)} / ${alpha})`;
}

export function themeLengthPx(variable: string): number {
  const value = themeValue(variable);
  if (value.endsWith("rem")) {
    return Number.parseFloat(value) * Number.parseFloat(rootStyles().fontSize);
  }
  return Number.parseFloat(value) || 0;
}
