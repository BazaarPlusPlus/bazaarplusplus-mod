import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import { getPlayerLink } from "../persistence/playerLinks";
import { upsertRun } from "../persistence/runs";
import { requireVerifiedClient } from "./verifiedClient";

const textDecoder = new TextDecoder();

type RunSummaryRequest = {
  run_id?: unknown;
  status?: unknown;
  hero_id?: unknown;
  hero_name?: unknown;
  started_at_utc?: unknown;
  ended_at_utc?: unknown;
  final_day?: unknown;
  final_wins?: unknown;
  final_losses?: unknown;
  mmr?: unknown;
  schema_version?: unknown;
};

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

export async function handleUploadRunSummary(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env);
  if (verified instanceof Response) {
    return verified;
  }

  const body = JSON.parse(textDecoder.decode(verified.payload)) as RunSummaryRequest;
  const runId = trimString(body.run_id);
  const status = trimString(body.status);
  const endedAtUtc = trimString(body.ended_at_utc);
  if (!runId) {
    return json({ error: "run_id_required" }, { status: 400 });
  }
  if (!status) {
    return json({ error: "status_required" }, { status: 400 });
  }
  if (!endedAtUtc) {
    return json({ error: "ended_at_utc_required" }, { status: 400 });
  }

  const receivedAtUtc = new Date().toISOString();
  const objectKey = `run-summaries/${verified.client.client_id}/${runId}/${verified.payloadHash}.json`;
  await env.PVP_BATTLE_BUCKET.put(objectKey, verified.payload, {
    httpMetadata: {
      contentType: "application/json; charset=utf-8",
    },
  });

  const playerLink = await getPlayerLink(env, verified.client.client_id);
  await upsertRun(env, {
    runId,
    clientId: verified.client.client_id,
    playerAccountId: playerLink?.player_account_id ?? null,
    status,
    heroId: trimString(body.hero_id) || null,
    heroName: trimString(body.hero_name) || null,
    startedAtUtc: trimString(body.started_at_utc) || null,
    endedAtUtc,
    finalDay: asNumber(body.final_day),
    finalWins: asNumber(body.final_wins),
    finalLosses: asNumber(body.final_losses),
    mmr: asNumber(body.mmr),
    summarySchemaVersion: asNumber(body.schema_version),
    summaryObjectKey: objectKey,
    createdAtUtc: receivedAtUtc,
    updatedAtUtc: receivedAtUtc,
  });

  return json({ status: "accepted" });
}
