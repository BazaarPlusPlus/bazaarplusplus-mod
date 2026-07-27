# ADR-0002: Mount Unity components through the mountable registry

Status: Accepted

## Decision

Use `IBppFeature`/`BppFeatureRegistry` for non-Unity start/stop modules and `IBppMountable`/`BppMountableRegistry` for Unity components. Register both in `BppComposition`; use `ComponentMount<T>` for the common add-initialize-destroy lifecycle and a bespoke mount only when a feature owns additional dependencies or objects.

## Why

Hand-wiring every `MonoBehaviour` across `Plugin` attach/detach paths scattered lifecycle ownership and made omission incomplete. A composition-owned registration makes the installed lifecycle visible in one place.

## Guardrails

- Mount in registration order and unmount in reverse order ([registry](../../src/BazaarPlusPlus/Core/Runtime/BppMountableRegistry.cs#L6-L23)); current mount failure behavior is deliberately pinned by [composition tests](../../tests/CompositionRuntime.Tests/BppMountableRegistryTests.cs#L25-L66).
- Keep simple components on [`ComponentMount<T>`](../../src/BazaarPlusPlus/Core/Runtime/ComponentMount.cs#L11-L33); Collection, History, LiveBuild, and upload workflows retain bespoke mounts because they own additional dependencies or component lifecycles ([composition](../../src/BazaarPlusPlus/BppComposition.cs#L188-L245)).
- `CombatReplayRuntime` remains the bootstrap exception because other modules need it before mountables run ([Plugin](../../src/BazaarPlusPlus/Plugin.cs#L78-L98)).
- BazaarAgent is not a mountable here; its independent plugin lifecycle is ADR-0006.
