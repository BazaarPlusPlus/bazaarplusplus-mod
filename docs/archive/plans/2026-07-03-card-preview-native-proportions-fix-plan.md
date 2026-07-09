---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master cd1e6566 (fixes A-E) + a37d477c (HistoryPanel frame-border follow-up), 2026-07-03; all constants live at HEAD.

# Card preview native-proportions fix plan

Status: IMPLEMENTED — fixes A–E landed in `cd1e6566` (post-landing adversarial review: approve; tests green). Follow-up below landed after the first in-game pass.
Date: 2026-07-03
Scope: `GameInterop/ItemBoardPreview`, `Game/CollectionPanel/Grid` — no consumer (HistoryPanel / LiveBuildPanel) changes.

## Background

Three user-visible defects on the mod's native-card preview surfaces:

- **S1** — attribute badges (gem band: damage/poison/etc.) render disproportionately large relative to the card frame.
- **S2** — Large item cards render shorter than Small/Medium in the CollectionPanel grid; expected model is uniform height with width:height 1:2 / 2:2 / 3:2.
- **S3** — cards in the LiveBuildPanel rows (ten-win FinalBuild, live shop/board/stash) and the HistoryPanel battle preview overlap each other.

Root causes were established by a multi-agent investigation (decompiled source + UnityPy dumps of the game's addressable bundles, catalog 2026.07.01) with per-symptom adversarial verification; the red-team pass independently re-dumped the bundles and re-derived all arithmetic. Facts below are dump-verified twice.

### Native card anatomy (ground truth)

The game has **no code-side card size model**. `AssetLoader.ConstructAndInstantiateUICard` (decompiled/TheBazaarRuntime/AssetLoader.cs:568-626) picks one of four prefabs and parents it; sizing is 100% prefab anchors + parent rect:

| Fact | Value | Source |
| --- | --- | --- |
| Item prefab root anchors | `(0,0)-(0,1)`, sizeDelta 0 — **stretches to parent height** | cardui_assets_all.bundle dump |
| Root width | `AspectRatioFitter` (HeightControlsWidth), ratio **0.52 / 1.04 / 1.56** (S/M/L) — exactly 1:2, 2:2, 3:2 widened 4% | same |
| FrameContainer overhang vs root | height **×1.03704** (all sizes, asymmetric: top 0.02315 / bottom 0.01389); width ×1.0445 (S), ×1.0579 (M), **×1.41548 (L)** — the Large frame art (1024×512) carries side flourishes | same + cardframes_assets_all.bundle |
| Gem band (`CardGems_Sprite_P`) | anchors `(0,1)-(1,1)`, sizeDelta `(0,50)`, anchoredPos `(0,-10)` — **fixed 50 root-local units, identical across S/M/L**; gems LayoutElement min 60 / preferred 90 | cardui bundle dump |
| Gem sizing code | none — 2D previews wire `CardGemSpriteGroupController` (visibility/dim only); the size table lives only on the 3D combat variant | decompiled CardGemSpriteGroupController.cs:46-103 vs CardGemGroupController.cs:122-150 |
| Native board comparator (`Tooltip_MonsterBoard_P`, desktop) | sockets sizeDelta `(0,484)` (width-0 point pins) at **240-px x-pitch** (x = 220 + 240·i), MainSize 2600×900, carpet 511.5 | tooltips_assets_all.bundle dump |

Derived native proportions:

- badge : card height = 50/484 = **10.33 %**
- card frame height : socket pitch = 484 × 1.03704 / 240 = **2.0913**
- Small body width : pitch = 0.52 × 484 / 240 = **1.0487** → native cards *touch by design* (~4.9 % per boundary).

### Root causes

| Symptom | Cause | Where |
| --- | --- | --- |
| S1 (slot-grid surfaces) | Cards bind into mod-invented 320-tall sockets; root stretches to 320 ⇒ badge = 50/320 = 15.6 % of card height (native 10.3 %, ×1.51). The later uniform localScale preserves the wrong ratio. | `ItemBoardSocketLayout.cs:13-14,33` |
| S1 (collection) | Zero-rect fallback force-sizes card roots to height **200** ⇒ badge = 50/200 = **25 %** (×2.42 native). | `CollectionGridVirtualizer.cs:38,573-597` |
| S2 (collection only) | `ApplyCellScale` height-fits then width-clamps using the **FrameContainer subtree width**; the Large frame overhangs its body ×1.415, so the clamp fires **only for Large, at every legal unit width** (crossover u≈21.1 < MinUnitWidth 24), shrinking Large 1.9–16.8 % (max at the 172-px unit cap). The comment claiming span-uniform clamping encodes the broken assumption. Slot-grid surfaces are NOT affected (their math already yields uniform heights). | `CollectionGridVirtualizer.cs:482-509,540-544` |
| S3 | SlotGrid height budget = `0.96 × 600` board units against a 260-unit pitch ⇒ body width / span = 0.52 × (576/1.03704) / 260 = **1.1108** for every size — ~2.3× the native 4.9 % touch, at every card boundary. Width is never validated against the span (enabling mechanism). Vertical component of the perceived overlap = S1's oversized badges overhanging the root top (+15 local units) into neighbors; true frame-vs-frame vertical overlap is geometrically impossible on these single-row surfaces. | `ItemBoardPreviewOptions.cs:26`, `ItemBoardSlotGridGeometry.cs:70-81`, `ItemBoardPreviewSurface.cs:642-705` |

Refuted (do not re-investigate): slot-planner packing errors; span/socket misassignment; `_boardRect` double-scaling; pool-reuse scale compounding (checkout resets `localScale=1` + `Resize()`, `NativeCardPreviewPool.cs:86-90`); non-proportional prefab hypothesis; gem-size-table mechanism; zero-value attribute filtering (native semantics are identical). Also rejected with grounded reasons (red-team): native `CardPreviewItem.Resize()` cannot fix S1 (it only recenters `anchoredPosition.x`, decompiled CardPreviewItem.cs:36-76); growing `SlotGridVerticalInsetPixels` cannot absorb the badge overhang (the inset only enters the slot-height term, not the board-cap term that binds in the touch regime, and would desync the UITK slot backdrops drawn at top:6/bottom:6, LiveBuildPanelView.cs:405-406).

## Fixes

Five changes (A–E) inside the two owning layers. Consumers keep passing bounds/cardScale unchanged.

### Fix A — bind sockets at native height (S1, slot-grid surfaces)

`ItemBoardSocketLayout.cs`: `FallbackSocketHeightPixels` **320 → 484**. Comment the constants honestly: 484 is the native `Tooltip_MonsterBoard_P` socket height; 240 is the native socket **x-pitch** (native sockets are width-0 point pins — socket width never drives card width because the card root's x-anchors are both 0).

Why it is sufficient: the card root stretches to socket height and the 50-unit gem band is authored against that height, so binding at 484 restores 50/484 = 10.3 % at the source. `LayoutCardsSlotGrid`'s height-normalizing scale (`targetHeight / measured frameHeight`) automatically compensates — final on-screen card size is unchanged (verified: root:frame ratio identical at 320 and 484, so hover rects and clip margins are invariant too).

### Fix B — derive the SlotGrid height cap from native card:pitch proportion (S3)

`ItemBoardPreviewOptions.SlotGridMaxHeightRatio` default **0.96 → 0.90626**, expressed as a derived constant next to the other native numbers rather than a magic literal:

```
// ItemBoardSocketLayout (new public consts)
NativeSocketHeightPixels = 484f;   // Tooltip_MonsterBoard_P socket height
NativeSocketPitchPixels  = 240f;   // native socket x-pitch
FrameHeightOverSocket    = 1.03704f; // FrameContainer y-overhang (bundle dump)

// default ratio = frame-height-per-pitch × mod pitch ÷ mod board height
// = (484 × 1.03704 / 240) × (2600/10) / 600 = 0.90626
```

Effect (regimes corrected per red-team): with `s = min(w/2600, h/600)` and target = `min(h−12, 600·s·ratio)`, the slot-height term binds only for rows shorter than ~128 physical px — for any realistic row the **board cap binds at every aspect ratio**. The `s` regime switch is at container aspect **w/h = 2600/600 = 4.333**:

- aspect ≤ 4.333 (HistoryPanel's ~2:1 preview container): body:pitch = exactly the native **1.0487** — cards touch by ~4.9 % like the in-game monster-board tooltip.
- aspect > 4.333 (LiveBuildPanel rows: ≈4.49 on 16:10, ≈5.08 on 16:9): proportion decays as 4.333/aspect — near-touch on 16:10 (body:pitch ≈ 1.01), visible ~10 %-of-pitch gaps on 16:9. This is accepted: gaps are correct-looking; the defect was overlap.
- Global guarantee (verified by case analysis over all regimes): post-fix **max body:pitch = 1.0487** — the 11 % overlap can never recur at any container geometry or DPI.

Deliberate choice: reproduce the **native touch** as the ceiling, not zero-gap. (Zero-overlap alternative: ratio 0.8642 = 260 × 1.03704 / 0.52 / 600; rejected — shrinks cards below the native look.)

### Fix C — clamp collection cards by body width, not frame width (S2)

`CollectionGridVirtualizer.ApplyCellScale`: keep the height-fit scale (FrameContainer height, uniform ×1.037 across sizes) but compute the max-width clamp against the **card body width**, derived from the already-measured bounds so the basis is self-consistent and independent of which measurement path ran (red-team correction — do NOT read `root.rect.height`, which is zero/stale on the exact pass where the clamp matters):

```
bodyW = fitter.aspectRatio × (natH / FrameHeightOverSocket)   // fitter = root's AspectRatioFitter
```

Guard: if the root has no usable `AspectRatioFitter` (skill prefab root is a fixed 200×200; item roots carry the fitter — dump- and decompile-confirmed), fall back to the current frame-subtree width `natW` — unchanged behavior for skills.

With body-width clamping the clamp never fires at the current grid constants (worst case Large: `1.56/1.03704 × 0.88 × (2u+g) = 1.3238×(2u+g)` vs budget `3(u+g)`; at u=172: 492 vs 558), so all three item sizes render the same height at 1.04×(1:2 / 2:2 / 3:2) — the user's expected model. Also rewrite the stale comment at `CollectionGridVirtualizer.cs:494-500`.

Accepted cosmetic consequence, with fallback: the Large frame's decorative flourish (~0.21 body-width per side) will overhang into neighbor cells. Unlike the native single-carpet board, collection cells each draw their own rounded backdrop tile (`CollectionGridSlotLayer.cs:108-130`), so the flourish crosses tile borders. We accept this initially (the flourish is translucent ornament and the alternative shrinks the card — the S2 defect itself); if it reads as broken in-game, the recorded fallback is a per-size overhang allowance on the clamp (S ×1.0445 / M ×1.0579 / L ×1.4155) instead of body-width clamping — do not regress to shrinking Large.

Implementation constraint: `tests/Architecture.Tests/CoreLayeringTests.cs:546-660` pins exact source substrings of `ApplyCellScale`/`Reposition`/`ResolveNativeVisualBounds` by ordered `IndexOf` (`var natW = visualBounds.Width;`, `var natH = visualBounds.Height;`, first-occurrence `GetComponent<AspectRatioFitter>()` anchored before `FallbackNativeCardHeight` uses). Keep the `natW`/`natH` locals, and update that test's assertion set deliberately in the same commit if Fix C's shape moves the pinned anchors (source-snapshot maintenance, not coverage theater).

`Reposition` keeps centering on the measured frame bounds (horizontal overhang is symmetric; vertical center offset ≈0.46 % of height — negligible).

### Fix D — collection fallback root height at native proportion (S1, collection)

`CollectionGridVirtualizer.FallbackNativeCardHeight` **200 → 484**. The fallback (`TryResolveAspectRatioFallbackBounds`) is what force-sizes item card roots after `PrepareGridRect` re-anchors them to a zero rect, so this constant *is* the collection badge denominator: 50/484 = 10.3 %. The height-fit scale self-corrects (natH measured after the resize), so final **native** card visuals are unchanged. Gem width follows: one gem = 90/(0.52×484) = 35.8 % of a Small card's width (native), instead of today's 86 %.

Skill path: unaffected — skill prefab root is a fixed 200×200 with no stretch anchors; the fallback requires a usable fitter and skills never reach it.

### Fix E — compensate the mod's source-attribution badge for Fix D (red-team finding)

`CollectionSourceAttributionBadge` (`CollectionSourceAttributionBadge.cs:42-67`) is a mod-attached child of the card **root** with fixed root-local geometry (sizeDelta 132×28, anchoredPos (−10,−12), fontSize 12). Fix D shrinks `rect.localScale` by 207.4/501.9 ≈ 0.413, which would render the badge at ~41 % of today (fontSize ≈ 7.5 pt at the unit cap — illegible). Scale its constants by 484/200 = 2.42 to preserve on-screen size: sizeDelta (132,28) → (320,68), anchoredPos (−10,−12) → (−24,−29), fontSize 12 → 29. Express as `× (FallbackNativeCardHeight / 200f)`-style derivation or update the literals with a comment tying them to the root-height basis. (Other root-attached mod children verified safe: `CollectionCardHoverRelay` has no visuals; `EnsureHitTarget`'s Image stretches with the root and is dead under `UsePolledHover=true`.)

## What deliberately does not change

- No gem counter-scaling, no attribute filtering (zero-value pass-through is native semantics).
- No slot-planner, mapper, or consumer changes. `NativeBoardWidth/Height` (2600/600) stay — consumer `cardScale` math and `ResolveOccupiedRect` slot-band math (and the UITK slot backdrops / hit targets built on it, `LiveBuildPanelView.cs:644-700`) are untouched.
- `LayoutCardsPacked` untouched (no consumers). Note: the unused default `LayoutMode = Socketed` applies no per-card scale, so a hypothetical future Socketed consumer would render cards 1.51× larger after Fix A — acceptable; no current consumer uses it.
- Hover hit-testing needs no change (`handle.Rect` world corners follow the corrected geometry; root:frame proportion is scale-invariant).

## Test & verification plan

Proportional to the change (constants + one measurement basis). Red-team-corrected audit:

1. **Confirmed unaffected — must NOT change**: `tests/HistoryPanelPreview.Tests/Program.cs` geometry cases (pass `maxHeightRatio: 0.96f` and heights explicitly — independent of the changed default; the options-forwarder case pins 0.75 explicitly); `tests/LiveBuildPanel.Tests/Program.cs:142-153` (`ResolveOccupiedRect` slot-band assertions — none of the changed knobs enters that function); `tests/CollectionGridLayout.Tests` (pure cell-rect math). No test observes the shipped ratio default; per the no-coverage-theater rule we do not add a float-literal assertion on it.
2. **Must audit/possibly update**: `tests/Architecture.Tests` (xunit — `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj`): `CollectionGridVirtualizer_measures_native_visual_bounds_for_card_layout` source-snapshots `ApplyCellScale` internals (see Fix C constraint).
3. Run: the two exe-runners via `dotnet run --project … -c Debug` (HistoryPanelPreview.Tests, LiveBuildPanel.Tests), `dotnet test` for Architecture.Tests and CollectionGridLayout.Tests (check csproj shape first), then `./run.sh build`.
4. In-game visual check (launch via Steam), **per-surface criteria**:
   - HistoryPanel battle preview (~2:1 container): adjacent cards just touch (~4.9 %, native look); badges ≈10 % of card height; no badge intrusion into neighbor frames.
   - LiveBuildPanel rows: **no overlap**; small gaps between cards are expected and correct on wide rows (16:9); near-touch on 16:10.
   - Collection: S/M/L uniform height per shelf, Large fills its 3-unit tile at 3:2; Large flourish overhang across tile borders is symmetric and tolerable (else invoke the Fix C fallback); source-attribution badge unchanged in on-screen size and legibility.
   - Eyeball the two transient behaviors: slot-grid one-frame unscaled flash (pre-existing, ×1.51 larger with Fix A — cards flash bigger for one frame inside the row clip before `LayoutCardsSlotGrid` scales them); collection `ShowWhenReady` re-scale pop.

## Risks

- The ×1.03704 frame-overhang and 484/240 socket numbers are dump-derived from game catalog 2026.07.01; a future art re-author shifts the touch amount by a few percent — degrades gracefully (slight gap or slight touch), never re-creates the 11 % overlap (ceiling proof above).
- RectMask2D clip of badge tops: at a 484 root the band top sits 3.9 local units above the frame (vs 7.6 at 320), and each row is its own RectMask2D-clipped canvas — residual overhang cannot cross row borders; verify visually.
- Fix D changes the pre-scale world size of collection cards (200 → 484 tall before `localScale`); `ApplyCellScale`/`Reposition` normalize it, but the first-frame fade-in path should be eyeballed for a one-frame pop.
- The collection path has no `Canvas.ForceUpdateCanvases` (unlike the slot-grid surface), so measurements can be a frame stale; pre-existing behavior, and Fix C's basis (derived from the same `visualBounds` measurement) does not add a new dependency on fresh layout.

## Follow-up (2026-07-03): HistoryPanel frame-border separation

First in-game pass: LiveBuildPanel rows read as correct (gap regime, aspect > 4.333 as predicted), but HistoryPanel — always board-cap-bound at its ~2:1 container — reproduced the native ~4.9 % body touch exactly, and at that proportion the tier-frame borders interleave 9.5 % (S–S) to 11 % (M–M) of a pitch, which the user reads as overlap.

Resolution (user-selected: full border separation over body-tiling): a stricter per-consumer preset `ItemBoardPreviewOptions.FrameSeparationSlotGridMaxHeightRatio ≈ 0.81692`, derived from the medium frame anatomy (`NativeMediumFrameWidthOverRoot = 1.05787`, `NativeMediumBodyAspect = 1.04` — medium is the widest frame per span, so it binds; small cards then gap slightly; the Large flourish still overhangs by native design). HistoryPanel opts in via its options; the shared default and LiveBuildPanel are untouched. History preview cards render ~10 % shorter than the native-touch proportion in exchange for silver borders never crossing.
