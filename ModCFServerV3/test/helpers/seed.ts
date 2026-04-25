type SqlValue = string | number | null;

export type InsertTokenArgs = {
  token: string;
  playerAccountId: string;
  issuedAtUtc: string;
  revokedAtUtc?: string | null;
  lastUsedAtUtc?: string | null;
};

export type InsertV3UserArgs = {
  playerAccountId: string;
  playerUsername: string;
  passwordHash: string;
  streamPlatform?: string | null;
  streamChannelId?: string | null;
  streamUrl?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastLoginAtUtc?: string | null;
};

export type InsertV3RunBundleArgs = {
  bundleId: string;
  installationId: string;
  playerAccountId: string;
  runId: string;
  payloadHash: string;
  schemaVersion: number;
  objectKey: string;
  codec: string;
  sizeBytes: number;
  submittedAtUtc: string;
  createdAtUtc: string;
};

export type InsertV3BattleArgs = {
  battleId: string;
  runId: string;
  installationId: string;
  playerAccountId: string;
  bundleId: string;
  recordedAtUtc: string;
  day?: number | null;
  playerName?: string | null;
  playerAccountIdInPayload?: string | null;
  playerHero?: string | null;
  playerRank?: string | null;
  playerRating?: number | null;
  playerLevel?: number | null;
  opponentName?: string | null;
  opponentAccountId?: string | null;
  opponentHero?: string | null;
  opponentRank?: string | null;
  opponentRating?: number | null;
  opponentLevel?: number | null;
  result?: string | null;
  replayAvailable: number;
  updatedAtUtc: string;
};

export type InsertReplayTokenArgs = {
  token: string;
  battleId: string;
  requestedByPlayerAccountId: string;
  expiresAtUtc: string;
  createdAtUtc: string;
  usedAtUtc?: string | null;
  revokedAtUtc?: string | null;
};

function statement<T extends SqlValue[]>(
  db: D1Database,
  sql: string,
  values: T,
): D1PreparedStatement {
  return db.prepare(sql).bind(...values);
}

export async function insertToken(
  db: D1Database,
  args: InsertTokenArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO tokens (
        token,
        player_account_id,
        issued_at_utc,
        revoked_at_utc,
        last_used_at_utc
      ) VALUES (?, ?, ?, ?, ?)
    `,
    [
      args.token,
      args.playerAccountId,
      args.issuedAtUtc,
      args.revokedAtUtc ?? null,
      args.lastUsedAtUtc ?? null,
    ],
  ).run();
}

export async function insertV3User(
  db: D1Database,
  args: InsertV3UserArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO users (
        player_account_id,
        player_username,
        password_hash,
        stream_platform,
        stream_channel_id,
        stream_url,
        created_at_utc,
        updated_at_utc,
        last_login_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    `,
    [
      args.playerAccountId,
      args.playerUsername,
      args.passwordHash,
      args.streamPlatform ?? null,
      args.streamChannelId ?? null,
      args.streamUrl ?? null,
      args.createdAtUtc,
      args.updatedAtUtc,
      args.lastLoginAtUtc ?? null,
    ],
  ).run();
}

export async function insertRunBundle(
  db: D1Database,
  args: InsertV3RunBundleArgs,
): Promise<void> {
  await statement(
    db,
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
    [
      args.bundleId,
      args.installationId,
      args.playerAccountId,
      args.runId,
      args.payloadHash,
      args.schemaVersion,
      args.objectKey,
      args.codec,
      args.sizeBytes,
      args.submittedAtUtc,
      args.createdAtUtc,
    ],
  ).run();
}

export async function insertV3Battle(
  db: D1Database,
  args: InsertV3BattleArgs,
): Promise<void> {
  await statement(
    db,
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
    `,
    [
      args.battleId,
      args.runId,
      args.installationId,
      args.playerAccountId,
      args.bundleId,
      args.recordedAtUtc,
      args.day ?? null,
      args.playerName ?? null,
      args.playerAccountIdInPayload ?? null,
      args.playerHero ?? null,
      args.playerRank ?? null,
      args.playerRating ?? null,
      args.playerLevel ?? null,
      args.opponentName ?? null,
      args.opponentAccountId ?? null,
      args.opponentHero ?? null,
      args.opponentRank ?? null,
      args.opponentRating ?? null,
      args.opponentLevel ?? null,
      args.result ?? null,
      args.replayAvailable,
      args.updatedAtUtc,
    ],
  ).run();
}

export async function insertReplayToken(
  db: D1Database,
  args: InsertReplayTokenArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO replay_tokens (
        token,
        battle_id,
        requested_by_player_account_id,
        expires_at_utc,
        created_at_utc,
        used_at_utc,
        revoked_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
    `,
    [
      args.token,
      args.battleId,
      args.requestedByPlayerAccountId,
      args.expiresAtUtc,
      args.createdAtUtc,
      args.usedAtUtc ?? null,
      args.revokedAtUtc ?? null,
    ],
  ).run();
}

export async function countRows(
  db: D1Database,
  tableName: string,
): Promise<number> {
  const result = await db
    .prepare(`SELECT COUNT(*) AS count FROM ${tableName}`)
    .first<{ count: number }>();
  return result?.count ?? 0;
}

export async function selectFirst<T>(
  db: D1Database,
  sql: string,
  ...values: SqlValue[]
): Promise<T | null> {
  return db.prepare(sql).bind(...values).first<T>();
}

async function deleteAllR2(bucket: R2Bucket): Promise<void> {
  let cursor: string | undefined;

  do {
    const page = await bucket.list({ cursor });
    if (page.objects.length > 0) {
      await bucket.delete(page.objects.map((object) => object.key));
    }
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor != null);
}

export async function resetTestState(env: Cloudflare.Env): Promise<void> {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM replay_tokens"),
    env.DB.prepare("DELETE FROM battles"),
    env.DB.prepare("DELETE FROM runs"),
    env.DB.prepare("DELETE FROM run_bundles"),
    env.DB.prepare("DELETE FROM tokens"),
    env.DB.prepare("DELETE FROM users"),
  ]);
  await deleteAllR2(env.RUN_BUNDLE_BUCKET);
  env.ALLOW_UNAUTHENTICATED_REPLAY_LINKS = "false";
  env.ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS = "false";
  env.GHOST_QUERY_LOOKBACK_DAYS = "3";
  env.RUN_BUNDLE_RETENTION_DAYS = "5";
}
