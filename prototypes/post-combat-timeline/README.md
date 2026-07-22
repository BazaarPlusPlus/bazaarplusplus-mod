# Post-combat timeline prototype

> PROTOTYPE — local-only HTML report for testing post-combat log analysis. Do not ship this directory as production code.

Run from the repository root:

```bash
node prototypes/post-combat-timeline/serve.mjs 8765
node prototypes/post-combat-timeline/verify-report.mjs http://127.0.0.1:8765/
```

The local server implements byte-range responses for MP4 seeking. A generic static server that ignores `Range` requests is not representative for hover preview testing.

Open <http://127.0.0.1:8765/?variant=D&lang=zh-CN> or <http://127.0.0.1:8765/?variant=D&lang=en-US>.

## Current direction

- One report surface rather than four design variants.
- Viewer chrome is backed by local `zh-CN` and `en-US` message packs. The language switch updates the `lang` query parameter and rerenders the existing imported battle without changing its language-neutral enum/numeric data; game entity names remain the captured snapshot values rather than being machine-translated.
- Product information and implementation diagnostics occupy separate visual levels; the library/version and raw profiler string are not part of the normal report UI.
- The document itself never overflows horizontally. The timeline uses a fixed 80 px/s default scale and its total width depends only on combat duration; narrow viewports expose native horizontal scrolling instead of compressing the fight.
- A sticky entity rail is rendered in DOM; ECharts renders fixed-width state and event fields on two Canvas surfaces. Zoom changes px/s, while native `scrollLeft` pans without rerendering the charts.
- The decision-oriented event projection omits generic rage, regeneration and shield Apply markers; those values appear in the state tracks. A Rage Apply record whose executing source is a skill remains visible on that skill's lane, while the state chart stays authoritative for the actual Rage value. The skill marker shows an exact delta only when one same-frame, same-target update is uniquely attributable; otherwise it explicitly reports that no numeric change was recorded or directs the reader to the state chart.
- Haste, slow and freeze ranges use flat fills with hard boundaries and report-local native icons.
- Hovering a haste, slow or freeze range reveals and highlights every recorded source, trigger and affected item; clicking pins the same relationship in the detail panel.
- Item thumbnails are full native `CardPreviewItem` composites materialized after an explicit Replay. Small/Medium/Large retain the exported card-frame ratios (`0.542 / 1.058 / 2.084`) instead of being forced into square or 1:2:3 geometry; skill art keeps a 1:1 circular viewport. Item borders and skill rings encode the captured tier, while the separate end marker continues to encode player/opponent ownership.
- The ruler and entity rail remain sticky inside one two-axis scroll surface, so their alignment stays exact during horizontal and vertical navigation.
- Health, rage, regeneration and shield share one chart. Metric color identifies the value, solid/dashed lines identify player/opponent, and missing-side updates become an explicit zero track rather than disappearing.
- The state chart can switch between a linear axis and a signed magnitude axis whose integer ticks map to `0 / ±1 / ±10 / ±100 / …`. The latter keeps zero and negative terminal health valid while making exponential values readable; tooltips always show the raw value.
- Hovering an actual state-change point replaces the overview snapshot with a causal card: delta, before/after values, occurrence time and related sources. Sources are correlated only from same-frame, same-target effect records whose action matches the settlement; persistent burn/regeneration ticks without a recorded applier are labelled as settlement fallbacks instead of being guessed.
- Generic `CardModifyAttribute` effect dots are replaced by the resulting non-routine card-attribute updates. Their hover cards show the localized attribute name, delta, before/after values, trigger and same-frame related sources; routine cooldown/status countdown records remain omitted.
- A separate statistics tab places damage/healing/shield output beside conditional damage composition, then gives charge/haste/slow/freeze applications a full-width linear comparison row. Zero damage types render an empty state, one type renders a direct summary, and only multiple types render 100% stacked bars. The UI names those four actions directly instead of grouping them under an invented “tempo” label.
- Event glyphs keep their compact visual hierarchy while a separate 20px transparent hit series makes hover and click reliable. Solid marks mean “施加”, hollow marks mean “受到”, and native skill art means “触发”; hover repeats the same roles as 施/受/触 badges on the related lanes, while click pins the relationship and opens the detail panel.
- Distinct event groups that share one entity and timestamp fan out vertically inside the same lane while retaining their exact time value. Records with the same timestamp, source, trigger and action remain one semantic node and expose an `×N` badge, so neither distinct actions nor repeated executions are silently hidden behind the last-painted glyph.
- Hovering any event in a crowded simulation frame shows one compact same-frame affordance. Clicking the event or its time column opens a bounded, scrollable frame inspector that groups every projected event by combat, numeric-state, and status type; each row then drills into its own source/target/raw-record detail.
- Damage, healing, regeneration, shield, burn, poison, charge and destroy events use the game's local `fonts_assets_all.bundle` sprites. The solid native icon marks an applied event, a hollow carrier marks a received event, and a diamond carrier remains reserved for a skill trigger; rage keeps its neutral diamond because the game bundle has no semantically exact rage icon.
- Combat hover cards separate role, event name, explicitly labelled occurrence time, semantic value and source/target path. Health and shield settlements display positive magnitudes such as `伤害 70` or `护盾损失 5`; a timestamp can no longer read like the event's unit. State changes call a source direct only when one matching effect and at most one settlement uniquely explain the update; same-frame collisions are labelled as related sources, and source-less persistent ticks are labelled as settlement evidence.
- The native haste/slow/freeze icon appears once at the start of each sustained interval instead of being repeated for both cause and duration.
- Both combatants use the matching local default-skin `PreviewCollection` portrait and the same 128×128 thumbnail pipeline; the player side no longer falls back to an unrelated line-art icon.
- Item previews are materialized by the local game runtime; skill, hero, event and status sprites are copied from the local game installation. No report resource is loaded from a CDN.
- The HTML entry point, stylesheet, and application share one revision contract. Mutable local HTML/JS/CSS/JSON responses use `no-store`; if a tab ever combines new markup with an old stylesheet, the viewer reloads once and then shows an explicit version error instead of leaving unbounded images or typography on screen.
- When a battle-matched recording with captured/exact sync metadata exists, its local MP4 uses a collapsible full-width workbench above the timeline. Paused hover requests a throttled preview seek, movement coalesces to the newest request while one seek is in flight, and leaving restores the last committed frame. Legacy recordings without the sync contract render only a compact re-record notice.
- Video time is mapped to combat time through monotonic piecewise anchors instead of a single duration ratio. Repeated combat frames are preserved on the playback path, because one simulation frame can legitimately occupy several encoded frames. Seeking to a combat frame chooses its first recorded media frame.
- The bundled historical recording predates sync metadata and is therefore labelled as estimated alignment. The UI never upgrades an estimated or manually calibrated mapping to a recorded/PTS mapping.

The fixture is battle `483f629c36564d9ca4b4f55dea584cc6`: 171 simulation frames, 8.5 seconds of combat time, 2,527 raw events, and a legacy 15.33-second 60 fps H.264 recording. Import accepts projected timeline JSON, gzip JSON, or a viewer envelope with a `battle`/`Battle` property. The viewer does not replace recordings or synthesize missing sync metadata.

## Recording sync metadata contract

The recorder must create an append-safe sidecar when capture starts and add an anchor for every frame successfully submitted to the encoder. Recording the association at capture time is essential: video duration, encoded frame number and combat time are different clocks, and cannot be reconstructed reliably from the final MP4 alone.

Each anchor carries:

```json
{
  "combatFrame": 29,
  "combatMs": 1450,
  "captureClockMs": 1826.438,
  "captureFrameIndex": 60,
  "cfrSlotIndex": 60,
  "encoderFrameIndex": 60,
  "mediaPtsMs": 1000.0
}
```

- `combatFrame` / `combatMs`: latest observed simulation position when the frame was captured.
- `captureClockMs`: monotonic recorder clock at the source pixel capture request, relative to recording start; never wall-clock time.
- `captureFrameIndex`: source pixel-capture sequence. Repeated CFR output frames intentionally reuse this value.
- `cfrSlotIndex`: wall-clock CFR slot, including slots later skipped during catch-up or backpressure.
- `encoderFrameIndex`: frame sequence actually submitted to FFmpeg, including duplicated frames.
- `mediaPtsMs`: display timestamp in the finalized video track. It is appended or materialized after the encoder closes; until then, `encoderFrameIndex / fps` is only a captured-frame estimate.

Capture and submission are two stages. The recorder snapshots `combatFrame`, `combatMs` and the monotonic clock when requesting the source pixels, retains that context with the completed readback, then joins it to each successful CFR submission in `EmitDueFrames`. A repeated submission therefore points back to the same source capture instead of incorrectly adopting the newer combat time at which the repeat was emitted. Metadata records should be queued and flushed in batches by a background writer; the Unity render loop must not synchronously fsync one record per frame.

Repeated `combatFrame` values are expected when rendering stalls or the recorder emits duplicate video frames. Gaps in source capture indexes and CFR slot indexes are diagnostic data, not values to normalize away; successful `encoderFrameIndex` values remain contiguous because they describe the actual FFmpeg input stream. Finalization reconciles the sidecar with the surviving video track, trims anchors beyond the finalized frame count, and attaches media PTS. The viewer accepts monotonic media anchors, preserves repeated combat positions for playback, and drops regressing/corrupt anchors rather than producing a false inverse mapping.

This still does not mean a game event and the pixels showing its effect are guaranteed to be the same instant: event observation, Unity rendering and capture can be separated by one or more frames. The strongest UI label is therefore “PTS anchor” rather than “frame-perfect”.
