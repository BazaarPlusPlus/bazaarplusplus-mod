// ModCFServerV3/test/token.generate.test.ts
import { describe, expect, it } from "vitest";
import { generateBearerToken } from "../src/token/generate";

describe("generateBearerToken", () => {
  it("produces base64url string of length 43 (32 bytes)", () => {
    const token = generateBearerToken();
    expect(token).toMatch(/^[A-Za-z0-9_-]{43}$/);
  });

  it("produces distinct tokens on repeated calls", () => {
    const a = generateBearerToken();
    const b = generateBearerToken();
    expect(a).not.toBe(b);
  });
});
