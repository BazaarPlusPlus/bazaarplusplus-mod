import { base64ToBytes, toBase64Url } from "./base64";

export function canonicalRequest(
  method: string,
  path: string,
  clientId: string,
  installId: string,
  timestamp: string,
  nonce: string,
  bodyHash: string,
): string {
  return [
    method.trim().toUpperCase(),
    path.trim() || "/",
    clientId.trim(),
    installId.trim(),
    timestamp.trim(),
    nonce.trim(),
    bodyHash.trim(),
  ].join("\n");
}

export async function verifySignature(
  modulusB64: string,
  exponentB64: string,
  canonical: string,
  signatureB64: string,
): Promise<boolean> {
  const key = await crypto.subtle.importKey(
    "jwk",
    {
      kty: "RSA",
      n: toBase64Url(modulusB64),
      e: toBase64Url(exponentB64),
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
    base64ToBytes(signatureB64),
    new TextEncoder().encode(canonical),
  );
}
