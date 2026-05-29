# BazaarPlusPlus Mod

In-game mod for *The Bazaar*. This glossary captures the project-specific vocabulary that recurs across features. General programming concepts are excluded.

## Run / encounters

**Encounter**:
A single stop on a run — a combat, PvP combat, event, shop, pedestal, loot, or level-up — that the player reaches and enters.
_Avoid_: node, map node

**Pedestal**:
An encounter that upgrades or enchants one of the player's existing items, rather than granting a new one. Whether the current Choice screen offers an upgrade pedestal, enchant pedestal, or neither is what drives the upgrade/enchant preview's "smart" mode.

**Encounter status probe**:
The on-demand, pull-based read of the player's *current* run/encounter state (`IEncounterStateProbe.GetCurrent()`). The project's chosen way to expose "where is the player in the run right now" — as a status query, not a recorded timeline.
_Avoid_: encounter tracker, run timeline (deliberately not built — see ADR)
