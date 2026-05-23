import { base64ToBytes } from "../../crypto/base64";
import { sha256Base64 } from "../../crypto/hash";
import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { optionalFiniteNumber, optionalTrimmedString } from "../../http/request";
import { logInfo, logWarn } from "../../observability";

type BazaarDbScreenshotRequest = {
  schema_version?: unknown;
  submitted_at_utc?: unknown;
  player_account_id?: unknown;
  screenshot_id?: unknown;
  run_id?: unknown;
  hero_name?: unknown;
  final_days?: unknown;
  final_victories?: unknown;
  player_name?: unknown;
  player_rank?: unknown;
  player_rating?: unknown;
  player_position?: unknown;
  captured_at_utc?: unknown;
  image_format?: unknown;
  image_bytes_base64?: unknown;
};

const SupportedSchemaVersion = 1;
const PngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

const ScreenshotIdPattern = /^[A-Za-z0-9._-]{1,128}$/;

function hasPngMagic(bytes: Uint8Array): boolean {
  if (bytes.length < PngMagic.length) {
    return false;
  }
  for (let index = 0; index < PngMagic.length; index += 1) {
    if (bytes[index] !== PngMagic[index]) {
      return false;
    }
  }
  return true;
}

function parseCapturedDateUtc(value: string): string | null {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  if (parsed.getTime() > Date.now() + 60_000) {
    return null;
  }
  const yyyy = parsed.getUTCFullYear().toString().padStart(4, "0");
  const mm = (parsed.getUTCMonth() + 1).toString().padStart(2, "0");
  const dd = parsed.getUTCDate().toString().padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function rejected(reason: string): Response {
  return json({ status: "rejected", reason }, { status: 400 });
}

export async function handleUploadBazaarDbScreenshot(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as BazaarDbScreenshotRequest;

  const schemaVersion = optionalFiniteNumber(body.schema_version);
  if (schemaVersion !== SupportedSchemaVersion) {
    return rejected("unsupported_schema_version");
  }

  const submittedAtUtc = optionalTrimmedString(body.submitted_at_utc);
  const playerAccountId = optionalTrimmedString(body.player_account_id);
  const screenshotId = optionalTrimmedString(body.screenshot_id);
  const capturedAtUtc = optionalTrimmedString(body.captured_at_utc);
  const imageFormat = optionalTrimmedString(body.image_format);
  const imageBytesBase64 =
    typeof body.image_bytes_base64 === "string" ? body.image_bytes_base64 : null;

  if (
    submittedAtUtc == null ||
    playerAccountId == null ||
    screenshotId == null ||
    capturedAtUtc == null ||
    imageFormat == null ||
    imageBytesBase64 == null
  ) {
    return rejected("missing_required_field");
  }

  if (!ScreenshotIdPattern.test(screenshotId)) {
    return rejected("invalid_screenshot_id");
  }

  if (imageFormat !== "png") {
    return rejected("unsupported_image_format");
  }

  const capturedDateUtc = parseCapturedDateUtc(capturedAtUtc);
  if (capturedDateUtc == null) {
    return rejected("invalid_captured_at_utc");
  }

  let imageBytes: Uint8Array;
  try {
    imageBytes = base64ToBytes(imageBytesBase64);
  } catch {
    return rejected("invalid_image_bytes_base64");
  }

  if (imageBytes.length === 0 || !hasPngMagic(imageBytes)) {
    return rejected("image_bytes_not_png");
  }

  const r2Key = `bazaardb/screenshots/${capturedDateUtc}/${screenshotId}.png`;

  await env.BAZAARDB_BUCKET.put(r2Key, imageBytes, {
    httpMetadata: { contentType: "image/png" },
  });

  const imageSha256 = await sha256Base64(imageBytes);
  const uploadedAtUtc = new Date().toISOString();

  try {
    await env.DB.prepare(
      `
        INSERT INTO bazaardb_screenshots (
          screenshot_id, player_account_id, run_id, hero_name, final_days,
          final_victories, player_name, player_rank, player_rating, player_position,
          captured_at_utc, captured_date_utc, image_format, image_sha256, image_bytes,
          r2_key, uploaded_at_utc, schema_version
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(screenshot_id) DO UPDATE SET
          uploaded_at_utc = excluded.uploaded_at_utc,
          image_sha256    = excluded.image_sha256,
          image_bytes     = excluded.image_bytes,
          r2_key          = excluded.r2_key
      `,
    )
      .bind(
        screenshotId,
        playerAccountId,
        optionalTrimmedString(body.run_id),
        optionalTrimmedString(body.hero_name),
        optionalFiniteNumber(body.final_days),
        optionalFiniteNumber(body.final_victories),
        optionalTrimmedString(body.player_name),
        optionalTrimmedString(body.player_rank),
        optionalFiniteNumber(body.player_rating),
        optionalFiniteNumber(body.player_position),
        capturedAtUtc,
        capturedDateUtc,
        imageFormat,
        imageSha256,
        imageBytes.length,
        r2Key,
        uploadedAtUtc,
        SupportedSchemaVersion,
      )
      .run();
  } catch (error) {
    logWarn("upload_bazaardb_screenshot.db_upsert_failed", {
      screenshot_id: screenshotId,
      error: String(error),
    });
    return json(
      { status: "error", reason: "db_upsert_failed" },
      { status: 500 },
    );
  }

  logInfo("upload_bazaardb_screenshot.accepted", {
    screenshot_id: screenshotId,
    captured_date_utc: capturedDateUtc,
    image_bytes: imageBytes.length,
  });

  return json({
    status: "ok",
    screenshot_id: screenshotId,
    uploaded_at_utc: uploadedAtUtc,
  });
}
