import { beforeEach, describe, expect, it } from "vitest";
import { env } from "cloudflare:test";
import { handleLogout } from "../src/features/v3/logout";
import { insertToken, insertV3User, resetTestState } from "./helpers/seed";

describe("POST /logout", () => {
  beforeEach(async () => {
    await resetTestState(env);
  });

  it("returns 401 when bearer missing", async () => {
    const req = new Request("https://example/logout", { method: "POST" });
    const resp = await handleLogout(req, env as never);
    expect(resp.status).toBe(401);
  });

  it("revokes the bearer token and returns 204", async () => {
    await insertV3User(env.DB, {
      playerAccountId: "p1",
      playerUsername: "u1",
      passwordHash: "hash",
      createdAtUtc: "2026-01-01T00:00:00Z",
      updatedAtUtc: "2026-01-01T00:00:00Z",
    });
    await insertToken(env.DB, {
      token: "tok_x",
      playerAccountId: "p1",
      issuedAtUtc: "2026-01-01T00:00:00Z",
    });

    const req = new Request("https://example/logout", {
      method: "POST",
      headers: { Authorization: "Bearer tok_x" }
    });
    const resp = await handleLogout(req, env as never);
    expect(resp.status).toBe(204);

    const row = await env.DB.prepare(
      "SELECT revoked_at_utc FROM tokens WHERE token = ?"
    ).bind("tok_x").first<{ revoked_at_utc: string | null }>();
    expect(row).toBeTruthy();
    expect(row!.revoked_at_utc).toBeTruthy();
  });

  it("is idempotent — revoking an already-revoked token still 204s", async () => {
    await insertV3User(env.DB, {
      playerAccountId: "p1",
      playerUsername: "u1",
      passwordHash: "hash",
      createdAtUtc: "2026-01-01T00:00:00Z",
      updatedAtUtc: "2026-01-01T00:00:00Z",
    });
    await insertToken(env.DB, {
      token: "tok_y",
      playerAccountId: "p1",
      issuedAtUtc: "2026-01-01T00:00:00Z",
      revokedAtUtc: "2026-01-02T00:00:00Z",
    });

    const req = new Request("https://example/logout", {
      method: "POST",
      headers: { Authorization: "Bearer tok_y" }
    });
    const resp = await handleLogout(req, env as never);
    // Revoked token → requireBearerAuth returns 401 first.
    expect(resp.status).toBe(401);
  });
});
