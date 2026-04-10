import { base64ToBytes, bytesToBase64 } from "./base64";

const PASSWORD_SCHEME = "v1";
const SALT_BYTES = 16;

export async function hashPassword(password: string): Promise<string> {
  const normalized = normalizePassword(password);
  const salt = crypto.getRandomValues(new Uint8Array(SALT_BYTES));
  const digest = await crypto.subtle.digest(
    "SHA-256",
    joinBytes(salt, new TextEncoder().encode(normalized)),
  );

  return `${PASSWORD_SCHEME}:${bytesToBase64(salt)}:${bytesToBase64(new Uint8Array(digest))}`;
}

export async function verifyPassword(
  password: string,
  hash: string,
): Promise<boolean> {
  const normalized = normalizePassword(password);
  const [scheme, saltB64, digestB64] = hash.split(":");
  if (
    scheme !== PASSWORD_SCHEME ||
    !saltB64 ||
    !digestB64
  ) {
    return false;
  }

  const salt = base64ToBytes(saltB64);
  const digest = await crypto.subtle.digest(
    "SHA-256",
    joinBytes(salt, new TextEncoder().encode(normalized)),
  );
  const actualDigest = new Uint8Array(digest);
  const expectedDigest = base64ToBytes(digestB64);
  if (actualDigest.length !== expectedDigest.length) {
    return false;
  }

  let mismatch = 0;
  for (let index = 0; index < actualDigest.length; index += 1) {
    mismatch |= actualDigest[index] ^ expectedDigest[index];
  }

  return mismatch === 0;
}

function normalizePassword(password: string): string {
  return password.trim();
}

function joinBytes(left: Uint8Array, right: Uint8Array): Uint8Array {
  const joined = new Uint8Array(left.length + right.length);
  joined.set(left, 0);
  joined.set(right, left.length);
  return joined;
}
