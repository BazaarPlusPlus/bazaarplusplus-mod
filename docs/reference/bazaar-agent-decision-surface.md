# BazaarAgent Decision Surface — Internal Reference

> **Status: optional host plugin.** The BazaarAgent host is a separate, optional BepInEx plugin; default mod builds ship without it. Build it on demand with `./run.sh build --with-bazaaragent-host` and set `[BazaarAgent] Enabled = true` in `BazaarPlusPlus.BazaarAgent.cfg` to serve the HTTP surface this doc derives. The wire contract itself is owned by [bazaar-agent-http-api-v1.md](bazaar-agent-http-api-v1.md); this doc owns the game-reader-side derivation.

## Scope

Companion to [bazaar-agent-http-api-v1.md](bazaar-agent-http-api-v1.md). Documents how `BazaarAgentHost/BazaarAgentGameContextReader.cs` populates each `BazaarAgentContext` field from live game state. Intended for contributors modifying the host-side game reader.

## Source of Truth

`BazaarAgentHost/BazaarAgentGameContextReader.cs` is authoritative for game-state derivation; `BazaarAgent/` owns the pure wire DTOs, validation, HTTP transport, queueing, and runtime controller. Read alongside `decompiled/` to confirm game-side types and property names.

## Top-Level Scalar Mapping

| Context field | Derivation |
|---|---|
| `SchemaVersion` | Const `"1.2.0"` |
| `TickId` | Assigned by `BazaarAgentContextSnapshotPublisher` (incremented on each publish) |
| `ServerTimeUtc` | `DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)` (ISO-8601) |
| `IsEnabled` | `BazaarAgentBepInExOptions.Enabled` (`[BazaarAgent] Enabled`) |
| `IsInRun` | `Data.Run != null && AppState.CurrentState is RunAppState` |
| `HasActiveRun` | `Data.HasActiveRun` (computed property on `Data`) |
| `CanStartOrContinueRun` | `BazaarAgentSceneProbe.IsAtHeroSelectAndReadyForNewRun()` — true iff `SceneManager.GetActiveScene().name == "HeroSelectScene"` AND `AppState.CurrentState == null` AND `ClientCache.Profile.Value != null` |
| `IsClientBusy` | Hardcoded `false` in v1 — `HttpGameClient` busy-tracking deferred |
| `RunId` | `Data.Run?.GameModeId.ToString("D")`, or `null` if Guid is default |
| `StateName` | See "State Mapping" section below |
| `PlayerHero` | `Data.Run?.Player?.Hero.ToString()` |
| `Day` | `(int)Data.Run.Day`, or null when no run is active |
| `Hour` | `(int)Data.Run.Hour`, or null when no run is active |
| `Wins` | `(int)Data.Run.Victories`, or null when no run is active |
| `Losses` | `(int)Data.Run.Losses`, or null when no run is active |
| `PlayerGold` | `Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Gold)` |
| `PlayerIncome` | `Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Income)` |
| `PlayerHealth` | `Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Health)` |
| `PlayerMaxHealth` | `Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthMax)` |
| `PlayerPrestige` | `Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Prestige)` |
| `PlayerLevel` | `Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Level)` |
| `SelectionIsFree` | `Data.CurrentState?.SelectionContextRules?.SelectionIsFree` |
| `CanExit` | `Data.CurrentState?.SelectionContextRules?.CanExit` |
| `CanReroll` | `AppState.CanHandleOperation(Reroll) && RerollsRemaining > 0 && PlayerGold >= RerollCost` |
| `RerollCost` | `(int)(Data.CurrentState?.RerollCost ?? 0u)` |
| `RerollsRemaining` | `(int)(Data.CurrentState?.RerollsRemaining ?? 0u)` |
| `CurrentEncounterId` | `Data.CurrentState?.CurrentEncounterId` (already `string?`) |
| `CurrentEncounterType` | Looks for a live `Data.Entities` card whose `TemplateId` matches `CurrentEncounterId`; uses `card.Template.GetType().Name` when available, otherwise `card.Type.ToString()` |
| `ActionCooldownRemainingSeconds` | Passed in by `BazaarAgentRuntimeController`, computed from the last non-wait action time and `IBazaarAgentOptions.ActionMinDelay` |
| `InteractableTemplateIds` | `BazaarAgentInteractionFilterProbe.ReadCurrentFilter()` — reflection on `AppState._iteractionFilter`. Null/omitted when the list is empty or reflection fails. |

## State Mapping

How `AppState.CurrentState` + `Data.CurrentState.StateName` (`ERunState`) maps to `BazaarAgentRunStateName`:

| Condition | `StateName` value |
|---|---|
| `AppState.CurrentState is null` | `Unknown` |
| `AppState.CurrentState is StartRunAppState` | `StartRun` |
| `AppState.CurrentState is ReplayState` | `Replay` |
| `ERunState.Choice` | `Choice` |
| `ERunState.Encounter` | `Encounter` |
| `ERunState.Combat` | `Combat` |
| `ERunState.PVPCombat` | `PvpCombat` |
| `ERunState.LevelUp` | `LevelUp` |
| `ERunState.Loot` | `Loot` |
| `ERunState.Pedestal` | `Pedestal` |
| `ERunState.EndRunVictory` | `EndRunVictory` |
| `ERunState.EndRunDefeat` | `EndRunDefeat` |

For all other `AppState.CurrentState` subtypes the builder falls through to `Data.CurrentState.StateName` for the `ERunState` match. Unrecognized `ERunState` values map to `Unknown`.

## Card List Mapping

| Context field | Source |
|---|---|
| `BoardItems` | Walk `Data.Run.Player.Hand` (`SocketedContainer`); collect each non-null socket slot |
| `ChestItems` | Walk `Data.Run.Player.Stash` (`SocketedContainer`); collect each non-null socket slot |
| `PlayerSkills` | `Data.Run.Player.Skills` (`List<SkillCard>`) |
| `SellableItems` | Item cards where `(allowedOps & SellItem) != 0` (filtered from `BoardItems` + `ChestItems`) |
| `SelectionOptions` | `Data.CurrentState.SelectionSet` (`List<string>` of InstanceId values), each resolved via `Data.Entities[instanceId]` |

Each `BazaarAgentCardSnapshot` also includes sorted `Tags`, sorted `HiddenTags`, sorted `Attributes`, and sorted active ability summaries from `Card.GetActiveAbilities()`. Ability summaries intentionally expose type names (`Trigger`, `Action`) and internal text instead of serializing the full game effect graph.

## AvailableActions Derivation

Each action's inclusion criterion in terms of game-state paths:

| Action | Game-state gate |
|---|---|
| `Wait` | Always included |
| `Reroll` | `AppState.CanHandleOperation(Reroll) && RerollsRemaining > 0 && PlayerGold >= RerollCost` |
| `ExitState` | `AppState.CanHandleOperation(ExitState) && SelectionContextRules.CanExit != false` |
| `SellItem` (per card) | `AppState.CanHandleOperation(SellItem)` — one entry per sellable card |
| `MoveItem` (per placement) | Legal placements computed by `BazaarAgentMoveTargetPlanner.Enumerate(itemSize, capacity=10, occupiedAndLockedSockets, excludeStart, excludeCount)`. A socket counts as unusable if `SocketedContainer.IsSocketLocked` returns true for it, or if it is occupied. One entry per legal `(card, targetSection, targetSocket)` triplet. |
| `SelectItem` (per card) | `AppState.CanHandleOperation(SelectItem) && card.CanSelect != false && card.CanAfford != false && card.CanFit != false` — one entry per offered item in `SelectionOptions` |
| `SelectSkill` (per skill) | `AppState.CanHandleOperation(SelectSkill) && card.CanSelect != false && card.CanAfford != false` — one entry per offered skill in `SelectionOptions` |

**Target-selection mode override**: when `InteractableTemplateIds` is non-empty (i.e. `AppState._iteractionFilter` is populated by an upgrade/enchant encounter), offer-based `SelectItem` entries are suppressed and the builder appends one `SelectItem` `BazaarAgentDecisionOption` per owned item card (across `BoardItems` / `ChestItems`) whose templateId is in the filter, using the card's current `Section` + `LeftSocketId` + `Size` to fill `targetSection` and `targetSockets`. See `BazaarAgentTargetSelectionActions.Emit`.

## Error Handling

`BazaarAgentGameContextReader.Build` wraps its body in try/catch. On any exception it logs through `IBazaarAgentLogger` and returns a degenerate context:

- `IsEnabled` — from config (same as normal path)
- `StateName` — `Unknown`
- `AvailableActions` — `[Wait]`

The HTTP endpoint must never return 500 due to a broken context build; the degenerate context ensures a valid response is always available.
