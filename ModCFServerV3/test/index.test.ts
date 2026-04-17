import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { resetTestState } from "./helpers/seed";

beforeEach(async () => {
  await resetTestState(env);
});

test("responds to the health endpoint", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/health", {
      method: "GET",
      headers: {
        origin: "https://frontend.example.com",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.json()).toEqual({ ok: true });
  expect(
    response.headers.get("access-control-allow-origin"),
  ).toBe("https://frontend.example.com");
  expect(
    response.headers.get("access-control-allow-headers"),
  ).toBe(
    "authorization, content-type, x-bpp-installation-id, x-bpp-timestamp, x-bpp-content-sha256, x-bpp-signature",
  );
});

test("activate route is wired and validates request payload", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      body: JSON.stringify({}),
      headers: {
        "content-type": "application/json",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(400);
  expect(await response.json()).toEqual({ error: "invalid_request" });
});

test("returns not_found for unsupported routes", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/nope", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "not_found" });
});

test("responds to CORS preflight for browser requests", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/installations", {
      method: "OPTIONS",
      headers: {
        origin: "https://frontend.example.com",
        "access-control-request-method": "POST",
        "access-control-request-headers": "authorization, content-type",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(204);
  expect(
    response.headers.get("access-control-allow-origin"),
  ).toBe("https://frontend.example.com");
  expect(
    response.headers.get("access-control-allow-methods"),
  ).toBe("GET, POST, OPTIONS");
  expect(
    response.headers.get("access-control-allow-headers"),
  ).toBe(
    "authorization, content-type, x-bpp-installation-id, x-bpp-timestamp, x-bpp-content-sha256, x-bpp-signature",
  );
});
