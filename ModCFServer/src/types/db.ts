export type ClientRow = {
  client_id: string;
  install_id: string;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
  registered_at_utc?: string;
  last_seen_at_utc?: string | null;
  revoked_at_utc?: string | null;
};

export type PlayerLinkRow = {
  client_id: string;
  player_account_id: string;
  bound_at_utc?: string;
  last_confirmed_at_utc?: string;
};
