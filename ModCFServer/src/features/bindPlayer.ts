import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import { upsertPlayerLink } from "../persistence/playerLinks";
import type { BindPlayerRequest } from "../types/api";
import { requireVerifiedClient } from "./verifiedClient";

const textDecoder = new TextDecoder();

export async function handleBindPlayer(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env);
  if (verified instanceof Response) {
    return verified;
  }

  const body = JSON.parse(
    textDecoder.decode(verified.payload),
  ) as BindPlayerRequest;
  const playerAccountId = trimString(body.player_account_id);
  if (!playerAccountId) {
    return json({ error: "player_account_id_required" }, { status: 400 });
  }

  const boundAtUtc = new Date().toISOString();
  await upsertPlayerLink(env, {
    clientId: verified.client.client_id,
    playerAccountId,
    boundAtUtc,
    lastConfirmedAtUtc: boundAtUtc,
  });

  return json({
    status: "bound",
    player_account_id: playerAccountId,
  });
}
