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
  run_id: string | null;
  payload_sha256: string;
  uploaded_at_utc: string;
};

class MockD1Statement {
  constructor(
    private readonly db: MockD1Database,
    private readonly sql: string,
    private readonly params: unknown[] = [],
  ) {}

  bind(...params: unknown[]): MockD1Statement {
    return new MockD1Statement(this.db, this.sql, params);
  }

  async first<T>(): Promise<T | null> {
    return this.db.first<T>(this.sql, this.params);
  }

  async run(): Promise<{ success: boolean }> {
    this.db.run(this.sql, this.params);
    return { success: true };
  }
}

export class MockD1Database {
  public readonly clients = new Map<string, ClientRow>();
  public readonly replayUploads = new Map<string, ReplayUploadRow>();
  public readonly runUploads = new Map<string, RunUploadRow>();
  public readonly nonces = new Set<string>();

  prepare(sql: string): MockD1Statement {
    return new MockD1Statement(this, sql);
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

    return null;
  }

  run(sql: string, params: unknown[]): void {
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
      return;
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
      return;
    }

    if (sql.includes("INSERT INTO run_uploads")) {
      const runId = String(params[2] ?? "");
      if (this.runUploads.has(runId) && !sql.includes("ON CONFLICT")) {
        throw new Error("SQLITE_CONSTRAINT: run_uploads.run_id");
      }

      this.runUploads.set(runId, {
        client_id: String(params[0]),
        install_id: String(params[1]),
        run_id: params[2] == null ? null : String(params[2]),
        payload_sha256: String(params[3]),
        uploaded_at_utc: String(params[4]),
      });
      return;
    }

    if (sql.includes("INSERT INTO request_nonces")) {
      this.nonces.add(String(params[0]));
    }
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
}

export function buildEnv() {
  return {
    DB: new MockD1Database(),
    REPLAY_BUCKET: new MockR2Bucket(),
  };
}
