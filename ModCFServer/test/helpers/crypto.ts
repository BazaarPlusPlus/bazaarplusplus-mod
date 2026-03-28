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
    method.toUpperCase(),
    path,
    clientId,
    installId,
    timestamp,
    nonce,
    bodyHash,
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
