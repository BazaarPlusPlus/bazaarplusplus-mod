import assert from "node:assert/strict";
import test from "node:test";

import type { Env } from "../src/env";
import { upsertBattle } from "../src/persistence/battles";
import { buildEnv } from "./helpers/mockEnv";

function buildBattleInput(overrides: Partial<Parameters<typeof upsertBattle>[1]> = {}) {
  return {
    battleId: "battle-001",
    runId: "run-001",
    clientId: "client-001",
    uploaderPlayerAccountId: "uploader-account-a",
    recordedAtUtc: "2026-04-01T00:00:00.000Z",
    day: 10,
    hour: 2,
    playerName: "Uploader",
    playerAccountId: "uploader-account-a",
    playerHero: "Dooley",
    playerRank: "Legendary",
    playerRating: 1800,
    playerLevel: 12,
    opponentName: "Target",
    opponentAccountId: "target-account",
    opponentHero: "Vanessa",
    opponentRank: "Legendary",
    opponentRating: 1780,
    opponentLevel: 12,
    combatKind: "PVPCombat",
    result: "win",
    winnerCombatantId: "Player",
    loserCombatantId: "Opponent",
    replaySchemaVersion: 2,
    replayObjectKey: "battle-replays/client-001/battle-001/hash.json",
    replaySizeBytes: 1234,
    createdAtUtc: "2026-04-02T00:00:00.000Z",
    updatedAtUtc: "2026-04-02T00:00:00.000Z",
    ...overrides,
  };
}

test("upsertBattle updates timestamps only when stored battle content changes", async () => {
  const env = buildEnv();
  const typedEnv = env as unknown as Env;

  await upsertBattle(typedEnv, buildBattleInput());
  const firstRow = env.DB.battles.get("battle-001");

  assert.ok(firstRow);
  assert.equal(firstRow.created_at_utc, "2026-04-02T00:00:00.000Z");
  assert.equal(firstRow.updated_at_utc, "2026-04-02T00:00:00.000Z");
  assert.equal(firstRow.uploader_player_account_id, "uploader-account-a");

  await upsertBattle(
    typedEnv,
    buildBattleInput({
      uploaderPlayerAccountId: "uploader-account-b",
      createdAtUtc: "2026-04-02T01:00:00.000Z",
      updatedAtUtc: "2026-04-02T01:00:00.000Z",
    }),
  );
  const secondRow = env.DB.battles.get("battle-001");

  assert.ok(secondRow);
  assert.equal(secondRow.created_at_utc, "2026-04-02T00:00:00.000Z");
  assert.equal(secondRow.updated_at_utc, "2026-04-02T01:00:00.000Z");
  assert.equal(secondRow.uploader_player_account_id, "uploader-account-b");

  await upsertBattle(
    typedEnv,
    buildBattleInput({
      uploaderPlayerAccountId: "uploader-account-b",
      createdAtUtc: "2026-04-02T02:00:00.000Z",
      updatedAtUtc: "2026-04-02T02:00:00.000Z",
    }),
  );
  const thirdRow = env.DB.battles.get("battle-001");

  assert.ok(thirdRow);
  assert.equal(thirdRow.created_at_utc, "2026-04-02T00:00:00.000Z");
  assert.equal(thirdRow.updated_at_utc, "2026-04-02T01:00:00.000Z");
  assert.equal(thirdRow.uploader_player_account_id, "uploader-account-b");
  assert.equal(
    thirdRow.replay_object_key,
    "battle-replays/client-001/battle-001/hash.json",
  );
});
