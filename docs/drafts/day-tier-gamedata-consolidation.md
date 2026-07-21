# Day-tier GameData consolidation note

Issue #130 removed the hardcoded `DayTierSchedule`. The corresponding gotcha in
`docs/MEMORY.md` is now historical and should be removed by the next consolidation run; do not
restore it as a fallback.

Verified replacement knowledge:

- The game replaces the `JsonGameDataManager` reference after a GameData download or forced
  recreation (`decompiled/TheBazaarRuntime/TheBazaar/Data.cs:502-523`). Manager reference identity
  is therefore the day-tier cache generation boundary.
- A game mode owns the day-keyed item/skill tier weights, and its runtime tier tables are built from
  that map (`decompiled/BazaarGameShared/BazaarGameShared.Domain.Game/TGameMode.cs:19-22,68-77`).
- The shared adapter lives under `src/BazaarPlusPlus/GameInterop/DayTiers/`. It normalizes positive
  finite weights and defines `MaximumTier` as the highest usable Bronze-to-Diamond tier, not the
  largest probability.
- Event Preview supplies its published plan manager as the expected generation and consumes one
  resolved table per query. Collection consumes the same resolver and fails open whenever the
  table is not available; it keeps fixed-tier source exemption, manual Tier AND semantics, and the
  Legendary-to-Diamond compatibility rule.

Consolidation action: replace the obsolete `DayTierSchedule` memory gotcha with the shared
GameInterop ownership and generation-safe cache rule, linking the current source paths above.
