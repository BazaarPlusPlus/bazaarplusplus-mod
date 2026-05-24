import { toBase64Url } from "../../crypto/base64";
import { sha256Base64 } from "../../crypto/hash";
import type { Env } from "../../env";
import { json, jsonError, readJson } from "../../http/json";
import { optionalFiniteNumber, optionalTrimmedString } from "../../http/request";
import { objectKeySegment, parseBody } from "../../http/validation";
import { logInfo, logWarn } from "../../observability";
import { getRunBundleRetentionDays } from "../../config/v3";

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

type RawRunBundleRequest = {
  schema_version?: unknown;
  player_account_id?: unknown;
  submitted_at_utc?: unknown;
  artifact_codec?: unknown;
  artifact_bytes?: unknown;
  run_projection?: Record<string, unknown>;
  battle_projections?: unknown;
};

const AnonymousPlayerAccountId = "anonymous-player";
const MaxDistinctOpponentAccountIds = 20;

function parseRunBundleOuter(rawBody: RawRunBundleRequest) {
  return parseBody(rawBody, {
    schema_version: { type: "finiteNumber", errorCode: "invalid_run_bundle_request" },
    player_account_id: "string?",
    submitted_at_utc: { type: "string", errorCode: "invalid_run_bundle_request" },
    artifact_codec: { type: "string", errorCode: "invalid_run_bundle_request" },
    artifact_bytes: {
      type: "byteArrayOrBase64",
      errorCode: "invalid_run_bundle_request",
    },
  });
}

function shouldProjectBattle(
  battle: BattleProjection,
  knownOpponentAccountIds: ReadonlySet<string>,
): boolean {
  const opponentAccountId = optionalTrimmedString(battle.opponent_account_id);
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
    const opponentAccountId = optionalTrimmedString(battle.opponent_account_id);
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
      FROM seen_player_accounts
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

function validateBattleProjections(
  battleProjections: BattleProjection[],
  runId: string,
): { finalBattleId: string | null } {
  const distinctOpponentAccountIds = new Set<string>();
  for (const battle of battleProjections) {
    const battleId = optionalTrimmedString(battle.battle_id);
    const battleRunId = optionalTrimmedString(battle.run_id);
    if (!battleId) {
      throw jsonError("battle_id_required");
    }

    if (!battleRunId || battleRunId !== runId) {
      throw jsonError("battle_run_id_mismatch");
    }

    const opponentAccountId = optionalTrimmedString(battle.opponent_account_id);
    if (opponentAccountId) {
      distinctOpponentAccountIds.add(opponentAccountId);
      if (distinctOpponentAccountIds.size > MaxDistinctOpponentAccountIds) {
        throw jsonError("too_many_opponent_account_ids");
      }
    }
  }

  return {
    finalBattleId: optionalTrimmedString(
      battleProjections.length > 0
        ? battleProjections[battleProjections.length - 1]?.battle_id
        : null,
    ),
  };
}

function buildRunBundleObjectKey(
  playerAccountId: string,
  runId: string,
  payloadHash: string,
): string | null {
  const safePlayerAccountSegment = objectKeySegment(playerAccountId);
  const safeRunIdSegment = objectKeySegment(runId);
  if (safePlayerAccountSegment == null || safeRunIdSegment == null) {
    return null;
  }

  return `run-bundles/${safePlayerAccountSegment}/${safeRunIdSegment}/${toBase64Url(payloadHash)}.mpack.gz`;
}

async function putRunBundleArtifact(args: {
  env: Env;
  objectKey: string;
  artifactBytes: Uint8Array;
  artifactCodec: string;
}): Promise<void> {
  await args.env.RUN_BUNDLE_BUCKET.put(args.objectKey, args.artifactBytes, {
    httpMetadata: {
      contentType: args.artifactCodec,
    },
    customMetadata: {
      retention_days: String(getRunBundleRetentionDays(args.env)),
    },
  });
}

async function insertRunBundleProjection(args: {
  env: Env;
  bundleId: string;
  persistedPlayerAccountId: string;
  runId: string;
  payloadHash: string;
  schemaVersion: number;
  objectKey: string;
  artifactCodec: string;
  sizeBytes: number;
  submittedAtUtc: string;
  createdAtUtc: string;
}): Promise<void> {
  // bundle_id is deterministically the run_id: one run -> one run_bundles row.
  // INSERT OR REPLACE keeps idempotent retries cheap on PK conflict.
  await args.env.DB.prepare(
    `
      INSERT OR REPLACE INTO run_bundles (
        bundle_id,
        player_account_id,
        run_id,
        payload_hash,
        schema_version,
        object_key,
        codec,
        size_bytes,
        submitted_at_utc,
        created_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `,
  )
    .bind(
      args.bundleId,
      args.persistedPlayerAccountId,
      args.runId,
      args.payloadHash,
      args.schemaVersion,
      args.objectKey,
      args.artifactCodec,
      args.sizeBytes,
      args.submittedAtUtc,
      args.createdAtUtc,
    )
    .run();
}

async function upsertRunProjection(args: {
  env: Env;
  runProjection: Record<string, unknown>;
  bundleId: string;
  persistedPlayerAccountId: string;
  runId: string;
  runStatus: string;
  endedAtUtc: string;
  createdAtUtc: string;
}): Promise<void> {
  await args.env.DB.prepare(
    `
      INSERT INTO runs (
        run_id,
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
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(run_id) DO UPDATE SET
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
      args.runId,
      args.persistedPlayerAccountId,
      args.bundleId,
      args.runStatus,
      optionalTrimmedString(args.runProjection.hero_id),
      optionalTrimmedString(args.runProjection.hero_name),
      optionalTrimmedString(args.runProjection.player_rank),
      optionalFiniteNumber(args.runProjection.player_rating),
      optionalFiniteNumber(args.runProjection.player_position),
      optionalTrimmedString(args.runProjection.started_at_utc),
      args.endedAtUtc,
      optionalFiniteNumber(args.runProjection.final_day),
      optionalFiniteNumber(args.runProjection.final_wins),
      optionalFiniteNumber(args.runProjection.final_losses),
      optionalTrimmedString(args.runProjection.final_player_rank),
      optionalFiniteNumber(args.runProjection.final_player_rating),
      optionalFiniteNumber(args.runProjection.final_player_position),
      args.createdAtUtc,
    )
    .run();
}

async function upsertBattleProjections(args: {
  env: Env;
  battleProjections: BattleProjection[];
  knownOpponentAccountIds: ReadonlySet<string>;
  runId: string;
  bundleId: string;
  persistedPlayerAccountId: string;
  finalBattleId: string | null;
  createdAtUtc: string;
}): Promise<void> {
  const battleStatements = args.battleProjections
    .filter((battle) => shouldProjectBattle(battle, args.knownOpponentAccountIds))
    .map((battle) =>
      args.env.DB.prepare(
        `
          INSERT INTO battles (
            battle_id,
            run_id,
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
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
          ON CONFLICT(battle_id) DO UPDATE SET
            run_id = excluded.run_id,
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
        optionalTrimmedString(battle.battle_id),
        args.runId,
        args.persistedPlayerAccountId,
        args.bundleId,
        optionalTrimmedString(battle.recorded_at_utc),
        optionalFiniteNumber(battle.day),
        optionalTrimmedString(battle.player_name),
        optionalTrimmedString(battle.player_account_id),
        optionalTrimmedString(battle.player_hero),
        optionalTrimmedString(battle.player_rank),
        optionalFiniteNumber(battle.player_rating),
        optionalFiniteNumber(battle.player_level),
        optionalTrimmedString(battle.opponent_name),
        optionalTrimmedString(battle.opponent_account_id),
        optionalTrimmedString(battle.opponent_hero),
        optionalTrimmedString(battle.opponent_rank),
        optionalFiniteNumber(battle.opponent_rating),
        optionalFiniteNumber(battle.opponent_level),
        optionalTrimmedString(battle.result),
        battle.replay_available === true ? 1 : 0,
        optionalTrimmedString(battle.battle_id) === args.finalBattleId ? 1 : 0,
        args.createdAtUtc,
      ),
    );

  if (battleStatements.length > 0) {
    await args.env.DB.batch(battleStatements);
  }
}

async function rememberUploader(
  env: Env,
  uploaderPlayerAccountId: string,
  nowUtc: string,
): Promise<void> {
  if (
    !uploaderPlayerAccountId ||
    uploaderPlayerAccountId === AnonymousPlayerAccountId
  ) {
    return;
  }

  await env.DB.prepare(
    `
      INSERT INTO seen_player_accounts (
        player_account_id,
        first_seen_at_utc,
        last_seen_at_utc
      ) VALUES (?, ?, ?)
      ON CONFLICT(player_account_id) DO UPDATE SET
        last_seen_at_utc = excluded.last_seen_at_utc
    `,
  )
    .bind(uploaderPlayerAccountId, nowUtc, nowUtc)
    .run();
}

export async function handleUploadRunBundle(
  request: Request,
  env: Env,
): Promise<Response> {
  const rawBody = (await readJson(request)) as RawRunBundleRequest;

  // All required-field validation rolls up to a single `invalid_run_bundle_request`
  // error to preserve the existing wire-compatible response from v3.
  let outer: ReturnType<typeof parseRunBundleOuter>;
  try {
    outer = parseRunBundleOuter(rawBody);
  } catch (error) {
    if (rawBody.artifact_bytes !== undefined) {
      // Preserve the v3 diagnostic for callers sending unrecognised artifact encodings.
      logWarn("upload_run_bundle.artifact_bytes_decode_failed", {
        raw_type: Array.isArray(rawBody.artifact_bytes)
          ? "array"
          : typeof rawBody.artifact_bytes,
      });
    }
    throw error;
  }
  const runProjectionRaw =
    typeof rawBody.run_projection === "object" && rawBody.run_projection != null
      ? rawBody.run_projection
      : {};
  const inner = parseBody(runProjectionRaw, {
    run_id: { type: "string", errorCode: "invalid_run_bundle_request" },
    status: { type: "string", errorCode: "invalid_run_bundle_request" },
    ended_at_utc: { type: "string", errorCode: "invalid_run_bundle_request" },
  });

  const battleProjections = Array.isArray(rawBody.battle_projections)
    ? (rawBody.battle_projections as BattleProjection[])
    : [];

  const persistedPlayerAccountId = outer.player_account_id ?? AnonymousPlayerAccountId;

  logInfo("upload_run_bundle.artifact_bytes_received", {
    encoding: outer.artifact_bytes.encoding,
    decoded_bytes: outer.artifact_bytes.bytes.byteLength,
    schema_version: outer.schema_version,
  });

  const { finalBattleId } = validateBattleProjections(battleProjections, inner.run_id);

  const payloadHash = await sha256Base64(outer.artifact_bytes.bytes);
  const objectKey = buildRunBundleObjectKey(
    persistedPlayerAccountId,
    inner.run_id,
    payloadHash,
  );
  if (objectKey == null) {
    return jsonError("invalid_run_bundle_request");
  }

  await putRunBundleArtifact({
    env,
    objectKey,
    artifactBytes: outer.artifact_bytes.bytes,
    artifactCodec: outer.artifact_codec,
  });

  const createdAtUtc = new Date().toISOString();
  const bundleId = inner.run_id;
  await insertRunBundleProjection({
    env,
    bundleId,
    persistedPlayerAccountId,
    runId: inner.run_id,
    payloadHash,
    schemaVersion: outer.schema_version,
    objectKey,
    artifactCodec: outer.artifact_codec,
    sizeBytes: outer.artifact_bytes.bytes.byteLength,
    submittedAtUtc: outer.submitted_at_utc,
    createdAtUtc,
  });

  await upsertRunProjection({
    env,
    runProjection: runProjectionRaw,
    bundleId,
    persistedPlayerAccountId,
    runId: inner.run_id,
    runStatus: inner.status,
    endedAtUtc: inner.ended_at_utc,
    createdAtUtc,
  });

  const knownOpponentAccountIds = await loadKnownOpponentAccountIds(
    battleProjections,
    persistedPlayerAccountId,
    env,
  );
  await upsertBattleProjections({
    env,
    battleProjections,
    knownOpponentAccountIds,
    runId: inner.run_id,
    bundleId,
    persistedPlayerAccountId,
    finalBattleId,
    createdAtUtc,
  });

  await rememberUploader(env, persistedPlayerAccountId, createdAtUtc);

  return json({ status: "accepted", bundle_id: bundleId, object_key: objectKey });
}
