# ADR-0013: Remote data separates runtime catalogs, release manifests, and build seed fetch

Status: Accepted, 2026-08-03

## Context

Three outbound data shapes need different lifecycles:

- runtime catalogs choose among cache, embedded seed, and remote refresh;
- the main menu performs a short-lived release-manifest request;
- production builds download embedded seeds before compilation.

The supporter feature had a second static cache/refresh state machine instead of using the existing
Remote Embedded Catalog. The version-check `MonoBehaviour` owned HTTP, JSON, cancellation, and stale
completion handling. The build target embedded a Roslyn downloader and could replace canonical seed
files before feature parsers accepted the complete set.

## Decision

### Supporter runtime catalog

Supporter data is the third `IRemoteEmbeddedCatalog<T>` consumer. `SupporterCatalogFactory` supplies
the feature parser, embedded resource, game-root cache, remote source, one-hour freshness, and
observer
([`SupporterCatalogFactory.cs:9`](../../src/BazaarPlusPlus/Game/Supporters/SupporterCatalogFactory.cs#L9)).
`SupporterCatalogModule` is the composition-owned lifecycle: fixed-list mode creates and warms no
catalog; normal mode publishes a session-stable snapshot, retries a missing snapshot after five
minutes, disposes the catalog before clearing the facade, and rejects late publications
([`SupporterCatalogModule.cs:9`](../../src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs#L9),
[`SupporterCatalogModule.cs:66`](../../src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs#L66),
[`SupporterCatalogModule.cs:83`](../../src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs#L83)).
The observer translates cache, embedded, remote, and cache-write issues into the existing supporter
degradation/recovery log vocabulary
([`SupporterCatalogModule.cs:133`](../../src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs#L133)).
The static supporter facade is projection and fixed-list policy only; it owns no client, task, cache,
or retry clock.

### Release manifest

`ReleaseManifestClient` is the outbound adapter for the installer `latest.json` shape. It maps HTTP,
missing-version, timeout, cancellation, and request exceptions into typed outcomes without touching
Unity state
([`ReleaseManifestClient.cs:6`](../../src/BazaarPlusPlus/Infrastructure/ReleaseManifest/ReleaseManifestClient.cs#L6),
[`ReleaseManifestClient.cs:35`](../../src/BazaarPlusPlus/Infrastructure/ReleaseManifest/ReleaseManifestClient.cs#L35)).
`ReleaseManifestCheckLifecycle` owns generation, cancellation, request-owner disposal, and the
publish gate: beginning a check cancels and disposes the prior request, and only the current,
non-cancelled lease may publish
([`ReleaseManifestCheckLifecycle.cs:9`](../../src/BazaarPlusPlus/Game/Lobby/ReleaseManifestCheckLifecycle.cs#L9)).
The `MonoBehaviour` constructs transport, delegates its lifetime and asynchronous publication to the
lifecycle, maps typed failures to existing Lobby log reasons, and applies accepted results to UI state
([`MainMenuVersionCheckController.cs:23`](../../src/BazaarPlusPlus/Game/Lobby/MainMenuVersionCheckController.cs#L23),
[`MainMenuVersionCheckController.cs:75`](../../src/BazaarPlusPlus/Game/Lobby/MainMenuVersionCheckController.cs#L75)).
This request is not Mod API health and does not use `ModApiSession`.

### Build seed fetch

`RemoteEmbeddedDataFetcher` is a normal build tool. Fetch stages through a unique temporary file,
uses streaming HTTP, checks coarse transfer integrity, atomically replaces only its staging
destination, and cleans temporary files on every outcome
([`RemoteEmbeddedDataFetch.cs:14`](../../build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetch.cs#L14)).
MSBuild delegates download work to the tool and continues to own item metadata and embedding
([`RemoteEmbeddedData.targets:62`](../../src/BazaarPlusPlus/RemoteEmbeddedData.targets#L62)).

`run.sh fetch-data` creates a staging directory, downloads the complete voice/ten-win set, runs the
real feature-owned semantic seed gates against that staging directory, then promotes both files as a
transaction and always removes staging
([`run.sh:142`](../../run.sh#L142)). Promotion backs up the existing set and rolls it back if any
replacement fails
([`RemoteEmbeddedDataFetch.cs:68`](../../build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetch.cs#L68)).
Transport validation deliberately does not duplicate voice or ten-win schemas; feature parsers are
the final semantic owners.

## Consequences

- Runtime catalog refresh, release checking, and build downloading share HTTP primitives only where
  appropriate; they do not share a lifecycle abstraction.
- Supporter cache moves from the process temporary directory to
  `<GameRoot>/BazaarPlusPlusV5/supporter-list.json`.
- A supporter snapshot is stable for the module session after warm-up. With no usable snapshot,
  access may schedule another warm after the five-minute latch.
- Reinitializing version check cancels, invalidates, and disposes the previous request owner; normal
  completion and destruction also dispose the active owner exactly once, and a late completion cannot
  overwrite newer UI state.
- A failed download or semantic gate leaves both canonical build seeds unchanged. Normal non-forced
  MSBuild still skips already-present seed files.

## Rejected alternatives

- Keeping a supporter-specific static refresh task and cache: it duplicates a tested deep lifecycle
  and violates composition ownership.
- Putting release manifest checks into `ModApiSession`: the endpoint, timeout, document, and owner are
  unrelated to the mod backend session.
- Validating feature schemas inside the fetcher: it creates a second schema owner and permits drift.
- Promoting seeds one by one before semantic validation: it exposes mixed-generation embedded data.
- Retaining the inline Roslyn downloader as a fallback: it would leave two build download paths.
