import type { Env } from "../../env";
import { requireBearer } from "../../http/auth";
import { json, jsonError } from "../../http/json";

const DatePattern = /^\d{4}-\d{2}-\d{2}$/;

function validateDate(date: string | null): string | null {
  if (date == null || !DatePattern.test(date)) {
    return null;
  }
  const parsed = new Date(`${date}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  if (parsed.getTime() > Date.now() + 60_000) {
    return null;
  }
  return date;
}

type BazaarDbScreenshotRow = {
  screenshot_id: string;
  player_account_id: string;
  run_id: string | null;
  hero_name: string | null;
  final_days: number | null;
  final_victories: number | null;
  player_name: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_position: number | null;
  captured_at_utc: string;
  image_format: string;
  image_sha256: string;
  image_bytes: number;
  uploaded_at_utc: string;
};

function buildImageUrl(request: Request, screenshotId: string): string {
  const url = new URL(request.url);
  return `${url.protocol}//${url.host}/bazaardb/image/${encodeURIComponent(screenshotId)}`;
}

export async function handleGetBazaarDbManifest(
  request: Request,
  env: Env,
): Promise<Response> {
  const unauthorized = requireBearer(request, env, "BAZAARDB_PULL_TOKEN");
  if (unauthorized != null) {
    return unauthorized;
  }

  const url = new URL(request.url);
  const validatedDate = validateDate(url.searchParams.get("date"));
  if (validatedDate == null) {
    return jsonError("invalid_date");
  }

  // Scan shape: covered by idx_bazaardb_screenshots_date (captured_date_utc,
  // uploaded_at_utc) — index seek + range, ORDER BY satisfied by the index.
  // No LIMIT: per-day cardinality is bounded by daily upload volume. If a
  // single day grows past a few thousand rows, add pagination here.
  const result = await env.DB.prepare(
    `
      SELECT screenshot_id, player_account_id, run_id, hero_name, final_days,
             final_victories, player_name, player_rank, player_rating, player_position,
             captured_at_utc, image_format, image_sha256, image_bytes, uploaded_at_utc
      FROM bazaardb_screenshots
      WHERE captured_date_utc = ?
      ORDER BY uploaded_at_utc ASC
    `,
  )
    .bind(validatedDate)
    .all<BazaarDbScreenshotRow>();

  const items = result.results.map((row) => ({
    screenshot_id: row.screenshot_id,
    run_id: row.run_id,
    hero_name: row.hero_name,
    final_days: row.final_days,
    final_victories: row.final_victories,
    player_name: row.player_name,
    player_account_id: row.player_account_id,
    player_rank: row.player_rank,
    player_rating: row.player_rating,
    player_position: row.player_position,
    captured_at_utc: row.captured_at_utc,
    image_url: buildImageUrl(request, row.screenshot_id),
    image_format: row.image_format,
    image_sha256: row.image_sha256,
    image_bytes: row.image_bytes,
  }));

  return json({
    date: validatedDate,
    schema_version: 1,
    generated_at_utc: new Date().toISOString(),
    items,
  });
}
