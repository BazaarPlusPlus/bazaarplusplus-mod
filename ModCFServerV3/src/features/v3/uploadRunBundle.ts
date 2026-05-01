import { base64ToBytes } from "../../crypto/base64";
import { sha256Base64 } from "../../crypto/hash";
import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { optionalFiniteNumber, optionalTrimmedString } from "../../http/request";
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

function validateBattleProjections(
  battleProjections: BattleProjection[],
  runId: string,
): { finalBattleId: string | null } | Response {
  const distinctOpponentAccountIds = new Set<string>();
  for (const battle of battleProjections) {
    const battleId = optionalTrimmedString(battle.battle_id);
    const battleRunId = optionalTrimmedString(battle.run_id);
    if (!battleId) {
      return json({ error: "battle_id_required" }, { status: 400 });
    }

    if (!battleRunId || battleRunId !== runId) {
      return json({ error: "battle_run_id_mismatch" }, { status: 400 });
    }

    const opponentAccountId = optionalTrimmedString(battle.opponent_account_id);
    if (opponentAccountId) {
      distinctOpponentAccountIds.add(opponentAccountId);
      if (distinctOpponentAccountIds.size > MaxDistinctOpponentAccountIds) {
        return json({ error: "too_many_opponent_account_ids" }, { status: 400 });
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

function toPayloadHashSegment(payloadHash: string): string {
  return payloadHash
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

function buildRunBundleObjectKey(
  playerAccountId: string,
  runId: string,
  payloadHash: string,
): string | null {
  const safePlayerAccountSegment = sanitizeObjectKeySegment(playerAccountId);
  const safeRunIdSegment = sanitizeObjectKeySegment(runId);
  if (safePlayerAccountSegment == null || safeRunIdSegment == null) {
    return null;
  }

  return `run-bundles/${safePlayerAccountSegment}/${safeRunIdSegment}/${toPayloadHashSegment(payloadHash)}.mpack.gz`;
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
  // INSERT OR REPLACE absorbs three cases with zero reads:
  //   - fresh run        -> plain INSERT
  //   - progress update  -> PK conflict on bundle_id, replace in place
  //   - transition row   -> UNIQUE(installation_id, run_id, payload_hash) conflict
  //                         with a pre-existing uuid-style bundle; old row is dropped
  //                         and the run_id-keyed row wins. Retention cleans up the
  //                         orphan R2 object. Safe to simplify after transition.
  await args.env.DB.prepare(
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
      args.bundleId,
      LegacyInstallationId,
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
  body: RunBundleRequest;
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
      args.runId,
      LegacyInstallationId,
      args.persistedPlayerAccountId,
      args.bundleId,
      args.runStatus,
      optionalTrimmedString(args.body.run_projection?.hero_id),
      optionalTrimmedString(args.body.run_projection?.hero_name),
      optionalTrimmedString(args.body.run_projection?.player_rank),
      optionalFiniteNumber(args.body.run_projection?.player_rating),
      optionalFiniteNumber(args.body.run_projection?.player_position),
      optionalTrimmedString(args.body.run_projection?.started_at_utc),
      args.endedAtUtc,
      optionalFiniteNumber(args.body.run_projection?.final_day),
      optionalFiniteNumber(args.body.run_projection?.final_wins),
      optionalFiniteNumber(args.body.run_projection?.final_losses),
      optionalTrimmedString(args.body.run_projection?.final_player_rank),
      optionalFiniteNumber(args.body.run_projection?.final_player_rating),
      optionalFiniteNumber(args.body.run_projection?.final_player_position),
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
        optionalTrimmedString(battle.battle_id),
        args.runId,
        LegacyInstallationId,
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

export async function handleUploadRunBundle(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as RunBundleRequest;
  const schemaVersion = optionalFiniteNumber(body.schema_version);
  const playerAccountId = optionalTrimmedString(body.player_account_id);
  const submittedAtUtc = optionalTrimmedString(body.submitted_at_utc);
  const artifactCodec = optionalTrimmedString(body.artifact_codec);
  const decodedArtifact = decodeArtifactBytes(body.artifact_bytes);
  const artifactBytes = decodedArtifact?.bytes ?? null;
  const runId = optionalTrimmedString(body.run_projection?.run_id);
  const runStatus = optionalTrimmedString(body.run_projection?.status);
  const endedAtUtc = optionalTrimmedString(body.run_projection?.ended_at_utc);
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

  const battleValidation = validateBattleProjections(battleProjections, runId);
  if (battleValidation instanceof Response) {
    return battleValidation;
  }

  const payloadHash = await sha256Base64(artifactBytes);
  const objectKey = buildRunBundleObjectKey(
    persistedPlayerAccountId,
    runId,
    payloadHash,
  );
  if (objectKey == null) {
    return json({ error: "invalid_run_bundle_request" }, { status: 400 });
  }

  await putRunBundleArtifact({
    env,
    objectKey,
    artifactBytes,
    artifactCodec,
  });

  const createdAtUtc = new Date().toISOString();
  const bundleId = runId;
  await insertRunBundleProjection({
    env,
    bundleId,
    persistedPlayerAccountId,
    runId,
    payloadHash,
    schemaVersion,
    objectKey,
    artifactCodec,
    sizeBytes: artifactBytes.byteLength,
    submittedAtUtc,
    createdAtUtc,
  });

  await upsertRunProjection({
    env,
    body,
    bundleId,
    persistedPlayerAccountId,
    runId,
    runStatus,
    endedAtUtc,
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
    runId,
    bundleId,
    persistedPlayerAccountId,
    finalBattleId: battleValidation.finalBattleId,
    createdAtUtc,
  });

  return json({ status: "accepted", bundle_id: bundleId, object_key: objectKey });
}
