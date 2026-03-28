# Random Hero Pool Progress

Date: 2026-03-28

## Scope

This progress note tracks the current implementation status of the Hero Select random-pool feature described in `docs/plans/2026-03-28-random-hero-pool-implementation.md`.

## Implemented

- Added a pure state model in `Game/Lobby/RandomHeroPool/RandomHeroPoolState.cs`.
- Added state construction helpers in `Game/Lobby/RandomHeroPool/RandomHeroPoolStateFactory.cs`.
- Added preference merge helpers in `Game/Lobby/RandomHeroPool/RandomHeroPoolPreferences.cs`.
- Added a focused test project in `tests/RandomHeroPoolState.Tests/`.
- Added Hero Select UI injection in `Patches/Lobby/RandomHeroPoolPatches.cs`.
- Added `RandomHeroPoolPanelController` in `Game/Lobby/RandomHeroPool/RandomHeroPoolPanelController.cs`.
- The injected popup currently:
  - clones native toggle/button visuals for a matching look
  - adds a `Pool` button beside the native random toggle
  - shows only unlocked heroes
  - supports per-hero toggles
  - supports `Select All`
  - supports `Clear` while preserving at least one selected hero
  - persists the selected pool through account-scoped `PlayerPrefs`
  - auto-merges newly unlocked heroes into the saved pool by default
- Added `RandomHeroPoolSelector` in `Game/Lobby/RandomHeroPool/RandomHeroPoolSelector.cs` as the pure selector primitive for the next routing step.

## Verified

- `dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj`
- `dotnet build BazaarPlusPlus.csproj`

Both commands passed on 2026-03-28 after the current changes.

## Not Yet Implemented

- The game start flow still uses the original native `HeroSelectButtonsView.SelectRandomHeroImmediate()` behavior.
- The configured subset is not yet wired into the actual random hero pick at run start.
- No manual in-game end-to-end verification has been recorded yet for:
  - popup placement and polish in the live scene
  - persistence across full game restart
  - random start honoring the configured subset

## Next Step

Patch the random hero selection entrypoint so `SelectRandomHeroImmediate()` uses the saved effective pool instead of the full unlocked hero list, while preserving the original `_isProgrammaticSelection` behavior and fallback semantics.
