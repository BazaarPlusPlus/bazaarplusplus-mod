import {
  createHash,
  createSign,
  generateKeyPairSync,
  type KeyObject,
} from "node:crypto";

export function toBase64(input: string | Uint8Array): string {
  return Buffer.from(input).toString("base64");
}

export function sha256Base64(input: string | Uint8Array): string {
  return toBase64(createHash("sha256").update(input).digest());
}

export function toBase64UrlSegment(base64: string): string {
  return base64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

export function canonicalRequest(
  method: string,
  path: string,
  clientId: string,
  installId: string,
  timestamp: string,
  nonceOrBodyHash: string,
  maybeBodyHash?: string,
): string {
  const bodyHash = maybeBodyHash ?? nonceOrBodyHash;
  return [
    method.toUpperCase(),
    path,
    clientId,
    installId,
    timestamp,
    bodyHash,
  ].join("\n");
}

export function canonicalRequestV3(input: {
  method: string;
  path: string;
  query?: string;
  installationId: string;
  timestamp: string;
  bodyHash: string;
}): string {
  return [
    input.method.toUpperCase(),
    input.path || "/",
    input.query ?? "",
    input.installationId,
    input.timestamp,
    input.bodyHash,
  ].join("\n");
}

export function signCanonical(
  privateKey: KeyObject,
  canonical: string,
): string {
  const signer = createSign("RSA-SHA256");
  signer.update(canonical);
  signer.end();
  return signer.sign(privateKey).toString("base64");
}

export function generateClientKeyPair() {
  const { privateKey, publicKey } = generateKeyPairSync("rsa", {
    modulusLength: 2048,
  });
  const jwk = publicKey.export({ format: "jwk" });
  if (typeof jwk.n !== "string" || typeof jwk.e !== "string") {
    throw new Error("expected RSA public key JWK export");
  }

  return {
    privateKey,
    modulusB64: Buffer.from(jwk.n, "base64url").toString("base64"),
    exponentB64: Buffer.from(jwk.e, "base64url").toString("base64"),
  };
}
