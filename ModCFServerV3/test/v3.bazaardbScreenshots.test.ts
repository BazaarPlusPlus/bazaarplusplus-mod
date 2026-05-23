import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { countRows, selectFirst } from "./helpers/seed";

// PNG magic bytes followed by a tiny payload.
const PNG_BYTES = new Uint8Array([
  0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
]);

const PNG_BASE64 = Buffer.from(PNG_BYTES).toString("base64");

function buildIngestRequest(body: unknown): Request {
  return new Request("https://example.com/bazaardb-screenshots", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
}

async function listScreenshotKeys(): Promise<string[]> {
  const listing = await env.BAZAARDB_BUCKET.list();
  return listing.objects.map((object) => object.key).sort();
}

async function resetBazaarDbState(): Promise<void> {
  await env.DB.prepare("DELETE FROM bazaardb_screenshots").run();
  let cursor: string | undefined;
  do {
    const page = await env.BAZAARDB_BUCKET.list({ cursor });
    if (page.objects.length > 0) {
      await env.BAZAARDB_BUCKET.delete(page.objects.map((o) => o.key));
    }
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor != null);
}

beforeEach(async () => {
  await resetBazaarDbState();
});

test("ingest stores R2 object and D1 row", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-1",
    run_id: "run-77",
    hero_name: "Mak",
    final_days: 14,
    final_victories: 10,
    player_name: "Xinyu",
    player_rank: "Diamond",
    player_rating: 1942,
    player_position: 1,
    captured_at_utc: "2026-05-23T20:30:05+00:00",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(200);
  const body = (await response.json()) as { status: string; screenshot_id: string };
  expect(body.status).toBe("ok");
  expect(body.screenshot_id).toBe("snap-1");

  expect(await listScreenshotKeys()).toEqual([
    "bazaardb/screenshots/2026-05-23/snap-1.png",
  ]);
  expect(await countRows(env.DB, "bazaardb_screenshots")).toBe(1);

  const row = await selectFirst<{
    screenshot_id: string;
    captured_date_utc: string;
    r2_key: string;
    image_bytes: number;
  }>(
    env.DB,
    "SELECT screenshot_id, captured_date_utc, r2_key, image_bytes FROM bazaardb_screenshots WHERE screenshot_id = ?",
    "snap-1",
  );
  expect(row?.captured_date_utc).toBe("2026-05-23");
  expect(row?.r2_key).toBe("bazaardb/screenshots/2026-05-23/snap-1.png");
  expect(row?.image_bytes).toBe(PNG_BYTES.length);
});

test("ingest accepts idempotent re-POST of the same screenshot id", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-dup",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const first = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(first.status).toBe(200);
  const second = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(second.status).toBe(200);

  expect(await countRows(env.DB, "bazaardb_screenshots")).toBe(1);
  expect((await listScreenshotKeys()).length).toBe(1);
});

test("ingest rejects unsupported schema_version with 400", async () => {
  const payload = {
    schema_version: 99,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-bad-version",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});

test("ingest rejects when image bytes are not a PNG", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-not-png",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: Buffer.from(new Uint8Array([0, 1, 2, 3])).toString("base64"),
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});

test("ingest rejects future captured_at_utc with 400", async () => {
  const futureIso = new Date(Date.now() + 86_400_000 * 365).toISOString();
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-future",
    captured_at_utc: futureIso,
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});

test("ingest requires player_account_id", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    screenshot_id: "snap-no-account",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});
