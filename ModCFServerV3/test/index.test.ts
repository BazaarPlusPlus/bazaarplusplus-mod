import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("responds to the health endpoint", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/health", {
      method: "GET",
    }),
    buildEnv() as never,
  );

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true });
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
    buildEnv() as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "invalid_activate_request" });
});

test("returns not_found for unsupported routes", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/nope", {
      method: "POST",
    }),
    buildEnv() as never,
  );

  assert.equal(response.status, 404);
  assert.deepEqual(await response.json(), { error: "not_found" });
});
