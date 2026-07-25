# Combat report asset orientation — repeated failure analysis

## Background

The post-combat HTML Viewer displays immutable, content-addressed PNG assets produced by the
Unity-side report asset pipeline. Item cards, skills, hero portraits, and status icons do not share
one render path: some originate from native textures/sprites copied through a `RenderTexture`,
while item cards can come from an offscreen native card renderer.

This area has already failed repeatedly:

1. wide item cards had incorrect transparent bounds and scaling;
2. item/skill art was visually reversed in earlier acceptance reports;
3. the v11 acceptance report still shows skill art vertically inverted.

Because installed Viewer releases and cached content-addressed assets are immutable, an orientation
fix must produce a new renderer/cache identity and new report. It must not rewrite old cache objects
or disguise the bytes with Viewer-specific CSS.

## Current problem

Observed in
`reports/__codex-v11-acceptance.html`: a skill lane image is vertically inverted relative to the
game-native presentation. The selected `Static Acceleration` entity resolves to:

- template ID `32e45628-c864-4f99-a2e8-94efe6f37cdc`;
- content key
  `sha256-84f32d75522089c271f9d986abe9922284bc6b5a53b5865d020fe6674a420289`;
- renderer `native-texture-copy`, renderer version `1`, asset type `skill-art`.

The affected element is a normal `<img class="bpp-lane-image">`. Viewer CSS uses only
`object-fit: contain` for lane and statistics images and applies no vertical transform.

The cached PNG bytes themselves are inverted. Three asymmetric skills were compared to their
current BazaarDB card-art references after resizing the references to the exported 512×512 size:

| Skill | RMSE as exported | RMSE after vertically flipping export |
| --- | ---: | ---: |
| Static Acceleration | 0.317138 | 0.025180 |
| Peaceful Eye | 0.362212 | 0.023987 |
| Searing Flames | 0.277427 | 0.016679 |

The remaining error is expected from WebP resizing/compression. All three comparisons independently
show that the exported PNG is the reference image reflected vertically, so this is a render-path
defect rather than a template-specific asset defect.

## Candidate cause mechanisms

### A. GPU readback origin is normalized twice or not normalized

Unity's source texture, `Graphics.Blit`, camera render target, `AsyncGPUReadback`, PNG encoder, and
browser image coordinates do not all use the same vertical origin on every graphics API. A global
`graphicsUVStartsAtTop` condition can be correct for one path and wrong for another if a prior stage
already inverted the image.

**Confirmed.** `NativeReportTexturePngExporter` currently uses
`!SystemInfo.graphicsUVStartsAtTop ^ invertReadbackVertically`. On the direct skill path the optional
toggle is omitted, while the offscreen item path passes it. The three exported skill PNGs prove that
the direct material-texture path needs the opposite normalization from the direct Unity-sprite path.

### B. Render paths require explicit orientation contracts

Direct texture/sprite copies and offscreen camera renders may need different source-origin metadata.
An optional boolean passed by callers is too easy to invert semantically. The exporter should expose
an explicit orientation contract per render path and normalize exactly once before encoding.

**Confirmed.** The current generic `ExportTextureAsync` is shared by raw skill material textures and
Unity sprites. The game loads skill art as a `Texture` and binds it directly to `_BaseMap`
(`SkillIconUpdater.UpdateSkillIconMaterial`), whereas report hero/status art is materialized from
Unity `Sprite` geometry. Treating both sources as one unnamed orientation is the architectural gap.

### C. Current bytes are correct but the report references an old cache object

Content-addressed cache entries are append-only. Fixing exporter code does not change an old report's
`ContentKey`, and an unchanged renderer key can keep reusing a previously inverted asset.

**Confirmed as a second defect.** `PostCombatReportNativeCardAssetExporter` still declares
`DirectRendererVersion = "1"`. Even after correcting pixels, an unchanged render key would resolve
the existing inverted mapping without invoking Unity materialization.

### D. Viewer presentation flips the image

A CSS `transform`, negative scale, or transformed ancestor could invert otherwise correct bytes.
This must be ruled out, but CSS compensation is not an acceptable fix because the same cached asset
is reused in lane labels, statistics, inspectors, and future Viewer versions.

**Ruled out.** Neither `.bpp-lane-image` nor `.bpp-activity-image` has a transform, and the only
negative-scale rule belongs to the PiP resize handle.

## Root cause

There are two coupled causes:

1. the report exporter applies the Unity-sprite readback normalization to raw skill material
   textures, although those two source types have opposite vertical semantics on the current Metal
   path;
2. the skill renderer identity remained at version `1`, so the immutable cache keeps serving the
   already inverted object.

## Revised implementation plan

1. Replace the optional inversion boolean with an explicit source-orientation enum covering
   `UnitySprite`, `MaterialTexture`, and `OffscreenCamera`.
2. Put the platform/source truth table in a pure helper and unit-test both top-origin and
   bottom-origin graphics APIs. `MaterialTexture` and `OffscreenCamera` retain the observed
   normalization; `UnitySprite` retains the already accepted portrait/status behavior.
3. Make every exporter call site name its source contract:
   - skill art → `MaterialTexture`;
   - hero/status/other sprite art → `UnitySprite`;
   - native item card capture → `OffscreenCamera`.
4. Bump only the skill renderer identity from `1` to `2`. Do not mutate old cache mappings or
   content objects, and do not unnecessarily invalidate upright item/portrait/status assets.
5. Add source-level architecture assertions so a future skill export cannot silently fall back to
   the sprite/default orientation.
6. Build and run cache/architecture/report tests, then generate a new report with the current
   Viewer release and newly materialized renderer-v2 skill assets. The frozen v11 report remains
   historical evidence, not the acceptance artifact.

## Verification

The fix is accepted only when all of the following hold:

- a newly generated report uses a new asset renderer/cache identity;
- at least three asymmetric skill icons are upright when their PNGs are viewed directly;
- direct PNG comparison against the same three references is closer without a vertical flip than
  with one;
- the same three icons are upright in every surface that renders skill art (currently lane labels
  and the statistics table), while the frame inspector's separate status-effect icons remain
  upright;
- Small/Medium/Large item card orientation remains unchanged and upright;
- hero portraits and status icons remain upright;
- no Viewer CSS rule contains `scaleY(-1)`, a 180-degree rotation, or asset-type-specific flip;
- old immutable reports and cache objects are not overwritten;
- the asset-cache tests prove an old render key is not reused after the orientation-version bump;
- browser acceptance is performed at 1199×857 and one narrow viewport with no broken images.

## Verification results — 2026-07-23

The accepted plan was implemented without changing frozen Viewer or cache artifacts:

- `ReportAssetReadbackSource` now makes `UnitySprite`, `MaterialTexture`, and
  `OffscreenCamera` explicit at every export call site.
- `ReportAssetPixelOrientation.RequiresVerticalFlip` owns the platform/source truth table and is
  covered for both values of `graphicsUVStartsAtTop`.
- skill assets now use renderer version `2`; item, portrait, and status renderer identities were not
  invalidated.
- no Viewer CSS transform was added.

A Debug build was installed and a real battle was recorded again through the F8 History Panel. The
new immutable report is
`reports/473037830df44c62849b5666174988a0.html`, references Viewer `v12`, and materialized 29 cache
misses with the new code. `Static Acceleration` now resolves to
`sha256-e64e271d8551f3c45a8f27a7effa7ba2376b9d22b06bca5fe9acba087fb3a660`;
its render-key metadata records `rendererVersion: "2"`. The frozen v11 report and its version-1
content object were not modified.

Three asymmetric renderer-v2 skill exports were compared with their BazaarDB card-art references:

| Skill | RMSE as newly exported | RMSE after vertically flipping new export |
| --- | ---: | ---: |
| Static Acceleration | 0.021806 | 0.274650 |
| Peaceful Eye | 0.020773 | 0.313685 |
| Line Cook | 0.013696 | 0.234101 |

The direct orientation is now an order of magnitude closer in all three cases.

Playwright acceptance used Chrome against the real `file://` report:

- at 1199×857, the Static Acceleration lane image and statistics image both load at 512×512 with
  `transform: none` and `object-fit: contain`;
- at 1199×857 and 480×857, there are zero broken images in the visible UI and no document-level
  horizontal or vertical overflow;
- time zoom from 100% to 200% doubles all three shared-axis canvas widths from 957 to 1914 CSS
  pixels, preserves their alignment, and enables horizontal scrolling;
- selecting the 4.05s marker opens the reversible frame inspector for all 45 same-frame events;
- the current frame inspector uses status-effect icons rather than skill art, so lane labels and the
  statistics table are the two actual Viewer surfaces for skill-art orientation.

Regression gates passed:

- `CombatReplayReportAssetCache.Tests`;
- `Architecture.Tests` — 120/120;
- `CombatReplayReportBundle.Tests`;
- `CombatReplayReportPublicationCoordinator.Tests`;
- `CombatReplayRecording.Tests`;
- `./run.sh build --fast` — zero warnings and zero errors.

## Rejected shortcuts

- Flip all skill images in CSS: hides bad bytes in one surface and risks flipping already-correct
  future assets.
- Overwrite the old SHA object: violates immutable content addressing and silently changes history.
- Special-case the selected skill/template ID: repeats the asset “whack-a-mole” failure mode.
