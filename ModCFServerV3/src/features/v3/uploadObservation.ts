import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { trimString } from "../../http/request";
import { requireInstallationAuth } from "./requireInstallationAuth";

export async function handleUploadObservation(
  request: Request,
  env: Env,
): Promise<Response> {
  const auth = await requireInstallationAuth(request, env);
  if (auth instanceof Response) {
    return auth;
  }
  if (auth == null) {
    return json({ error: "installation_auth_required" }, { status: 401 });
  }

  const body = (await readJson(request)) as {
    observed_player_account_id?: unknown;
    observed_player_username?: unknown;
    observed_at_utc?: unknown;
  };
  const observedPlayerAccountId = trimString(body.observed_player_account_id);
  const observedPlayerUsername = trimString(body.observed_player_username);
  const observedAtUtc = trimString(body.observed_at_utc);

  if (!observedPlayerAccountId || !observedPlayerUsername || !observedAtUtc) {
    return json({ error: "invalid_observation_request" }, { status: 400 });
  }

  await env.DB.prepare(
    `
      INSERT INTO installation_observations (
        installation_id,
        observed_player_account_id,
        observed_player_username,
        observed_at_utc,
        signature,
        status
      ) VALUES (?, ?, ?, ?, ?, ?)
    `,
  )
    .bind(
      auth.installationId,
      observedPlayerAccountId,
      observedPlayerUsername,
      observedAtUtc,
      request.headers.get("x-bpp-signature") ?? "",
      "observed",
    )
    .run();

  return json({ status: "accepted" });
}

