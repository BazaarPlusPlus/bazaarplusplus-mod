export function trimString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

export function absolutePath(request: Request): string {
  return new URL(request.url).pathname || "/";
}
