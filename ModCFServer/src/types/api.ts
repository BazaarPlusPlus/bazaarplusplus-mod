export type RegisterRequest = {
  install_id?: unknown;
  plugin_version?: unknown;
  public_key?: {
    modulus_b64?: unknown;
    exponent_b64?: unknown;
  };
};

export type BindPlayerRequest = {
  player_account_id?: unknown;
};
