import { describe, expect, it } from "vitest";

import {
  absolutePath,
  optionalFiniteNumber,
  optionalTrimmedString,
  parseClampedInteger,
  trimString,
} from "../src/http/request";

describe("request helpers", () => {
  it("normalizes required and optional string fields", () => {
    expect(trimString("  player-one  ")).toBe("player-one");
    expect(trimString(42)).toBe("");

    expect(optionalTrimmedString("  run-001  ")).toBe("run-001");
    expect(optionalTrimmedString("   ")).toBeNull();
    expect(optionalTrimmedString(null)).toBeNull();
  });

  it("normalizes optional finite numeric fields", () => {
    expect(optionalFiniteNumber(0)).toBe(0);
    expect(optionalFiniteNumber(12.5)).toBe(12.5);
    expect(optionalFiniteNumber(Number.POSITIVE_INFINITY)).toBeNull();
    expect(optionalFiniteNumber("12")).toBeNull();
  });

  it("parses query limits with legacy parseInt semantics and clamps bounds", () => {
    expect(parseClampedInteger(null, 200, 1, 200)).toBe(200);
    expect(parseClampedInteger("0", 200, 1, 200)).toBe(1);
    expect(parseClampedInteger("201", 200, 1, 200)).toBe(200);
    expect(parseClampedInteger("12items", 200, 1, 200)).toBe(12);
    expect(parseClampedInteger("nope", 200, 1, 200)).toBe(200);
  });

  it("returns the absolute request path", () => {
    expect(absolutePath(new Request("https://example.com/ghost-battles?limit=1"))).toBe(
      "/ghost-battles",
    );
  });
});
