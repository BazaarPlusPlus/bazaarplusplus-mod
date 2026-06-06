# Collection Panel First-Load Performance Plan

Status: Partially implemented (P0/P1/P2 shipped 2026-05-31 on `master`). The panel now paints a loading shell before catalog work, builds catalog metadata through a cancellable frame-budgeted coroutine, rejects known bad art keys, negative-caches failed panel-owned item art loads, and keeps pure VM catalog metadata across normal scene runtime disposal. P3 default-order precomputation, P4 persisted VM snapshot, and P5 opportunistic prewarm remain deferred until runtime timing justifies them.

Related spec: [2026-05-31-collection-panel-design.md](2026-05-31-collection-panel-design.md).

## Scope

This document targets the slow first visible load of the Collection Panel built under `Game/CollectionPanel/`.

Before this plan landed, the first-open path ran `EnsureView()`, catalog rebuild, filter, and view refresh synchronously from `CollectionPanel.Open()` before the normal frame loop could paint a loading state.

Current code shows the shell first and then starts `LoadPanelAsync(...)`: `Game/CollectionPanel/CollectionPanel.cs:555-560` and `Game/CollectionPanel/CollectionPanel.cs:573-686`.

Card realization after the panel is open is already virtualized and budgeted per frame, so this plan separates catalog/filter work from native card binding work instead of treating the panel as one monolithic load step: `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:106-165`.

This document does not propose replacing the native `CardPreviewBase` rendering path, because the Collection Panel deliberately reuses game-native card frames, art, and tooltip setup through `CardPreviewBase.SetUp(...)`: `decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs:64-85`.

## Advisor Review Record

Claude Advisor was requested for an adversarial review of the earlier plan, but it did not return a usable advisory in this session.

The advisor setup command succeeded and confirmed the local `claude` CLI plus auth, but two wrapper invocations and one direct review invocation timed out or stalled before producing review content.

The revised plan below therefore treats the advisor review as unavailable evidence and only keeps conclusions that are independently grounded in local source, decompiled game code, and the local runtime log.

## Current Load Shape

As implemented, `Open()` creates/activates the UITK shell and overlay, then calls `StartPanelLoad()` instead of synchronously rebuilding the catalog: `Game/CollectionPanel/CollectionPanel.cs:555-560`.

`LoadPanelAsync(...)` immediately publishes `CatalogLoading()`, clears the visible set, refreshes the view, yields one frame, then loads catalog metadata, applies filters, refreshes, and logs `CollectionPanelLoad` timing: `Game/CollectionPanel/CollectionPanel.cs:573-686`.

Catalog loading now first checks `CollectionCatalog.TryGetCached(...)`, then falls back to `CollectionCatalogBuildSession.Step(...)` with a per-frame budget. Cache identity is guarded by the static data manager object, and cache invalidation logs a reason: `Game/CollectionPanel/Data/CollectionCatalog.cs:17-43`, `Game/CollectionPanel/Data/CollectionCatalog.cs:45-119`, and `Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs`.

Scene changes call the Unity-runtime disposal path and leave the VM catalog intact; locale changes and `OnDestroy()` still invalidate it: `Game/CollectionPanel/CollectionPanel.cs:115-121`, `Game/CollectionPanel/CollectionPanel.cs:287-322`, and `Game/CollectionPanel/CollectionPanel.cs:634-638`.

Fresh runtime evidence from the implementation session:

```text
[BPP][CollectionCatalog] Catalog built: 1725 cards from 2888 templates, rejected=1163.
[BPP][CollectionPanelLoad] outcome=loaded, total=4797.5ms, catalog=3296.1ms, catalogCacheHit=false, sourceTemplates=2888, accepted=1725, rejected=1163, filter=2.7ms, refresh=0.9ms, catalogCards=1725, visibleCards=1105
[BPP][CollectionCatalog] Catalog cache hit: 1725 cards from 2888 templates.
[BPP][CollectionPanelLoad] outcome=loaded, total=11.8ms, catalog=0.0ms, catalogCacheHit=true, sourceTemplates=2888, accepted=1725, rejected=1163, filter=2.2ms, refresh=0.4ms, catalogCards=1725, visibleCards=1105
```

## Original Load Shape

The following facts describe the pre-implementation state that motivated this plan. Keep them as historical context, not current code truth.

`CollectionPanel.Open()` creates the UITK view and overlay before it tries to build the catalog, which means UI construction cost and data construction cost are both paid on the first open: `Game/CollectionPanel/CollectionPanel.cs:141-151`.

`EnsureView()` creates the `CollectionPanelView`, `CollectionGridOverlay`, art/material caches, card pool, card factory, and virtualizer in one block: `Game/CollectionPanel/CollectionPanel.cs:304-392`.

The original `RebuildCatalogIfPossible()` path only skipped work when `_catalogCards.Count > 0`, so a scene-change disposal that cleared `_catalogCards` forced a future open to rebuild the catalog.

Before P2, `DisposeRuntime()` disposed Unity-owned runtime objects and also cleared `_catalogCards` plus `CollectionCatalog._cache`, which coupled scene cleanup to catalog invalidation.

The original one-shot catalog build got the game's static data manager, called `JsonGameDataManager.GetCardMap()`, scanned every card template, filtered each template, and projected each accepted `TCardBase` into a `CollectionCardVm`. The current code keeps the same projection but routes it through `CollectionCatalog.TryCreateBuildSession(...)` and `CollectionCatalogBuildSession.Step(...)`: `Game/CollectionPanel/Data/CollectionCatalog.cs:45-110` and `Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs:33-66`.

The decompiled game manager stores cards in `_cards`, and `GetCardMap()` only returns that dictionary, so a BPP catalog snapshot cannot skip the game's static data creation step: `decompiled/TheBazaarRuntime/TheBazaar.DataManagement.Json/JsonGameDataManager.cs:29-67`.

`CollectionCardVm.From(...)` copies pure catalog/filter fields and resolves localized display text plus merchant classification, so the VM list is safe to keep independently of Unity card GameObjects: `Game/CollectionPanel/Data/CollectionCardVm.From.cs:11-27` and `Game/CollectionPanel/Data/CollectionCardVm.cs:14-25`.

`CollectionFilterEngine.Apply(...)` allocates a result list, scans the full catalog, and sorts the result every time it is called: `Game/CollectionPanel/Data/CollectionFilterEngine.cs:15-65`.

`CollectionGridVirtualizer.SetVisible(...)` consumes an already ordered visible list and rebuilds layout from that list, so ordering can move earlier into a cache or precomputed index without changing the virtualizer contract: `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:77-89`.

`CollectionGridVirtualizer.Tick()` rate-limits newly visible card binds with `CollectionGridConstants.ColdBindBudgetMs`, so cold native-card binding is already spread across frames: `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:155-165` and `Game/CollectionPanel/Grid/CollectionGridConstants.cs:42-44`.

`CollectionCardFactory.TryBind(...)` still performs a static template lookup, pool take, synthetic instance construction, and reflected `CardPreviewBase.SetUp(...)` for each realized card: `Game/CollectionPanel/Grid/CollectionCardFactory.cs:37-66`.

`CardPreviewBase.SetUp(...)` constructs tooltip data and then awaits both frame loading and art loading, so the first visible cells can be delayed by game Addressables work even if catalog construction is fast: `decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs:82-85`.

## Important Findings

The original idea that game-version content is stable is directionally right for BPP catalog metadata, because the card map is loaded into a static `JsonGameDataManager` and exposed as an in-memory dictionary: `decompiled/TheBazaarRuntime/TheBazaar.DataManagement.Json/JsonGameDataManager.cs:45-67`.

A persisted BPP VM snapshot is not the first optimization to implement, because it cannot skip `Data.CreateManager()` or `JsonGameDataManager.Create()` and only replaces BPP's projection/sort work after the game manager already exists: `decompiled/TheBazaarRuntime/TheBazaar/Data.cs:503-516` and `Game/CollectionPanel/Data/CollectionCatalog.cs:64-75`.

Before P2, the in-memory catalog cache was useful but short-lived because scene changes invalidated it even though the cached VMs do not own Unity resources. Scene changes now call `DisposeUnityRuntime()` and keep the VM catalog intact: `Game/CollectionPanel/CollectionPanel.cs:282-322` and `Game/CollectionPanel/Data/CollectionCatalog.cs:17-43`.

Locale changes still must invalidate the catalog, because `DisplayName` is resolved from the live card localization text when the VM is created: `Game/CollectionPanel/CollectionPanel.cs:91-102` and `Game/CollectionPanel/Data/CollectionLocalizationResolver.cs:16-31`.

The Item art path is more expensive and riskier than the Skill art path because `CardPreviewItem.LoadArt(...)` directly calls `Addressables.LoadAssetAsync<CardAssetDataSO>(_cardData.ArtKey)`, while `CardPreviewSkill.LoadArt(...)` goes through `AssetLoader.LoadAssetAsyncByAddress<Texture>(...)`: `decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs:80-95` and `decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewSkill.cs:14-22`.

The Collection Panel patch already replaces Item art loading for panel-owned cards, but `CollectionCardArtCache.Get(...)` only caches successful handles and does not remember failed art keys: `src/BazaarPlusPlus/Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:21-34` and `Game/CollectionPanel/Grid/CollectionCardArtCache.cs:39-102`.

Before P1, the catalog classifier only rejected non Item/Skill cards, empty art keys, `"Invalid"` art keys, and internal names containing `[DEBUG]` or `[TEMPLATE]`.

The current classifier rejects `"Placeholder"` art keys, `.mat` art keys, and template-name markers before those entries reach the panel: `Game/CollectionPanel/Data/CollectionCardClassifier.cs:14-15` and `Game/CollectionPanel/Data/CollectionCardClassifier.cs:105-109`.

The pre-fix runtime log from 2026-05-31 showed `CollectionCatalog` building `1734` cards from `2888` templates and then repeated `CollectionCardArtCache` failures for `artKey='Placeholder'`. The post-fix runtime log built `1725` cards from the same `2888` templates with placeholder/template rejection counted in the catalog result.

## Revised Priority Order

### P0: Add Load Instrumentation

Status: Partially landed.

The shipped instrumentation records `EnsureView()`, catalog, filter, refresh, cache-hit/source counts, accepted/rejected counts, catalog card count, and visible card count under `[BPP][CollectionPanelLoad]`. It does not yet separately time the first-window native card bind/art path, so that remains optional future instrumentation if logs show native realization is still the user-visible delay.

Add a small, removable or debug-level timing helper around the first-open path before changing behavior.

The remaining instrumentation gap is the first several `CollectionCardFactory.TryBind(...)` calls and any art loads they trigger; shipped logs already cover `EnsureView()`, the catalog build session, filter pass, refresh pass, cache-hit state, source counts, and visible count: `Game/CollectionPanel/CollectionPanel.cs:445-517` and `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:155-165`.

Log catalog source counts, accepted counts, rejected placeholder/template counts, filter result count, and first-window bind count because those values decide whether the fix should focus on data projection or native card binding: `Game/CollectionPanel/Data/CollectionCatalog.cs:64-79` and `Game/CollectionPanel/Data/CollectionFilterEngine.cs:20-65`.

Keep these logs under the existing BPP log style and component names because runtime debugging depends on `[BPP][<Component>]` log prefixes: `Infrastructure/BppLog.cs` and `Game/CollectionPanel/Data/CollectionCatalog.cs:76-79`.

Acceptance for P0 is one log block that can distinguish catalog build time, filter/sort time, and first-window bind/art time on a cold panel open.

### P1: Harden Catalog Rejection And Art Failure Caching

Status: Landed.

`CollectionCardClassifier.HasValidArtKey(...)` now rejects `Placeholder` art keys and `.mat` art keys. The existing template-name marker is broad enough to catch `[SMALL ITEM TEMPLATE]` because it matches the substring `TEMPLATE`. `CollectionCardArtCache` now keeps a per-cache-lifetime `_failedKeys` set for cache-owned failed Addressables loads and clears it in `DisposeAll()`.

Extend `CollectionCardClassifier.HasValidArtKey(...)` so `"Placeholder"` and legacy material keys ending in `.mat` are not treated as valid catalog art: `Game/CollectionPanel/Data/CollectionCardClassifier.cs:105-107`.

Generalize template-name rejection so names like `[SMALL ITEM TEMPLATE]`, `[MEDIUM ITEM TEMPLATE]`, `[LARGE ITEM TEMPLATE]`, and `[SKILL TEMPLATE]` are filtered even though they do not contain the exact substring `[TEMPLATE]`: `Game/CollectionPanel/Data/CollectionCardClassifier.cs:14-15`.

Add a failed-key set inside `CollectionCardArtCache` so a failed `Addressables.LoadAssetAsync<CardAssetDataSO>(artKey)` does not retry on every later bind for the same art key during the same panel cache lifetime: `Game/CollectionPanel/Grid/CollectionCardArtCache.cs:39-80`.

Only negative-cache definite Addressables failures and exceptions from the cache-owned load path, because the patch is scoped to panel-owned cards through `CollectionPanelOwnedMarker`: `src/BazaarPlusPlus/Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:24-34` and `Game/CollectionPanel/Grid/CollectionPanelOwnedMarker.cs:6-16`.

Do not persist failed art keys across process runs because Addressables catalog content can change with a game update while the BPP cache file would not own that invalidation contract: `decompiled/TheBazaarRuntime/TheBazaar.DataManagement/DataManifestType.cs:18-26`.

Acceptance for P1 is that fresh runtime logs no longer show repeated failures for `artKey='Placeholder'`, and classifier tests prove the template/placeholder rules.

### P2: Keep The VM Catalog Across Scene Runtime Disposal

Status: Landed.

`DisposeRuntime()` is split into Unity runtime disposal plus explicit catalog invalidation. Scene transitions call only `DisposeUnityRuntime()`, while locale changes and object destruction invalidate the VM catalog. `CollectionCatalog` also checks static-data manager identity before serving a cache hit.

Split Unity runtime disposal from catalog invalidation so scene changes tear down card GameObjects, overlay Canvas, and art/material caches without discarding `CollectionCardVm` metadata: `Game/CollectionPanel/CollectionPanel.cs:282-302`.

Keep invalidation on locale changes because `CollectionCardVm.DisplayName` is computed once during VM creation: `Game/CollectionPanel/CollectionPanel.cs:91-102` and `Game/CollectionPanel/Data/CollectionCardVm.From.cs:21-23`.

Guard the cache by static-data manager identity or an equivalent source token so a forced game data manager recreation does not leave BPP with stale VMs: `GameInterop/StaticCards/BppStaticDataAccess.cs:13-19` and `decompiled/TheBazaarRuntime/TheBazaar/Data.cs:503-516`.

Keep art/material caches scene-bound because they hold Unity/Addressables objects and already have explicit `DisposeAll()` lifecycles: `Game/CollectionPanel/Grid/CollectionCardArtCache.cs:120-139` and `Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs:53-71`.

Acceptance for P2 is that opening the panel after a non-locale scene transition logs a catalog cache hit instead of `Catalog built`, while card pool and overlay still reset cleanly.

### P3: Precompute Default Ordering Only If Measurement Justifies It

Status: Deferred.

The fresh runtime log showed `filter=2.7ms` cold and `filter=2.2ms` on cache-hit reopen, so this is not currently the first-load bottleneck.

Revalidate this deferred decision if the catalog exceeds roughly 3000 accepted cards, a profiling run shows filter/sort above 10ms on target hardware, or the next CollectionPanel performance session records filter time as a visible share of first-open latency.

If P0 shows `CollectionFilterEngine.Apply(...)` is a meaningful share of first-open cost, add precomputed default Item and Skill ordered lists at catalog build time: `Game/CollectionPanel/Data/CollectionFilterEngine.cs:52-65`.

For the default path, return the precomputed active-type list when `IncludePackages == false`, no hero/tier/size/merchant filters are selected, and search is empty: `Game/CollectionPanel/Data/CollectionFilterState.cs:11-20`.

For filtered paths, scan the already sorted active-type list and keep order instead of sorting the filtered result again when the sort key is unchanged: `Game/CollectionPanel/Data/CollectionFilterEngine.cs:29-65`.

Do not introduce source-text tests for this optimization; the meaningful seam is output ordering and inclusion/exclusion behavior in `tests/CollectionFilterEngine.Tests`: `tests/CollectionFilterEngine.Tests/Program.cs:7-79`.

Acceptance for P3 is that default first-open avoids a full result sort and existing filter behavior remains unchanged in the filter-engine test project.

### P4: Add A Persisted Snapshot Only After P0-P3 Show Remaining Catalog Cost

Status: Deferred.

Cold process catalog projection still showed a meaningful catalog segment (`catalog=3296.1ms`) even after the loading shell and frame-budgeted build, so this remains the next candidate if product goals require faster cold-process first content, but it has not been implemented.

Persisting `CollectionCardVm` snapshots should be treated as a second-phase optimization, not the first patch.

A persisted snapshot must include a BPP snapshot schema version, classifier version, locale mode, active game version, and game data manifest or DB fingerprint because every one of those inputs can change the VM list or display names: `Game/CollectionPanel/Data/CollectionCardVm.From.cs:21-26`, `Game/CollectionPanel/Data/CollectionCardClassifier.cs:44-63`, and `decompiled/TheBazaarRuntime/TheBazaar.AppFramework/AppLoader.cs:86-89`.

The snapshot should live under the BPP data root, following the existing `BepInExPathProvider` convention for `BazaarPlusPlusV4`: `Core/Paths/BepInExPathProvider.cs:20-40`.

On a snapshot hit, the panel still must wait until `BppStaticDataAccess.TryGet()` succeeds because card binding still needs live game templates by GUID: `GameInterop/StaticCards/BppStaticDataAccess.cs:13-19` and `Game/CollectionPanel/Grid/CollectionCardFactory.cs:42-47`.

On a snapshot miss or parse failure, the panel should fall back to the live `CollectionCatalog.TryCreateBuildSession(...)` / `CollectionCatalogBuildSession.Step(...)` path and rewrite the snapshot after a successful build: `Game/CollectionPanel/Data/CollectionCatalog.cs:45-110` and `Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs:33-66`.

Acceptance for P4 is that a second process launch on the same game data and locale loads VM metadata from disk without changing visible card count or filter results.

### P5: Opportunistic Prewarm

Status: Deferred.

No catalog or prefab-reference prewarm has been implemented. The current path remains lazy and succeeds without prewarm.

Catalog prewarm can run after the static data manager is ready, but it must not create Unity card instances or Addressables handles outside the panel lifecycle: `GameInterop/StaticCards/BppStaticDataAccess.cs:13-19` and `Game/CollectionPanel/CollectionPanel.cs:385-392`.

Prefab-reference prewarm is only useful after `MonsterBoardTooltip` prefab references exist, because `CollectionCardPool.TryEnsurePrefabRefs()` searches `Resources.FindObjectsOfTypeAll(...)`: `Game/CollectionPanel/Grid/CollectionCardPool.cs:93-148`.

Do not make prewarm a hard dependency of opening the panel because `TryEnsurePrefabRefs()` can return false when the tooltip host is not available yet: `Game/CollectionPanel/Grid/CollectionCardPool.cs:116-149`.

Acceptance for P5 is that a prewarmed open is faster in logs but a non-prewarmed open still succeeds through the current lazy path.

## Implementation Notes

P0 and P1 are safe to implement together if the instrumentation is committed as normal debug/diagnostic logging rather than temporary noisy logs.

P2 landed by separating `DisposeRuntime()` into a Unity-runtime disposal path and a catalog invalidation path, rather than by simply deleting cache invalidation: `Game/CollectionPanel/CollectionPanel.cs:300-322` and `Game/CollectionPanel/CollectionPanel.cs:634-638`.

P2 added clear log lines for catalog cache hit, catalog built, cache invalidation, and static-data-source invalidation because otherwise later performance regressions would be hard to attribute: `Game/CollectionPanel/Data/CollectionCatalog.cs:17-43` and `Game/CollectionPanel/Data/CollectionCatalog.cs:106-119`.

P3 should avoid exposing mutable cached lists to callers that might sort or modify them, because `CollectionGridVirtualizer.SetVisible(...)` stores the list reference and reads it later during `Tick()`: `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:79-89` and `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:312-345`.

P4 should not serialize Unity objects, `TCardBase`, `TCardInstance`, Addressables handles, or `Material` objects; only serialize the pure fields already present on `CollectionCardVm`: `Game/CollectionPanel/Data/CollectionCardVm.cs:14-25`.

## Verification Plan

Run `dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj` after changing classifier or filter behavior, because that project already compiles `CollectionCardClassifier`, `CollectionFilterEngine`, and `CollectionFilterState`: `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj:22-46`.

Add classifier assertions for `"Placeholder"`, `.mat`, and `[SMALL ITEM TEMPLATE]` so the catalog hardening has a pure test seam: `tests/CollectionFilterEngine.Tests/Program.cs:46-65`.

Run `dotnet build BazaarPlusPlus.csproj --no-restore` after code changes because the main plugin must compile against the publicized game assemblies and the Unity/BepInEx references: `BazaarPlusPlus.csproj:127-169`.

In game, open the Collection Panel from a cold session and capture the new P0 timing logs from `<GameDir>/BepInEx/LogOutput.log`, because BepInEx runtime logs are the source for panel startup timing.

In game, close and reopen the panel in the same scene, then transition to another non-combat scene and reopen, because this distinguishes view/runtime reuse from catalog metadata reuse: `Game/CollectionPanel/CollectionPanel.cs:264-272`.

In game, switch the BPP Chinese locale mode and reopen the panel, because locale changes must still force catalog rebuild and refreshed display names: `Game/CollectionPanel/CollectionPanelMount.cs:25-27` and `Game/CollectionPanel/CollectionPanel.cs:91-102`.

Verify hover, wheel scroll, click-miss, and tooltip behavior after any overlay or prewarm change, because prior Collection Panel regressions came from overlay/UI Toolkit input layering rather than catalog data logic: `Game/CollectionPanel/Grid/CollectionGridConstants.cs:46-64` and `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:196-291`.

## Rollout Criteria

P0/P1/P2 have shipped.

Do not ship P3 unless logs show filter/sort time becoming visible again.

Consider P4 if cold process launch still needs faster first content after accepting the complexity of snapshot schema/version/locale/game-data invalidation.

Consider P5 only if runtime logs prove prewarm improves real open latency without making panel open depend on tooltip-prefab availability.

Do not claim first-load performance is fixed from build/test success alone; the acceptance signal is runtime timing in `BepInEx/LogOutput.log` plus manual in-game panel interaction checks.
