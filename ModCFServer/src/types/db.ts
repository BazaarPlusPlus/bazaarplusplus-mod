import type { UploadPurpose } from "./api";

export type RegisteredClientRow = {
  client_id: string;
  install_id: string;
  purpose: UploadPurpose;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
};

export type ActiveBindingUidRow = {
  uid: string;
};

export type ActiveBindingPlayerAccountRow = {
  player_account_id: string;
};

export type ObservedPlayerAccountRow = {
  player_account_id: string;
};

export type ReplayUploadLookupRow = {
  battle_id: string;
  object_key: string;
  uploaded_at_utc: string;
};

export type ProjectedBattleQueryRow = {
  battle_id: string;
  recorded_at_utc: string;
  opponent_account_id: string | null;
  payload_json: string;
  replay_available: number;
};

export type RunUploadProjectionStatus =
  | "pending"
  | "projected"
  | "failed";
