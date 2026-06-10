---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# AutoBazaar Module Isolation Implementation Plan

> **Status: IMPLEMENTED — 历史归档（spent plan）。** 本计划描述的迁移已全部落地（`AutoBazaar/` 模块 + `Game/AutoBazaarHost/`，`Game/AutoBazaar` 已删，ProjectReference + `#if BPP_AUTOBAZAAR_HOST`）；复选框未回填不代表有未完成项。设计细节见被 ADR-0005 引用的 `specs/2026-06-02-autobazaar-module-isolation-design.md`。保留为历史记录，勿据此重做。*注:此后 commit eb8ad5a(2026-06-05)完成依赖反转与全量改名——`AutoBazaar/` → `src/BazaarPlusPlus.BazaarAgent/`,`Game/AutoBazaarHost/` → 独立程序集 `src/BazaarPlusPlus.BazaarAgentHost/`,`#if BPP_AUTOBAZAAR_HOST` 已删除,详见 ADR-0006。*

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move AutoBazaar into a root-level independently built module while preserving the currently parked runtime behavior.

**Architecture:** Create `BazaarPlusPlus.AutoBazaar.csproj` for pure contracts, transport, validation, queueing, snapshot, logging, and orchestration. Keep Unity, BepInEx config binding, game reads, and game writes in a thin `Game/AutoBazaarHost` adapter owned by the main plugin.

**Tech Stack:** C# 12, `netstandard2.1`, BepInEx 5, Unity, xUnit, Newtonsoft.Json, existing MSBuild copy flow.

---

### Task 1: Split Core Sources Into A New AutoBazaar Assembly

**Files:**
- Create: `BazaarPlusPlus.AutoBazaar.csproj`
- Create: `AutoBazaar/Contract/AutoBazaarDecision.cs`
- Create: `AutoBazaar/Contract/AutoBazaarPorts.cs`
- Create: `AutoBazaar/Decisions/AutoBazaarActionValidator.cs`
- Create: `AutoBazaar/Decisions/AutoBazaarMoveTargetPlanner.cs`
- Create: `AutoBazaar/Decisions/AutoBazaarTargetSelectionActions.cs`
- Create: `AutoBazaar/Diagnostics/AutoBazaarDecisionLog.cs`
- Create: `AutoBazaar/Runtime/AutoBazaarActionQueue.cs`
- Create: `AutoBazaar/Runtime/AutoBazaarContextSnapshot.cs`
- Create: `AutoBazaar/Runtime/AutoBazaarRuntimeController.cs`
- Create: `AutoBazaar/Runtime/AutoBazaarUlid.cs`
- Create: `AutoBazaar/Runtime/SystemAutoBazaarClock.cs`
- Create: `AutoBazaar/Transport/AutoBazaarHttpServer.cs`
- Create: `AutoBazaar/Transport/AutoBazaarResponseJson.cs`
- Create: `AutoBazaar/IsExternalInitPolyfill.cs`
- Delete: pure source files moved from `Game/AutoBazaar/`

- [ ] Move pure files from `Game/AutoBazaar` into `AutoBazaar` folders.
- [ ] Rename namespace `BazaarPlusPlus.Game.AutoBazaar` to `BazaarPlusPlus.AutoBazaar`.
- [ ] Make core DTOs, enums, interfaces, validator, queue, HTTP server, runtime controller, and helpers public where host/tests need them.
- [ ] Remove `BppLog` from core by adding `IAutoBazaarLogger` and passing it into transport/runtime classes.
- [ ] Add `IAutoBazaarOptions`, `IAutoBazaarContextReader`, `IAutoBazaarActionDispatcher`, `IAutoBazaarLogger`, and `IAutoBazaarClock`.
- [ ] Add `AutoBazaarRuntimeController.Tick()` so Unity lifecycle no longer owns HTTP reconcile, snapshot publish, action validation, or decision logging.
- [ ] Run `dotnet build BazaarPlusPlus.AutoBazaar.csproj --no-restore`.

### Task 2: Add The Main Plugin Host Bridge

**Files:**
- Create: `Game/AutoBazaarHost/AutoBazaarBepInExOptions.cs`
- Create: `Game/AutoBazaarHost/AutoBazaarBppLogger.cs`
- Create: `Game/AutoBazaarHost/AutoBazaarGameActionDispatcher.cs`
- Create: `Game/AutoBazaarHost/AutoBazaarGameContextReader.cs`
- Create: `Game/AutoBazaarHost/AutoBazaarHostMount.cs`
- Create: `Game/AutoBazaarHost/AutoBazaarUnityRuntime.cs`
- Move/replace: `Game/AutoBazaar/AutoBazaarActionDispatcher.cs`
- Move/replace: `Game/AutoBazaar/AutoBazaarContextBuilder.cs`
- Move/replace: `Game/AutoBazaar/AutoBazaarSceneProbe.cs`
- Move/replace: `Game/AutoBazaar/AutoBazaarUiPlumbing.cs`
- Modify: `BppComposition.cs`
- Modify: `Core/Config/BppConfig.cs`
- Modify: `Core/Config/IBppConfig.cs`

- [ ] Move game-coupled AutoBazaar files into `Game/AutoBazaarHost`.
- [ ] Rename host namespace to `BazaarPlusPlus.Game.AutoBazaarHost`.
- [ ] Add `using BazaarPlusPlus.AutoBazaar` to host files.
- [ ] Replace direct `BppLog` calls in host context/dispatcher code with `AutoBazaarBppLogger` where the call is now behind a port.
- [ ] Bind `[AutoBazaar] Enabled` and `[AutoBazaar] HttpListenerPort` in `AutoBazaarBepInExOptions`.
- [ ] Remove AutoBazaar-specific properties and bindings from `IBppConfig`/`BppConfig`.
- [ ] Keep the `BppComposition` mount registration commented out, now as `// _mountables.Register(new AutoBazaarHostMount(configFile));`.
- [ ] Run `dotnet build BazaarPlusPlus.csproj --no-restore` after host wiring compiles.

### Task 3: Wire MSBuild And Tests To The New Project

**Files:**
- Modify: `BazaarPlusPlus.csproj`
- Modify: `Directory.Build.props`
- Modify: `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj`
- Modify: `tests/AutoBazaar.Tests/*.cs`

- [ ] Add `BazaarPlusPlus.AutoBazaar.csproj` as a project reference from `BazaarPlusPlus.csproj`.
- [ ] Exclude `AutoBazaar/**` from implicit compilation in the main project.
- [ ] Add output path isolation for the new project in `Directory.Build.props`.
- [ ] Copy `BazaarPlusPlus.AutoBazaar.dll` in Debug and Release plugin artifact item groups.
- [ ] Change AutoBazaar tests from source links to a `ProjectReference`.
- [ ] Update test namespaces from `BazaarPlusPlus.Game.AutoBazaar` to `BazaarPlusPlus.AutoBazaar`.
- [ ] Remove the `BppLog` shim from `AutoBazaarHttpServerTests`.
- [ ] Run `dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --no-restore`.

### Task 4: Add Architecture Guards And Documentation Updates

**Files:**
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`
- Modify: `docs/reference/auto-bazaar-http-api-v1.md`
- Modify: `docs/reference/auto-bazaar-decision-surface.md`
- Modify: `docs/mod-features-overview.md`

- [ ] Add architecture assertions that `AutoBazaar/` does not import Unity, BepInEx, Harmony, game DLLs, `Game`, or `GameInterop`.
- [ ] Add an assertion that `GameInterop/` does not import `BazaarPlusPlus.AutoBazaar`.
- [ ] Add an assertion that AutoBazaar tests do not source-link `Game/AutoBazaar`.
- [ ] Add an assertion that the main project excludes `AutoBazaar/**`.
- [ ] Update parked AutoBazaar docs to reference `Game/AutoBazaarHost` and `BazaarPlusPlus.AutoBazaar`.
- [ ] Run `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-restore`.

### Task 5: Final Verification And Commit

**Files:**
- All task files above.

- [ ] Run `dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --no-restore`.
- [ ] Run `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-restore`.
- [ ] Run `dotnet build BazaarPlusPlus.csproj --no-restore`.
- [ ] Run `git diff --check`.
- [ ] Review `git status --short` and stage only AutoBazaar isolation files, leaving pre-existing unrelated changes untouched.
- [ ] Commit with message `Isolate AutoBazaar module`.
- [ ] Lock the screen after completion or after a genuine blocker.
