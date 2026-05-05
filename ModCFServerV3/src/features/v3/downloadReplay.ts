import type { Env } from "../../env";
import { json } from "../../http/json";

type ReplayTokenRow = {
  token: string;
  battle_id: string;
  requested_by_player_account_id: string;
  expires_at_utc: string;
  used_at_utc: string | null;
  revoked_at_utc: string | null;
};

type BattleRow = { bundle_id: string };
type RunBundleRow = { object_key: string };

export async function handleDownloadReplay(
  request: Request,
  env: Env,
  token: string,
): Promise<Response> {
  const replayToken = await env.DB.prepare(
    `
      SELECT
        token,
        battle_id,
        requested_by_player_account_id,
        expires_at_utc,
        used_at_utc,
        revoked_at_utc
      FROM replay_tokens
      WHERE token = ?
    `,
  )
    .bind(token)
    .first<ReplayTokenRow>();
  if (!replayToken || replayToken.revoked_at_utc != null) {
    return json({ error: "replay_token_not_found" }, { status: 404 });
  }
  if (Date.parse(replayToken.expires_at_utc) < Date.now()) {
    return json({ error: "replay_token_expired" }, { status: 410 });
  }

  const battle = await env.DB.prepare(
    `SELECT bundle_id FROM battles WHERE battle_id = ?`,
  )
    .bind(replayToken.battle_id)
    .first<BattleRow>();
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  const runBundle = await env.DB.prepare(
    `SELECT object_key FROM run_bundles WHERE bundle_id = ?`,
  )
    .bind(battle.bundle_id)
    .first<RunBundleRow>();
  if (!runBundle?.object_key) {
    return json({ error: "artifact_expired" }, { status: 410 });
  }

  const object = await env.RUN_BUNDLE_BUCKET.get(runBundle.object_key);
  if (!object) {
    return json({ error: "artifact_expired" }, { status: 410 });
  }

  if (replayToken.used_at_utc == null) {
    await env.DB.prepare(
      `UPDATE replay_tokens SET used_at_utc = ? WHERE token = ?`,
    )
      .bind(new Date().toISOString(), replayToken.token)
      .run();
  }

  const headers = new Headers({
    "content-type": object.httpMetadata?.contentType ?? "application/octet-stream",
  });
  if (typeof object.size === "number") {
    headers.set("content-length", String(object.size));
  }

  return new Response(object.body, { status: 200, headers });
}
