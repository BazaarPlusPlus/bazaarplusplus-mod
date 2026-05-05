import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { resetTestState } from "./helpers/seed";

const EXPECTED_ALLOWED_HEADERS =
  "content-type, x-bpp-timestamp, x-bpp-content-sha256, x-bpp-signature";

beforeEach(async () => {
  await resetTestState(env);
});

test("responds to the health endpoint", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/health", {
      method: "GET",
      headers: { origin: "https://frontend.example.com" },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.json()).toEqual({ ok: true });
  expect(response.headers.get("access-control-allow-origin")).toBe(
    "https://frontend.example.com",
  );
  expect(response.headers.get("access-control-allow-headers")).toBe(
    EXPECTED_ALLOWED_HEADERS,
  );
});

test("returns not_found for unsupported routes", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/nope", { method: "POST" }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "not_found" });
});

test("activate route is gone", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: "{}",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "not_found" });
});

test("login route is gone", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: "{}",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
});

test("responds to CORS preflight for browser requests", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "OPTIONS",
      headers: {
        origin: "https://frontend.example.com",
        "access-control-request-method": "POST",
        "access-control-request-headers": "content-type",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(204);
  expect(response.headers.get("access-control-allow-origin")).toBe(
    "https://frontend.example.com",
  );
  expect(response.headers.get("access-control-allow-methods")).toBe(
    "GET, POST, OPTIONS",
  );
  expect(response.headers.get("access-control-allow-headers")).toBe(
    EXPECTED_ALLOWED_HEADERS,
  );
});
