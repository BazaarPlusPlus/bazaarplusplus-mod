---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Item-board preview abstraction target

Status: IMPLEMENTED - automated verification complete; runtime startup/resource validation complete; manual interaction validation pending.

This document records the target architecture for replacing CardSetPreview's
live `MonsterBoardTooltip` clone path with a shared native card-preview
foundation that HistoryPanel and CardSetPreview can both use.

## Goal

Create two reusable layers:

1. A single-card native preview primitive: given a card spec and a parent
   transform, create one live native `CardPreviewBase` instance, await its
   `SetUp` task, show it, hover it, and return it to a pool.
2. An item-board preview surface: given a list of item card specs, render them
   into a 10-slot board inside a `ScreenSpaceOverlay` canvas with clipping,
   scale, generation cancellation, and selectable layout behavior.

The end state is:

- `HistoryPanel` uses the shared item-board surface for its battle preview.
- `CardSetPreview` uses the same shared item-board surface for selected-set and
  ten-win recommendation previews.
- `CardSetPreview` no longer creates a real card tooltip just to obtain and
  clone a live `MonsterBoardTooltip`.
- Feature-specific policy and chrome stay in their feature directories.

## Non-goals

- Do not move `CardSetPreview` hotkeys, recommendation selection, candidate
  state, or sponsor/status chrome into `GameInterop`.
- Do not move `HistoryPanel` selection state, preview status text, signature
  cache decisions, or UI Toolkit bounds orchestration into `GameInterop`.
- Do not migrate `CollectionPanel` into the item-board abstraction in this pass.
  CollectionPanel is a virtualized catalog grid with skills, scroll, cache
  marker patches, and many live cards; it should continue to consume the lower
  `GameInterop/CardPreview` primitives through its own grid code.
- Do not reintroduce offscreen Camera -> RenderTexture rendering for uGUI card
  previews. The validated path is `ScreenSpaceOverlay` + `RectMask2D`.

## Implementation evidence

The shared single-card primitive now lives in `GameInterop/CardPreview/`:
`NativeCardPreviewSpec`, `NativeCardPreviewHandle`, `NativeCardPreviewPool`,
`NativeCardPreviewFactory`, and `NativeCardPreviewHoverRelay`. It reuses
`NativeCardPreviewRuntime` and `NativeCardPreviewPrefabResolver` for native
`CardPreviewBase` reflection, prefab lookup, `Resize`, `Show`, and async
`SetUp`.

The shared item-board surface now lives in `GameInterop/ItemBoardPreview/`:
`ItemBoardPreviewSurface`, `ItemBoardPreviewOptions`, `ItemBoardPreviewPhase`,
`ItemBoardPreviewLayoutMode`, `ItemBoardSocketLayout`,
`ItemBoardSocketResolver`, `ItemBoardPreviewGenerationGuard`, and
`ItemBoardPreviewSignatureGate`. It owns the `ScreenSpaceOverlay` canvas,
`RectMask2D` clipping, 10 sockets, generation cancellation, optional signature
cache, packed/socketed layout, and setup-gated polled hover.

`Game/HistoryPanel/Preview/BattleBoardPreview.cs` — thin wrapper file deleted (50e64b1); HistoryPanel now directly holds `BppItemBoardPreview` (`HistoryPanel.cs:42/359`), mapping `HistoryItemSpec` to `NativeCardPreviewSpec` inline. The feature-local `BattleBoardCardFactory`,
`HistoryPanelPreviewCardPool`, socket resolver, layout, generation guard, and
signature gate files were removed.

`Game/CardSetPreview/ItemBoardService.cs` now owns one
`ItemBoardPreviewSurface` plus CardSet-specific sponsor chrome. It maps
`ItemBoardTemplateSetRequest.Items` to `NativeCardPreviewSpec`. The old live
tooltip path files were removed:

```text
Game/CardSetPreview/MonsterBoardTooltipBindings.cs
Game/CardSetPreview/SyntheticMonsterFactory.cs
Game/CardSetPreview/ItemBoardRenderInput.cs
Game/CardSetPreview/ItemBoardOverlay.cs
```

`CardSetPreviewRuntime` now calls `ShowTemplateSet(request)` directly and no
longer starts `RenderWhenTooltipHostReady`.

Debug runtime validation was built and launched through Steam on 2026-06-02.
`BepInEx/LogOutput.log` showed `BazaarPlusPlus 4.0.0` loading, plugin
initialization completing, the game reaching lobby, and
`CollectionCardPool` acquiring native card preview prefab refs and 10 sockets.
No `MethodAccessException`, `MissingMethodException`, `ItemBoardService`,
`BattleBoardPreview`, or `CardPreviewBase.SetUp` errors appeared in the
startup/resource log. Full HistoryPanel and CardSetPreview click-through
validation remains pending because the available UI automation could read the
game window and press keys, but could not deliver coordinate clicks or Unity
hotkeys reliably.

## Target architecture

### Layer 1: native card preview primitive

Add the following under `GameInterop/CardPreview/`:

```text
NativeCardPreviewSpec.cs
NativeCardPreviewHandle.cs
NativeCardPreviewPool.cs
NativeCardPreviewFactory.cs
NativeCardPreviewHoverRelay.cs
```

`NativeCardPreviewSpec` is the reusable input shape:

```csharp
internal sealed class NativeCardPreviewSpec
{
    public Guid TemplateId { get; init; }
    public ETier Tier { get; init; }
    public EContainerSocketId? SocketId { get; init; }
    public EEnchantmentType? EnchantmentType { get; init; }
    public IReadOnlyDictionary<ECardAttributeType, int>? Attributes { get; init; }
    public string InstanceIdPrefix { get; init; } = "bpp-card-preview";
}
```

`NativeCardPreviewFactory` owns the card creation contract:

- Reject null/empty specs.
- Read static data through `BppStaticDataAccess`.
- Resolve `TCardBase` by `TemplateId`.
- Determine `NativeCardPreviewKind` from the resolved template, not from caller
  guesses.
- Take a prefab instance from `NativeCardPreviewPool`.
- Build `TCardInstanceItem` for item templates and `TCardInstanceSkill` for
  skill templates. Item-board callers should pass only item specs, but the
  primitive can support both because CollectionPanel already needs skills at
  the lower layer.
- Invoke `NativeCardPreviewRuntime.InvokeSetUpSafe`.
- Return a `NativeCardPreviewHandle` containing the component, rect, kind,
  setup task, and original spec.

`NativeCardPreviewPool` is per-owner/per-surface, not global. Prefab references
remain static and are resolved by `NativeCardPreviewPrefabResolver`; live card
instances remain owned by the caller's pool so HistoryPanel, CardSetPreview, and
CollectionPanel cannot return each other's objects.

`NativeCardPreviewHoverRelay` moves the current CollectionPanel hover relay
behavior into `GameInterop/CardPreview`. It should expose explicit `Bind`,
`Clear`, `InvokeHover`, and `InvokeHoverOut` methods and continue resolving
`OnHover` / `OnHoverOut` through `NativeCardPreviewReflection`.

### Layer 2: item-board preview surface

Add the following under `GameInterop/ItemBoardPreview/`:

```text
ItemBoardPreviewSurface.cs
ItemBoardPreviewOptions.cs
ItemBoardPreviewPhase.cs
ItemBoardPreviewLayoutMode.cs
ItemBoardSocketLayout.cs
ItemBoardSocketResolver.cs
ItemBoardPreviewGenerationGuard.cs
```

`ItemBoardPreviewSurface` owns:

- root `GameObject`
- `ScreenSpaceOverlay` canvas
- optional `CanvasGroup`
- `RectMask2D` clip rect
- board rect
- 10 socket rects
- `NativeCardPreviewFactory`
- active `NativeCardPreviewHandle` list
- active setup task list
- generation guard
- optional rendered signature

The surface API should be feature-neutral:

```csharp
internal sealed class ItemBoardPreviewSurface : IDisposable
{
    public bool EnsureInitialized();
    public void SetPosition(Vector2 screenBottomLeft);
    public void SetClipSize(Vector2 pixels);
    public bool SetCardScale(float scale);
    public IEnumerator Render(
        IReadOnlyList<NativeCardPreviewSpec>? cards,
        ItemBoardPreviewOptions options,
        string? signature = null,
        Action<ItemBoardPreviewPhase>? onPhase = null,
        Action? onComplete = null
    );
    public void PollHover(Vector2 mousePixels);
    public void Hide();
    public void CancelPending();
    public void Dispose();
}
```

`ItemBoardPreviewOptions` should include:

- `Layer`
- `SortingOrder`
- `LayoutMode`
- `ShowHover`
- `UseCanvasGroup`
- `LogComponent`

`ItemBoardPreviewPhase` mirrors the existing HistoryPanel phase shape:
`Empty`, `InitFailed`, `Loading`, `Done`.

`ItemBoardPreviewLayoutMode` starts with:

- `Socketed`: parent cards under resolved sockets and keep the socket layout.
- `Packed`: after setup/show, measure each card's `FrameContainer` width and
  pack visible cards edge-to-edge centered on the board. This preserves the
  current HistoryPanel visual behavior.

`ItemBoardSocketLayout` builds the 10 socket rects. It should reuse native
socket size and pivot templates captured by `NativeCardPreviewPrefabResolver`
where available, with the same fallback dimensions as HistoryPanel today.

`ItemBoardSocketResolver` is the pure function currently represented by
`BattleBoardSocketResolver`. It stays unit-testable:

```csharp
ResolveIndex(int socketCount, int? requestedIndex, int fallbackIndex, int span)
```

### Consumer: HistoryPanel

HistoryPanel should migrate first with no intended behavior change.

Preferred shape:

- Keep `Game/HistoryPanel/Preview/BattleBoardPreview.cs` initially as a thin
  wrapper over `ItemBoardPreviewSurface`.
- Map `HistoryItemSpec` to `NativeCardPreviewSpec` in HistoryPanel, not in
  `GameInterop`.
- Keep HistoryPanel status strings and UI callbacks in `Game/HistoryPanel`.
- Keep HistoryPanel's signature decisions at the wrapper or caller layer, or
  pass the signature through the surface if the same short-circuit remains
  useful.
- Preserve current packed layout and auto-fit behavior before changing
  CardSetPreview.

After this migration, `BattleBoardCardFactory` and
`HistoryPanelPreviewCardPool` should be deleted or reduced to compatibility
wrappers with no independent lifecycle logic.

### Consumer: CardSetPreview

> **注（历史存档）：** CardSetPreview 已由 LiveBuildPanel 全量替代（commit 50e64b1）；`LiveItemBoardRowPreview.cs` 已用 `BppItemBoardPreview` + SlotGrid。本节为历史存档。

CardSetPreview should then switch from live tooltip cloning to the shared
surface.

Preferred shape:

- `ItemBoardTemplateSetRequest.Items` are mapped to `NativeCardPreviewSpec`.
- `ItemBoardService` owns one `ItemBoardPreviewSurface` plus CardSet-specific
  chrome.
- `ItemBoardService.ShowTemplateSet(request)` no longer needs a
  `CardTooltipController`.
- `CardSetPreviewRuntime.StartRenderForSelection` calls `ShowTemplateSet`
  directly and does not start `RenderWhenTooltipHostReady`.
- Remove the tooltip-host coroutine once the new surface is verified.
- Keep `SponsorPanelRenderer` in `Game/CardSetPreview`; it should attach to a
  CardSet-specific chrome root, not to `GameInterop/ItemBoardPreview`.
- Keep candidate count, alert state, sponsor text, and source labels in
  `ItemBoardTemplateSetRequest` or a CardSet-specific render model. The shared
  item-board surface only receives card specs and layout options.

The target delete list after CardSetPreview migration:

```text
Game/CardSetPreview/MonsterBoardTooltipBindings.cs
Game/CardSetPreview/SyntheticMonsterFactory.cs
Game/CardSetPreview/ItemBoardRenderInput.cs
Game/CardSetPreview/ItemBoardOverlay.cs
```

`ItemBoardService.cs` remains, but becomes the CardSetPreview adapter over
`ItemBoardPreviewSurface` plus sponsor/status chrome.

### Consumer: CollectionPanel

CollectionPanel should not migrate in this pass.

After `NativeCardPreviewPool`, `NativeCardPreviewFactory`, and
`NativeCardPreviewHoverRelay` exist, CollectionPanel may optionally adopt those
lower primitives in a later cleanup. That later migration must preserve:

- CollectionPanel-owned marker behavior.
- CollectionPanel art/material cache patches.
- item and skill support.
- virtualized cell generation and pending-return behavior.
- polled hover and no default overlay `GraphicRaycaster`.

Do not let this target doc expand into a CollectionPanel rewrite.

## Input and interaction policy

The shared item-board overlay should be visual by default. It should not attach
a `GraphicRaycaster` unless a feature explicitly requests raycaster input and
also forwards scroll/click appropriately.

Hover should be polled:

1. caller or surface provides current mouse position in screen pixels;
2. surface maps the point into board/clip space;
3. surface finds the active card rect under the cursor;
4. hover is dispatched only if the card setup task completed successfully;
5. moving away invokes hover-out.

This mirrors the CollectionPanel fix and avoids the known failure mode where
native `RawImage.raycastTarget` catches events over cards while the underlying
UI cannot scroll or click.

## Error handling

- Missing native reflection metadata: return `InitFailed`, log once per owner
  component.
- No static data: return `InitFailed` or skip cards depending on call site.
  HistoryPanel should surface preview unavailable; CardSetPreview should keep
  its visible mode indicator and log the failure.
- Unknown template id: skip that card and log debug/warn with the template id.
- `SetUp` fault: do not cache the signature; keep the frame retryable on the
  next render request.
- Generation invalidation: stale setup completions must not show or return
  cards twice.
- Empty card list: hide the surface and return `Empty`.

## Testing plan

Automated tests:

- Extend `tests/HistoryPanelPreview.Tests` to cover the renamed/shared socket
  resolver.
- Add tests for any pure layout helpers introduced by `ItemBoardSocketLayout`
  and packed width ordering if the logic can be isolated from Unity objects.
- Add an architecture test that `Game/HistoryPanel` and `Game/CardSetPreview`
  do not import each other's preview internals.
- Keep `Core` and `GameInterop` layering tests passing; `GameInterop` must not
  import `Game/HistoryPanel` or `Game/CardSetPreview`.
- Keep `tests/MonsterPreviewResilience.Tests` for CardSetPreview hotkeys/status
  behavior (当前为空 test shell，无源文件).

Target command set:

```bash
dotnet run --project tests/HistoryPanelPreview.Tests/HistoryPanelPreview.Tests.csproj
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
dotnet build BazaarPlusPlus.csproj --no-restore
```

Runtime validation:

1. Build Debug and launch The Bazaar through Steam. Completed on 2026-06-02.
2. Inspect startup log for plugin load and native card preview resource
   acquisition. Completed on 2026-06-02.
3. Open HistoryPanel and verify preview position, scale, packed layout, and
   hover tooltip.
4. Enable CardSetPreview with CapsLock.
5. Left-click item cards to add, right-click to remove.
6. Switch `A` / `D` display modes and `W` / `S` candidates.
7. Verify selected-set and ten-win recommendation boards render without first
   opening a real card tooltip.
8. Hover CardSetPreview cards and confirm native tooltips appear only after
   cards are fully loaded.
9. Disable mode and verify overlay, hover tooltip, and input state are cleaned
   up.
10. Inspect `BepInEx/LogOutput.log` for `CardPreviewBase.SetUp`, prefab
    resolver, generation, and CardSet render errors.

## Implementation sequence

1. Add `NativeCardPreviewSpec`, `NativeCardPreviewHandle`,
   `NativeCardPreviewPool`, `NativeCardPreviewFactory`, and
   `NativeCardPreviewHoverRelay`.
2. Add shared item-board pure helpers: phase, layout mode, options, generation
   guard, socket resolver, socket layout.
3. Add `ItemBoardPreviewSurface` using the new native card preview primitive.
4. Migrate HistoryPanel through a thin `BattleBoardPreview` wrapper; keep the
   rendered behavior unchanged.
5. Run HistoryPanel tests and build.
6. Migrate CardSetPreview `ItemBoardService` to the shared surface.
7. Remove `RenderWhenTooltipHostReady` and the `CardTooltipController` overload
   from CardSetPreview item-board calls.
8. Re-parent/rework CardSet sponsor/status chrome so it is feature-owned.
9. Delete the old CardSet live-tooltip files.
10. Update feature docs and this design doc status after runtime validation.

## Review gate

This is a large refactor across shared runtime rendering and two user-visible
features. Before implementation, run an independent review of this target
against current source and revise the plan from that review. The review should
be strictly review-only: no patches, no speculative implementation.

After the reviewed plan is revised, send it back for confirmation before
writing implementation code.

## Suggested rule additions

None yet. This document is a one-off target plan; it does not establish a
validated repeated trap until implementation and runtime validation prove the
pattern.
