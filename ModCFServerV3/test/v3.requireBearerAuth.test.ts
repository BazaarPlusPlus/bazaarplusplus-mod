import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { requireBearerAuth } from "../src/features/v3/requireBearerAuth";
import { buildEnv } from "./helpers/mockEnv";

describe("requireBearerAuth", () => {
  it("returns 401 when Authorization header missing", async () => {
    const env = buildEnv();
    const req = new Request("https://example/any");
    const result = await requireBearerAuth(req, env);
    assert.ok(result instanceof Response);
    assert.equal(result.status, 401);
  });

  it("returns 401 for Bearer token not in tokens table", async () => {
    const env = buildEnv();
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer nonexistent_abc" },
    });
    const result = await requireBearerAuth(req, env);
    assert.ok(result instanceof Response);
    assert.equal(result.status, 401);
  });

  it("returns 401 for revoked token", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_id, installation_id, revoked_at_utc, last_used_at_utc) VALUES (?, ?, ?, ?, ?)`,
    )
      .bind(
        "tok_revoked",
        "p1",
        "inst_1",
        "2026-01-02T00:00:00Z",
        null,
      )
      .run();
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer tok_revoked" },
    });
    const result = await requireBearerAuth(req, env);
    assert.ok(result instanceof Response);
    assert.equal(result.status, 401);
  });

  it("returns auth object for active token", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_id, installation_id, revoked_at_utc, last_used_at_utc) VALUES (?, ?, ?, ?, ?)`,
    )
      .bind(
        "tok_active",
        "p1",
        "inst_1",
        null,
        null,
      )
      .run();
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer tok_active" },
    });
    const result = await requireBearerAuth(req, env);
    assert.deepEqual(result, {
      token: "tok_active",
      playerId: "p1",
      installationId: "inst_1",
    });
  });
});
