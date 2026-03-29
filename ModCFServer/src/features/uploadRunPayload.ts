import { json } from "../http/json";
import { trimString } from "../http/request";

const textDecoder = new TextDecoder();

export type JsonObject = Record<string, unknown>;

export type ParsedRunUploadBody = {
  runId: string | null;
  schemaVersion: number | null;
};

function asObject(value: unknown): JsonObject | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return null;
  }

  return value as JsonObject;
}

function asString(value: unknown): string | null {
  return typeof value === "string" ? trimString(value) || null : null;
}

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

export function parseRunUploadBody(
  payload: ArrayBuffer,
): ParsedRunUploadBody | Response {
  try {
    const parsed = JSON.parse(textDecoder.decode(payload));
    const raw = asObject(parsed);
    if (raw == null) {
      return json({ error: "invalid_json_body" }, { status: 400 });
    }

    return {
      runId: asString(raw.run_id),
      schemaVersion: asNumber(raw.schema_version),
    };
  } catch {
    return json({ error: "invalid_json_body" }, { status: 400 });
  }
}
