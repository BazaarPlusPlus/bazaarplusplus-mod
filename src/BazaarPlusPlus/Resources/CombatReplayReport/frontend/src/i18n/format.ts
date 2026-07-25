function finiteNumber(value: unknown, fallback: number): number {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

export function formatDuration(milliseconds: number): string {
  const seconds = Math.max(0, milliseconds) / 1_000;
  return seconds < 10 ? `${seconds.toFixed(2)}s` : `${seconds.toFixed(1)}s`;
}

export function formatMilliseconds(value: unknown): string {
  const milliseconds = finiteNumber(value, Number.NaN);
  if (!Number.isFinite(milliseconds)) return "";
  if (Math.abs(milliseconds) < 1_000) {
    return `${formatCompactNumber(milliseconds)}ms`;
  }
  return `${new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 2,
  }).format(milliseconds / 1_000)}s`;
}

export function formatTimecode(milliseconds: unknown): string {
  const totalMilliseconds = Math.max(
    0,
    Math.round(finiteNumber(milliseconds, 0)),
  );
  const minutes = Math.floor(totalMilliseconds / 60_000);
  const seconds = Math.floor((totalMilliseconds % 60_000) / 1_000);
  const remainder = totalMilliseconds % 1_000;
  return (
    String(minutes).padStart(2, "0")
    + ":"
    + String(seconds).padStart(2, "0")
    + "."
    + String(remainder).padStart(3, "0")
  );
}

export function formatNumber(value: unknown): string {
  const number = finiteNumber(value, Number.NaN);
  return Number.isFinite(number) ? new Intl.NumberFormat().format(number) : "";
}

export function formatCompactNumber(value: unknown): string {
  const number = finiteNumber(value, Number.NaN);
  if (!Number.isFinite(number)) return "";
  const absolute = Math.abs(number);
  if (absolute < 10_000) {
    return new Intl.NumberFormat(undefined, {
      maximumFractionDigits: 1,
    }).format(number);
  }
  if (absolute < 1e15) {
    return new Intl.NumberFormat(undefined, {
      notation: "compact",
      maximumFractionDigits: 2,
    }).format(number);
  }
  return number.toExponential(2).replace("e+", "e");
}

export function signedOrder(value: unknown): number {
  const number = finiteNumber(value, 0);
  return Math.sign(number) * Math.log10(1 + Math.abs(number));
}

function restoreSignedOrder(value: unknown): number {
  const number = finiteNumber(value, 0);
  return Math.sign(number) * (Math.pow(10, Math.abs(number)) - 1);
}

export function orderAxisLabel(value: unknown): string {
  const mapped = finiteNumber(value, 0);
  const mappedExponent = Math.round(Math.abs(mapped));
  if (
    mappedExponent >= 3
    && Math.abs(Math.abs(mapped) - mappedExponent) < 1e-9
  ) {
    return `${mapped < 0 ? "−" : ""}1e${mappedExponent}`;
  }
  const restored = restoreSignedOrder(mapped);
  if (Math.abs(restored) < 0.5) return "0";
  const exponent = Math.floor(Math.log10(Math.max(1, Math.abs(restored))));
  if (exponent >= 3) return `${restored < 0 ? "−" : ""}1e${exponent}`;
  return formatCompactNumber(restored);
}
