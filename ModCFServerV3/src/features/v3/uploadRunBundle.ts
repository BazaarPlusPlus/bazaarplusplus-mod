import { base64ToBytes } from "../../crypto/base64";
import { sha256Base64 } from "../../crypto/hash";
import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { logInfo, logWarn } from "../../observability";
import { getRunBundleRetentionDays } from "../../config/v3";

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
  player_account_id?: unknown;
  submitted_at_utc?: unknown;
  artifact_codec?: unknown;
  artifact_bytes?: unknown;
  run_projection?: RunProjection;
  battle_projections?: BattleProjection[];
};

const AnonymousPlayerAccountId = "anonymous-player";
const LegacyInstallationId = "legacy";
const MaxDistinctOpponentAccountIds = 20;

const ObjectKeySegmentPattern = /^[A-Za-z0-9._-]{1,128}$/;

function sanitizeObjectKeySegment(value: string): string | null {
  return ObjectKeySegmentPattern.test(value) ? value : null;
}

function asString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

type ArtifactBytesEncoding = "base64" | "byte-array";

type DecodedArtifactBytes = {
  bytes: Uint8Array;
  encoding: ArtifactBytesEncoding;
};

function decodeArtifactBytes(value: unknown): DecodedArtifactBytes | null {
  if (typeof value === "string") {
    try {
      return { bytes: base64ToBytes(value), encoding: "base64" };
    } catch {
      return null;
    }
  }

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

  return { bytes, encoding: "byte-array" };
}

function shouldProjectBattle(
  battle: BattleProjection,
  knownOpponentAccountIds: ReadonlySet<string>,
): boolean {
  const opponentAccountId = asString(battle.opponent_account_id);
  return opponentAccountId != null && knownOpponentAccountIds.has(opponentAccountId);
}

async function loadKnownOpponentAccountIds(
  battleProjections: BattleProjection[],
  uploaderPlayerAccountId: string,
  env: Env,
): Promise<Set<string>> {
  const opponentAccountIds = new Set<string>();
  const knownOpponentAccountIds = new Set<string>();

  if (uploaderPlayerAccountId && uploaderPlayerAccountId !== AnonymousPlayerAccountId) {
    knownOpponentAccountIds.add(uploaderPlayerAccountId);
  }

  for (const battle of battleProjections) {
    const opponentAccountId = asString(battle.opponent_account_id);
    if (opponentAccountId && opponentAccountId !== uploaderPlayerAccountId) {
      opponentAccountIds.add(opponentAccountId);
    }
  }

  if (opponentAccountIds.size === 0) {
    return knownOpponentAccountIds;
  }

  const opponentAccountIdList = Array.from(opponentAccountIds);
  const placeholders = opponentAccountIdList.map(() => "?").join(", ");
  const result = await env.DB.prepare(
    `
      SELECT player_account_id
      FROM users
      WHERE player_account_id IN (${placeholders})
    `,
  )
    .bind(...opponentAccountIdList)
    .all<{ player_account_id: string }>();

  for (const row of result.results) {
    knownOpponentAccountIds.add(row.player_account_id);
  }

  return knownOpponentAccountIds;
}

export async function handleUploadRunBundle(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as RunBundleRequest;
  const schemaVersion = asNumber(body.schema_version);
  const playerAccountId = asString(body.player_account_id);
  const submittedAtUtc = asString(body.submitted_at_utc);
  const artifactCodec = asString(body.artifact_codec);
  const decodedArtifact = decodeArtifactBytes(body.artifact_bytes);
  const artifactBytes = decodedArtifact?.bytes ?? null;
  const runId = asString(body.run_projection?.run_id);
  const runStatus = asString(body.run_projection?.status);
  const endedAtUtc = asString(body.run_projection?.ended_at_utc);
  const battleProjections = Array.isArray(body.battle_projections)
    ? body.battle_projections
    : [];

  const persistedPlayerAccountId = playerAccountId ?? AnonymousPlayerAccountId;

  if (
    schemaVersion == null ||
    !submittedAtUtc ||
    !artifactCodec ||
    !decodedArtifact ||
    !artifactBytes ||
    !runId ||
    !runStatus ||
    !endedAtUtc
  ) {
    if (body.artifact_bytes !== undefined && decodedArtifact == null) {
      logWarn("upload_run_bundle.artifact_bytes_decode_failed", {
        raw_type: Array.isArray(body.artifact_bytes) ? "array" : typeof body.artifact_bytes,
      });
    }
    return json({ error: "invalid_run_bundle_request" }, { status: 400 });
  }

  logInfo("upload_run_bundle.artifact_bytes_received", {
    encoding: decodedArtifact.encoding,
    decoded_bytes: artifactBytes.byteLength,
    schema_version: schemaVersion,
  });

  const distinctOpponentAccountIds = new Set<string>();
  for (const battle of battleProjections) {
    const battleId = asString(battle.battle_id);
    const battleRunId = asString(battle.run_id);
    if (!battleId) {
      return json({ error: "battle_id_required" }, { status: 400 });
    }

    if (!battleRunId || battleRunId !== runId) {
      return json({ error: "battle_run_id_mismatch" }, { status: 400 });
    }

    const opponentAccountId = asString(battle.opponent_account_id);
    if (opponentAccountId) {
      distinctOpponentAccountIds.add(opponentAccountId);
      if (distinctOpponentAccountIds.size > MaxDistinctOpponentAccountIds) {
        return json({ error: "too_many_opponent_account_ids" }, { status: 400 });
      }
    }
  }
  const finalBattleId = asString(
    battleProjections.length > 0
      ? battleProjections[battleProjections.length - 1]?.battle_id
      : null,
  );

  const safePlayerAccountSegment = sanitizeObjectKeySegment(persistedPlayerAccountId);
  const safeRunIdSegment = sanitizeObjectKeySegment(runId);
  if (safePlayerAccountSegment == null || safeRunIdSegment == null) {
    return json({ error: "invalid_run_bundle_request" }, { status: 400 });
  }

  const payloadHash = await sha256Base64(artifactBytes);
  const payloadHashSegment = payloadHash
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
  const objectKey = `run-bundles/${safePlayerAccountSegment}/${safeRunIdSegment}/${payloadHashSegment}.mpack.gz`;
  await env.RUN_BUNDLE_BUCKET.put(objectKey, artifactBytes, {
    httpMetadata: {
      contentType: artifactCodec,
    },
    customMetadata: {
      retention_days: String(getRunBundleRetentionDays(env)),
    },
  });

  const createdAtUtc = new Date().toISOString();
  // bundle_id is deterministically the run_id: one run -> one run_bundles row.
  // INSERT OR REPLACE absorbs three cases with zero reads:
  //   - fresh run        -> plain INSERT
  //   - progress update  -> PK conflict on bundle_id, replace in place
  //   - transition row   -> UNIQUE(installation_id, run_id, payload_hash) conflict
  //                         with a pre-existing uuid-style bundle; old row is dropped
  //                         and the run_id-keyed row wins. Retention cleans up the
  //                         orphan R2 object. Safe to simplify after transition.
  const bundleId = runId;
  await env.DB.prepare(
    `
      INSERT OR REPLACE INTO run_bundles (
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
      LegacyInstallationId,
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
      LegacyInstallationId,
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

  const knownOpponentAccountIds = await loadKnownOpponentAccountIds(
    battleProjections,
    persistedPlayerAccountId,
    env,
  );

  const battleStatements = battleProjections
    .filter((battle) => shouldProjectBattle(battle, knownOpponentAccountIds))
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
            is_bundle_final_battle,
            updated_at_utc
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
            is_bundle_final_battle = excluded.is_bundle_final_battle,
            updated_at_utc = excluded.updated_at_utc
        `,
      ).bind(
        asString(battle.battle_id),
        runId,
        LegacyInstallationId,
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
        asString(battle.battle_id) === finalBattleId ? 1 : 0,
        createdAtUtc,
      ),
    );

  if (battleStatements.length > 0) {
    await env.DB.batch(battleStatements);
  }

  return json({ status: "accepted", bundle_id: bundleId, object_key: objectKey });
}
