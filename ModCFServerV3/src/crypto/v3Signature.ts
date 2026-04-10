import { base64ToBytes, toBase64Url } from "./base64";
import { sha256Base64 } from "./hash";

export function normalizeQueryString(searchParams: URLSearchParams): string {
  return Array.from(searchParams.entries())
    .map(([key, value]) => ({
      encodedKey: encodeURIComponent(key),
      encodedValue: encodeURIComponent(value),
    }))
    .sort((left, right) => {
      if (left.encodedKey !== right.encodedKey) {
        return left.encodedKey < right.encodedKey ? -1 : 1;
      }

      if (left.encodedValue !== right.encodedValue) {
        return left.encodedValue < right.encodedValue ? -1 : 1;
      }

      return 0;
    })
    .map(
      ({ encodedKey, encodedValue }) =>
        `${encodedKey}=${encodedValue}`,
    )
    .join("&");
}

export function buildCanonicalRequest(input: {
  method: string;
  path: string;
  query: string;
  installationId: string;
  timestamp: string;
  bodyHash: string;
}): string {
  return [
    input.method.trim().toUpperCase(),
    input.path.trim() || "/",
    input.query.trim(),
    input.installationId.trim(),
    input.timestamp.trim(),
    input.bodyHash.trim(),
  ].join("\n");
}

export async function computeRequestBodyHash(request: Request): Promise<string> {
  const body = await request.clone().arrayBuffer();
  return sha256Base64(body);
}

export async function verifyInstallationSignature(input: {
  publicKey: { modulus_b64: string; exponent_b64: string };
  canonical: string;
  signatureB64: string;
}): Promise<boolean> {
  const key = await crypto.subtle.importKey(
    "jwk",
    {
      kty: "RSA",
      n: toBase64Url(input.publicKey.modulus_b64),
      e: toBase64Url(input.publicKey.exponent_b64),
      alg: "RS256",
      ext: true,
    },
    {
      name: "RSASSA-PKCS1-v1_5",
      hash: "SHA-256",
    },
    false,
    ["verify"],
  );

  return crypto.subtle.verify(
    "RSASSA-PKCS1-v1_5",
    key,
    base64ToBytes(input.signatureB64),
    new TextEncoder().encode(input.canonical),
  );
}

