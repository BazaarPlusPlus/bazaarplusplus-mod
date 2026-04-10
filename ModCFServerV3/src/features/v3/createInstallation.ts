import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { trimString } from "../../http/request";
import type { CreateInstallationRequest } from "../../types/api";
import { requireInstallerSession } from "./requireInstallerSession";

export async function handleCreateInstallation(
  request: Request,
  env: Env,
): Promise<Response> {
  const session = await requireInstallerSession(request, env);
  if (!session) {
    return json({ error: "invalid_installer_session" }, { status: 401 });
  }

  const body = (await readJson(request)) as CreateInstallationRequest;
  const playerAccountId = trimString(body.player_account_id);
  const installationPublicKey = trimString(body.installation_public_key);

  if (!playerAccountId || !installationPublicKey) {
    return json({ error: "invalid_installation_request" }, { status: 400 });
  }

  if (playerAccountId !== session.playerAccountId) {
    return json({ error: "player_account_mismatch" }, { status: 403 });
  }

  const nowUtc = new Date().toISOString();
  const installationId = `inst_${crypto.randomUUID().replace(/-/g, "")}`;

  await env.DB.prepare(
    `
      INSERT INTO installations (
        installation_id,
        player_account_id,
        public_key,
        status,
        created_at_utc,
        last_seen_at_utc,
        revoked_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
    `,
  )
    .bind(
      installationId,
      playerAccountId,
      installationPublicKey,
      "active",
      nowUtc,
      null,
      null,
    )
    .run();

  return json({
    installation_id: installationId,
    status: "active",
  });
}
