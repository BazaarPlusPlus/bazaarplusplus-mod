import { json } from "../http/json";
import { trimString } from "../http/request";

const textDecoder = new TextDecoder();

export type JsonObject = Record<string, unknown>;

export type ParsedRunBattle = {
  raw: JsonObject;
  battleId: string | null;
  runId: string | null;
  recordedAtUtc: string | null;
  day: number | null;
  hour: number | null;
  encounterId: string | null;
  playerName: string | null;
  playerAccountId: string | null;
  playerHero: string | null;
  playerRank: string | null;
  playerRating: number | null;
  playerLevel: number | null;
  opponentName: string | null;
  opponentAccountId: string | null;
  opponentHero: string | null;
  opponentRank: string | null;
  opponentRating: number | null;
  opponentLevel: number | null;
  combatKind: string | null;
  result: string | null;
  winnerCombatantId: string | null;
  loserCombatantId: string | null;
};

export type ParsedRunUploadBody = {
  raw: JsonObject;
  runId: string | null;
  battles: ParsedRunBattle[];
};

function asObject(value: unknown): JsonObject | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return null;
  }

  return value as JsonObject;
}

function asString(value: unknown): string | null {
  return typeof value === "string" ? trimString(value) || null : null;
}

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function hasRequiredProjectedBattleFields(raw: JsonObject): boolean {
  const combatKind = asString(raw.combat_kind);
  if (combatKind == null) {
    return false;
  }

  if (combatKind !== "PVPCombat") {
    return true;
  }

  return asString(raw.battle_id) != null && asString(raw.opponent_account_id) != null;
}

function parseBattle(value: unknown): ParsedRunBattle | null {
  const raw = asObject(value);
  if (raw == null) {
    return null;
  }

  return {
    raw,
    battleId: asString(raw.battle_id),
    runId: asString(raw.run_id),
    recordedAtUtc: asString(raw.recorded_at_utc),
    day: asNumber(raw.day),
    hour: asNumber(raw.hour),
    encounterId: asString(raw.encounter_id),
    playerName: asString(raw.player_name),
    playerAccountId: asString(raw.player_account_id),
    playerHero: asString(raw.player_hero),
    playerRank: asString(raw.player_rank),
    playerRating: asNumber(raw.player_rating),
    playerLevel: asNumber(raw.player_level),
    opponentName: asString(raw.opponent_name),
    opponentAccountId: asString(raw.opponent_account_id),
    opponentHero: asString(raw.opponent_hero),
    opponentRank: asString(raw.opponent_rank),
    opponentRating: asNumber(raw.opponent_rating),
    opponentLevel: asNumber(raw.opponent_level),
    combatKind: asString(raw.combat_kind),
    result: asString(raw.result),
    winnerCombatantId: asString(raw.winner_combatant_id),
    loserCombatantId: asString(raw.loser_combatant_id),
  };
}

export function parseRunUploadBody(
  payload: ArrayBuffer,
): ParsedRunUploadBody | Response {
  try {
    const parsed = JSON.parse(textDecoder.decode(payload));
    const raw = asObject(parsed);
    if (raw == null) {
      return json({ error: "invalid_json_body" }, { status: 400 });
    }

    if (!Array.isArray(raw.pvp_battles)) {
      return json({ error: "pvp_battles_required" }, { status: 400 });
    }

    for (const battle of raw.pvp_battles) {
      const battleObject = asObject(battle);
      if (battleObject == null || !hasRequiredProjectedBattleFields(battleObject)) {
        return json({ error: "invalid_pvp_battle_payload" }, { status: 400 });
      }
    }

    const battles = raw.pvp_battles
      .map(parseBattle)
      .filter((battle): battle is ParsedRunBattle => battle != null);

    return {
      raw,
      runId: asString(raw.run_id),
      battles,
    };
  } catch {
    return json({ error: "invalid_json_body" }, { status: 400 });
  }
}
