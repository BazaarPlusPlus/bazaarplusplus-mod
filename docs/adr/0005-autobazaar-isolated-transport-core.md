# Isolate AutoBazaar as a transport-only core behind host-owned ports

AutoBazaar lives in its own assembly (`AutoBazaar/` → `BazaarPlusPlus.AutoBazaar.csproj`) that contains only the automation bridge's contract, transport, action validation, queue, runtime controller, and decision-log shape. It reaches the game and Unity exclusively through a thin host adapter (`Game/AutoBazaarHost/`) that implements a small set of ports. It runs **no play policy**: the mod publishes a versioned game-state snapshot (`GET /v1/context`) and accepts one validated action per turn (`POST /v1/actions`); all decision-making lives outside the mod, in the separately-owned `bazaarplusplus-agent` repo.

## Context

AutoBazaar began under `Game/AutoBazaar`, which made it look like an ordinary game feature. It is not — it is a separately-owned automation bridge whose only stable surface is the versioned HTTP wire contract (`docs/reference/bazaar-agent-http-api-v1.md`). Leaving it mixed in with feature/UI/Unity code made it impossible to remove or test in isolation, let `MonoBehaviour` ticks and reflection probes run even when disabled, and blurred the line that the mod must never contain play policy.

Two facts shaped the decision. First, AutoBazaar is parked by default, so its runtime must be physically omittable without leaving dead Unity work behind (the broader mountable story is [ADR-0002](0002-mountable-feature-registry.md)). Second, the intelligence is intentionally external: the mod's job is dumb, auditable transport, and the strategy — the swappable rule/`claude`/`deepseek` decision providers — lives in the `bazaarplusplus-agent` process, not here.

## Consequences

- The core assembly must not reference `UnityEngine`, `BepInEx`, `HarmonyLib`, `TheBazaar`, `BazaarGameClient`, `BazaarGameShared`, `BazaarPlusPlus.Game`, or `BazaarPlusPlus.GameInterop`. This is enforced by project references and architecture tests, not by comments.
- Game/Unity access is funneled through host-owned ports — `IAutoBazaarOptions`, `IAutoBazaarContextReader`, `IAutoBazaarActionDispatcher`, `IAutoBazaarLogger`, `IAutoBazaarClock` (`AutoBazaar/Contract/AutoBazaarPorts.cs`) — implemented in `Game/AutoBazaarHost/`. The core runtime is a plain disposable (`AutoBazaarRuntimeController`); the Unity `MonoBehaviour` only forwards `Update()`/`OnDestroy()`.
- The mod does transport + validation only: build a snapshot, serve `GET /v1/context`, validate `POST /v1/actions` against the latest snapshot, dispatch the action, and append a decision-log entry. No strategy or action-selection policy lives in the mod; `AutoBazaar/Decisions/` is validation and move-target planning, not policy. The wire field names (`stateName`, `availableActions`, `actionKind`, `cardInstanceId`, `targetSection`, `targetSockets`, `reason`) are a stable contract — do not rename.
- The host is compile-gated: its registration sits behind `#if BPP_AUTOBAZAAR_HOST` in `BppComposition`, built with `./run.sh build --with-autobazaar-host` (`-p:EnableAutoBazaarHost=true`). Default builds neither compile `Game/AutoBazaarHost/` nor reference/copy `BazaarPlusPlus.AutoBazaar.dll`; starting the loopback server additionally requires `[AutoBazaar] Enabled=true` in `BazaarPlusPlus.cfg`.
- Reusable adapters over The Bazaar runtime stay in `GameInterop/`, not in AutoBazaar core; AutoBazaar-specific schema, validation, decision-log format, and available-action derivation stay in the core.
- Reopen if AutoBazaar ever needs to become its own BepInEx plugin, or if play policy is ever pulled into the mod (it should not be — policy belongs to `bazaarplusplus-agent`).

Full design detail: [docs/superpowers/specs/2026-06-02-autobazaar-module-isolation-design.md](../superpowers/specs/2026-06-02-autobazaar-module-isolation-design.md). Feature overview and build flags: [docs/features/bazaar-agent.md](../features/bazaar-agent.md).
