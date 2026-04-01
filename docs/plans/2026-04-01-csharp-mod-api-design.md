# C# Mod API Redesign

## Goal

Rebuild the C# client-side remote integration around a single `ModApi` subsystem instead of separate run-upload and replay-upload infrastructure.

The redesign should produce:

- one client identity per install
- one local client-state model
- one request-signing stack
- one route builder rooted at a single API base URL
- three business services on top of that shared base
  - run summary upload
  - battle artifact upload
  - ghost query and replay download

## Design Principle

The current C# layout still reflects historical upload evolution:

- run upload owns most of the auth/session machinery
- battle upload partially wraps run upload machinery
- ghost sync derives routes from run upload endpoints

This creates the wrong dependency direction. In the new design:

- `ModApi` is the shared infrastructure
- run upload is only one consumer of `ModApi`
- battle upload is only one consumer of `ModApi`
- ghost sync is only one consumer of `ModApi`

No feature should derive its routes or client identity from another feature.

## Target Architecture

The C# side should be organized into two layers:

- shared `ModApi` infrastructure
- feature-level services

### Shared ModApi infrastructure

The shared layer owns:

- API base URL
- route construction
- install identity
- key material
- client registration
- client state
- signed request creation
- authenticated send/re-register flow

### Feature services

The feature layer owns:

- how to build a run summary payload
- how to build a battle artifact payload
- how to query ghost battles and download remote replay payloads

Feature services must not own registration, route derivation, or route-scoped client state.

## Shared Infrastructure Design

### `ModApiRoutes`

This should become the only place that knows route paths.

It takes a single `ApiBaseUrl` and exposes strongly named endpoints:

- `RegisterClient`
- `BindPlayer`
- `UploadRunSummary`
- `UploadBattleArtifact`
- `QueryGhostBattles`
- `CreateReplayLink(string battleId)`
- `DownloadReplay(string token)`

This explicitly replaces:

- direct use of `RunUploadDefaults.UploadEndpoint`
- direct use of `RunUploadDefaults.RegistrationEndpoint`
- `BattleUploadService.TryDeriveBattleUploadEndpoint`
- route derivation inside `GhostBattleApiClient`
- bind-route derivation inside `GhostBattleSyncService`

### `ModApiIdentityStore`

This owns only install identity.

It should not contain any run-specific naming or assumptions.

It replaces the old `RunUploadIdentityStore` name and semantics.

### `ModApiKeyStore`

This owns the local private key and signing key material lifecycle.

It should be shared by all remote features.

It replaces the old `RunUploadKeyStore` name and semantics.

### `ModApiClientStateStore`

This owns the entire persisted remote session state.

The state should be reduced to one client model:

- `client_id`
- `bound_player_account_id`

Optional metadata:

- `registered_at_utc`
- `last_binding_at_utc`

The store must not retain:

- route scopes
- run-specific client ids
- replay-specific client ids
- per-scope bound-account maps

This directly replaces the scoped dictionary model from `RunUploadClientStateStore`.

### `ModApiRequestSigner`

This becomes the single signer for all remote requests.

It should expose:

- general signed request creation
- stable canonical request construction
- body hash computation

It replaces:

- `RunUploadRequestSigner`
- `BattleUploadRequestSigner`

There should not be a separate battle signer wrapper anymore.

### `ModApiRegistrationClient`

This performs one registration flow for the install.

It must:

- register without purpose
- persist one `client_id`
- support re-registration on auth invalidation

It replaces:

- purpose-aware `RunUploadRegistrationClient`

### `ModApiSession`

This is the central authenticated request coordinator.

It should encapsulate:

- get or create `client_id`
- retry after re-registration
- hand `client_id` to feature-specific send delegates

It is the intended replacement for the current `BppAuthenticatedRouteClient`, but without scope.

Recommended behavior:

- `SendAsync<TResult>(...)`
- if request says re-register, clear `client_id`, register again, retry once

This layer should not know whether the caller is run upload, battle upload, or ghost sync.

## Feature Service Design

### `RunSummaryUploadService`

This replaces the conceptual role of `RunUploadService`, but with reduced scope.

Responsibilities:

- read pending completed runs from local storage
- build compact run summary payloads
- send them to `POST /runs`
- mark local upload success/failure

Non-responsibilities:

- route creation
- registration
- client state
- battle projection
- replay-specific behavior

### `BattleArtifactUploadService`

This replaces the conceptual role of `BattleUploadService`.

Responsibilities:

- read pending battle artifacts from local storage
- build payloads containing manifest plus replay payload
- send them to `POST /battles`
- mark local battle upload success/failure

Non-responsibilities:

- deriving upload endpoints from run upload endpoints
- owning a second registration path
- wrapping a separate replay client identity

### `GhostSyncService`

This replaces the current ghost-sync transport assumptions.

Responsibilities:

- ensure current player binding when needed
- query `GET /players/me/ghost-battles`
- request `POST /players/me/ghost-battles/:battleId/replay-link`
- download `GET /replays/:token`
- persist local remote-replay payload cache

Non-responsibilities:

- deriving bind/query endpoints from registration/upload endpoints
- owning its own signing stack
- owning its own registration stack

## Controller Design

Controllers should stay thin.

### `RunUploadController`

Owns only:

- startup gate
- periodic scheduling
- service construction
- shutdown/disposal

It should construct `RunSummaryUploadService` using shared `ModApi` infrastructure.

### `BattleUploadController`

Owns only:

- startup gate
- periodic scheduling
- service construction
- shutdown/disposal

It should construct `BattleArtifactUploadService` using shared `ModApi` infrastructure.

It must not derive the battle endpoint from the run endpoint.

### `HistoryPanel` ghost entry

The history-panel ghost creation path should only construct `GhostSyncService` from shared `ModApi` infrastructure and local repository/cache dependencies.

It must not pull route information from run-upload-specific endpoint models.

## Configuration Design

The old config shape should be simplified.

### Keep

- `StartupDelaySeconds`
- `IntervalSeconds`
- `BatchSize`
- `RequestTimeoutSeconds`

### Replace

Replace specific endpoint constants with one base URL:

- `ApiBaseUrl`

The client should never store or pass feature-specific endpoint strings around when the route can be built from that base URL.

## Local Payload Design

### Run summary payload

The C# run upload path should emit a compact payload containing only run-level summary fields.

It should not include:

- `pvp_battles`
- replay payload data
- old event-heavy upload bodies used by server-side projection logic

### Battle artifact payload

The C# battle upload path should emit:

- `battle_id`
- optional `run_id`
- `battle_manifest`
- `replay_payload`

This is the only remote source of truth for:

- ghost discovery
- replay existence
- replay payload retrieval

## File-Level Refactor Direction

### Keep and rename or generalize

- `RunUploadRequestSigner.cs` -> `ModApiRequestSigner.cs`
- `RunUploadIdentityStore.cs` -> `ModApiIdentityStore.cs`
- `RunUploadKeyStore.cs` -> `ModApiKeyStore.cs`
- `RunUploadRegistrationClient.cs` -> `ModApiRegistrationClient.cs`
- `BppAuthenticatedRouteClient.cs` -> `ModApiSession.cs`

### Extract or add

- `ModApiRoutes.cs`
- `ModApiClientStateStore.cs`
- optionally `SignedJsonApiClient.cs`

### Delete after migration

- `RunUploadRouting.cs`
- `BattleUploadRequestSigner.cs`
- `BattleUploadApiClient.cs`
- `RunUploadApiClient.cs`

The old split run/battle transport clients should collapse into one shared JSON client or direct session-driven request path.

## Legacy Concepts To Remove

The C# redesign should remove these concepts completely:

- route scope names such as `Runs` and `ReplayCloudflare`
- route-scoped client ids
- replay-specific registration purpose
- battle upload endpoint derivation
- bind-route derivation from registration route
- ghost query route derivation from run upload route

## Testing Strategy

The C# redesign should be validated using targeted test seams.

### `RunUploadBootstrap.Tests`

Repurpose this seam to validate:

- API base URL validation
- `ModApiRoutes` generation
- no endpoint-pair assumptions

It should stop asserting:

- `RegistrationEndpoint`
- `UploadEndpoint`
- dual-endpoint bootstrap shape

### `RunUploadAuth.Tests`

Update to validate:

- registration without purpose
- unified local client-state persistence
- run summary upload against `/runs`

### `CombatReplayUploadSync.Tests`

Update to validate:

- battle artifact upload against `/battles`
- no derived battle endpoint logic
- same client identity as run summary upload

### `GhostBattleSync.Tests`

Update to validate:

- bind route is `/clients/bind-player`
- query route is `/players/me/ghost-battles`
- replay-link route is `/players/me/ghost-battles/:id/replay-link`
- no dependency on `RunUploadEndpointSet`

## Migration Assumption

This design assumes the C# client can clean-break with the Worker.

That means:

- old local scoped client state is discarded
- the client re-registers on the new Worker
- the client re-binds the current player
- no legacy route fallback exists

## Recommended Implementation Order

1. Introduce `ApiBaseUrl` plus `ModApiRoutes`
2. Replace scoped client state with `ModApiClientStateStore`
3. Generalize signer, identity, key, and registration classes
4. Replace `BppAuthenticatedRouteClient` with scope-free `ModApiSession`
5. Convert run upload to run summary upload
6. Convert battle upload to battle artifact upload
7. Convert ghost sync to explicit routes
8. Delete old route-derivation and scope infrastructure

## Companion Documents

Overall system design:

- [2026-04-01-mod-api-unification-design.md](/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/docs/plans/2026-04-01-mod-api-unification-design.md)

Execution plan:

- [2026-04-01-mod-api-unification-plan.md](/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/docs/plans/2026-04-01-mod-api-unification-plan.md)
