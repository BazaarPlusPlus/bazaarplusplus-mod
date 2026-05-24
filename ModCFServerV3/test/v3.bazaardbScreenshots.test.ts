import { beforeEach, expect, test, vi } from "vitest";
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

test("ingest deletes R2 object when D1 upsert fails", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-d1-fail",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const prepareSpy = vi
    .spyOn(env.DB, "prepare")
    .mockImplementationOnce(() => {
      throw new Error("simulated d1 outage");
    });

  try {
    const response = await worker.fetch(buildIngestRequest(payload), env as never);
    expect(response.status).toBe(500);

    expect(await listScreenshotKeys()).toEqual([]);
    expect(await countRows(env.DB, "bazaardb_screenshots")).toBe(0);
  } finally {
    prepareSpy.mockRestore();
  }
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

test("ingest rejects oversized images with 400 image_too_large", async () => {
  const oversized = Buffer.concat([
    Buffer.from(PNG_BYTES),
    Buffer.alloc(3 * 1024 * 1024),
  ]).toString("base64");
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-too-large",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: oversized,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
  const body = (await response.json()) as { status: string; reason: string };
  expect(body.status).toBe("rejected");
  expect(body.reason).toBe("image_too_large");
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

function buildManifestRequest(date: string, authorization?: string): Request {
  const headers = new Headers();
  if (authorization != null) {
    headers.set("Authorization", authorization);
  }
  return new Request(`https://example.com/bazaardb/manifest?date=${date}`, {
    method: "GET",
    headers,
  });
}

async function ingestOne(
  screenshotId: string,
  capturedAtUtc: string,
): Promise<void> {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: screenshotId,
    captured_at_utc: capturedAtUtc,
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };
  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(200);
}

test("manifest returns 401 with no token", async () => {
  const response = await worker.fetch(
    buildManifestRequest("2026-05-23"),
    env as never,
  );
  expect(response.status).toBe(401);
});

test("manifest returns 401 with wrong token", async () => {
  const response = await worker.fetch(
    buildManifestRequest("2026-05-23", "Bearer wrong"),
    env as never,
  );
  expect(response.status).toBe(401);
});

test("manifest returns 400 for invalid date", async () => {
  const response = await worker.fetch(
    buildManifestRequest("not-a-date", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(400);
});

test("manifest returns 200 with empty items when no screenshots for the date", async () => {
  const response = await worker.fetch(
    buildManifestRequest("2026-05-23", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(200);
  const body = (await response.json()) as { items: unknown[]; date: string };
  expect(body.date).toBe("2026-05-23");
  expect(body.items).toEqual([]);
});

test("manifest returns ascending-by-uploaded items for the requested date", async () => {
  await ingestOne("snap-day-a-1", "2026-05-23T01:00:00Z");
  await ingestOne("snap-day-a-2", "2026-05-23T02:00:00Z");
  await ingestOne("snap-day-b-1", "2026-05-24T01:00:00Z");

  const response = await worker.fetch(
    buildManifestRequest("2026-05-23", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(200);
  const body = (await response.json()) as {
    items: { screenshot_id: string; image_url: string }[];
  };
  expect(body.items.length).toBe(2);
  expect(body.items.map((i) => i.screenshot_id)).toEqual([
    "snap-day-a-1",
    "snap-day-a-2",
  ]);
  expect(body.items[0]?.image_url).toMatch(/\/bazaardb\/image\/snap-day-a-1$/);
});

function buildImageRequest(id: string, authorization?: string): Request {
  const headers = new Headers();
  if (authorization != null) {
    headers.set("Authorization", authorization);
  }
  return new Request(`https://example.com/bazaardb/image/${id}`, {
    method: "GET",
    headers,
  });
}

test("image proxy returns 401 without token", async () => {
  const response = await worker.fetch(
    buildImageRequest("snap-1"),
    env as never,
  );
  expect(response.status).toBe(401);
});

test("image proxy returns 404 for unknown screenshot", async () => {
  const response = await worker.fetch(
    buildImageRequest("does-not-exist", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(404);
});

test("image proxy streams the PNG bytes", async () => {
  await ingestOne("snap-image-stream", "2026-05-23T01:00:00Z");

  const response = await worker.fetch(
    buildImageRequest("snap-image-stream", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("image/png");
  const bytes = new Uint8Array(await response.arrayBuffer());
  expect(bytes.length).toBe(PNG_BYTES.length);
  expect(bytes.slice(0, 8)).toEqual(
    new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  );
});
