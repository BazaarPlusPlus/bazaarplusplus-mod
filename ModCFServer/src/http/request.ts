import type { UploadPurpose } from "../types/api";

export function trimString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

export function normalizePurpose(value: unknown): UploadPurpose | null {
  const normalized = trimString(value).toLowerCase();
  if (normalized === "runs" || normalized === "replays") {
    return normalized;
  }

  return null;
}

export function absolutePath(request: Request): string {
  return new URL(request.url).pathname || "/";
}
