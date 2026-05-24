import type { Env } from "../../env";
import { requireBearer } from "../../http/auth";
import { jsonError } from "../../http/json";

export async function handleGetBazaarDbImage(
  request: Request,
  env: Env,
  screenshotId: string,
): Promise<Response> {
  const unauthorized = requireBearer(request, env, "BAZAARDB_PULL_TOKEN");
  if (unauthorized != null) {
    return unauthorized;
  }

  if (screenshotId.length === 0) {
    return jsonError("screenshot_id_required");
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
