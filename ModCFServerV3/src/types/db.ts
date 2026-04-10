export type V3UserRow = {
  player_account_id: string;
  player_username: string;
  password_hash: string;
  stream_platform: string | null;
  stream_channel_id: string | null;
  stream_url: string | null;
  created_at_utc: string;
  updated_at_utc: string;
  last_login_at_utc: string | null;
};

export type V3InstallationRow = {
  installation_id: string;
  player_account_id: string;
  public_key: string;
  status: string;
  created_at_utc: string;
  last_seen_at_utc: string | null;
  revoked_at_utc: string | null;
};

export type V3InstallationSessionRow = {
  session_id: string;
  player_account_id: string;
  created_at_utc: string;
  expires_at_utc: string;
  revoked_at_utc: string | null;
};

export type V3InstallationObservationRow = {
  installation_id: string;
  observed_player_account_id: string;
  observed_player_username: string;
  observed_at_utc: string;
  signature: string;
  status: string;
};

