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

export type ProjectedBattleQueryRow = {
  battle_id: string;
  recorded_at_utc: string;
  opponent_account_id: string | null;
  run_id?: string | null;
  day?: number | null;
  hour?: number | null;
  encounter_id?: string | null;
  player_name?: string | null;
  player_account_id?: string | null;
  player_hero?: string | null;
  player_rank?: string | null;
  player_rating?: number | null;
  player_level?: number | null;
  opponent_name?: string | null;
  opponent_hero?: string | null;
  opponent_rank?: string | null;
  opponent_rating?: number | null;
  opponent_level?: number | null;
  combat_kind?: string;
  result?: string | null;
  winner_combatant_id?: string | null;
  loser_combatant_id?: string | null;
  replay_available: number;
  replay_object_key?: string | null;
  replay_uploaded_at_utc?: string | null;
};

export type RunUploadProjectionStatus =
  | "received"
  | "stored"
  | "projecting"
  | "pending"
  | "projected"
  | "failed";
