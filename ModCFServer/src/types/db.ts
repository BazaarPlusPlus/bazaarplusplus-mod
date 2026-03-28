import type { UploadPurpose } from "./api";

export type RegisteredClientRow = {
  client_id: string;
  install_id: string;
  purpose: UploadPurpose;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
};
