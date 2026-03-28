export type UploadPurpose = "runs" | "replays";

export type RegisterRequest = {
  install_id?: unknown;
  plugin_version?: unknown;
  purpose?: unknown;
  public_key?: {
    modulus_b64?: unknown;
    exponent_b64?: unknown;
  };
};
