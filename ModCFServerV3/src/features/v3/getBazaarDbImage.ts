import type { Env } from "../../env";
import { json } from "../../http/json";

function constantTimeEquals(a: string, b: string): boolean {
  if (a.length !== b.length) {
    return false;
  }
  let result = 0;
  for (let index = 0; index < a.length; index += 1) {
    result |= a.charCodeAt(index) ^ b.charCodeAt(index);
  }
  return result === 0;
}

function authorizeBearer(request: Request, env: Env): boolean {
  const header = request.headers.get("Authorization");
  if (header == null) {
    return false;
  }
  const prefix = "Bearer ";
  if (!header.startsWith(prefix)) {
    return false;
  }
  const presented = header.slice(prefix.length).trim();
  const expected = (env.BAZAARDB_PULL_TOKEN ?? "").trim();
  if (expected.length === 0) {
    return false;
  }
  return constantTimeEquals(presented, expected);
}

export async function handleGetBazaarDbImage(
  request: Request,
  env: Env,
  screenshotId: string,
): Promise<Response> {
  if (!authorizeBearer(request, env)) {
    return new Response(null, { status: 401 });
  }

  if (screenshotId.length === 0) {
    return json({ error: "screenshot_id_required" }, { status: 400 });
  }

  const row = await env.DB.prepare(
    "SELECT r2_key, image_format FROM bazaardb_screenshots WHERE screenshot_id = ?",
  )
    .bind(screenshotId)
    .first<{ r2_key: string; image_format: string }>();

  if (row == null) {
    return new Response(null, { status: 404 });
  }

  const object = await env.BAZAARDB_BUCKET.get(row.r2_key);
  if (object == null) {
    return new Response(null, { status: 404 });
  }

  const contentType =
    row.image_format === "png" ? "image/png" : "application/octet-stream";

  return new Response(object.body, {
    status: 200,
    headers: {
      "content-type": contentType,
      "cache-control": "private, max-age=86400",
    },
  });
}
