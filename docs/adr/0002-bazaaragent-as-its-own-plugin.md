# ADR-0002: Ship BazaarAgent as an optional dependent plugin

Status: Accepted

## Decision

Keep three assemblies: the main `BazaarPlusPlus.dll`, an optional `BazaarPlusPlus.BazaarAgentHost.dll` BepInEx plugin, and the pure transport core `BazaarPlusPlus.BazaarAgent.dll`. The host depends on the main plugin and consumes narrow public game/replay facades; an architecture test keeps the main assembly free of agent-module references ([test](../../tests/Architecture.Tests/CoreLayeringTests.cs#L1335-L1440)).

## Why

The earlier in-process host preserved transport purity but could not be packaged independently without making the main composition root know about it. A second BepInEx entry owns its own `Awake`/`Update`/`OnDestroy` lifecycle and avoids a project-reference cycle ([host](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentHostPlugin.cs#L9-L65)).

## Guardrails

- Cross-plugin access goes only through `BazaarAgentGameBridge`; the host’s hard BepInEx dependency ensures the main plugin publishes it first ([bridge](../../src/BazaarPlusPlus/GameInterop/BazaarAgent/BazaarAgentGameBridge.cs#L4-L19)).
- The pure core remains `System` + `Newtonsoft.Json` only, enforced by architecture tests ([project](../../src/BazaarPlusPlus.BazaarAgent/BazaarPlusPlus.BazaarAgent.csproj#L1-L14), [test](../../tests/Architecture.Tests/CoreLayeringTests.cs#L1207-L1224)).
- Installing the host enables fixed-loopback `127.0.0.1:47900`; removing it disables the bridge. There is no runtime enable/port config ([port](../../src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentPorts.cs#L6-L27), [loopback prefix](../../src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs#L96)).
- A default build must scrub both optional DLLs; `--with-bazaaragent` builds and copies them ([main project](../../src/BazaarPlusPlus/BazaarPlusPlus.csproj#L161-L173), [run.sh](../../run.sh#L90-L97)).
- Agent diagnostics live under `<GameRoot>/BazaarPlusPlusV5/BazaarAgent`, never under `Application.dataPath` ([options](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentBepInExOptions.cs#L7-L13)).
- The mod carries transport and validation only: no play policy. The external wire contract is V3; its field names live in `src/BazaarPlusPlus.BazaarAgent/AGENT_README.md`.

Reopen only if the agent needs capabilities beyond the narrow facade or a first-party discovery/configuration protocol is introduced.
