# AutoBazaar Module Isolation Design

Date: 2026-06-02
Status: Implemented and landed.

## Context

The AutoBazaar module isolation migration is complete. `Game/AutoBazaar` no longer exists; the code now lives in a standalone `AutoBazaar/` root-level module (`BazaarPlusPlus.AutoBazaar.csproj`) containing `Contract/`, `Decisions/`, `Diagnostics/`, `Runtime/`, and `Transport/`. The thin host adapter that owns Unity lifecycle, BepInEx config binding, and game-runtime access lives under `Game/AutoBazaarHost/`. The main plugin consumes the module via `ProjectReference` and gates the host mount registration with `#if BPP_AUTOBAZAAR_HOST` in `BppComposition.cs` (lines 31–32, 126–127). `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj` references the new assembly via `ProjectReference` (line 27) rather than linking source files directly. Architecture tests in `tests/Architecture.Tests/CoreLayeringTests.cs` (line 253) enforce that the core module does not reference Unity, BepInEx, Harmony, or game DLLs.

AutoBazaar remains parked at runtime: the mount is not registered in normal builds unless the `BPP_AUTOBAZAAR_HOST` compilation symbol is defined. The isolation work described in this document has been carried out in full; the remaining open item is an explicit re-enable task.

> **2026-06-05 update(ADR-0006):** 上述路径与结构在依赖反转后已变更:`BazaarPlusPlus.AutoBazaar.csproj` → `src/BazaarPlusPlus.BazaarAgent/`;`Game/AutoBazaarHost/` → `src/BazaarPlusPlus.BazaarAgentHost/`(独立 BepInEx 插件);`tests/AutoBazaar.Tests/` → `tests/BazaarAgent.Tests/`;`#if BPP_AUTOBAZAAR_HOST` 已删除。架构测试现为 CoreLayeringTests.BazaarAgent_core_does_not_depend_on_host_or_game_runtime_namespaces。详见 [ADR-0006](../../adr/0006-bazaaragent-as-its-own-plugin.md)。

## Goals

- Move AutoBazaar ownership to a root-level module instead of `Game/AutoBazaar`.
- Build AutoBazaar core as its own assembly through `BazaarPlusPlus.AutoBazaar.csproj`.
- Keep the main BepInEx plugin as the only owner of Unity lifecycle, BepInEx config binding, and game-runtime access.
- Preserve the existing HTTP wire contract documented in `docs/reference/bazaar-agent-http-api-v1.md`.
- Preserve the current parked runtime behavior unless a later task explicitly re-enables the mount.
- Make the dependency rules enforceable with project references and architecture tests, not just comments.

## Non-Goals

- Do not turn AutoBazaar into a separate BepInEx plugin in this phase.
- Do not change external agent strategy behavior or action selection policy.
- Do not rename stable wire fields such as `stateName`, `availableActions`, `actionKind`, `cardInstanceId`, `targetSection`, `targetSockets`, or `reason`.
- Do not move shared The Bazaar runtime adapters into AutoBazaar core.

## Recommended Architecture

Create a root-level module:

```text
AutoBazaar/
  Contract/
  Transport/
  Decisions/
  Runtime/
  Diagnostics/
BazaarPlusPlus.AutoBazaar.csproj
```

The core module owns AutoBazaar-specific contracts and orchestration:

- DTOs and enums for context, cards, actions, validation, and responses.
- HTTP listener, request parsing, response serialization, ETag behavior, and body limits.
- Action queue and pending-response coordination.
- Snapshot publisher and tick stability behavior.
- Action validation against the latest snapshot.
- Move-target planning and target-selection action filtering.
- Decision log record shape and file append logic, using an injected log root.

The core module must not reference:

- `UnityEngine`
- `BepInEx`
- `HarmonyLib`
- `TheBazaar`
- `BazaarGameClient`
- `BazaarGameShared`
- `BazaarPlusPlus.Game`
- `BazaarPlusPlus.GameInterop`

The main plugin owns a thin adapter layer, kept outside AutoBazaar core:

```text
Game/AutoBazaarHost/
  AutoBazaarHostMount.cs
  AutoBazaarUnityRuntime.cs
  AutoBazaarGameContextReader.cs
  AutoBazaarGameActionDispatcher.cs
  AutoBazaarBepInExOptions.cs
  AutoBazaarBppLogger.cs
```

This host layer is allowed to reference Unity, BepInEx, GameInterop, and game DLLs. It translates game runtime state into AutoBazaar core DTOs, executes validated actions through the game runtime, and forwards configuration/logging/path information into the core module.

## Ports

AutoBazaar core should communicate with the host through small interfaces:

- `IAutoBazaarOptions`
  - `Enabled`
  - `HttpListenerPort`
  - `ActionMinDelay`
  - `EndpointFilePath`
  - `DecisionLogRoot`
- `IAutoBazaarContextReader`
  - Builds an `AutoBazaarContext` from the live game state.
  - The implementation lives in the host layer.
- `IAutoBazaarActionDispatcher`
  - Executes a validated `AutoBazaarAction`.
  - The implementation lives in the host layer.
- `IAutoBazaarLogger`
  - Provides `Info`, `Warning`, and `Error` without referencing `BppLog`.
- `IAutoBazaarClock`
  - Provides UTC time and monotonic elapsed time for testable cooldown behavior.

The core runtime should become a plain disposable class, for example `AutoBazaarRuntimeController`. The Unity `MonoBehaviour` should only call this controller from `Update()` and `OnDestroy()`.

## Data Flow

1. `BppComposition` optionally registers `AutoBazaarHostMount`.
2. The mount attaches a Unity runtime component to the plugin host object.
3. The Unity runtime constructs `AutoBazaarRuntimeController` with host-provided ports.
4. On each tick, the controller reconciles the HTTP listener against options.
5. If enabled, it requests a context from `IAutoBazaarContextReader`.
6. The controller publishes a snapshot and serves it through `GET /v1/context`.
7. `POST /v1/actions` enqueues a request and waits for main-thread processing.
8. On the next Unity tick, the controller validates the action against the current snapshot.
9. If valid, the controller calls `IAutoBazaarActionDispatcher`.
10. The controller records the decision log entry and completes the HTTP response.

## GameInterop Boundary

Reusable adapters over The Bazaar runtime surfaces still belong in `GameInterop`. AutoBazaar-specific policy does not.

Examples that may belong in `GameInterop` if shared by multiple features:

- `AppState` and `Data` read wrappers.
- `ClientCache.RunConfig` reflection helpers.
- Inventory/container socket inspection helpers.
- Encounter targeting probes already used outside AutoBazaar.

Examples that must stay in AutoBazaar core or host:

- HTTP API schema.
- Snapshot wire DTOs.
- Action validation rules.
- Decision log format.
- AutoBazaar-specific available-action derivation.
- The host dispatcher that maps an AutoBazaar action to game commands.

## Configuration

The current `IBppConfig` exposes AutoBazaar settings globally. To reduce Core pollution, move AutoBazaar-specific binding behind the host adapter.

Preferred implementation:

- Keep `BppConfig` as the central BepInEx config initializer for now.
- Remove AutoBazaar entries from `IBppConfig` once no shared service needs them.
- Add `AutoBazaarBepInExOptions` in the host layer, backed by the existing `ConfigFile` or narrowly passed `ConfigEntry` values.
- Preserve existing config section/key names:
  - `[AutoBazaar] Enabled`
  - `[AutoBazaar] HttpListenerPort`

This avoids breaking user cfg files while keeping the generic runtime service surface from advertising parked AutoBazaar behavior.

## Build And Packaging

Add `BazaarPlusPlus.AutoBazaar.csproj` targeting `netstandard2.1`. The main `BazaarPlusPlus.csproj` references it with `ProjectReference`.

The main project should explicitly exclude `AutoBazaar/**` from implicit compilation, matching the existing treatment of `ModApi/**` and `Storage/**`. The Debug and Release copy flows must copy `BazaarPlusPlus.AutoBazaar.dll` alongside `BazaarPlusPlus.dll` only when the main plugin references it.

The module remains removable at source level: if the host mount and project reference are removed, the main plugin should no longer compile or ship AutoBazaar code.

## Test Strategy

Update `tests/AutoBazaar.Tests` to reference `BazaarPlusPlus.AutoBazaar.csproj` instead of linking source files from `Game/AutoBazaar`.

Keep tests focused on pure behavior:

- Snapshot publishing and ETag stability.
- Action validation.
- Move-target planning.
- Target-selection action filtering.
- HTTP response and error bodies.
- Action queue behavior.
- Decision log serialization.

Add architecture tests:

- AutoBazaar core must not reference Unity, BepInEx, Harmony, game DLLs, `Game`, or `GameInterop`.
- `GameInterop` must not reference AutoBazaar.
- AutoBazaar tests must not link implementation source files from the main plugin tree.
- The main project must exclude `AutoBazaar/**` from implicit compilation.

For the first migration, run:

```bash
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --no-restore
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-restore
dotnet build BazaarPlusPlus.csproj --no-restore
```

Runtime validation is not required while AutoBazaar remains parked. If a later task re-enables the mount, validate by launching The Bazaar through Steam and checking `BepInEx/LogOutput.log` plus `GET /v1/context`.

## Migration Plan

1. Create `BazaarPlusPlus.AutoBazaar.csproj` and move pure AutoBazaar files into `AutoBazaar/`.
2. Rename namespaces from `BazaarPlusPlus.Game.AutoBazaar` to `BazaarPlusPlus.AutoBazaar` for core files.
3. Convert `tests/AutoBazaar.Tests` to a project reference.
4. Add architecture tests for the new boundary.
5. Extract a plain `AutoBazaarRuntimeController` from the current Unity `AutoBazaarRuntime`.
6. Move game reads into `AutoBazaarGameContextReader` in the host layer.
7. Move game writes into `AutoBazaarGameActionDispatcher` in the host layer.
8. Replace the old `AutoBazaarMount` with a host mount that wires the ports.
9. Preserve the commented-out registration in `BppComposition` unless re-enable is explicitly requested.
10. Update the parked AutoBazaar docs to point at the new module and host layer.

## Risks And Mitigations

- Risk: The first split accidentally changes runtime behavior.
  - Mitigation: Keep the mount parked and prioritize test parity before any re-enable.
- Risk: The context builder still depends heavily on game types.
  - Mitigation: Move it into the host first, then extract shared GameInterop adapters only when the same runtime behavior is needed elsewhere.
- Risk: Shipping copies miss the new DLL.
  - Mitigation: update Debug and Release copy item groups with explicit verification.
- Risk: AutoBazaar settings stay visible in the global config interface.
  - Mitigation: treat config surface cleanup as part of the split, not as a later polish task.

## Acceptance Criteria

- AutoBazaar core source lives under root `AutoBazaar/`.
- AutoBazaar core builds through `BazaarPlusPlus.AutoBazaar.csproj`.
- `Game/AutoBazaar` no longer exists as the module owner.
- Host-only game/Unity/BepInEx code is isolated under a thin main-plugin adapter.
- Existing AutoBazaar tests pass through project reference, not source links.
- Architecture tests enforce the new dependency boundary.
- Current parked behavior remains unchanged until an explicit re-enable task.
