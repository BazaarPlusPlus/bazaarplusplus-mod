// ModCFServerV3/test/token.generate.test.ts
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { generateBearerToken } from "../src/token/generate";

describe("generateBearerToken", () => {
  it("produces base64url string of length 43 (32 bytes)", () => {
    const token = generateBearerToken();
    assert.match(token, /^[A-Za-z0-9_-]{43}$/);
  });

  it("produces distinct tokens on repeated calls", () => {
    const a = generateBearerToken();
    const b = generateBearerToken();
    assert.notEqual(a, b);
  });
});
