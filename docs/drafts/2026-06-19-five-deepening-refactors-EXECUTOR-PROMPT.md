# Executor handoff prompt — five deepening refactors

> Copy everything below the line into the executor agent. It is written to be self-contained: that agent has no memory of how this plan was produced. It is expected to drive sub-agents.

---

You are implementing a confirmed, red-teamed batch of four internal architecture refactors in the BazaarPlusPlus mod (a C# / netstandard2.1 / BepInEx 5 / Harmony game mod for *The Bazaar*).

**Repo root:** `/Users/yxinyu/codes/bpp/bazaarplusplus-mod`

## 0. Read these first (authoritative; do not improvise around them)

1. `docs/drafts/2026-06-19-five-deepening-refactors-plan.md` — **the authoritative design.** It contains the per-candidate target module, exact interface, files added/changed/deleted, migration steps, red-team fixes already folded in, the risk register, and the locked decisions (§6). Implement exactly this. If you find the design is wrong against the real code, STOP and report — do not silently deviate.
2. `CLAUDE.md` (repo root) and `docs/MEMORY.md` — process rules and durable gotchas. Re-read the "Mod-specific traps" and the Rules section.
3. The actual source files cited in the plan. **Code is the source of truth.** `decompiled/` is read-only reference — never edit it.

## 1. Scope (locked — do not expand or re-open)

Implement, in this order, one git branch per candidate, merged to `master` sequentially only after a review gate:

1. **C1 — `BackgroundUploadPump`**: collapse `RunUploadController.cs` + `BazaarDbSnapshotUploadController.cs` into one `IUploadFeed`-parameterized pump (`Game/Upload/`). Delete both controllers.
2. **C4 — Muxer owns the output decision**: add `ReplayVideoAudioMuxer.Resolve(...)` that owns the 4-way "what is the final video file" decision; the muxer **receives a resolved `string? ffmpegExecutable`** (it must NOT call `FfmpegLocator.Resolve` itself). Extract the tap usable/delete rule as a pure static helper. Delete dead overloads + `_muxTasks`.
3. **C2 — `CollectionQuery`**: lift the filter pipeline out of `CollectionPanel.cs` into a pure `static CollectionQuery.Run(...)` returning ordered cards + offer matches + a normalization the panel adopts. **Keep `PruneInvisibleSourceSelections` calls** — `Run` does not subsume them.
4. **C3 — HistoryPanel testable core (trimmed)**: extract `DeleteConfirmation` (RunId+ExpiresAt only, clock-injected), `CanDeleteRun`+`ResolveDatabaseChip` de-dup, `HistoryPanelButtonModel`+builder; inline `HistoryPanelPreviewSource`. **Do NOT** extract the ghost filter and **do NOT** inline `HistoryPanelDataService` (both are reflection/no-op-pinned — see plan).

**C5 is intentionally NOT in scope.** Do not remove `IGameStateProbe`/`IRunContext`/`IBppConfig`; they have a live test fake. Do not re-suggest it.

At the very end of the batch (after C3 is approved): bump `BppVersion 4.2.1 → 4.3.0` in `Directory.Build.props`, one commit.

## 2. Hard operating rules (from the repo's CLAUDE.md — violations get the work rejected)

- **No old+new dual paths.** When you replace a controller/method, delete the old one in the same commit that wires the new one. No fallback, no feature flag, no parallel chain.
- **Touch only the named target.** Do not opportunistically refactor unrelated code or adjust unrelated config.
- **Reuse native UI + existing prior-art patterns** (e.g. `HistoryPanelMount`/`CollectionPanelMount` for the new `UploadPumpMount`); do not hand-roll new chains.
- **Do not build standalone probe/diagnostic scaffolding.** If something needs in-game verification, record it as a to-verify item for the user; do not create throwaway harness projects.
- **Keep commits scoped.** If `csharpier`/`./run.sh format` reformats files outside your change, commit that reformatting separately.
- **Do not edit `.rules`/`CLAUDE.md`/`MEMORY.md` inline.** If you discover a reusable trap, collect it under a "Suggested rule additions" heading in your final report.
- **Review your own diff before every commit.** Do not commit unreviewed.

## 3. Build / test / format discipline (every commit must be green)

Per commit, run from the repo root:

```bash
./run.sh build      # Debug; auto-copies to BepInEx/plugins if the game is found
./run.sh test       # runs every test project under tests/
./run.sh format     # csharpier
```

Gotchas (these have bitten before — see MEMORY.md):
- **`./run.sh test` exit code is NOT sufficient.** exe-runner `Program.cs` projects can fail a check yet exit 0. **Grep the full output** for `"Failed test projects:"` and per-runner failure lines; do not tail-truncate.
- **Test projects come in two shapes.** xUnit (has `Microsoft.NET.Test.Sdk`, run via `dotnet test`) vs exe-runner (`<OutputType>Exe</OutputType>` + `Program.cs`, run via `dotnet run --project ...`). Check the csproj before running one directly.
- **exe-runner csprojs pin source files via explicit `<Compile Include>`.** Moving/renaming/deleting a shared source file breaks them silently. When you add a new pure module that a test needs, add it to the test csproj's Compile-Include list — and for C2, include ONLY the dictionary-fake path, never `CollectionSourceCatalog.cs`/`StaticCollectionSourceCatalog.cs` (they pull `Newtonsoft.Json`+`Infrastructure`, absent from that test project).
- If you build from a **git worktree** under `.claude/worktrees/...`, pass `-p:BPPInstallerSourcePath=<abs path to installer resources>` or the build fails with MSB3030. Do NOT edit `BazaarPlusPlus.csproj` to "fix" it.
- Do not change the `BazaarPlusPlus.csproj` build-and-copy flow; it is load-bearing.

## 4. How to use sub-agents (per candidate)

For each candidate, run this loop. Keep the candidate on its own branch (`git checkout -b yxinyux/c1-upload-pump` etc.).

1. **Implement** — either do it yourself or delegate to one implementer sub-agent. Give it the relevant §3 slice of the plan doc verbatim, the file list, and rules §2–§3. Follow the migration steps' commit granularity (each sub-step builds+tests green).
2. **Independent review (mandatory gate, review-only)** — spawn a *separate* reviewer sub-agent that did NOT write the code. Its job: verify the diff against the plan and the real code, find correctness breaks, missed call sites, lifecycle/async hazards, Compile-Include pins, and confirm the red-team fixes from the plan are actually present. It applies NO patches — it reports `file:line` findings. Feed its findings back to the implementer and iterate until clean.
3. **Verify** — run `./run.sh build` + `./run.sh test` (with the grep) + `./run.sh format`. Capture the evidence.
4. **Report + STOP at the merge gate** — do not merge to `master` yourself. Summarize the branch (commits, diff stat, what each red-team fix did, test evidence, and the in-game checklist for that candidate from the plan's "USER must validate in-game" list). Hand it back for human review. Merge only after approval; then move to the next candidate.

If you choose to parallelize the mutually-independent candidates (C2/C3/C4 touch disjoint dirs), each parallel worker MUST use its own git worktree (they mutate files), and mind the worktree build flag in §3. The safe default is sequential, one branch at a time. C1 must land before C4 (both touch `BppComposition.cs`).

## 5. In-game validation

You cannot reliably validate game behavior headlessly. For anything in the plan's per-candidate "USER must validate in-game" list, **do not claim it verified** — surface it as an explicit checklist item the user runs (build + reload; launch The Bazaar via Steam App ID 1617400). Your "green" bar is: builds + all test projects pass (verified via the grep) + independent review clean.

## 6. Definition of done (for the whole batch)

- Four branches (C1, C4, C2, C3), each build+test-green, each independently reviewed, each with its in-game checklist, presented for review at its merge gate.
- After C3 approval: `BppVersion → 4.3.0` commit.
- A final report: per-candidate diff stat + what deepened (interface that shrank, behavior that moved behind it, lines deleted), the consolidated in-game validation checklist, any "Suggested rule additions", and confirmation that no item from C5 leaked in.

Start with **C1**. Read the plan doc fully before writing any code.
