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
  payload_bytes?: number | null;
  schema_version?: number | null;
  content_type?: string | null;
  created_at_utc?: string;
  updated_at_utc?: string;
};

export type ProjectedBattleQueryRow = {
  battle_id: string;
  recorded_at_utc: string;
  opponent_account_id: string | null;
  payload_json: string;
  summary_json?: string;
  replay_available: number;
  projection_version?: number;
};

export type RunUploadProjectionStatus =
  | "received"
  | "stored"
  | "projecting"
  | "pending"
  | "projected"
  | "failed";
