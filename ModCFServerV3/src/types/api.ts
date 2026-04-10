export type ActivateRequest = {
  player_account_id?: unknown;
  player_username?: unknown;
  password?: unknown;
  installation_public_key?: unknown;
};

export type LoginRequest = {
  player_username?: unknown;
  password?: unknown;
};

export type CreateInstallationRequest = {
  player_account_id?: unknown;
  installation_public_key?: unknown;
};
