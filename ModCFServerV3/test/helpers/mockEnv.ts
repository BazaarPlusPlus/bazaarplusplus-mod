export type ClientRow = {
  client_id: string;
  install_id: string;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
  registered_at_utc: string;
  last_seen_at_utc?: string | null;
  revoked_at_utc?: string | null;
};

export type V3UserRow = {
  player_account_id: string;
  player_username: string;
  password_hash: string;
  stream_platform?: string | null;
  stream_channel_id?: string | null;
  stream_url?: string | null;
  created_at_utc?: string;
  updated_at_utc?: string;
  last_login_at_utc?: string | null;
};

export type V3InstallationRow = {
  installation_id: string;
  player_account_id: string;
  public_key: string;
  status: string;
  created_at_utc: string;
  last_seen_at_utc: string | null;
  revoked_at_utc: string | null;
};

export type V3InstallationSessionRow = {
  session_id: string;
  player_account_id: string;
  created_at_utc: string;
  expires_at_utc: string;
  revoked_at_utc: string | null;
};

export type V3TokenRow = {
  token: string;
  player_account_id?: string;
  player_id?: string;
  installation_id?: string | null;
  issued_at_utc?: string;
  revoked_at_utc: string | null;
  last_used_at_utc: string | null;
};

export type V3InstallationObservationRow = {
  installation_id: string;
  observed_player_account_id: string;
  observed_player_username: string;
  observed_at_utc: string;
  signature: string;
  status: string;
};

export type V3RunBundleRow = {
  bundle_id: string;
  installation_id: string;
  player_account_id: string;
  run_id: string;
  payload_hash: string;
  schema_version: number;
  object_key: string;
  codec: string;
  size_bytes: number;
  submitted_at_utc: string;
  created_at_utc: string;
};

export type V3RunRow = {
  run_id: string;
  installation_id: string;
  player_account_id: string;
  bundle_id: string;
  status: string;
  hero_id: string | null;
  hero_name: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_position: number | null;
  started_at_utc: string | null;
  ended_at_utc: string;
  final_day: number | null;
  final_wins: number | null;
  final_losses: number | null;
  final_player_rank: string | null;
  final_player_rating: number | null;
  final_player_position: number | null;
  updated_at_utc: string;
};

export type V3BattleRow = {
  battle_id: string;
  run_id: string;
  installation_id: string;
  player_account_id: string;
  bundle_id: string;
  recorded_at_utc: string;
  day: number | null;
  player_name: string | null;
  player_account_id_in_payload: string | null;
  player_hero: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_level: number | null;
  opponent_name: string | null;
  opponent_account_id: string | null;
  opponent_hero: string | null;
  opponent_rank: string | null;
  opponent_rating: number | null;
  opponent_level: number | null;
  result: string | null;
  replay_available: number;
  updated_at_utc: string;
};

export type V3ReplayTokenRow = {
  token: string;
  battle_id: string;
  requested_by_player_account_id: string;
  expires_at_utc: string;
  created_at_utc: string;
  used_at_utc: string | null;
  revoked_at_utc: string | null;
};

export type PlayerLinkRow = {
  client_id: string;
  player_account_id: string;
  bound_at_utc: string;
  last_confirmed_at_utc: string;
};

export type RunRow = {
  run_id: string;
  client_id: string;
  player_account_id: string | null;
  status: string;
  hero_id: string | null;
  hero_name: string | null;
  started_at_utc: string | null;
  ended_at_utc: string;
  final_day: number | null;
  final_wins: number | null;
  final_losses: number | null;
  mmr: number | null;
  summary_schema_version: number | null;
  summary_object_key: string | null;
  created_at_utc: string;
  updated_at_utc: string;
};

export type BattleRow = {
  battle_id: string;
  run_id: string | null;
  client_id: string;
  uploader_player_account_id: string | null;
  recorded_at_utc: string;
  day: number | null;
  hour: number | null;
  player_name: string | null;
  player_account_id: string | null;
  player_hero: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_level: number | null;
  opponent_name: string | null;
  opponent_account_id: string | null;
  opponent_hero: string | null;
  opponent_rank: string | null;
  opponent_rating: number | null;
  opponent_level: number | null;
  combat_kind: string;
  result: string | null;
  winner_combatant_id: string | null;
  loser_combatant_id: string | null;
  replay_schema_version: number;
  replay_object_key: string;
  replay_size_bytes: number;
  created_at_utc: string;
  updated_at_utc: string;
};

export type ReplayTokenRow = {
  token: string;
  battle_id: string;
  requested_by_player_account_id: string;
  expires_at_utc: string;
  created_at_utc: string;
  used_at_utc: string | null;
  revoked_at_utc: string | null;
};

export type RunUploadRow = {
  client_id: string;
  install_id: string;
  run_id: string;
  payload_sha256: string;
  payload_object_key?: string | null;
  payload_bytes?: number | null;
  schema_version?: number | null;
  projection_status: string;
  projected_at_utc: string | null;
  last_error_code?: string | null;
  last_error_detail?: string | null;
  uploaded_at_utc?: string;
  created_at_utc?: string;
  updated_at_utc?: string;
  projection_error: string | null;
};

export type ClientUidBindingRow = {
  binding_id: string;
  client_id: string;
  uid: string;
  bound_at_utc: string;
  unbound_at_utc: string | null;
};

export type ClientPlayerAccountBindingRow = {
  binding_id: string;
  client_id: string;
  player_account_id: string;
  binding_source: string;
  confidence: number;
  bound_at_utc: string;
  unbound_at_utc: string | null;
};

export type ClientPlayerAccountObservationRow = {
  client_id: string;
  player_account_id: string;
  first_seen_at_utc: string;
  last_seen_at_utc: string;
  evidence_count: number;
};

export type UidPlayerAccountRow = {
  uid: string;
  player_account_id: string;
  first_seen_at_utc: string;
  last_seen_at_utc: string;
  last_client_id: string;
};

export type PvpBattleRow = {
  battle_id: string;
  run_id: string | null;
  source_client_id: string;
  recorded_at_utc: string;
  day: number | null;
  hour: number | null;
  encounter_id: string | null;
  player_name: string | null;
  player_account_id: string | null;
  player_hero: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_level: number | null;
  opponent_name: string | null;
  opponent_account_id: string | null;
  opponent_hero: string | null;
  opponent_rank: string | null;
  opponent_rating: number | null;
  opponent_level: number | null;
  combat_kind: string;
  result: string | null;
  winner_combatant_id: string | null;
  loser_combatant_id: string | null;
  replay_available?: number;
  replay_object_key?: string | null;
  replay_uploaded_at_utc?: string | null;
  created_at_utc: string;
  updated_at_utc: string;
};

class MockD1Statement {
  constructor(
    public readonly db: MockD1Database,
    public readonly sql: string,
    public readonly params: unknown[] = [],
  ) {}

  bind(...params: unknown[]): MockD1Statement {
    return new MockD1Statement(this.db, this.sql, params);
  }

  async first<T>(): Promise<T | null> {
    return this.db.first<T>(this.sql, this.params);
  }

  async all<T>(): Promise<{ results: T[] }> {
    return this.db.all<T>(this.sql, this.params);
  }

  async run(): Promise<{ success: boolean; meta: { changes: number } }> {
    const result = this.db.run(this.sql, this.params);
    return { success: true, meta: { changes: result.changes } };
  }
}

export class MockD1Database {
  public readonly v3Users = new Map<string, V3UserRow>();
  public readonly v3Installations = new Map<string, V3InstallationRow>();
  public readonly v3InstallationSessions = new Map<string, V3InstallationSessionRow>();
  public readonly v3Tokens = new Map<string, V3TokenRow>();
  public readonly v3InstallationObservations = new Map<
    string,
    V3InstallationObservationRow
  >();
  public readonly v3RunBundles = new Map<string, V3RunBundleRow>();
  public readonly v3Runs = new Map<string, V3RunRow>();
  public readonly v3Battles = new Map<string, V3BattleRow>();
  public readonly v3ReplayTokens = new Map<string, V3ReplayTokenRow>();
  public readonly clients = new Map<string, ClientRow>();
  public readonly playerLinks = new Map<string, PlayerLinkRow>();
  public readonly runs = new Map<string, RunRow>();
  public readonly battles = new Map<string, BattleRow>();
  public readonly replayTokens = new Map<string, ReplayTokenRow>();
  public readonly runUploads = new Map<string, RunUploadRow>();
  public readonly clientUidBindings = new Map<string, ClientUidBindingRow>();
  public readonly clientPlayerAccountBindings = new Map<
    string,
    ClientPlayerAccountBindingRow
  >();
  public readonly clientPlayerAccountObservations = new Map<
    string,
    ClientPlayerAccountObservationRow
  >();
  public readonly uidPlayerAccounts = new Map<string, UidPlayerAccountRow>();
  public readonly pvpBattles = new Map<string, PvpBattleRow>();
  prepare(sql: string): MockD1Statement {
    return new MockD1Statement(this, sql);
  }

  async batch(
    statements: MockD1Statement[],
  ): Promise<Array<{ success: boolean }>> {
    const results: Array<{ success: boolean }> = [];
    for (const statement of statements) {
      this.run(statement.sql, statement.params);
      results.push({ success: true });
    }

    return results;
  }

  first<T>(sql: string, params: unknown[]): T | null {
    if (sql.includes("FROM users") && sql.includes("WHERE player_account_id = ?")) {
      const playerAccountId = String(params[0] ?? "");
      const row = this.v3Users.get(playerAccountId);
      return row ? ({ player_account_id: row.player_account_id } as T) : null;
    }

    if (sql.includes("FROM users") && sql.includes("WHERE player_username = ?")) {
      const playerUsername = String(params[0] ?? "");
      const row = Array.from(this.v3Users.values()).find(
        (candidate) => candidate.player_username === playerUsername,
      );
      return (row as T | undefined) ?? null;
    }

    if (sql.includes("FROM installation_sessions")) {
      const sessionId = String(params[0] ?? "");
      return (this.v3InstallationSessions.get(sessionId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM installations")) {
      const installationId = String(params[0] ?? "");
      return (this.v3Installations.get(installationId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM replay_tokens")) {
      const token = String(params[0] ?? "");
      return (this.v3ReplayTokens.get(token) as T | undefined) ?? null;
    }

    if (sql.includes("FROM run_bundles")) {
      if (
        sql.includes("WHERE installation_id = ?")
        && sql.includes("AND run_id = ?")
        && sql.includes("AND payload_hash = ?")
      ) {
        const installationId = String(params[0] ?? "");
        const runId = String(params[1] ?? "");
        const payloadHash = String(params[2] ?? "");
        const row = Array.from(this.v3RunBundles.values()).find(
          (candidate) =>
            candidate.installation_id === installationId
            && candidate.run_id === runId
            && candidate.payload_hash === payloadHash,
        );
        return (row as T | undefined) ?? null;
      }

      const bundleId = String(params[0] ?? "");
      return (this.v3RunBundles.get(bundleId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM battles") && sql.includes("WHERE battle_id = ?")) {
      const battleId = String(params[0] ?? "");
      return (this.v3Battles.get(battleId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM clients")) {
      const clientId = String(params[0] ?? "");
      return (this.clients.get(clientId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM player_links")) {
      const clientId = String(params[0] ?? "");
      return (this.playerLinks.get(clientId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM battles") && sql.includes("WHERE battle_id = ?")) {
      const battleId = String(params[0] ?? "");
      return (this.battles.get(battleId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM replay_tokens")) {
      const token = String(params[0] ?? "");
      return (this.replayTokens.get(token) as T | undefined) ?? null;
    }

    if (sql.includes("FROM client_uid_bindings")) {
      const clientId = String(params[0] ?? "");
      const row = Array.from(this.clientUidBindings.values()).find(
        (binding) =>
          binding.client_id === clientId && binding.unbound_at_utc == null,
      );
      return (row ? ({ uid: row.uid } as T) : null);
    }

    if (sql.includes("FROM client_player_account_bindings")) {
      const clientId = String(params[0] ?? "");
      const row = Array.from(this.clientPlayerAccountBindings.values()).find(
        (binding) =>
          binding.client_id === clientId && binding.unbound_at_utc == null,
      );
      return row
        ? ({ player_account_id: row.player_account_id } as T)
        : null;
    }

    if (sql.includes("WHERE pb.battle_id = ?")) {
      const battleId = String(params[0] ?? "");
      const row = this.pvpBattles.get(battleId);
      return row
        ? ({
            battle_id: row.battle_id,
            run_id: row.run_id,
            recorded_at_utc: row.recorded_at_utc,
            day: row.day,
            hour: row.hour,
            encounter_id: row.encounter_id,
            player_name: row.player_name,
            player_account_id: row.player_account_id,
            player_hero: row.player_hero,
            player_rank: row.player_rank,
            player_rating: row.player_rating,
            player_level: row.player_level,
            opponent_name: row.opponent_name,
            opponent_account_id: row.opponent_account_id,
            opponent_hero: row.opponent_hero,
            opponent_rank: row.opponent_rank,
            opponent_rating: row.opponent_rating,
            opponent_level: row.opponent_level,
            combat_kind: row.combat_kind,
            result: row.result,
            winner_combatant_id: row.winner_combatant_id,
            loser_combatant_id: row.loser_combatant_id,
            replay_available: row.replay_available ?? 0,
            replay_object_key: row.replay_object_key ?? null,
            replay_uploaded_at_utc: row.replay_uploaded_at_utc ?? null,
          } as T)
        : null;
    }

    if (sql.includes("FROM tokens WHERE token = ?")) {
      const token = String(params[0] ?? "");
      return (this.v3Tokens.get(token) as T | undefined) ?? null;
    }

    return null;
  }

  all<T>(sql: string, params: unknown[]): { results: T[] } {
    if (sql.includes("FROM uid_player_accounts")) {
      const uid = String(params[0] ?? "");
      return {
        results: Array.from(this.uidPlayerAccounts.values())
          .filter((row) => row.uid === uid)
          .sort((left, right) =>
            right.last_seen_at_utc.localeCompare(left.last_seen_at_utc) ||
            left.player_account_id.localeCompare(right.player_account_id),
          )
          .map((row) => ({ player_account_id: row.player_account_id } as T)),
      };
    }

    if (sql.includes("FROM pvp_battles AS pb")) {
      const limit = Number(params[params.length - 1] ?? 0);
      const fromUtc = String(params[params.length - 2] ?? "");
      const opponentAccountIds = new Set(
        params
          .slice(0, Math.max(0, params.length - 2))
          .map((value) => String(value)),
      );

      return {
        results: Array.from(this.pvpBattles.values())
          .filter(
            (row) =>
              opponentAccountIds.has(row.opponent_account_id ?? "") &&
              row.combat_kind === "PVPCombat" &&
              row.recorded_at_utc >= fromUtc,
          )
          .sort((left, right) =>
            right.recorded_at_utc.localeCompare(left.recorded_at_utc) ||
            right.battle_id.localeCompare(left.battle_id),
          )
          .slice(0, limit)
          .map(
            (row) =>
              ({
                battle_id: row.battle_id,
                run_id: row.run_id,
                recorded_at_utc: row.recorded_at_utc,
                day: row.day,
                hour: row.hour,
                encounter_id: row.encounter_id,
                player_name: row.player_name,
                player_account_id: row.player_account_id,
                player_hero: row.player_hero,
                player_rank: row.player_rank,
                player_rating: row.player_rating,
                player_level: row.player_level,
                opponent_name: row.opponent_name,
                opponent_account_id: row.opponent_account_id,
                opponent_hero: row.opponent_hero,
                opponent_rank: row.opponent_rank,
                opponent_rating: row.opponent_rating,
                opponent_level: row.opponent_level,
                combat_kind: row.combat_kind,
                result: row.result,
                winner_combatant_id: row.winner_combatant_id,
                loser_combatant_id: row.loser_combatant_id,
                replay_available: row.replay_available ?? 0,
                replay_object_key: row.replay_object_key ?? null,
                replay_uploaded_at_utc: row.replay_uploaded_at_utc ?? null,
              }) as T,
          ),
      };
    }

    if (sql.includes("FROM battles AS b")) {
      const opponentAccountId = String(params[0] ?? "");
      const fromUtc = String(params[1] ?? "");
      const limit = Number(params[2] ?? Number.MAX_SAFE_INTEGER);
      return {
        results: Array.from(this.v3Battles.values())
          .filter(
            (row) =>
              row.opponent_account_id === opponentAccountId &&
              row.recorded_at_utc >= fromUtc,
          )
          .sort((left, right) =>
            right.recorded_at_utc.localeCompare(left.recorded_at_utc) ||
            right.battle_id.localeCompare(left.battle_id),
          )
          .slice(0, limit)
          .map((row) => row as T),
      };
    }

    if (sql.includes("FROM battles AS legacy_b")) {
      const opponentAccountId = String(params[0] ?? "");
      const fromUtc = String(params[1] ?? "");
      const limit = Number(params[2] ?? Number.MAX_SAFE_INTEGER);
      return {
        results: Array.from(this.battles.values())
          .filter(
            (row) =>
              row.opponent_account_id === opponentAccountId &&
              row.combat_kind === "PVPCombat" &&
              row.recorded_at_utc >= fromUtc,
          )
          .sort((left, right) =>
            right.recorded_at_utc.localeCompare(left.recorded_at_utc) ||
            right.battle_id.localeCompare(left.battle_id),
          )
          .slice(0, limit)
          .map((row) => row as T),
      };
    }

    return { results: [] };
  }

  run(sql: string, params: unknown[]): { changes: number } {
    if (sql.includes("INSERT INTO users")) {
      const row = {
        player_account_id: String(params[0] ?? ""),
        player_username: String(params[1] ?? ""),
        password_hash: String(params[2] ?? ""),
        stream_platform: params[3] == null ? null : String(params[3]),
        stream_channel_id: params[4] == null ? null : String(params[4]),
        stream_url: params[5] == null ? null : String(params[5]),
        created_at_utc: String(params[6] ?? ""),
        updated_at_utc: String(params[7] ?? ""),
        last_login_at_utc: params[8] == null ? null : String(params[8]),
      } satisfies V3UserRow;
      this.v3Users.set(row.player_account_id, row);
      return { changes: 1 };
    }

    if (sql.includes("UPDATE users")) {
      const playerAccountId = String(params[2] ?? "");
      const existing = this.v3Users.get(playerAccountId);
      if (!existing) {
        return { changes: 0 };
      }

      this.v3Users.set(playerAccountId, {
        ...existing,
        last_login_at_utc: params[0] == null ? null : String(params[0]),
        updated_at_utc: params[1] == null ? existing.updated_at_utc : String(params[1]),
      });
      return { changes: 1 };
    }

    if (sql.includes("UPDATE installations")) {
      const installationId = String(params[1] ?? "");
      const existing = this.v3Installations.get(installationId);
      if (!existing) {
        return { changes: 0 };
      }

      this.v3Installations.set(installationId, {
        ...existing,
        last_seen_at_utc: params[0] == null ? null : String(params[0]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO installations")) {
      const row = {
        installation_id: String(params[0] ?? ""),
        player_account_id: String(params[1] ?? ""),
        public_key: String(params[2] ?? ""),
        status: String(params[3] ?? ""),
        created_at_utc: String(params[4] ?? ""),
        last_seen_at_utc: params[5] == null ? null : String(params[5]),
        revoked_at_utc: params[6] == null ? null : String(params[6]),
      } satisfies V3InstallationRow;
      this.v3Installations.set(row.installation_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO installation_sessions")) {
      const row = {
        session_id: String(params[0] ?? ""),
        player_account_id: String(params[1] ?? ""),
        created_at_utc: String(params[2] ?? ""),
        expires_at_utc: String(params[3] ?? ""),
        revoked_at_utc: params[4] == null ? null : String(params[4]),
      } satisfies V3InstallationSessionRow;
      this.v3InstallationSessions.set(row.session_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO installation_observations")) {
      const row = {
        installation_id: String(params[0] ?? ""),
        observed_player_account_id: String(params[1] ?? ""),
        observed_player_username: String(params[2] ?? ""),
        observed_at_utc: String(params[3] ?? ""),
        signature: String(params[4] ?? ""),
        status: String(params[5] ?? ""),
      } satisfies V3InstallationObservationRow;
      this.v3InstallationObservations.set(
        `${row.installation_id}:${row.observed_at_utc}`,
        row,
      );
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO replay_tokens")) {
      const row = {
        token: String(params[0] ?? ""),
        battle_id: String(params[1] ?? ""),
        requested_by_player_account_id: String(params[2] ?? ""),
        expires_at_utc: String(params[3] ?? ""),
        created_at_utc: String(params[4] ?? ""),
        used_at_utc: params[5] == null ? null : String(params[5]),
        revoked_at_utc: params[6] == null ? null : String(params[6]),
      } satisfies V3ReplayTokenRow;
      this.v3ReplayTokens.set(row.token, row);
      return { changes: 1 };
    }

    if (sql.includes("UPDATE replay_tokens")) {
      const token = String(params[1] ?? "");
      const existing = this.v3ReplayTokens.get(token);
      if (!existing) {
        return { changes: 0 };
      }

      this.v3ReplayTokens.set(token, {
        ...existing,
        used_at_utc: params[0] == null ? null : String(params[0]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO run_bundles")) {
      const row = {
        bundle_id: String(params[0] ?? ""),
        installation_id: String(params[1] ?? ""),
        player_account_id: String(params[2] ?? ""),
        run_id: String(params[3] ?? ""),
        payload_hash: String(params[4] ?? ""),
        schema_version: Number(params[5] ?? 0),
        object_key: String(params[6] ?? ""),
        codec: String(params[7] ?? ""),
        size_bytes: Number(params[8] ?? 0),
        submitted_at_utc: String(params[9] ?? ""),
        created_at_utc: String(params[10] ?? ""),
      } satisfies V3RunBundleRow;
      this.v3RunBundles.set(row.bundle_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO runs")) {
      const row = {
        run_id: String(params[0] ?? ""),
        installation_id: String(params[1] ?? ""),
        player_account_id: String(params[2] ?? ""),
        bundle_id: String(params[3] ?? ""),
        status: String(params[4] ?? ""),
        hero_id: params[5] == null ? null : String(params[5]),
        hero_name: params[6] == null ? null : String(params[6]),
        player_rank: params[7] == null ? null : String(params[7]),
        player_rating: params[8] == null ? null : Number(params[8]),
        player_position: params[9] == null ? null : Number(params[9]),
        started_at_utc: params[10] == null ? null : String(params[10]),
        ended_at_utc: String(params[11] ?? ""),
        final_day: params[12] == null ? null : Number(params[12]),
        final_wins: params[13] == null ? null : Number(params[13]),
        final_losses: params[14] == null ? null : Number(params[14]),
        final_player_rank: params[15] == null ? null : String(params[15]),
        final_player_rating: params[16] == null ? null : Number(params[16]),
        final_player_position: params[17] == null ? null : Number(params[17]),
        updated_at_utc: String(params[18] ?? ""),
      } satisfies V3RunRow;
      this.v3Runs.set(row.run_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO battles")) {
      const row = {
        battle_id: String(params[0] ?? ""),
        run_id: String(params[1] ?? ""),
        installation_id: String(params[2] ?? ""),
        player_account_id: String(params[3] ?? ""),
        bundle_id: String(params[4] ?? ""),
        recorded_at_utc: String(params[5] ?? ""),
        day: params[6] == null ? null : Number(params[6]),
        player_name: params[7] == null ? null : String(params[7]),
        player_account_id_in_payload:
          params[8] == null ? null : String(params[8]),
        player_hero: params[9] == null ? null : String(params[9]),
        player_rank: params[10] == null ? null : String(params[10]),
        player_rating: params[11] == null ? null : Number(params[11]),
        player_level: params[12] == null ? null : Number(params[12]),
        opponent_name: params[13] == null ? null : String(params[13]),
        opponent_account_id: params[14] == null ? null : String(params[14]),
        opponent_hero: params[15] == null ? null : String(params[15]),
        opponent_rank: params[16] == null ? null : String(params[16]),
        opponent_rating: params[17] == null ? null : Number(params[17]),
        opponent_level: params[18] == null ? null : Number(params[18]),
        result: params[19] == null ? null : String(params[19]),
        replay_available: Number(params[20] ?? 0),
        updated_at_utc: String(params[21] ?? ""),
      } satisfies V3BattleRow;
      this.v3Battles.set(row.battle_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO clients")) {
      const clientId = String(params[0]);
      if (this.clients.has(clientId) && !sql.includes("ON CONFLICT")) {
        throw new Error("SQLITE_CONSTRAINT: clients.client_id");
      }

      const row = {
        client_id: clientId,
        install_id: String(params[1]),
        modulus_b64: String(params[2]),
        exponent_b64: String(params[3]),
        plugin_version: params[4] == null ? null : String(params[4]),
        registered_at_utc: String(params[5]),
        last_seen_at_utc: params[6] == null ? null : String(params[6]),
        revoked_at_utc: params[7] == null ? null : String(params[7]),
      } satisfies ClientRow;
      this.clients.set(row.client_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO player_links")) {
      const clientId = String(params[0] ?? "");
      this.playerLinks.set(clientId, {
        client_id: clientId,
        player_account_id: String(params[1] ?? ""),
        bound_at_utc: String(params[2] ?? ""),
        last_confirmed_at_utc: String(params[3] ?? ""),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO runs")) {
      const runId = String(params[0] ?? "");
      const existing = this.runs.get(runId);
      this.runs.set(runId, {
        run_id: runId,
        client_id: String(params[1] ?? ""),
        player_account_id: params[2] == null ? null : String(params[2]),
        status: String(params[3] ?? ""),
        hero_id: params[4] == null ? null : String(params[4]),
        hero_name: params[5] == null ? null : String(params[5]),
        started_at_utc: params[6] == null ? null : String(params[6]),
        ended_at_utc: String(params[7] ?? ""),
        final_day: params[8] == null ? null : Number(params[8]),
        final_wins: params[9] == null ? null : Number(params[9]),
        final_losses: params[10] == null ? null : Number(params[10]),
        mmr: params[11] == null ? null : Number(params[11]),
        summary_schema_version: params[12] == null ? null : Number(params[12]),
        summary_object_key: params[13] == null ? null : String(params[13]),
        created_at_utc: existing?.created_at_utc ?? String(params[14] ?? ""),
        updated_at_utc: String(params[15] ?? ""),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO battles")) {
      const battleId = String(params[0] ?? "");
      const existing = this.battles.get(battleId);
      const nextRow = {
        battle_id: battleId,
        run_id: params[1] == null ? null : String(params[1]),
        client_id: String(params[2] ?? ""),
        uploader_player_account_id: params[3] == null ? null : String(params[3]),
        recorded_at_utc: String(params[4] ?? ""),
        day: params[5] == null ? null : Number(params[5]),
        hour: params[6] == null ? null : Number(params[6]),
        player_name: params[7] == null ? null : String(params[7]),
        player_account_id: params[8] == null ? null : String(params[8]),
        player_hero: params[9] == null ? null : String(params[9]),
        player_rank: params[10] == null ? null : String(params[10]),
        player_rating: params[11] == null ? null : Number(params[11]),
        player_level: params[12] == null ? null : Number(params[12]),
        opponent_name: params[13] == null ? null : String(params[13]),
        opponent_account_id: params[14] == null ? null : String(params[14]),
        opponent_hero: params[15] == null ? null : String(params[15]),
        opponent_rank: params[16] == null ? null : String(params[16]),
        opponent_rating: params[17] == null ? null : Number(params[17]),
        opponent_level: params[18] == null ? null : Number(params[18]),
        combat_kind: String(params[19] ?? ""),
        result: params[20] == null ? null : String(params[20]),
        winner_combatant_id: params[21] == null ? null : String(params[21]),
        loser_combatant_id: params[22] == null ? null : String(params[22]),
        replay_schema_version: Number(params[23] ?? 0),
        replay_object_key: String(params[24] ?? ""),
        replay_size_bytes: Number(params[25] ?? 0),
        created_at_utc: existing?.created_at_utc ?? String(params[26] ?? ""),
        updated_at_utc: String(params[27] ?? ""),
      } satisfies BattleRow;

      if (!existing) {
        this.battles.set(battleId, nextRow);
        return { changes: 1 };
      }

      const hasMeaningfulChange =
        existing.run_id !== nextRow.run_id ||
        existing.client_id !== nextRow.client_id ||
        existing.uploader_player_account_id !== nextRow.uploader_player_account_id ||
        existing.recorded_at_utc !== nextRow.recorded_at_utc ||
        existing.day !== nextRow.day ||
        existing.hour !== nextRow.hour ||
        existing.player_name !== nextRow.player_name ||
        existing.player_account_id !== nextRow.player_account_id ||
        existing.player_hero !== nextRow.player_hero ||
        existing.player_rank !== nextRow.player_rank ||
        existing.player_rating !== nextRow.player_rating ||
        existing.player_level !== nextRow.player_level ||
        existing.opponent_name !== nextRow.opponent_name ||
        existing.opponent_account_id !== nextRow.opponent_account_id ||
        existing.opponent_hero !== nextRow.opponent_hero ||
        existing.opponent_rank !== nextRow.opponent_rank ||
        existing.opponent_rating !== nextRow.opponent_rating ||
        existing.opponent_level !== nextRow.opponent_level ||
        existing.combat_kind !== nextRow.combat_kind ||
        existing.result !== nextRow.result ||
        existing.winner_combatant_id !== nextRow.winner_combatant_id ||
        existing.loser_combatant_id !== nextRow.loser_combatant_id ||
        existing.replay_schema_version !== nextRow.replay_schema_version ||
        existing.replay_object_key !== nextRow.replay_object_key ||
        existing.replay_size_bytes !== nextRow.replay_size_bytes;

      if (!hasMeaningfulChange) {
        return { changes: 0 };
      }

      this.battles.set(battleId, nextRow);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO replay_tokens")) {
      const token = String(params[0] ?? "");
      this.replayTokens.set(token, {
        token,
        battle_id: String(params[1] ?? ""),
        requested_by_player_account_id: String(params[2] ?? ""),
        expires_at_utc: String(params[3] ?? ""),
        created_at_utc: String(params[4] ?? ""),
        used_at_utc: params[5] == null ? null : String(params[5]),
        revoked_at_utc: params[6] == null ? null : String(params[6]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO run_uploads")) {
      const runId = String(params[2] ?? "");
      if (this.runUploads.has(runId) && !sql.includes("ON CONFLICT")) {
        throw new Error("SQLITE_CONSTRAINT: run_uploads.run_id");
      }

      this.runUploads.set(runId, {
        client_id: String(params[0]),
        install_id: String(params[1]),
        run_id: String(params[2]),
        payload_sha256: String(params[3]),
        payload_object_key: params[4] == null ? null : String(params[4]),
        payload_bytes: params[5] == null ? null : Number(params[5]),
        schema_version: params[6] == null ? null : Number(params[6]),
        projection_status: String(params[7]),
        projected_at_utc: params[8] == null ? null : String(params[8]),
        last_error_code: params[9] == null ? null : String(params[9]),
        last_error_detail: params[10] == null ? null : String(params[10]),
        created_at_utc: params[11] == null ? undefined : String(params[11]),
        updated_at_utc: params[12] == null ? undefined : String(params[12]),
        uploaded_at_utc: params[11] == null ? undefined : String(params[11]),
        projection_error: params[10] == null ? null : String(params[10]),
      });
      return { changes: 1 };
    }

    if (sql.includes("UPDATE run_uploads")) {
      const runId = String(params[5]);
      const existing = this.runUploads.get(runId);
      if (!existing) {
        return { changes: 0 };
      }

      this.runUploads.set(runId, {
        ...existing,
        projection_status: String(params[0]),
        projected_at_utc: params[1] == null ? null : String(params[1]),
        last_error_code: params[2] == null ? null : String(params[2]),
        last_error_detail: params[3] == null ? null : String(params[3]),
        updated_at_utc: params[4] == null ? existing.updated_at_utc : String(params[4]),
        projection_error: params[3] == null ? null : String(params[3]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO uid_player_accounts")) {
      const key = `${String(params[0])}:${String(params[1])}`;
      const existing = this.uidPlayerAccounts.get(key);
      this.uidPlayerAccounts.set(key, {
        uid: String(params[0]),
        player_account_id: String(params[1]),
        first_seen_at_utc: existing?.first_seen_at_utc ?? String(params[2]),
        last_seen_at_utc: String(params[3]),
        last_client_id: String(params[4]),
      });
      return { changes: 1 };
    }

    if (sql.includes("UPDATE client_player_account_bindings")) {
      const unboundAtUtc = String(params[0] ?? "");
      const clientId = String(params[1] ?? "");
      let changes = 0;
      for (const [bindingId, row] of this.clientPlayerAccountBindings.entries()) {
        if (row.client_id === clientId && row.unbound_at_utc == null) {
          this.clientPlayerAccountBindings.set(bindingId, {
            ...row,
            unbound_at_utc: unboundAtUtc,
          });
          changes += 1;
        }
      }
      return { changes };
    }

    if (sql.includes("INSERT INTO client_player_account_bindings")) {
      const bindingId = String(params[0] ?? "");
      this.clientPlayerAccountBindings.set(bindingId, {
        binding_id: bindingId,
        client_id: String(params[1] ?? ""),
        player_account_id: String(params[2] ?? ""),
        binding_source: String(params[3] ?? ""),
        confidence: Number(params[4] ?? 0),
        bound_at_utc: String(params[5] ?? ""),
        unbound_at_utc: params[6] == null ? null : String(params[6]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO client_player_account_observations")) {
      const key = `${String(params[0] ?? "")}:${String(params[1] ?? "")}`;
      const existing = this.clientPlayerAccountObservations.get(key);
      this.clientPlayerAccountObservations.set(key, {
        client_id: String(params[0] ?? ""),
        player_account_id: String(params[1] ?? ""),
        first_seen_at_utc: existing?.first_seen_at_utc ?? String(params[2] ?? ""),
        last_seen_at_utc: String(params[3] ?? ""),
        evidence_count: existing ? existing.evidence_count + 1 : 1,
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO pvp_battles")) {
      const battleId = String(params[0]);
      const existing = this.pvpBattles.get(battleId);
      this.pvpBattles.set(battleId, {
        battle_id: battleId,
        run_id: params[1] == null ? null : String(params[1]),
        source_client_id: String(params[2]),
        recorded_at_utc: String(params[3]),
        day: params[4] == null ? null : Number(params[4]),
        hour: params[5] == null ? null : Number(params[5]),
        encounter_id: params[6] == null ? null : String(params[6]),
        player_name: params[7] == null ? null : String(params[7]),
        player_account_id: params[8] == null ? null : String(params[8]),
        player_hero: params[9] == null ? null : String(params[9]),
        player_rank: params[10] == null ? null : String(params[10]),
        player_rating: params[11] == null ? null : Number(params[11]),
        player_level: params[12] == null ? null : Number(params[12]),
        opponent_name: params[13] == null ? null : String(params[13]),
        opponent_account_id: params[14] == null ? null : String(params[14]),
        opponent_hero: params[15] == null ? null : String(params[15]),
        opponent_rank: params[16] == null ? null : String(params[16]),
        opponent_rating: params[17] == null ? null : Number(params[17]),
        opponent_level: params[18] == null ? null : Number(params[18]),
        combat_kind: String(params[19]),
        result: params[20] == null ? null : String(params[20]),
        winner_combatant_id: params[21] == null ? null : String(params[21]),
        loser_combatant_id: params[22] == null ? null : String(params[22]),
        replay_available: params[23] == null ? 0 : Number(params[23]),
        replay_object_key: params[24] == null ? null : String(params[24]),
        replay_uploaded_at_utc: params[25] == null ? null : String(params[25]),
        created_at_utc: existing?.created_at_utc ?? String(params[26]),
        updated_at_utc: String(params[27]),
      });
      return { changes: 1 };
    }

    if (sql.includes("DELETE FROM pvp_battles")) {
      const sourceClientId = String(params[0]);
      const runId = String(params[1]);
      let changes = 0;
      for (const [battleId, row] of this.pvpBattles.entries()) {
        if (row.source_client_id === sourceClientId && row.run_id === runId) {
          this.pvpBattles.delete(battleId);
          changes++;
        }
      }
      return { changes };
    }

    if (sql.includes("UPDATE pvp_battles") && sql.includes("SET replay_available = 1")) {
      const battleId = String(params[2] ?? "");
      const existing = this.pvpBattles.get(battleId);
      if (!existing) {
        return { changes: 0 };
      }

      this.pvpBattles.set(battleId, {
        ...existing,
        replay_available: 1,
        replay_object_key: params[0] == null ? null : String(params[0]),
        replay_uploaded_at_utc: params[1] == null ? null : String(params[1]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO tokens")) {
      const row: V3TokenRow = sql.includes(
        "INSERT INTO tokens (token, player_account_id, issued_at_utc)",
      )
        ? {
            token: String(params[0] ?? ""),
            player_account_id: String(params[1] ?? ""),
            player_id: String(params[1] ?? ""),
            installation_id: null,
            issued_at_utc: String(params[2] ?? ""),
            revoked_at_utc: null,
            last_used_at_utc: null,
          }
        : {
            token: String(params[0] ?? ""),
            player_account_id: String(params[1] ?? ""),
            player_id: String(params[1] ?? ""),
            installation_id: params[2] == null ? null : String(params[2]),
            revoked_at_utc: params[3] == null ? null : String(params[3]),
            last_used_at_utc: params[4] == null ? null : String(params[4]),
          };
      this.v3Tokens.set(row.token, row);
      return { changes: 1 };
    }

    if (sql.includes("UPDATE tokens SET revoked_at_utc")) {
      const token = String(params[1] ?? "");
      const existing = this.v3Tokens.get(token);
      if (!existing) {
        return { changes: 0 };
      }

      this.v3Tokens.set(token, {
        ...existing,
        revoked_at_utc: params[0] == null ? null : String(params[0]),
      });
      return { changes: 1 };
    }

    if (sql.includes("UPDATE tokens SET last_used_at_utc")) {
      const token = String(params[1] ?? "");
      const existing = this.v3Tokens.get(token);
      if (!existing) {
        return { changes: 0 };
      }

      this.v3Tokens.set(token, {
        ...existing,
        last_used_at_utc: params[0] == null ? null : String(params[0]),
      });
      return { changes: 1 };
    }

    return { changes: 0 };
  }
}

export class MockR2Bucket {
  public lastPutKey: string | null = null;
  public lastPutOptions:
    | {
        httpMetadata?: { contentType?: string; contentEncoding?: string };
        customMetadata?: Record<string, string>;
        readError?: Error;
      }
    | null = null;
  public readonly objects = new Map<
    string,
    {
      body: Uint8Array;
      httpMetadata?: { contentType?: string; contentEncoding?: string };
      customMetadata?: Record<string, string>;
      readError?: Error;
    }
  >();

  async put(
    key: string,
    value: ArrayBuffer | ArrayBufferView,
    options?: {
      httpMetadata?: { contentType?: string; contentEncoding?: string };
      customMetadata?: Record<string, string>;
      readError?: Error;
    },
  ): Promise<void> {
    const buffer =
      value instanceof ArrayBuffer
        ? new Uint8Array(value)
        : new Uint8Array(
            value.buffer.slice(
              value.byteOffset,
              value.byteOffset + value.byteLength,
            ),
          );
    this.objects.set(key, {
      body: buffer,
      httpMetadata: options?.httpMetadata,
      customMetadata: options?.customMetadata,
      readError: options?.readError,
    });
    this.lastPutKey = key;
    this.lastPutOptions = options ?? null;
  }

  async get(key: string): Promise<{
    httpMetadata?: { contentType?: string; contentEncoding?: string };
    customMetadata?: Record<string, string>;
    arrayBuffer(): Promise<ArrayBuffer>;
  } | null> {
    const object = this.objects.get(key);
    if (!object) {
      return null;
    }

    return {
      httpMetadata: object.httpMetadata,
      customMetadata: object.customMetadata,
      arrayBuffer: async () => {
        if (object.readError) {
          throw object.readError;
        }
        const copy = new Uint8Array(object.body);
        return copy.buffer as ArrayBuffer;
      },
    };
  }
}

type MockKvEntry = {
  value: string;
  expirationTtl?: number;
};

export class MockKVNamespace {
  public readonly entries = new Map<string, MockKvEntry>();

  async get(key: string): Promise<string | null> {
    return this.entries.get(key)?.value ?? null;
  }

  async put(
    key: string,
    value: string,
    options?: KVNamespacePutOptions,
  ): Promise<void> {
    this.entries.set(key, {
      value,
      expirationTtl: options?.expirationTtl,
    });
  }
}

export function buildEnv() {
  return {
    DB: new MockD1Database(),
    RUN_BUNDLE_BUCKET: new MockR2Bucket(),
    KNOWN_PLAYER_ACCOUNTS: new MockKVNamespace(),
    REPLAY_DOWNLOAD_SECRET: "test-replay-download-secret",
    GHOST_QUERY_LOOKBACK_DAYS: "3",
    RUN_BUNDLE_RETENTION_DAYS: "5",
    ALLOW_UNAUTHENTICATED_REPLAY_LINKS: "false",
    ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS: "false",
  };
}
