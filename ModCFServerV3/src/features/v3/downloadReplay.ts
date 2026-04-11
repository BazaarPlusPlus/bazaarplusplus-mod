import { allowUnauthenticatedReplayDownloads } from "../../config/v3";
import type { Env } from "../../env";
import { json } from "../../http/json";
import { requireInstallationAuth } from "./requireInstallationAuth";

type ReplayTokenRow = {
  token: string;
  battle_id: string;
  requested_by_player_account_id: string;
  expires_at_utc: string;
  used_at_utc: string | null;
  revoked_at_utc: string | null;
};

type BattleRow = {
  bundle_id: string;
};

type RunBundleRow = {
  object_key: string;
};

export async function handleDownloadReplay(
  request: Request,
  env: Env,
  token: string,
): Promise<Response> {
  let requesterPlayerAccountId: string | null = null;
  if (!allowUnauthenticatedReplayDownloads(env)) {
    const auth = await requireInstallationAuth(request, env);
    if (auth instanceof Response) {
      return auth;
    }
    if (auth == null) {
      return json({ error: "installation_auth_required" }, { status: 401 });
    }

    requesterPlayerAccountId = auth.playerAccountId;
  }

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
  if (
    requesterPlayerAccountId != null &&
    replayToken.requested_by_player_account_id !== requesterPlayerAccountId
  ) {
    return json({ error: "replay_token_forbidden" }, { status: 403 });
  }

  const battle = await env.DB.prepare(
    `
      SELECT
        bundle_id
      FROM battles
      WHERE battle_id = ?
    `,
  )
    .bind(replayToken.battle_id)
    .first<BattleRow>();
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  const runBundle = await env.DB.prepare(
    `
      SELECT
        object_key
      FROM run_bundles
      WHERE bundle_id = ?
    `,
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
      `
        UPDATE replay_tokens
        SET used_at_utc = ?
        WHERE token = ?
      `,
    )
      .bind(new Date().toISOString(), replayToken.token)
      .run();
  }

  return new Response(await object.arrayBuffer(), {
    status: 200,
    headers: {
      "content-type": object.httpMetadata?.contentType ?? "application/octet-stream",
    },
  });
}

