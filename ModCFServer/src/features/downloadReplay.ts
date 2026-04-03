import type { Env } from "../env";
import { json } from "../http/json";
import { logWarn } from "../observability";
import { getReplayToken } from "../persistence/replayTokens";
import { gunzipBytes } from "./replayCompression";

export async function handleDownloadReplay(
  _request: Request,
  env: Env,
  token: string,
): Promise<Response> {
  const replayToken = await getReplayToken(env, token);
  if (!replayToken || replayToken.revoked_at_utc != null) {
    return json({ error: "replay_token_not_found" }, { status: 404 });
  }
  if (Date.parse(replayToken.expires_at_utc) < Date.now()) {
    return json({ error: "replay_token_expired" }, { status: 410 });
  }

  const battle = await env.DB.prepare(
    `
      SELECT *
      FROM battles
      WHERE battle_id = ?
    `,
  )
    .bind(replayToken.battle_id)
    .first<{
      replay_object_key: string;
    }>();
  if (!battle?.replay_object_key) {
    return json({ error: "replay_not_found" }, { status: 404 });
  }

  const object = await env.PVP_BATTLE_BUCKET.get(battle.replay_object_key);
  if (!object) {
    return json({ error: "replay_not_found" }, { status: 404 });
  }

  const codec = object.customMetadata?.["bpp-storage-codec"]?.trim()
    || object.httpMetadata?.contentEncoding?.trim()
    || null;

  try {
    if (codec && codec !== "gzip") {
      throw new Error(`unsupported replay codec: ${codec}`);
    }
  } catch (error) {
    logWarn("replay.decode_failed", {
      battle_id: replayToken.battle_id,
      replay_object_key: battle.replay_object_key,
      codec,
      detail: error instanceof Error ? error.message : String(error),
    });
    return json({ error: "replay_decode_failed" }, { status: 502 });
  }

  let storedBytes: ArrayBuffer;
  try {
    storedBytes = await object.arrayBuffer();
  } catch (error) {
    logWarn("replay.read_failed", {
      battle_id: replayToken.battle_id,
      replay_object_key: battle.replay_object_key,
      codec,
      detail: error instanceof Error ? error.message : String(error),
    });
    return json({ error: "replay_read_failed" }, { status: 502 });
  }

  try {
    const responseBody =
      codec === "gzip"
        ? await gunzipBytes(storedBytes)
        : storedBytes;

    return new Response(responseBody, {
      status: 200,
      headers: {
        "content-type": "application/json; charset=utf-8",
      },
    });
  } catch (error) {
    logWarn("replay.decode_failed", {
      battle_id: replayToken.battle_id,
      replay_object_key: battle.replay_object_key,
      codec,
      detail: error instanceof Error ? error.message : String(error),
    });
    return json({ error: "replay_decode_failed" }, { status: 502 });
  }
}
