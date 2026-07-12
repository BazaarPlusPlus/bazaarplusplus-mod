# End-of-run screenshot / run-record regression diagnosis

## Background

- Commit `f556be58` replaced a fixed eight-second end-of-run screenshot delay with native reveal readiness detection.
- The readiness path waits for the summary display latch, native transition completion, card `FaceUp` state, and the skill reveal sequence before capturing.
- A first follow-up attempted to recover Replay-leaked pause state at end-of-run initialization and moved gate deadlines from scaled to unscaled time.
- That follow-up passed the source-level gate runner and compiled, but it had not yet been validated through a complete live run before being described as fixed.

## Current problem

After installing the latest local Debug DLL and completing a live run, the user reports that the result was not saved. “Result” must be separated into two independently verifiable artifacts:

1. the run row / completion data in `bazaarplusplus.db` used by HistoryPanel and uploads;
2. the automatic end-of-run PNG plus its screenshot metadata row.

The immediate task is to identify which artifact is missing, reconstruct the latest run timeline from logs and SQLite, and determine whether the screenshot gate, run lifecycle, or deployment/version provenance caused the loss.

## Candidate mechanisms

1. **Capture readiness still never reached `Ready`.** Prediction: latest Player log has reveal start without reveal completion; BPP log has no successful capture; no new PNG exists, while the run completion row may still exist.
2. **Resuming game time during `EndOfRunScreenInitializing` changed lifecycle timing.** Prediction: run completion / run-ended handlers are missing or interrupted after the end-of-run event, and both run completion data and screenshot metadata are absent.
3. **The screenshot was written but metadata persistence or run-id association failed.** Prediction: a new PNG exists, but its SQLite screenshot row is absent or has a null/wrong run id.
4. **The tested DLL was not the intended build or the game had already loaded an older DLL.** Prediction: process start time predates deployed DLL mtime, or BPP version/log provenance does not match the deployed build.
5. **“No saved result” is a HistoryPanel/read-path issue rather than persistence loss.** Prediction: the latest completed run exists in SQLite but is filtered, malformed, or not rendered in HistoryPanel.

## Verification method

- Record DLL hash/mtime, version file, game process lifetime, and latest log timestamps.
- Read the latest BepInEx and Unity Player logs around `RunEnded`, end-of-run scene initialization, reveal start/completion, capture start/completion/fallback, and run persistence.
- Query SQLite schema first, then inspect the newest run, completion, event, and screenshot records without modifying the database.
- Compare the newest PNG timestamp and run id with the database and log timeline.
- Only after one candidate is confirmed, add a regression test at the responsible seam, implement one change, rebuild/deploy, and repeat the same verification checklist.

## Completion criteria

- A live run produces a completed run record visible to the HistoryPanel path.
- The final reveal settles and produces exactly one primary end-of-run screenshot linked to that run.
- Continue becomes available without relying on a long fallback.
- Logs show no capture/readiness/persistence exception, and the installed DLL hash matches the tested build.

## Confirmed findings

- Both live test runs were persisted as completed rows: `29b50554-de40-4e63-9047-93fd7b614d28` and `813330b5-d42c-45d4-a3c2-898d9dac6063`.
- The first run completed native reveal and saved/linked `2026-07-12_14-49-31-410_final_run-29b50554-de40-4e63-9047-93fd7b614d28.png`.
- After the first end-of-run scene was destroyed, `EndOfRunMouseBlocker` retained an `EndOfRunInputCaptureSink` managed wrapper whose Unity native component no longer existed.
- `DestroyBlocker` used C# null-conditional invocation (`_inputSink?.ReleaseFocus()`), which checks CLR null rather than Unity's overloaded destroyed-object equality. `ReleaseFocus` then accessed `gameObject` and threw.
- The exception aborted both the next `OnRunStarted` reset and the next `OnEndOfRunScreenInitializing` reset before the capture gate could be rearmed. The second run row therefore persisted normally, but no end-of-run screenshot was captured.

## Confirmed fix direction

- Treat every blocker Unity object through Unity's overloaded null checks before invoking members.
- Make `EndOfRunInputCaptureSink.CaptureFocus` and `ReleaseFocus` independently tolerate a destroyed native component.
- Add a regression assertion that the mouse blocker contains no null-conditional calls on retained Unity object wrappers.

## Reproduction after stale-reference fix

The user reports that the problem still reproduces after the stale Unity wrapper guard was built and deployed. Before another implementation change, the next investigation must:

- prove that the new process loaded the post-fix DLL rather than the DLL merely being replaced on disk;
- compare the newest run row, screenshot row, and PNG against the exact latest end-of-run timestamp;
- read the complete latest exception stack rather than assuming the previous `ReleaseFocus` NRE recurred;
- reverse the working assumption and check whether persistence succeeded but HistoryPanel presentation or the user's expected artifact did not.

### Results

- Process `28307` started after the post-stale-reference DLL was deployed, and the installed/build DLL hashes matched. The stale `ReleaseFocus` NRE did not recur.
- Run `4d4748fd-b57f-46aa-a2b4-a4bbee120e19` persisted as completed, but produced no screenshot.
- The native log again stopped after `Starting card reveal sequence`; there was no completion line or later exception.
- This disproves the stale-reference exception as the primary cause of the original first-run capture failure. It was a real second-run bug, but only a secondary defect.

### Temporary probe design

The main path temporarily emitted a uniquely tagged diagnostic on readiness changes and every three seconds. It recorded:

- readiness and reveal-start latch;
- `Time.timeScale` and `GameServiceManager.GamePaused`;
- loaded/face-up/face-down/animator-null card counts;
- skill sequence duration/completion;
- reveal PlayableGraph validity, root count, and root-0 done/time/duration.

One clean restart and fast end-run reproduction distinguished a paused clock, a completed-but-unobserved graph, and a genuinely stuck card animation. The probe was removed after validation.

### Probe run results

- Run `e7ef3e28-162c-4ae8-898d-4939b0b7b133` loaded the intended probe DLL and persisted as completed.
- Across realtime 39.621–54.647, `Time.timeScale` remained `1.000`; two loaded cards remained FaceDown; the skill sequence had zero duration.
- Pause leakage is therefore disproved for this reproduction.
- The first graph probe reported `null`, but `_playableGraph` is declared on `BaseCardRevealAnimationDriver` while the runtime object is `CardEndOfRunAnimationDriver`: the base class owns the private field, the runtime driver inherits that base, and the summary controller instantiates the derived driver (`decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/BaseCardRevealAnimationDriver.cs:18,88-92`; `decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/CardEndOfRunAnimationDriver.cs:7`; `decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunSummaryController.cs:128-134`). The probe used a derived-type-only `GetField`, so `null` meant “field lookup failed or value null” and was not valid graph evidence.
- The graph probe now walks the inheritance chain and reports field-missing separately from a real null graph.
- The inherited-field probe run then confirmed a valid graph with four roots while all four cards remained FaceDown and game time stayed at `1.000`.
- Root state/time/duration were still unavailable because the probe attempted to reflect `Playable` extension methods as instance methods. The probe now unboxes `PlayableGraph` and calls the strongly typed Unity Playables API for every root.
- The strongly typed probe reproduced the failure with two roots. Both remained `Playing`, both advanced from `0.008` to `18.019` seconds, both stayed `IsDone == false`, and both reported `double.MaxValue` as their duration. The cards remained FaceDown throughout.
- The native raw builder selects its raw subgraph for every card and wires each `DelayPlayableBehavior` root only to port 0 of an `AnimationPlayableOutput` (`decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/BaseCardRevealAnimationDriver.cs:224-249`). The behavior's completion check exists exclusively in `ProcessFrame`, where completed inputs cause `SetDone(true)` (`decompiled/TheBazaarRuntime/TheBazaar.Animation.Playables/DelayPlayableBehavior.cs:48-59`); [Unity's `ProcessFrame` API contract](https://docs.unity3d.com/2020.2/Documentation/ScriptReference/Playables.PlayableBehaviour.ProcessFrame.html) invokes that process pass only for playables connected directly or indirectly to a `ScriptPlayableOutput`.
- The sibling non-raw subgraph creates exactly that missing `ScriptPlayableOutput` and connects the same delay root's port 1 (`decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/BaseCardRevealAnimationDriver.cs:252-300`). `Play` waits until every root reports done before it restores state and returns (`decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/BaseCardRevealAnimationDriver.cs:149-193,201-204`), while the end-of-run controller awaits `Play` before setting `FaceUp` and logging reveal completion (`decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunSummaryController.cs:547-559`). Without graph completion, BPP's detector keeps a face-down card in `RevealInProgress`, so automatic capture cannot enter `Ready`, and the Continue prefix remains blocked by the screenshot gate (`src/BazaarPlusPlus/Game/Screenshots/EndOfRunSummaryRevealDetector.cs:62-95`; `src/BazaarPlusPlus/Game/Screenshots/EndOfRunCaptureReadinessDetector.cs:56-64`; `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotGate.cs:32-87`; `src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunScreenshotPatch.cs:9-16`).

### Final ranked hypotheses

1. **Confirmed: raw graphs omit the process-pass output.** The raw/non-raw topology difference and the delay behavior's only `SetDone(true)` path are visible at `decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/BaseCardRevealAnimationDriver.cs:224-249,297-300` and `decompiled/TheBazaarRuntime/TheBazaar.Animation.Playables/DelayPlayableBehavior.cs:48-59`. Adding the same port-1 `ScriptPlayableOutput` used by the non-raw builder should make each root become done after its clip input completes; native reveal completion, `FaceUp`, capture, and Continue should then occur without the fallback.
2. **Alternative: root duration alone prevents completion.** If this were sufficient by itself, adding the process-pass output would still leave roots playing at `double.MaxValue`; live validation will distinguish it.
3. **Alternative: `FaceUp` is not a reliable settled-frame signal.** If graph completion is restored but capture still does not start, logs will show native completion with cards remaining FaceDown, isolating detector semantics from the native graph deadlock.

### Fix seam and regression coverage

- The minimal seam is the Harmony postfix on `BaseCardRevealAnimationDriver.CreateRawGraph`, which adds one `ScriptPlayableOutput` per root and sources output port 1 (`src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunRawRevealCompletionPatch.cs:8-30`). `Play` selects `CreateRawGraph` only when `useRawAnimation` is true, and the decompiled runtime's two true-valued call sites are the end-of-run reveal and disable sequences (`decompiled/TheBazaarRuntime/TheBazaar.Game.Cards.Animation/BaseCardRevealAnimationDriver.cs:149-169`; `decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunSummaryController.cs:547-576`).
- There is no correct automated behavioral seam in the current test host: Unity's PlayableGraph scheduler and `ProcessFrame` callbacks require a running Unity player. The executable test can pin the patch topology, but only a live game run can prove the callback executes and the graph completes.
- Live acceptance requires native `Card reveal sequence completed`, a BPP saved-screenshot line, a new PNG, and a `run_screenshots` row linked to the newest completed run.

### Live validation

- The game process started at 15:31:20 after loading deployed DLL SHA-256 `272656c5a7988a0e568c1ff7519c051db0bf3b6953cf641caac22470d622b31f`.
- On run `499b889f-e95a-44c9-bfe6-237cc0e2bfe8`, three raw-graph roots began playing at realtime 59.075. At realtime 64.916 the native graph had been destroyed normally and all three card animators were FaceUp, so readiness became `Ready` without the 20-second fallback.
- Player.log recorded `Card reveal sequence completed` at 15:32:29.221 followed by `EndOfRun card update done`.
- `ScreenshotService` saved `2026-07-12_15-32-29-241_final_run-499b889f-e95a-44c9-bfe6-237cc0e2bfe8.png` (6,577,129 bytes).
- SQLite contains completed run `499b889f-e95a-44c9-bfe6-237cc0e2bfe8` and primary screenshot row `a272897416cd40b8bf05667222b3e1dc` linked to that run.
- The user confirmed the interaction now appears fixed. The temporary diagnostic probe and the disproved pause-resume workaround were removed; the raw-graph completion patch, unscaled fallback/retry deadlines, and independently reproduced Unity destroyed-wrapper guards remain.

### L3 debugging checklist

- [x] Read the complete latest BepInEx/Player failure signals and timestamps.
- [x] Searched all current `EndOfRunScreenshot`, native reveal, exception, SQLite, and PNG evidence.
- [x] Read the detector, native reveal driver, `TaskUtils`, blocker lifecycle, logger suppressor, and HistoryPanel read path sources.
- [x] Verified process start, DLL mtime/hash, config, database WAL, run rows, screenshot rows, and native time state.
- [x] Reversed assumptions: checked persistence/UI instead of screenshot, clean first-run instead of second-run stale state, and non-pause causes instead of pause leakage.
- [x] Reduced reproduction to a fresh process, immediate day-one abandon, and a single end-of-run readiness timeline.
- [x] Changed method from speculative timeout patches to main-path state instrumentation covering native graph/card/skill state.
