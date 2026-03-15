# Monster Preview First-Open Lag Analysis (2026-03-14)

## Summary

The current `MonsterPreview` first-open hitch is most likely a cold-start aggregation problem, not a single isolated slow call.

When the preview is opened for the first time in a run, several lazy paths are activated at once:

1. local preview filtering loads and parses `cards.json`
2. monster attribute expansion loads and parses `cards.json` again
3. preview card factories call `Data.GetStatic()` on first use
4. preview card creation enters the game's `AssetLoader.InstantiateCardAsync(...)`
5. spawned item and skill controllers perform their own first-time frame, art, material, and icon setup

Once those assets and data are hot, later preview opens still rebuild card objects, but the expensive cold-start work is mostly gone. That matches the observed behavior: first open feels sticky, later opens feel smooth.

## Investigation Goal

The question was not "why is MonsterPreview always slow", but specifically:

- why the first trigger is noticeably more expensive
- why later triggers are much smoother

That distinction matters, because it points toward lazy initialization, file parsing, and asset warmup instead of steady-state layout work.

## Investigation Path

The investigation followed the runtime path from trigger to card instantiation:

`MonsterLockShowcaseRuntime`
-> `MonsterPreviewController`
-> `MonsterPreviewOverlayCoordinator`
-> `PreviewBoardSession`
-> `MonsterPreviewBoardRenderTarget`
-> `MonsterPreviewBoard`
-> `MonsterPreviewItemCardFactory` / `MonsterPreviewSkillCardFactory`

Then each cold path was checked for one-time work:

- board creation timing
- preview model construction timing
- local template filtering
- monster attribute loading
- static data loading
- card instantiation
- per-card visual setup

## What Is Not The Main Cause

### Board shell creation is not the first-open bottleneck

`MonsterPreviewController.Awake()` constructs `MonsterPreviewBoardRenderTarget`, and that immediately creates `MonsterPreviewBoard`.

That means the base board shell, borders, slot objects, and text meshes are already created before the first preview is shown.

Relevant files:

- `Game/MonsterPreview/MonsterPreviewController.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`

Implication:

- the first-open hitch is not primarily caused by creating the board root itself

### Monster database loading is also not the first-open bottleneck

`MonsterDatabase.Load()` is called in plugin `Awake()`, not on first preview open.

Relevant files:

- `Plugin.cs`
- `Data/MonsterDatabase.cs`

Implication:

- the embedded monster DB load is paid during plugin startup, not when the user first opens a preview

## Main Cold Paths

### 1. `cards.json` is parsed lazily during the first preview

The first preview build can trigger `PreviewCardSpecFilter.FilterLocallyRenderable(...)`, which depends on `LocalCardTemplateCatalog.Contains(...)`.

`LocalCardTemplateCatalog` is a `Lazy<HashSet<Guid>>` and loads by:

- reading the full `cards.json`
- parsing it into a `JObject`
- scanning all template IDs

Relevant files:

- `Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs`
- `Data/LocalCardTemplateCatalog.cs`

Why this matters:

- this cost is naturally paid only on the first call
- after that, template-ID membership checks are fast

### 2. `cards.json` can be parsed a second time for monster attributes

If the preview path comes from `MonsterDatabasePreviewDataSource.BuildModel(...)`, it expands card or skill attributes through `ItemAttr.GetAttributes(...)`.

`ItemAttr` also uses a lazy cache, but its cache is separate from `LocalCardTemplateCatalog`. On first use it:

- reads the same `cards.json`
- parses it again
- builds a dictionary of attributes by template and tier

Relevant files:

- `Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs`
- `Data/ItemAttr.cs`

Why this matters:

- the first preview may pay two independent full-file parse costs for the same source file
- this is exactly the kind of one-time hitch that disappears once warm

### 3. `Data.GetStatic()` is deferred to the first preview card creation

Both preview card factories cache `_staticData`, but only after first use.

Relevant files:

- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs`

Why this matters:

- the first preview card creation can block on the game's static-data initialization path
- later preview opens reuse the cached `_staticData`

### 4. Card instantiation is serial and enters the game's cold asset path

`MonsterPreviewBoard.RebuildAsync(...)` clears the board and then creates preview cards one by one:

- item cards first
- skill cards second
- each creation is awaited before the next one starts

Relevant file:

- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`

Each created preview card then goes through `AssetLoader.InstantiateCardAsync(...)`.

Relevant file:

- `decompiled/TheBazaarRuntime/AssetLoader.cs`

Why this matters:

- the first item or skill of each type can trigger addressable loads, prefab activation, and internal game-side initialization
- serial awaits amplify the perceived hitch because multiple cold misses stack instead of overlapping

### 5. Spawned controllers do additional first-time visual work

After instantiation, preview factories explicitly refresh spawned item and skill objects:

- `ItemController.Setup(card)`
- `SkillController.Setup(iconArtKey, card, false)`

Those paths load or instantiate:

- frames
- card backs
- art textures
- icon textures
- materials and related visuals

Relevant files:

- `decompiled/TheBazaarRuntime/ItemController.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs`
- `decompiled/TheBazaarRuntime/SkillController.cs`

Why this matters:

- this is exactly the kind of work that feels bad only on the first hit
- once the underlying assets are cached, later setup calls are much cheaper

## Current Best Root-Cause Statement

The first-open lag is caused by several independent lazy systems warming up on the same user action:

1. local template catalog warmup
2. attribute cache warmup
3. static game data warmup
4. first real card prefab instantiation
5. first real art/frame/icon/material setup

The board layout code contributes some cost, but it does not explain the strong difference between first-open and warm-open behavior.

## Nullability And Lazy-Load Risk Analysis

The lazy paths do introduce correctness risk, but the main risk is not a classic null-pointer dereference.

The code today is more likely to:

- cache an empty result too early
- cache and repeatedly rethrow an initialization exception
- silently degrade into an empty preview

than to directly crash on a `null` read.

### `Lazy<T>` itself is not the problem

`LocalCardTemplateCatalog` and `ItemAttr` both use `Lazy<T>` with the default constructor:

- `LocalCardTemplateCatalog.TemplateIds`
- `ItemAttr.CardsByTemplateId`

With the default .NET behavior, this is thread-safe `ExecutionAndPublication`.

Implication:

- concurrent first access should not create two partially initialized caches
- a half-built cache from racing threads is not the likely failure mode

### `LocalCardTemplateCatalog` is null-safe, but can cache the wrong empty state

`LocalCardTemplateCatalog.LoadTemplateIds()` explicitly checks:

- `string.IsNullOrWhiteSpace(path)`
- `File.Exists(path)`

and returns an empty `HashSet<Guid>` if either check fails.

Implication:

- this path is not likely to throw a null-reference exception
- but if it is first touched before `ModState.CardsJsonPath` is ready, it will permanently cache an empty template catalog for the process lifetime

That is a lazy-load timing bug, not a null-pointer bug.

Practical effect:

- `PreviewCardSpecFilter.FilterLocallyRenderable(...)` can start rejecting every card
- the preview can appear unavailable or blank even though data would become valid later

### `ItemAttr` is also null-safe, but has the same stale-empty-cache risk

`ItemAttr.LoadCards()` also checks for a blank or missing `CardsJsonPath` and returns an empty dictionary.

Implication:

- this path is also not likely to produce a null-reference exception on its own
- but a too-early first access can permanently cache "no attributes available"

Practical effect:

- preview cards can still render
- but attributes may stay empty for the rest of the process even after the real file path becomes available

### Both lazy JSON loaders can poison themselves with cached exceptions

Neither `LocalCardTemplateCatalog.LoadTemplateIds()` nor `ItemAttr.LoadCards()` wraps:

- `File.ReadAllText(...)`
- `JObject.Parse(...)`

in a try/catch.

Implication:

- malformed JSON, transient read failure, or unexpected content can throw during the first lazy evaluation
- `Lazy<T>` will cache that exception
- every later access will rethrow the same failure

This is worse than returning empty data, because the lazy cache becomes permanently faulted until restart.

In the current preview path, those exceptions are not locally contained near the call sites:

- `PreviewCardSpecFilter.FilterLocallyRenderable(...)`
- `MonsterDatabasePreviewDataSource.BuildModel(...)`
- `MonsterLockShowcaseRuntime.TryBuildPreview(...)`

So a first lazy-load failure can break the whole preview path.

### `Data.GetStatic()` is mostly null-guarded, but not exception-guarded

The item and skill factories both do:

- `if (_staticData == null) _staticData = await Data.GetStatic();`
- then explicitly return `null` if `_staticData` is still `null`

Implication:

- a plain `null` result from `Data.GetStatic()` does not look like an immediate null-pointer risk in these factories
- card creation degrades into "return null" instead of dereferencing `_staticData`

But there is still a correctness gap:

- exceptions from `Data.GetStatic()` are not caught here
- those exceptions would escape `CreateCardAsync(...)`
- `MonsterPreviewBoardRenderTarget.Render(...)` fires `RebuildAsync(...)` without awaiting it

So the likely failure mode is:

- async task failure or logged exception
- partially built or empty preview

not a deterministic local null-reference at the `_staticData` read site

### Preview session and request assembly are mostly defensive against null

The request and render path already checks several nullable inputs:

- `PreviewBoardSession.ResolveModel(...)`
- `PreviewBoardSession.ResolvePose(...)`
- `MonsterLockShowcaseRuntime.CreateShowcaseRequest(...)`
- `InMemoryPreviewDataSource.TryBuild(...)`

Implication:

- the current architecture tends to normalize missing data into empty models rather than dereferencing null blindly
- this further reduces the chance that lazy loading specifically causes a direct null-pointer exception

## Most Likely Failure Modes From Lazy Loading

Based on the current code, the most realistic outcomes are:

1. first access happens too early and caches an empty result forever
2. first access throws and `Lazy<T>` caches the exception forever
3. `Data.GetStatic()` throws during first preview card creation and the rebuild task faults
4. preview rendering silently degrades into no cards or missing attributes

The least likely outcome is:

- a direct null-reference exception caused only by `LocalCardTemplateCatalog` or `ItemAttr` lazy loading

## Recommended Guardrails For Lazy Paths

### Guardrail 1: do not cache "path unavailable" as final state

For `LocalCardTemplateCatalog` and `ItemAttr`, prefer one of these:

1. replace `Lazy<T>` with an explicit reloadable cache
2. keep `Lazy<T>` but only initialize after `ModState.CardsJsonPath` is known valid
3. expose a reset method for tests and runtime recovery if initialization was attempted too early

This avoids permanently caching empty data because initialization order was wrong.

### Guardrail 2: catch and log JSON load failures inside the lazy initializer

Wrap file read and parse inside try/catch and return an empty cache plus a log entry.

This is not ideal for correctness, but it is much safer than faulting the `Lazy<T>` forever.

### Guardrail 3: explicitly distinguish "empty because valid" from "empty because failed"

Add internal state such as:

- not initialized
- initialized successfully
- initialization failed

That makes logs and recovery behavior far easier to reason about.

### Guardrail 4: catch `Data.GetStatic()` failures at the preview factory boundary

If `Data.GetStatic()` throws, log once with context and fail the specific preview card gracefully.

This keeps a first-time static-data failure from turning into a harder-to-debug async task fault.

## Why The Warm Path Feels Good

After the first preview:

- `LocalCardTemplateCatalog` is already loaded
- `ItemAttr` is already loaded
- preview factories already hold `_staticData`
- the game's asset loader has already touched some relevant prefabs and assets
- frame/icon/material paths are no longer fully cold

So later opens still rebuild preview content, but mostly on top of hot caches.

That is why the feature can feel "very happy" once resources are warm even though the code still recreates cards.

## Prioritized Optimization Plan

### Phase 1: Move cold work off the first user-visible open

Goal:

- keep behavior unchanged
- reduce first-open hitch with minimal feature risk

Actions:

1. warm `LocalCardTemplateCatalog` proactively
2. warm `ItemAttr` proactively
3. warm `Data.GetStatic()` proactively

Recommended timing:

- after plugin startup if safe
- or after entering a run
- or on a delayed background-style task after scene stabilization

Expected result:

- the first visible preview open no longer pays file parsing and static-data startup all at once

### Phase 2: Add lightweight preview asset warmup

Goal:

- reduce first-hit asset instantiation spikes

Actions:

1. preload one representative item preview card
2. preload one representative skill preview card
3. optionally warm common frames or token prefabs only

Constraints:

- keep the warmup set intentionally small
- avoid warming a large card population

Expected result:

- first real preview open is less likely to pay the worst addressable and prefab startup cost

### Phase 3: Reduce rebuild churn

Goal:

- improve steady-state cost and reduce repeated object churn

Actions:

1. consider pooling preview card objects instead of destroy-and-recreate
2. consider refreshing existing spawned cards when the signature is compatible
3. avoid unnecessary full board clears if only pose or presentation changed

Expected result:

- smaller GC pressure
- smoother repeated toggles even beyond the first-open problem

This phase is useful, but it is not the first thing to do if the target is specifically first-open hitch reduction.

## Recommended Order

The recommended implementation order is:

1. proactive warmup for `LocalCardTemplateCatalog`
2. proactive warmup for `ItemAttr`
3. proactive warmup for `Data.GetStatic()`
4. minimal asset warmup for one item and one skill preview
5. measure again
6. only then decide whether card pooling is still worth the complexity

Reason:

- the first three items directly target the strongest cold-path signals already visible in the code
- they are lower risk than changing preview card lifetime management

## Suggested Validation Plan

### Validation 1: Add timing around each cold stage

Instrument these points:

- `PreviewCardSpecFilter.FilterLocallyRenderable(...)`
- `LocalCardTemplateCatalog.LoadTemplateIds()`
- `ItemAttr.LoadCards()`
- first `Data.GetStatic()`
- `MonsterPreviewBoard.RebuildAsync(...)`
- each `CreateCardAsync(...)`
- each `InstantiateCardAsync(...)` call boundary

Expected outcome:

- we can separate file-parse cost from asset-instantiation cost instead of guessing

### Validation 2: Compare cold run and warm run

Measure:

1. first preview open after game start
2. second preview open immediately after
3. first preview open after applying warmup

Expected outcome:

- cold-to-warm delta should shrink significantly if the hypothesis is correct

### Validation 3: Confirm no user-visible startup regression

If warmup is moved earlier, verify:

- plugin startup does not become unacceptably slower
- entering a run does not hitch harder than before

The purpose is to shift the hitch away from first open, not simply relocate it to a worse moment.

## Practical Next Step

The lowest-risk next implementation is:

1. add optional timing logs
2. add a small warmup routine for `LocalCardTemplateCatalog`, `ItemAttr`, and `Data.GetStatic()`
3. re-measure first-open preview latency

If that already removes most of the hitch, stop there before changing preview object lifecycle.

## Conclusion

The current evidence supports a simple interpretation:

- `MonsterPreview` first-open lag is a cold-start aggregation issue
- the strongest local causes are two lazy `cards.json` parses plus deferred static-data and asset initialization
- the feature feels smooth when warm because those costs are mostly one-time

The implementation plan should therefore start with targeted warmup and measurement, not with large structural changes.
