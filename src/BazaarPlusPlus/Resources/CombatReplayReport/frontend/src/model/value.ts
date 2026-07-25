export type UnknownRecord = Record<string, unknown>;

export function isRecord(value: unknown): value is UnknownRecord {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export function asArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

export function asString(value: unknown, fallback: string): string {
  return typeof value === "string" && value.trim().length > 0
    ? value.trim()
    : fallback;
}

export function asFiniteNumber(value: unknown, fallback: number): number {
  const number = typeof value === "number" ? value : Number(value);
  return Number.isFinite(number) ? number : fallback;
}

export function pick(
  record: unknown,
  names: readonly string[],
  fallback: unknown,
): unknown {
  if (!isRecord(record)) return fallback;
  for (const name of names) {
    if (
      Object.prototype.hasOwnProperty.call(record, name)
      && record[name] !== null
      && record[name] !== undefined
    ) {
      return record[name];
    }
  }
  return fallback;
}
