import { beforeEach, describe, expect, it } from "vitest";
import { env } from "cloudflare:test";
import { requireBearerAuth } from "../src/features/v3/requireBearerAuth";
import { insertToken, insertV3User, resetTestState } from "./helpers/seed";

describe("requireBearerAuth", () => {
  beforeEach(async () => {
    await resetTestState(env);
  });

  it("returns 401 when Authorization header missing", async () => {
    const req = new Request("https://example/any");
    const result = await requireBearerAuth(req, env as never);
    expect(result).toBeInstanceOf(Response);
    expect((result as Response).status).toBe(401);
  });

  it("returns 401 for Bearer token not in tokens table", async () => {
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer nonexistent_abc" },
    });
    const result = await requireBearerAuth(req, env as never);
    expect(result).toBeInstanceOf(Response);
    expect((result as Response).status).toBe(401);
  });

  it("returns 401 for revoked token", async () => {
    await insertV3User(env.DB, {
      playerAccountId: "p1",
      playerUsername: "u1",
      passwordHash: "hash",
      createdAtUtc: "2026-01-01T00:00:00Z",
      updatedAtUtc: "2026-01-01T00:00:00Z",
    });
    await insertToken(env.DB, {
      token: "tok_revoked",
      playerAccountId: "p1",
      issuedAtUtc: "2026-01-01T00:00:00Z",
      revokedAtUtc: "2026-01-02T00:00:00Z",
    });
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer tok_revoked" },
    });
    const result = await requireBearerAuth(req, env as never);
    expect(result).toBeInstanceOf(Response);
    expect((result as Response).status).toBe(401);
  });

  it("returns auth object for active token", async () => {
    await insertV3User(env.DB, {
      playerAccountId: "p1",
      playerUsername: "u1",
      passwordHash: "hash",
      createdAtUtc: "2026-01-01T00:00:00Z",
      updatedAtUtc: "2026-01-01T00:00:00Z",
    });
    await insertToken(env.DB, {
      token: "tok_active",
      playerAccountId: "p1",
      issuedAtUtc: "2026-01-01T00:00:00Z",
    });
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer tok_active" },
    });
    const result = await requireBearerAuth(req, env as never);
    expect(result).toEqual({ token: "tok_active", playerAccountId: "p1" });
  });
});
