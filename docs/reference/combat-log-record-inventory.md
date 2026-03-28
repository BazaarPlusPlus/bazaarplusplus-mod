# Combat Log Record Inventory

## Scope

This note lists the combat records that matter to the current `CombatLog` pipeline and the
planned frame-grouped combat panel. It separates:

- raw combat events from `simFrame.Events`
- combatant-side updates from `PlayerUpdates` / `OpponentUpdates`
- card updates from `CardUpdates`
- useful metadata already available or worth preserving for debug formatting

Primary source files:

- `Game/CombatLog/CombatLogRuntime.cs`
- `Game/CombatLog/CombatLogModels.cs`
- `Game/CombatLog/CombatLogFormatter.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.CombatSimEvents/ICombatSimEvent.cs`

## Raw Combat Events

These are the actual `ICombatSimEvent` entries found in `CombatSimFrame.Events`.

| Record Category | Raw Type | Current `EventType` | Key Fields | Current Panel Category | Suggested Priority |
| --- | --- | --- | --- | --- | --- |
| Raw event | `CombatSimEventEffectExecuted` | `EffectExecuted` | `ExecutionContextId`, `EffectId`, `ActionType`, `Source`, `TriggerSource`, `Target`, `VfxIndex` | `Event` | P0 |
| Raw event | `CombatSimEventEffectTriggered` | `EffectTriggered` | `ExecutionContextId`, `EffectId`, `Source`, `TriggerSource`, `Targets` | `Event` | P0 |
| Raw event | `CombatSimEventEffectAuraExecuted` | `EffectAuraExecuted` | `ExecutionContextId`, `EffectId`, `Source`, `TriggerSource`, `AppliedTo`, `RemovedFrom` | `Event` | P0 |
| Raw event | `CombatSimEventCombatantDied` | `CombatantDied` | `CombatantId` | `Death` | P0 |
| Raw event | `CombatSimEventCardEnchanted` | `CardEnchanted` | `InstanceId`, `EnchantmentType`, `IsReverted` | `Event` | P0 |
| Raw event | `CombatSimEventCardTransformed` | `CardTransformed` | `ExecutionContextId`, `OriginalInstanceId`, `TransformedCards` | `Event` | P0 |
| Raw event | `CombatSimEventCardTransformReverted` | `CardTransformReverted` | `OriginalCard`, `TransformedCardInstanceIds` | `Event` | P1 |
| Raw event | `CombatSimEventCardQuestCompleted` | `CardQuestCompleted` | `InstanceId`, `QuestGroupIndex`, `QuestEntryIndex` | `Event` | P1 |
| Raw event | `CombatSimEventCardQuestUpdated` | `CardQuestUpdated` | `InstanceId`, `QuestGroupIndex`, `QuestEntryIndex`, `OldProgress`, `NewProgress` | `Event` | P1 |
| Raw event | `CombatSimEventMonsterGoldReceived` | `MonsterGoldReceived` | `HealthAmount` | `Reward` | P1 |
| Raw event | `CombatSimEventMonsterXpReceived` | `MonsterXpReceived` | `HealthAmount` | `Reward` | P1 |
| Raw event | `CombatSimEventSandstormCountdownStarted` | `SandstormCountdownStarted` | none | `System` | P1 |
| Raw event | `CombatSimEventSandstormStarted` | `SandstormStarted` | none | `System` | P1 |

## Combatant Update Records

These are not raw `ICombatSimEvent` values, but they are part of the combat log timeline and
should be treated as first-class records in the grouped panel.

| Record Category | Source | Concrete Record | Key Fields | Current Panel Category | Suggested Priority |
| --- | --- | --- | --- | --- | --- |
| Combatant update | `PlayerUpdates` / `OpponentUpdates` | Health adjustment | `HealthType`, `Amount`, `IsCrit`, `IsReduced` | `Health` | P0 |
| Combatant update | `PlayerUpdates` / `OpponentUpdates` | Attribute change | `AttributeKey`, `PreviousValue`, `CurrentValue` | `Attribute` | P0 |
| Combatant update | `PlayerUpdates` / `OpponentUpdates` | Detail: portrait change | `PortraitIndex` | `Attribute` | P2 |
| Combatant update | `PlayerUpdates` / `OpponentUpdates` | Detail: death marker | `IsPlayerDead` rendered as `Marked dead` | `Attribute` | P1 |

## Card Update Records

These are also not raw `ICombatSimEvent` values, but they carry critical combat state changes and
belong in the combat log.

| Record Category | Source | Concrete Record | Key Fields | Current Panel Category | Suggested Priority |
| --- | --- | --- | --- | --- | --- |
| Card update | `CardUpdates` | Card attribute change | `AttributeKey`, `PreviousValue`, `CurrentValue` | `CardAttribute` | P0 |
| Card update | `CardUpdates` | Detail: enchantment | `Enchantment` | `CardAttribute` | P1 |
| Card update | `CardUpdates` | Detail: size | `Size` | `CardAttribute` | P2 |
| Card update | `CardUpdates` | Detail: tier | `Tier` | `CardAttribute` | P2 |
| Card update | `CardUpdates` | Detail: state change | `State.PreviousValue`, `State.CurrentValue` | `CardAttribute` | P1 |
| Card update | `CardUpdates` | Detail: placement | `Placement` | `CardAttribute` | P2 |
| Card update | `CardUpdates` | Detail: tags | `Tags` | `CardAttribute` | P2 |
| Card update | `CardUpdates` | Detail: hidden tags | `HiddenTags` | `CardAttribute` | P3 |
| Card update | `CardUpdates` | Detail: heroes | `Heroes` | `CardAttribute` | P3 |

## Shared Metadata And Debug Context

These values are already present in the current combat log models, or are upstream values worth
preserving because they make Debug formatting materially better.

| Metadata | Current Status | Source | Why It Matters | Suggested Priority |
| --- | --- | --- | --- | --- |
| `FrameIndex` | Already available | `CombatLogFrame` | Stable grouping key and replay alignment | P0 |
| `LogicalTime` | Already available | `CombatLogFrame` | Human-readable time in grouped headers | P0 |
| `FramesLeft` | Already available, mostly unused | `CombatLogFrame` | Useful for end-of-combat debug views | P2 |
| `ExecutionContextId` | Already available on event entries | `CombatLogEventEntry` | Best field for chaining related event sequences | P0 |
| `SourceId` | Already available | `CombatLogEventEntry` | Stable source identity for filtering and debugging | P0 |
| `TargetId` | Already available | `CombatLogEventEntry` | Stable target identity for filtering and debugging | P0 |
| `SourceDisplayName` | Already available | `CombatLogEventEntry` | Readable release text | P1 |
| `TargetDisplayName` | Already available | `CombatLogEventEntry` | Readable release text | P1 |
| `TriggerSource` | Upstream field, not fully preserved in current log model | effect event types | Explains chained triggers cleanly in Debug mode | P0 |
| `TargetType` | Derivable, not preserved explicitly | `IEffectTarget` | Distinguishes card targets from player targets | P1 |
| `VfxIndex` | Upstream field, currently dropped | `CombatSimEventEffectExecuted` | Useful when reconciling gameplay with VFX playback | P2 |
| `InstanceId` | Already available for cards | `CombatLogCardDisplayInfo` | Stable card instance identity | P1 |
| `TemplateId` | Already available for cards | `CombatLogCardDisplayInfo` | Useful in Debug mode and deduping card families | P1 |
| `OwnerSide` | Already available for cards, not displayed | `CombatLogCardDisplayInfo` | Important to identify player vs opponent cards | P1 |
| `CardType` | Already available for cards, not displayed | `CombatLogCardDisplayInfo` | Useful but less important than owner and template | P2 |

## Recommended Display Priority By Verbosity

This table maps the records above onto the planned grouped combat panel.

| Record Group | Compact | Standard | Verbose |
| --- | --- | --- | --- |
| Raw combat events | Show all | Show all | Show all |
| Combatant health updates | Show key changes | Show all | Show all |
| Combatant attribute updates | Show only important swings | Show most | Show all |
| Combatant detail rows | Hide noisy rows | Show key rows | Show all |
| Card attribute updates | Show only important rows | Show most | Show all |
| Card detail rows | Hide noisy rows | Show important rows | Show all |
| Reward rows | Show | Show | Show |
| System rows | Show | Show | Show |
| Unknown rows | Hide in Release | Show in Debug | Show in Debug |
| Debug metadata secondary text | Hide | Debug only | Debug only, full detail |

## Recommended Display Priority By Mode

| Mode | Priority Focus |
| --- | --- |
| `Release` | readable event flow, combatant results, only important card state changes |
| `Debug` | event causality, source and target identity, trigger chains, card ownership, and full per-frame detail when expanded |
