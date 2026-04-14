import { sha256Base64 } from "../../crypto/hash";
import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import {
  getBattleIngestMinDayIfBelowRating,
  getBattleIngestMinRating,
  getRunBundleRetentionDays,
} from "../../config/v3";
import { requireInstallationAuth } from "./requireInstallationAuth";

type RunProjection = {
  run_id?: unknown;
  status?: unknown;
  hero_id?: unknown;
  hero_name?: unknown;
  player_rank?: unknown;
  player_rating?: unknown;
  player_position?: unknown;
  started_at_utc?: unknown;
  ended_at_utc?: unknown;
  final_day?: unknown;
  final_wins?: unknown;
  final_losses?: unknown;
  final_player_rank?: unknown;
  final_player_rating?: unknown;
  final_player_position?: unknown;
};

type BattleProjection = {
  battle_id?: unknown;
  run_id?: unknown;
  recorded_at_utc?: unknown;
  day?: unknown;
  player_name?: unknown;
  player_account_id?: unknown;
  player_hero?: unknown;
  player_rank?: unknown;
  player_rating?: unknown;
  player_level?: unknown;
  opponent_name?: unknown;
  opponent_account_id?: unknown;
  opponent_hero?: unknown;
  opponent_rank?: unknown;
  opponent_rating?: unknown;
  opponent_level?: unknown;
  result?: unknown;
  replay_available?: unknown;
};

type RunBundleRequest = {
  schema_version?: unknown;
  installation_id?: unknown;
  player_account_id?: unknown;
  submitted_at_utc?: unknown;
  artifact_codec?: unknown;
  artifact_bytes?: unknown;
  run_projection?: RunProjection;
  battle_projections?: BattleProjection[];
};

type ExistingRunBundleRow = {
  bundle_id: string;
  object_key: string;
};

const AnonymousInstallationId = "anonymous";
const AnonymousPlayerAccountId = "anonymous-player";

function asString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function asBytes(value: unknown): Uint8Array | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const bytes = new Uint8Array(value.length);
  for (let index = 0; index < value.length; index += 1) {
    const current = value[index];
    if (typeof current !== "number" || !Number.isInteger(current) || current < 0 || current > 255) {
      return null;
    }
    bytes[index] = current;
  }

  return bytes;
}

function shouldProjectBattle(battle: BattleProjection, env: Env): boolean {
  const rating = asNumber(battle.player_rating);
  const day = asNumber(battle.day);

  if (rating != null && rating >= getBattleIngestMinRating(env)) {
    return true;
  }

  return day != null && day >= getBattleIngestMinDayIfBelowRating(env);
}

export async function handleUploadRunBundle(
  request: Request,
  env: Env,
): Promise<Response> {
  const auth = await requireInstallationAuth(request, env, { allowMissingAuth: true });
  if (auth instanceof Response) {
    return auth;
  }

  const body = (await readJson(request)) as RunBundleRequest;
  const schemaVersion = asNumber(body.schema_version);
  const installationId = asString(body.installation_id);
  const playerAccountId = asString(body.player_account_id);
  const submittedAtUtc = asString(body.submitted_at_utc);
  const artifactCodec = asString(body.artifact_codec);
  const artifactBytes = asBytes(body.artifact_bytes);
  const runId = asString(body.run_projection?.run_id);
  const runStatus = asString(body.run_projection?.status);
  const endedAtUtc = asString(body.run_projection?.ended_at_utc);
  const battleProjections = Array.isArray(body.battle_projections)
    ? body.battle_projections
    : [];

  const persistedInstallationId = installationId ?? auth?.installationId ?? AnonymousInstallationId;
  const persistedPlayerAccountId =
    playerAccountId ?? auth?.playerAccountId ?? AnonymousPlayerAccountId;

  if (
    schemaVersion == null ||
    !submittedAtUtc ||
    !artifactCodec ||
    !artifactBytes ||
    !runId ||
    !runStatus ||
    !endedAtUtc
  ) {
    return json({ error: "invalid_run_bundle_request" }, { status: 400 });
  }

  if (
    auth != null
    && (
      (installationId != null && installationId !== auth.installationId)
      || (playerAccountId != null && playerAccountId !== auth.playerAccountId)
    )
  ) {
    return json({ error: "installation_player_mismatch" }, { status: 403 });
  }

  for (const battle of battleProjections) {
    const battleId = asString(battle.battle_id);
    const battleRunId = asString(battle.run_id);
    if (!battleId) {
      return json({ error: "battle_id_required" }, { status: 400 });
    }

    if (!battleRunId || battleRunId !== runId) {
      return json({ error: "battle_run_id_mismatch" }, { status: 400 });
    }
  }

  const payloadHash = await sha256Base64(artifactBytes);
  const objectKey =
    `run-bundles/${persistedPlayerAccountId}/${persistedInstallationId}/${runId}/${payloadHash}.mpack.gz`;
  await env.RUN_BUNDLE_BUCKET.put(objectKey, artifactBytes, {
    httpMetadata: {
      contentType: artifactCodec,
    },
    customMetadata: {
      retention_days: String(getRunBundleRetentionDays(env)),
    },
  });

  const createdAtUtc = new Date().toISOString();
  const existingBundle = await env.DB.prepare(
    `
      SELECT
        bundle_id,
        object_key
      FROM run_bundles
      WHERE installation_id = ?
        AND run_id = ?
        AND payload_hash = ?
    `,
  )
    .bind(persistedInstallationId, runId, payloadHash)
    .first<ExistingRunBundleRow>();
  const bundleId = existingBundle?.bundle_id ?? `bundle_${crypto.randomUUID().replace(/-/g, "")}`;
  const persistedObjectKey = existingBundle?.object_key ?? objectKey;

  if (!existingBundle) {
    await env.DB.prepare(
      `
        INSERT INTO run_bundles (
          bundle_id,
          installation_id,
          player_account_id,
          run_id,
          payload_hash,
          schema_version,
          object_key,
          codec,
          size_bytes,
          submitted_at_utc,
          created_at_utc
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      `,
    )
      .bind(
        bundleId,
        persistedInstallationId,
        persistedPlayerAccountId,
        runId,
        payloadHash,
        schemaVersion,
        objectKey,
        artifactCodec,
        artifactBytes.byteLength,
        submittedAtUtc,
        createdAtUtc,
      )
      .run();
  }

  await env.DB.prepare(
    `
      INSERT INTO runs (
        run_id,
        installation_id,
        player_account_id,
        bundle_id,
        status,
        hero_id,
        hero_name,
        player_rank,
        player_rating,
        player_position,
        started_at_utc,
        ended_at_utc,
        final_day,
        final_wins,
        final_losses,
        final_player_rank,
        final_player_rating,
        final_player_position,
        updated_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(run_id) DO UPDATE SET
        installation_id = excluded.installation_id,
        player_account_id = excluded.player_account_id,
        bundle_id = excluded.bundle_id,
        status = excluded.status,
        hero_id = excluded.hero_id,
        hero_name = excluded.hero_name,
        player_rank = excluded.player_rank,
        player_rating = excluded.player_rating,
        player_position = excluded.player_position,
        started_at_utc = excluded.started_at_utc,
        ended_at_utc = excluded.ended_at_utc,
        final_day = excluded.final_day,
        final_wins = excluded.final_wins,
        final_losses = excluded.final_losses,
        final_player_rank = excluded.final_player_rank,
        final_player_rating = excluded.final_player_rating,
        final_player_position = excluded.final_player_position,
        updated_at_utc = excluded.updated_at_utc
    `,
  )
    .bind(
      runId,
      persistedInstallationId,
      persistedPlayerAccountId,
      bundleId,
      runStatus,
      asString(body.run_projection?.hero_id),
      asString(body.run_projection?.hero_name),
      asString(body.run_projection?.player_rank),
      asNumber(body.run_projection?.player_rating),
      asNumber(body.run_projection?.player_position),
      asString(body.run_projection?.started_at_utc),
      endedAtUtc,
      asNumber(body.run_projection?.final_day),
      asNumber(body.run_projection?.final_wins),
      asNumber(body.run_projection?.final_losses),
      asString(body.run_projection?.final_player_rank),
      asNumber(body.run_projection?.final_player_rating),
      asNumber(body.run_projection?.final_player_position),
      createdAtUtc,
    )
    .run();

  const battleStatements = battleProjections
    .filter((battle) => shouldProjectBattle(battle, env))
    .map((battle) =>
      env.DB.prepare(
        `
          INSERT INTO battles (
            battle_id,
            run_id,
            installation_id,
            player_account_id,
            bundle_id,
            recorded_at_utc,
            day,
            player_name,
            player_account_id_in_payload,
            player_hero,
            player_rank,
            player_rating,
            player_level,
            opponent_name,
            opponent_account_id,
            opponent_hero,
            opponent_rank,
            opponent_rating,
            opponent_level,
            result,
            replay_available,
            updated_at_utc
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
          ON CONFLICT(battle_id) DO UPDATE SET
            run_id = excluded.run_id,
            installation_id = excluded.installation_id,
            player_account_id = excluded.player_account_id,
            bundle_id = excluded.bundle_id,
            recorded_at_utc = excluded.recorded_at_utc,
            day = excluded.day,
            player_name = excluded.player_name,
            player_account_id_in_payload = excluded.player_account_id_in_payload,
            player_hero = excluded.player_hero,
            player_rank = excluded.player_rank,
            player_rating = excluded.player_rating,
            player_level = excluded.player_level,
            opponent_name = excluded.opponent_name,
            opponent_account_id = excluded.opponent_account_id,
            opponent_hero = excluded.opponent_hero,
            opponent_rank = excluded.opponent_rank,
            opponent_rating = excluded.opponent_rating,
            opponent_level = excluded.opponent_level,
            result = excluded.result,
            replay_available = excluded.replay_available,
            updated_at_utc = excluded.updated_at_utc
        `,
      ).bind(
        asString(battle.battle_id),
        runId,
        persistedInstallationId,
        persistedPlayerAccountId,
        bundleId,
        asString(battle.recorded_at_utc),
        asNumber(battle.day),
        asString(battle.player_name),
        asString(battle.player_account_id),
        asString(battle.player_hero),
        asString(battle.player_rank),
        asNumber(battle.player_rating),
        asNumber(battle.player_level),
        asString(battle.opponent_name),
        asString(battle.opponent_account_id),
        asString(battle.opponent_hero),
        asString(battle.opponent_rank),
        asNumber(battle.opponent_rating),
        asNumber(battle.opponent_level),
        asString(battle.result),
        battle.replay_available === true ? 1 : 0,
        createdAtUtc,
      ),
    );

  if (battleStatements.length > 0) {
    await env.DB.batch(battleStatements);
  }

  return json({ status: "accepted", bundle_id: bundleId, object_key: persistedObjectKey });
}
