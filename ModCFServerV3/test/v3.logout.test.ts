import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { buildEnv } from "./helpers/mockEnv";
import { handleLogout } from "../src/features/v3/logout";

describe("POST /logout", () => {
  it("returns 401 when bearer missing", async () => {
    const env = buildEnv();
    const req = new Request("https://example/logout", { method: "POST" });
    const resp = await handleLogout(req, env);
    assert.equal(resp.status, 401);
  });

  it("revokes the bearer token and returns 204", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`
    ).bind("tok_x", "p1", "2026-01-01T00:00:00Z").run();

    const req = new Request("https://example/logout", {
      method: "POST",
      headers: { Authorization: "Bearer tok_x" }
    });
    const resp = await handleLogout(req, env);
    assert.equal(resp.status, 204);

    const row = await env.DB.prepare(
      "SELECT revoked_at_utc FROM tokens WHERE token = ?"
    ).bind("tok_x").first<{ revoked_at_utc: string | null }>();
    assert.ok(row);
    assert.ok(row!.revoked_at_utc);
  });

  it("is idempotent — revoking an already-revoked token still 204s", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc, revoked_at_utc) VALUES (?, ?, ?, ?)`
    ).bind("tok_y", "p1", "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z").run();

    const req = new Request("https://example/logout", {
      method: "POST",
      headers: { Authorization: "Bearer tok_y" }
    });
    const resp = await handleLogout(req, env);
    // Revoked token → requireBearerAuth returns 401 first.
    assert.equal(resp.status, 401);
  });
});
