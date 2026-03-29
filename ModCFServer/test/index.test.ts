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
