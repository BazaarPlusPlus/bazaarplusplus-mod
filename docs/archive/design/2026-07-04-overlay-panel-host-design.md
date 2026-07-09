---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at a3690e9d, merged e882dec1 (2026-07-04): OverlayPanelHost owns main-overlay-panel lifecycle (mutual exclusion, scene policy, combat gating, hotkey/escape routing, per-frame tick); CollectionPanel/HistoryPanel/LiveBuildPanel all register through it. Tests: tests/OverlayPanelLifecycle.Tests.

# Overlay Panel Host — design

Status: revised after red-team review (2026-07-04), pending user confirmation.
Origin: architecture review 2026-07-04, candidate 1 ("One overlay host"). Vocabulary: `CONTEXT.md` §Overlay panels.

## Background

Three Main Overlay Panels (CollectionPanel, HistoryPanel, LiveBuildPanel) each re-implement the same lifecycle around a shallow `IBppOverlayPanel` seam:

| Concern | Collection | LiveBuild | History |
| --- | --- | --- | --- |
| Register/unregister (mount-time init, not uniformly Awake) | `CollectionPanel.cs:118-134` (`Initialize`) / `:459` | `LiveBuildPanel.cs:55-67` (`Awake`) / `:78` | `HistoryPanel.cs:479-496` (`EnsureInitialized`) / `:160` |
| `CloseOthers` on open | `CollectionPanel.cs:271` | `LiveBuildPanel.cs:137` | `HistoryPanel.cs:209` |
| Combat open suppression | `CollectionPanel.cs:265-269` | `LiveBuildPanel.cs:131-135` | `HistoryPanel.cs:299-302` |
| Combat auto-close in Update | `CollectionPanel.cs:344-348` | `LiveBuildPanel.cs:89-93` | `HistoryPanel.cs:171-175` |
| Scene-token detect | `CollectionPanel.cs:444-453` | `LiveBuildPanel.cs:554-563` | `HistoryPanel.cs:498-512` |
| Hotkey toggle | `CollectionPanel.cs:350-360` | `LiveBuildPanel.cs:95-105` | `HistoryPanel.cs:180-187` |
| Escape close | `CollectionPanel.cs:383-387` | `LiveBuildPanel.cs:110-114` | `HistoryPanel.cs:196-200` |
| `GetSceneToken` helper (verbatim copy) | `CollectionPanel.cs` | `LiveBuildPanel.cs:580-581` | `HistoryPanel.cs:524-527` |

`BppOverlayPanelMutex` (65 lines) + `BppOverlayPanelRegistration` (34) + `IBppOverlayPanel` (14) hide none of this: the interface is a 4-property callback bag, and only one sorting band is ever used (`MainOverlayPanelBand`, all three panels). Deletion test passes: deleting the per-panel ceremony concentrates lifecycle in one module. None of the lifecycle rules are tested today.

### Per-panel behavior inventory (must survive the refactor)

Scene-change matrix — side effects run **unconditionally** (even when closed); close is policy-gated:

| Panel | Unconditional side effect on scene change | Close condition |
| --- | --- | --- |
| Collection | `DisposeUnityRuntime()` (`CollectionPanel.cs:452`) | visible → close (`:450-451`) |
| LiveBuild | none | visible → close (`LiveBuildPanel.cs:561-562`) |
| History | font-prewarm re-arm + event-system diagnostics + `DisposePreviewRenderer()` (`HistoryPanel.cs:504-511`) | visible **and** in combat → close (`:508-509`) |

History staying open across a non-combat scene change with a disposed preview is today's behavior and is safe: `RefreshSelectedBattlePreview` lazily recreates the renderer via `EnsurePreviewRenderer` (`HistoryPanel.cs:314`). Whether History *should* close on all scene changes is an open product question (see §Open questions); this design preserves current behavior via `SceneChangeClosePolicy.OnlyWhenInCombat`.

Other divergences:

1. **Hotkey guard.** History suppresses the toggle entirely — including close — while its search box has focus (`HistoryPanel.cs:180-183`).
2. **Combat gate on History hotkey.** `CanOpenHistoryReview` blocks *all* hotkey toggles in combat, not just opens (`HistoryPanel.cs:234-235`).
3. **External open entry points.** Collection and History open from settings-dock entries (`CollectionPanelDockButtonController.cs:107`, `HistoryPanelSettingsDockEntry`); Collection resolves a selection payload first (`CollectionPanel.cs:159-193`).
4. **Closed-state per-frame work.** Collection ticks fade-out (drives overlay alpha), warms the card catalog, runs deferred native cleanup, and polls the typography self-heal even while closed (`CollectionPanel.cs:337-394`).
5. **History coordinator per-frame tick** while visible, currently *before* hotkey handling (`HistoryPanel.cs:177-178`).
6. **History `OnDisable`** force-hides (clears static `IsVisible`, notifies coordinator, hides UI) *without* preview-renderer disposal or unregister (`HistoryPanel.cs:146-153`).
7. **Locale refresh hooks** while open: `CollectionPanel.NotifyLocaleChanged` (mount subscription), `HistoryPanel.RefreshLocalization` (mount + `OptionsDialogLanguageRefreshPatch.cs:21`).
8. **Static surface consumed elsewhere** (preserve): `HistoryPanel.IsVisible` (`HistoryPanelSettingsDockEntry.cs:26`), `CollectionPanel.OpenFromDockButton`, `HistoryPanel.OpenFromDockEntry`, `NotifyLocaleChanged`/`RefreshLocalization`. `CollectionPanel.GetCurrentSelectionState` has no src callers (dead API — delete during migration).
9. **Mount-time extras**: LiveBuild `BeginCorpusLoad` in Awake (`LiveBuildPanel.cs:67`); History mount can skip entirely (`HistoryPanelMount.cs:33-37`) or add the component without `Configure` when the online client is missing.

## Decisions (settled in grilling + review, 2026-07-04)

1. **Host shape** — one `OverlayPanelHost` MonoBehaviour owns the per-frame `Update`. Panels remain MonoBehaviours (keep coroutines, transform parenting) but delete their lifecycle preamble; the host calls their `Tick(dt, isVisible)` every frame, unconditionally.
2. **Mutex deleted** — `BppOverlayPanelMutex`, `BppOverlayPanelRegistration`, `IBppOverlayPanel`, and the per-panel `OverlaySortingBand` constants are removed. Mutual exclusion becomes host-internal state. No fallback path (repo rule: replace in place). `BppOverlaySorting.MainOverlayPanelBand` itself is deleted (its only consumer was the mutex path); the host's canvas sorting stays where each panel already sets it.
3. **External requests run synchronously** (review finding 1) — `RequestOpen`/`RequestClose` execute immediately at the call site: combat gate, close-others, then open, in one synchronous sequence, exactly like today's dock click handlers. Only *input-driven* transitions (hotkey, escape, combat auto-close, scene change) are evaluated in the host's `Update`.
4. **Variance via registration options** — per-panel scene-change close policy, an unconditional `OnSceneChanged` side-effect hook, a hotkey-guard predicate (swallows the entire toggle, open *and* close), and a host handle exposing `RequestOpen`/`RequestClose`.
5. **Pure decision core** — input-driven lifecycle rules live in a pure `OverlayLifecycleCore` fed one `OverlayFrameSnapshot` per frame. Mock-category seam with one prod adapter, justified by testability of the rules; directives stay thin (close/notify/open only), no speculative generalization (review finding 12).
6. **Wiring through composition** — `BppComposition` registers an `OverlayPanelHostMount` before the three panel mounts; panel mounts receive a nullable host accessor and register their panel explicitly. No static registry.
7. **Supporter sampling is content** — each panel samples in its own open callback.
8. **All three panels migrate in one change**, including the architecture-test updates (review finding 6).

## Module design

All types live in `src/BazaarPlusPlus/Game/OverlayPanels/` (replacing the three deleted files).

### Interface (what a panel must know)

```csharp
internal sealed class OverlayPanelRegistration
{
    public required string PanelId { get; init; }
    public required BppHotkeyActionId ToggleHotkey { get; init; }
    public SceneChangeClosePolicy SceneChangeClose { get; init; } = SceneChangeClosePolicy.Always;
    public Func<bool>? HotkeyGuard { get; init; }          // false → swallow the whole toggle (open AND close)
    public Action? OnSceneChanged { get; init; }           // unconditional side effects (dispose runtime, etc.)
    public required Action OnOpen { get; init; }           // content: build view, sample supporters, resolve selection
    public required Action OnClose { get; init; }          // content: hide view, clear state
    public required Action<float, bool> Tick { get; init; } // (unscaledDeltaTime, isVisible), every frame, after directives
}

internal enum SceneChangeClosePolicy { Always, OnlyWhenInCombat }

internal interface IOverlayPanelHandle
{
    bool IsVisible { get; }
    void RequestOpen();   // synchronous: combat gate + close-others + OnOpen, at the call site
    void RequestClose();  // synchronous: OnClose if visible
    void Dispose();       // unregister (panel OnDestroy / OnDisable-driven teardown)
}
```

Panels shrink accordingly: no `_isVisible` bookkeeping for exclusion (host is the single source of truth; panels' static `IsVisible` accessors forward to their handle), no scene-token field, no hotkey/escape/combat checks, no `Update` (renamed to `Tick`, invoked by the host).

### Pure core

```csharp
internal readonly struct OverlayFrameSnapshot
{
    public string SceneToken { get; init; }
    public bool IsInCombat { get; init; }
    public bool EscapePressed { get; init; }
    public string? HotkeyPressedPanelId { get; init; }  // resolved by the adapter, hotkey-guard already applied
}

internal sealed class OverlayLifecycleCore
{
    // Owns: open-panel id (the mutual exclusion), last scene token.
    // Also mutated synchronously by RequestOpen/RequestClose through host methods.
    public IReadOnlyList<OverlayDirective> Evaluate(OverlayFrameSnapshot frame);
    public OverlayRequestOutcome ExecuteOpenRequest(string panelId, bool isInCombat);  // sync path
    public OverlayRequestOutcome ExecuteCloseRequest(string panelId);
}
```

Rule order inside `Evaluate` (matches today's per-panel `Update` order: scene → combat → hotkey → escape):

1. Scene token changed → `NotifySceneChanged` for **every** registered panel (unconditional, per the matrix above); close the open panel per its `SceneChangeClosePolicy` (`OnlyWhenInCombat` consults `IsInCombat`).
2. Open panel + `IsInCombat` → close (reason: combat).
3. Hotkey for the open panel → close; hotkey for another panel → close current, then open — unless `IsInCombat` (open suppressed, matching today's `Open` guards). Guarded hotkeys never reach the snapshot.
4. Escape with an open panel → close.

Directive order guarantees close-before-open. The host adapter executes directives by invoking registration callbacks inside try/catch (preserving the mutex's per-panel error isolation, `BppOverlayPanelMutex.cs:50-63`), then calls every registration's `Tick(dt, isVisibleNow)` with post-directive visibility.

### Accepted behavioral deltas (review findings 2, 5)

- **History coordinator tick timing**: today the coordinator ticks *before* hotkey handling with pre-toggle visibility (`HistoryPanel.cs:177-187`) — it ticks on the close frame and skips the open frame. Under the host it ticks with post-directive visibility: skips the close frame, ticks the open frame. The coordinator tick is a polling refresh and `OnPanelShown`/`OnPanelHidden` still fire inside open/close; a one-frame shift is accepted. Verified in-game (§Verification).
- **Collection early-return frames**: today a combat-close/hotkey/escape frame `return`s before the fade tick (`CollectionPanel.cs:344-387`); under unconditional `Tick` the fade runs on those frames too. This *improves* close-frame fade-out continuity and is accepted. Verified in-game.

### History `OnDisable` (review finding 4)

`OnDisable` routes through `handle.RequestClose()` so host state and panel state cannot desync; the coordinator's `OnPanelHidden` idempotency keeps the existing double-notify protection. `OnDestroy` calls `handle.Dispose()`. The lighter today's-`OnDisable` behavior (no preview-renderer disposal) is preserved by keeping renderer disposal in `SetHistoryVisible`'s explicit-close path only if in-game verification shows a difference; default is the simpler unified close.

### Composition changes

- `OverlayPanelHostMount` registered in `BppComposition` before `CollectionPanelMount` / `HistoryPanelMount` / `LiveBuildPanelMount` (mountable order is already explicit, `BppComposition.cs:122-140`).
- Panel mounts take a nullable host accessor (same shape as `HistoryPanelMount`'s existing `Func<>` service accessors, though the target here is a sibling mountable — new pattern, noted deliberately). Host mount always succeeds; if a panel mount observes a null host it logs a warning and skips registration (panel unusable but inert). History's skip path (`HistoryPanelMount.cs:33-37`) never registers; its partial-mount path (component added, `Configure` skipped) must also not register with the host.
- Dock entries call the panel, which calls `handle.RequestOpen()` synchronously; Collection resolves its open selection inside `OnOpen` (selection reads happen at open-execution time — same frame as the click, per decision 3).

## What does NOT change

- ADR-0002 mounting: panels are still MonoBehaviours attached via `IBppMountable`.
- Panel content: views, virtualizers, preview renderers, coordinators, supporters untouched except call-site renames.
- `BppUiChromeSuppression` (separate concern, stays).
- `BppOverlaySorting` canvas sorting constants (comment refreshed; only the mutex band parameter dies).
- Static entry points preserved: `HistoryPanel.IsVisible`, `OpenFromDockEntry`, `OpenFromDockButton`, `NotifyLocaleChanged`, `RefreshLocalization`.

## Tests

New exe-runner test project `tests/OverlayPanelLifecycle.Tests/` (no `Microsoft.NET.Test.Sdk`; `OverlayLifecycleCore` and its DTOs compile-linked via `<Compile Include=... Link=...>` with zero Unity refs, same pattern as `CombatStatusBarState.Tests`):

- mutual exclusion: opening B closes A first; directive order close-before-open; synchronous request path matches hotkey path.
- combat: auto-close when open; open suppressed while in combat (hotkey and request paths).
- scene change: `Always` vs `OnlyWhenInCombat`; `NotifySceneChanged` fan-out to all registrants including closed ones.
- hotkey: toggle open/close; guard swallows the whole toggle including close (visible + guarded → no close directive); cross-panel supersede.
- escape: closes open panel only.

**Architecture tests** (same commit): `CoreLayeringTests.cs:190-192` and `:790-802` currently require the hotkey calls *inside* `CollectionPanel.cs`/`LiveBuildPanel.cs`; retarget these ratchets at the host/registration wiring instead.

## Verification

1. `./run.sh build` clean.
2. `dotnet run --project tests/OverlayPanelLifecycle.Tests/...` green; `./run.sh test` for regressions.
3. In-game: open each panel via hotkey + dock entry (all three hotkey bindings — recently migrated, commits `b9e81f20`/`39dcbc4b`/`cffa6697`); mutual exclusion including dock-click-while-other-open (same-frame close-then-open, no z-fight); escape; combat auto-close; scene-transition behavior per the matrix (History stays open across non-combat scene change, preview re-renders on next selection); Collection close-frame fade-out; History search-box hotkey guard (no open *and* no close); History coordinator-driven refresh after open.

## Open questions (for user)

1. History's `OnlyWhenInCombat` scene-close policy: intentional feature (keep) or latent inconsistency (switch to `Always` and delete the enum, simplifying the interface)? Default: keep current behavior.
2. Delete the dead `CollectionPanel.GetCurrentSelectionState` API during migration? Default: yes.
