import { generateKeyPairSync, createHash, createSign } from "node:crypto";
import test from "node:test";
import assert from "node:assert/strict";

import worker from "../src/index";

type ClientRow = {
  client_id: string;
  install_id: string;
  purpose: string;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
  registered_at_utc: string;
};

type ReplayUploadRow = {
  client_id: string;
  install_id: string;
  battle_id: string;
  run_id: string | null;
  payload_sha256: string;
  object_key: string;
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

class MockD1Database {
  public readonly clients = new Map<string, ClientRow>();
  public readonly replayUploads = new Map<string, ReplayUploadRow>();
  public readonly runUploads = new Map<string, { client_id: string; install_id: string; run_id: string | null; payload_sha256: string; uploaded_at_utc: string }>();
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
      return;
    }
  }
}

class MockR2Bucket {
  public readonly objects = new Map<string, Uint8Array>();

  async put(key: string, value: ArrayBuffer | ArrayBufferView): Promise<void> {
    const buffer =
      value instanceof ArrayBuffer
        ? new Uint8Array(value)
        : new Uint8Array(value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength));
    this.objects.set(key, buffer);
  }
}

function buildEnv() {
  return {
    DB: new MockD1Database(),
    REPLAY_BUCKET: new MockR2Bucket(),
  };
}

function toBase64(input: string | Uint8Array): string {
  return Buffer.from(input).toString("base64");
}

function canonicalRequest(
  method: string,
  path: string,
  clientId: string,
  installId: string,
  timestamp: string,
  nonce: string,
  bodyHash: string,
): string {
  return [method.toUpperCase(), path, clientId, installId, timestamp, nonce, bodyHash].join("\n");
}

test("registers replay clients and persists verified replay uploads", async () => {
  const env = buildEnv();
  const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const jwk = publicKey.export({ format: "jwk" });
  assert.equal(typeof jwk.n, "string");
  assert.equal(typeof jwk.e, "string");

  const registerResponse = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        install_id: "install-001",
        plugin_version: "1.9.0",
        purpose: "replays",
        public_key: {
          modulus_b64: Buffer.from(jwk.n!, "base64url").toString("base64"),
          exponent_b64: Buffer.from(jwk.e!, "base64url").toString("base64"),
        },
      }),
    }),
    env as never,
  );

  assert.equal(registerResponse.status, 200);
  const registerJson = (await registerResponse.json()) as { client_id: string };
  assert.ok(registerJson.client_id);
  assert.equal(env.DB.clients.size, 1);

  const payload = JSON.stringify({
    battle_id: "battle-001",
    client_id: registerJson.client_id,
    replay_payload: { battle_id: "battle-001" },
  });
  const bodyHash = toBase64(createHash("sha256").update(payload).digest());
  const timestamp = new Date().toISOString();
  const nonce = "nonce-001";
  const canonical = canonicalRequest(
    "POST",
    "/replays/upload",
    registerJson.client_id,
    "install-001",
    timestamp,
    nonce,
    bodyHash,
  );
  const signer = createSign("RSA-SHA256");
  signer.update(canonical);
  signer.end();
  const signature = signer.sign(privateKey).toString("base64");

  const uploadResponse = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": registerJson.client_id,
        "x-bpp-install-id": "install-001",
        "x-bpp-battle-id": "battle-001",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(uploadResponse.status, 200);
  const uploadJson = (await uploadResponse.json()) as { object_key: string };
  assert.match(uploadJson.object_key, /^combat-replays\/replays\//);
  assert.equal(env.DB.replayUploads.get("battle-001")?.client_id, registerJson.client_id);
  assert.ok(env.REPLAY_BUCKET.objects.has(uploadJson.object_key));
});

test("rejects replay uploads with an invalid body hash", async () => {
  const env = buildEnv();
  const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const jwk = publicKey.export({ format: "jwk" });
  assert.equal(typeof jwk.n, "string");
  assert.equal(typeof jwk.e, "string");

  const clientId = "client-bad-hash";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-002",
    purpose: "replays",
    modulus_b64: Buffer.from(jwk.n!, "base64url").toString("base64"),
    exponent_b64: Buffer.from(jwk.e!, "base64url").toString("base64"),
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ battle_id: "battle-bad-hash" });
  const timestamp = new Date().toISOString();
  const nonce = "nonce-bad-hash";
  const wrongHash = toBase64("wrong-hash");
  const canonical = canonicalRequest(
    "POST",
    "/replays/upload",
    clientId,
    "install-002",
    timestamp,
    nonce,
    wrongHash,
  );
  const signer = createSign("RSA-SHA256");
  signer.update(canonical);
  signer.end();
  const signature = signer.sign(privateKey).toString("base64");

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-002",
        "x-bpp-battle-id": "battle-bad-hash",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
        "x-bpp-content-sha256": wrongHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.equal(env.DB.replayUploads.size, 0);
  assert.equal(env.REPLAY_BUCKET.objects.size, 0);
});

test("re-registering the same replay client returns the existing registration", async () => {
  const env = buildEnv();
  const { publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const jwk = publicKey.export({ format: "jwk" });
  assert.equal(typeof jwk.n, "string");
  assert.equal(typeof jwk.e, "string");

  const body = JSON.stringify({
    install_id: "install-repeat",
    plugin_version: "1.9.0",
    purpose: "replays",
    public_key: {
      modulus_b64: Buffer.from(jwk.n!, "base64url").toString("base64"),
      exponent_b64: Buffer.from(jwk.e!, "base64url").toString("base64"),
    },
  });

  const first = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body,
    }),
    env as never,
  );
  assert.equal(first.status, 200);
  const firstJson = (await first.json()) as { client_id: string };

  const second = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body,
    }),
    env as never,
  );
  assert.equal(second.status, 200);
  const secondJson = (await second.json()) as { client_id: string };
  assert.equal(secondJson.client_id, firstJson.client_id);
  assert.equal(env.DB.clients.size, 1);
});

test("re-uploading the same replay payload remains idempotent", async () => {
  const env = buildEnv();
  const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const jwk = publicKey.export({ format: "jwk" });
  assert.equal(typeof jwk.n, "string");
  assert.equal(typeof jwk.e, "string");

  const clientId = "replays-client-repeat";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-repeat-upload",
    purpose: "replays",
    modulus_b64: Buffer.from(jwk.n!, "base64url").toString("base64"),
    exponent_b64: Buffer.from(jwk.e!, "base64url").toString("base64"),
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-repeat",
    replay_payload: { battle_id: "battle-repeat" },
  });
  const bodyHash = toBase64(createHash("sha256").update(payload).digest());

  for (const nonce of ["nonce-repeat-1", "nonce-repeat-2"]) {
    const timestamp = new Date().toISOString();
    const canonical = canonicalRequest(
      "POST",
      "/replays/upload",
      clientId,
      "install-repeat-upload",
      timestamp,
      nonce,
      bodyHash,
    );
    const signer = createSign("RSA-SHA256");
    signer.update(canonical);
    signer.end();
    const signature = signer.sign(privateKey).toString("base64");

    const response = await worker.fetch(
      new Request("https://example.com/replays/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": clientId,
          "x-bpp-install-id": "install-repeat-upload",
          "x-bpp-battle-id": "battle-repeat",
          "x-bpp-plugin-version": "1.9.0",
          "x-bpp-timestamp": timestamp,
          "x-bpp-nonce": nonce,
          "x-bpp-content-sha256": bodyHash,
          "x-bpp-signature-alg": "rsa-pkcs1-sha256",
          "x-bpp-signature": signature,
        },
        body: payload,
      }),
      env as never,
    );

    assert.equal(response.status, 200);
  }

  assert.equal(env.DB.replayUploads.size, 1);
  assert.equal(env.REPLAY_BUCKET.objects.size, 1);
});
