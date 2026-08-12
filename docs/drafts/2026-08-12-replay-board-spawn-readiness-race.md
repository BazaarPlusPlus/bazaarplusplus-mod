# Saved replay board-spawn readiness race

Status: implemented; approved for merge (2026-08-12)

## Observed problem

Saved replay playback can begin while one or more board items are still visually blank or still
being replaced by their loaded presentation. The 2026-08-12 screenshot shows combat already at
`00:00:50` while the lower board still contains unrendered item slots.

This has recurred after the item-setup readiness gate was introduced, so the next fix must close
the startup race rather than add an arbitrary delay.

## Verified native behavior

- `ReplayState.OnEnter` calls `SpawnCombatCards()` without awaiting the returned task, then
  completes state entry (`decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs:79-84`).
- `SpawnCombatCards()` awaits player and opponent `BoardManager.SpawnCards` calls
  (`decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs:112-125`).
- `BoardManager.SpawnCards` keeps `IsUpdatingBoard` true for the complete card-controller creation,
  item setup, skill presentation, and layout operation
  (`decompiled/TheBazaarRuntime/BoardManager.cs:837-954`).
- Card construction creates its object inactive, awaits `ItemController.Setup`, and only then
  activates it (`decompiled/TheBazaarRuntime/AssetLoader.cs:450-493`).
- The previous BPP gate ignored a pending setup whenever its controller was inactive, and checked
  board flags before checking setup tasks (see the pre-fix versions of
  `src/BazaarPlusPlus/Game/CombatReplay/Bootstrap/ReplayItemPresentationReadiness.cs` and
  `src/BazaarPlusPlus/Game/CombatReplay/Bootstrap/AppStateHandlerInstaller.cs`).

Therefore a render-boundary sample can observe no active pending item setup before the first
controller activates, even though the unawaited `SpawnCards` operation is still running. Playback
then calls `ReplayState.Replay()` and races the remaining board construction.

## Candidate approaches

1. **Track the complete native `ReplayState.SpawnCombatCards` task during saved-replay bootstrap.**
   This covers both sequential `BoardManager.SpawnCards` calls, controller instantiation before
   `ItemController.Setup` exists, every item setup, skill presentation, and final layout. Wait until
   every tracked spawn completes and a render boundary passes with no new spawn. This matches the
   exact unawaited native work boundary and is the selected implementation.
2. Count expected item models/controllers and wait for equality plus visual checks. Rejected as the
   primary fix because socket effects, duplicate/replaced controllers, and skills require policy
   that the native `SpawnCards` task already implements.
3. Increase `Task.Delay(50)` or add a fixed hold before `Replay()`. Rejected because asset latency is
   machine/cache dependent and this would only shrink the race.

## Failure and timeout policy

The gate retains the existing bounded five-second timeout. A load fault or timeout does not create
an unbounded startup hang or allow combat to begin on a partial board: it propagates through the
existing saved-replay start-failure rollback.

## Verification

- Unit-test the pure task tracker: a prespawn phase remains pending; a tracked spawn that appears
  after the first quiet sample is visible to the next sample; scope disposal resets state.
- Run the replay playback logging tests and the repository build.
- Replace the development DLL and replay the reported battle after restarting through Steam.
- Runtime acceptance: simulation time must remain at zero until all expected item faces on both
  boards have rendered; no blank item may fill in after the combat clock starts.
