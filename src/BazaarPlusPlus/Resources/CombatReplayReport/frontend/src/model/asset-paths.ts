import { asString } from "./value.ts";

function hasUnsafeUrlSyntax(candidate: string): boolean {
  return (
    !candidate
    || candidate.length > 2048
    || candidate.startsWith("/")
    || candidate.startsWith("\\")
    || /[:?#\\\u0000-\u001f]/u.test(candidate)
  );
}

function safeDecodedPathSegment(rawSegment: string): string {
  if (!rawSegment || rawSegment === "." || rawSegment === "..") {
    return "";
  }

  // Match the producer's Uri.EscapeDataString output exactly: unreserved bytes
  // stay raw and every other UTF-8 byte uses an uppercase percent triplet.
  if (!/^(?:[A-Za-z0-9._~-]|%[0-9A-F]{2})+$/u.test(rawSegment)) {
    return "";
  }

  let decoded: string;
  try {
    decoded = decodeURIComponent(rawSegment);
  } catch {
    return "";
  }

  // Producer paths encode each segment exactly once. A remaining percent sign
  // permits a second interpretation such as %252e%252e, so reject it.
  if (
    !decoded
    || decoded === "."
    || decoded === ".."
    || decoded.includes("%")
    || /[/:?#\\\u0000-\u001f]/u.test(decoded)
  ) {
    return "";
  }
  const canonical = encodeURIComponent(decoded).replace(
    /[!'()*]/gu,
    (character) =>
      `%${character.charCodeAt(0).toString(16).toUpperCase().padStart(2, "0")}`,
  );
  return canonical === rawSegment ? decoded : "";
}

export function safeAssetUrl(value: unknown): string {
  const candidate = asString(value, "");
  if (hasUnsafeUrlSyntax(candidate)) {
    return "";
  }

  const parts = candidate.split("/");
  if (
    parts.length !== 5
    || parts[0] !== ".."
    || parts[1] !== "report-assets"
    || parts[2] !== "objects"
    || !/^[0-9a-f]{2}$/u.test(parts[3])
    || !/^[0-9a-f]{64}\.png$/u.test(parts[4])
    || parts[4].slice(0, 2) !== parts[3]
  ) {
    return "";
  }

  return candidate;
}

export function safeVideoUrl(value: unknown): string {
  const candidate = asString(value, "");
  if (hasUnsafeUrlSyntax(candidate)) {
    return "";
  }

  const parts = candidate.split("/");
  if (
    parts.length < 3
    || parts[0] !== ".."
    || parts[1] !== "CombatReplayVideos"
  ) {
    return "";
  }

  for (let index = 2; index < parts.length; index += 1) {
    const decoded = safeDecodedPathSegment(parts[index]);
    if (!decoded) {
      return "";
    }
    if (
      index === parts.length - 1
      && !decoded.toLowerCase().endsWith(".mp4")
    ) {
      return "";
    }
  }

  return candidate;
}
