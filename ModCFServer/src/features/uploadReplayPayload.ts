import { json } from "../http/json";
import { trimString } from "../http/request";

const textDecoder = new TextDecoder();

type JsonObject = Record<string, unknown>;

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

export function parseReplayUploadBody(
  payload: ArrayBuffer,
  headerBattleId: string,
): Response | { battleId: string; schemaVersion: number } {
  try {
    const parsed = JSON.parse(textDecoder.decode(payload));
    const raw = asObject(parsed);
    if (raw == null) {
      return json({ error: "invalid_json_body" }, { status: 400 });
    }

    const battleId = asString(raw.battle_id);
    if (!battleId) {
      return json({ error: "battle_id_required" }, { status: 400 });
    }

    if (battleId !== headerBattleId) {
      return json({ error: "battle_id_mismatch" }, { status: 400 });
    }

    const replayPayload = asObject(raw.replay_payload);
    if (replayPayload == null) {
      return json({ error: "replay_payload_required" }, { status: 400 });
    }

    const replayBattleId = asString(replayPayload.battle_id);
    const version = asNumber(replayPayload.version);
    const spawnMessageBase64 = asString(replayPayload.spawn_message_base64);
    const combatMessageBase64 = asString(replayPayload.combat_message_base64);
    const despawnMessageBase64 = asString(replayPayload.despawn_message_base64);

    if (
      replayBattleId !== battleId
      || version == null
      || !spawnMessageBase64
      || !combatMessageBase64
      || !despawnMessageBase64
    ) {
      return json({ error: "invalid_replay_payload" }, { status: 400 });
    }

    return { battleId, schemaVersion: version };
  } catch {
    return json({ error: "invalid_json_body" }, { status: 400 });
  }
}
