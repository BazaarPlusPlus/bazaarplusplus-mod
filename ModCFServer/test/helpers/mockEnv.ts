export type ClientRow = {
  client_id: string;
  install_id: string;
  purpose: string;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
  registered_at_utc: string;
};

export type ReplayUploadRow = {
  client_id: string;
  install_id: string;
  battle_id: string;
  run_id: string | null;
  payload_sha256: string;
  object_key: string;
  uploaded_at_utc: string;
};

export type RunUploadRow = {
  client_id: string;
  install_id: string;
  run_id: string;
  payload_sha256: string;
  uploaded_at_utc: string;
  projection_status: string;
  projected_at_utc: string | null;
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
  payload_json: string;
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
  public readonly clients = new Map<string, ClientRow>();
  public readonly replayUploads = new Map<string, ReplayUploadRow>();
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
  public readonly nonces = new Set<string>();

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
    if (sql.includes("FROM registered_clients")) {
      const clientId = String(params[0] ?? "");
      return (this.clients.get(clientId) as T | undefined) ?? null;
    }

    if (sql.includes("FROM request_nonces")) {
      const nonceKey = String(params[0] ?? "");
      return this.nonces.has(nonceKey)
        ? ({ nonce_key: nonceKey } as T)
        : null;
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

    if (sql.includes("FROM replay_uploads")) {
      const battleId = String(params[0] ?? "");
      const row = this.replayUploads.get(battleId);
      return row
        ? ({
            battle_id: row.battle_id,
            object_key: row.object_key,
            uploaded_at_utc: row.uploaded_at_utc,
          } as T)
        : null;
    }

    if (sql.includes("WHERE pb.battle_id = ?")) {
      const battleId = String(params[0] ?? "");
      const row = this.pvpBattles.get(battleId);
      return row
        ? ({
            battle_id: row.battle_id,
            recorded_at_utc: row.recorded_at_utc,
            opponent_account_id: row.opponent_account_id,
            payload_json: row.payload_json,
            replay_available: this.replayUploads.has(row.battle_id) ? 1 : 0,
          } as T)
        : null;
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
                recorded_at_utc: row.recorded_at_utc,
                opponent_account_id: row.opponent_account_id,
                payload_json: row.payload_json,
                replay_available: this.replayUploads.has(row.battle_id) ? 1 : 0,
              }) as T,
          ),
      };
    }

    return { results: [] };
  }

  run(sql: string, params: unknown[]): { changes: number } {
    if (sql.includes("INSERT INTO registered_clients")) {
      const clientId = String(params[0]);
      if (this.clients.has(clientId) && !sql.includes("ON CONFLICT")) {
        throw new Error("SQLITE_CONSTRAINT: registered_clients.client_id");
      }

      const row = {
        client_id: clientId,
        install_id: String(params[1]),
        purpose: String(params[2]),
        modulus_b64: String(params[3]),
        exponent_b64: String(params[4]),
        plugin_version: params[5] == null ? null : String(params[5]),
        registered_at_utc: String(params[6]),
      } satisfies ClientRow;
      this.clients.set(row.client_id, row);
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO replay_uploads")) {
      const battleId = String(params[2]);
      if (this.replayUploads.has(battleId) && !sql.includes("ON CONFLICT")) {
        throw new Error("SQLITE_CONSTRAINT: replay_uploads.battle_id");
      }

      const row = {
        client_id: String(params[0]),
        install_id: String(params[1]),
        battle_id: battleId,
        run_id: params[3] == null ? null : String(params[3]),
        payload_sha256: String(params[4]),
        object_key: String(params[5]),
        uploaded_at_utc: String(params[6]),
      } satisfies ReplayUploadRow;
      this.replayUploads.set(row.battle_id, row);
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
        uploaded_at_utc: String(params[4]),
        projection_status: String(params[5]),
        projected_at_utc: params[6] == null ? null : String(params[6]),
        projection_error: params[7] == null ? null : String(params[7]),
      });
      return { changes: 1 };
    }

    if (sql.includes("UPDATE run_uploads")) {
      const runId = String(params[3]);
      const existing = this.runUploads.get(runId);
      if (!existing) {
        return { changes: 0 };
      }

      this.runUploads.set(runId, {
        ...existing,
        projection_status: String(params[0]),
        projected_at_utc: params[1] == null ? null : String(params[1]),
        projection_error: params[2] == null ? null : String(params[2]),
      });
      return { changes: 1 };
    }

    if (sql.includes("INSERT INTO request_nonces")) {
      const nonceKey = String(params[0]);
      if (this.nonces.has(nonceKey)) {
        return { changes: 0 };
      }

      this.nonces.add(nonceKey);
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
        payload_json: String(params[23]),
        created_at_utc: existing?.created_at_utc ?? String(params[24]),
        updated_at_utc: String(params[25]),
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

    return { changes: 0 };
  }
}

export class MockR2Bucket {
  public readonly objects = new Map<string, Uint8Array>();

  async put(
    key: string,
    value: ArrayBuffer | ArrayBufferView,
    _options?: unknown,
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
    this.objects.set(key, buffer);
  }

  async get(key: string): Promise<{ arrayBuffer(): Promise<ArrayBuffer> } | null> {
    const value = this.objects.get(key);
    if (!value) {
      return null;
    }

    return {
      arrayBuffer: async () => {
        const copy = new Uint8Array(value);
        return copy.buffer as ArrayBuffer;
      },
    };
  }
}

export function buildEnv() {
  return {
    DB: new MockD1Database(),
    REPLAY_BUCKET: new MockR2Bucket(),
    REPLAY_DOWNLOAD_SECRET: "test-replay-download-secret",
  };
}
