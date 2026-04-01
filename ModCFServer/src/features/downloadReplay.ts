import type { Env } from "../env";
import { json } from "../http/json";
import { getReplayToken } from "../persistence/replayTokens";

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

  return new Response(await object.arrayBuffer(), {
    status: 200,
    headers: {
      "content-type": "application/json; charset=utf-8",
    },
  });
}
