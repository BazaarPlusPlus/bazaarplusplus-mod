# Installer-Mod V2 Identity And Upload Design

## Goal

Replace the current upload identity model built around:

- `client_id`
- `install_id`
- late `bind-player`
- optional uploader player linkage

with a new V2 model where:

- the installer is the identity and activation entrypoint
- the mod remains the runtime online client and uploads directly
- the business user identity is keyed by `player_account_id`
- installation-level request signing replaces the old client registration flow

This design intentionally treats V2 as a clean-break model. The old service can remain for legacy clients, but V2 should not inherit the old auth semantics.

## Problem Summary

The current model has three structural problems:

1. upload authentication and player identity are separate and loosely coupled
2. player linkage is completed opportunistically through later flows such as ghost sync
3. many uploaded records end up with empty uploader/player linkage because upload does not require a confirmed player identity

The result is a system where request authenticity and business ownership are different concerns handled by different paths, which makes the model difficult to reason about and hard to evolve.

## Design Principles

- installer owns identity bootstrap
- mod owns runtime service communication
- `player_account_id` is the business user key
- `player_username` is the login/display name
- binding requires explicit confirmation in installer
- the mod can continue operating while installer is not running
- local shared files should not use JSON
- request signing is the real trust boundary; local file integrity is only a supporting check

## Identity Model

### User identity

The V2 user is keyed by `player_account_id`.

`player_username` is used for login and display, but does not replace the primary key.

This means:

- one `player_account_id` maps to exactly one user
- all business ownership, uploads, and installations ultimately resolve to `player_account_id`
- no separate `user_id -> player_account_id` binding table is required in V2

### Installation identity

Each activated mod installation receives its own `installation_id`.

Properties:

- one user may have multiple installations
- each installation has its own keypair
- requests are signed by the installation private key
- switching accounts on the same machine creates a new `installation_id`

### Observed player identity

The mod reads:

- `player_account_id`
- `player_username`

from the game runtime and writes that observation into the shared identity directory.

That observation is not enough to complete binding by itself. Installer must present it and require explicit user confirmation before activation is finalized.

## Data Model

### `users`

Suggested columns:

- `player_account_id` `TEXT PRIMARY KEY`
- `player_username` `TEXT NOT NULL UNIQUE`
- `password_hash` `TEXT NOT NULL`
- `stream_platform` `TEXT NULL`
- `stream_channel_id` `TEXT NULL`
- `stream_url` `TEXT NULL`
- `created_at_utc` `TEXT NOT NULL`
- `updated_at_utc` `TEXT NOT NULL`
- `last_login_at_utc` `TEXT NULL`

Notes:

- this design assumes `player_username` collisions and renames are rare enough to ignore in V2
- if that assumption later breaks, a dedicated `login_name` can be added in a future migration

### `installations`

Suggested columns:

- `installation_id` `TEXT PRIMARY KEY`
- `player_account_id` `TEXT NOT NULL`
- `public_key` `TEXT NOT NULL`
- `status` `TEXT NOT NULL`
- `created_at_utc` `TEXT NOT NULL`
- `last_seen_at_utc` `TEXT NULL`
- `revoked_at_utc` `TEXT NULL`

Suggested status values:

- `active`
- `revoked`
- `stale`

### `installation_observations`

Suggested columns:

- `installation_id` `TEXT NULL`
- `observed_player_account_id` `TEXT NOT NULL`
- `observed_player_username` `TEXT NOT NULL`
- `observed_at_utc` `TEXT NOT NULL`
- `signature` `TEXT NOT NULL`
- `status` `TEXT NOT NULL`

Suggested status values:

- `observed`
- `confirmed`
- `stale`

This table exists to audit what the mod reported before or during activation. It is not the business ownership source of truth. Ownership is still determined by `users.player_account_id`.

## Shared Local Files

Use:

```text
GameRoot/
  BazaarPlusPlus/
    Identity/
      player-observation.bpp
      installation.bpp
      installation.key
```

This directory is the shared runtime data root between installer and mod.

### `player-observation.bpp`

Writer:

- mod

Reader:

- installer

Purpose:

- publish the latest observed game identity

Payload should include:

- `player_account_id`
- `player_username`
- `observed_at_utc`
- optional local installation hint if available

### `installation.bpp`

Writer:

- installer

Reader:

- mod

Purpose:

- publish active installation metadata required for online requests

Payload should include:

- `installation_id`
- `player_account_id`
- `api_base_url`
- `public_key`
- `status`
- `created_at_utc`

### `installation.key`

Writer:

- installer

Reader:

- mod

Purpose:

- hold the installation private key used to sign runtime requests

The private key must not be embedded into `installation.bpp`.

## Local File Format

Do not use JSON.

Use a lightweight binary envelope:

- 4 bytes magic
- 2 bytes schema version
- 2 bytes flags
- 4 bytes payload length
- payload bytes
- 32 bytes payload checksum

Suggested magic:

- `BPP1`

Validation rules:

- reject on unknown magic
- reject on unsupported version
- reject on invalid length
- reject on checksum mismatch

This format raises tampering and corruption cost, but it is not the trust root.

## Trust Model

### What V2 can prove

V2 can prove:

- a request came from an activated installation
- the request was signed by that installation's private key
- the installation belongs to a specific `player_account_id`

### What V2 cannot prove

V2 cannot cryptographically prove that the game-derived `player_account_id` is impossible to spoof, because that value is still read locally by the mod.

Therefore, V2 should treat the game identity as:

- client-observed
- installer-confirmed

and not as a third-party verified identity.

### Real trust boundary

The real trust boundary is:

- installation registration on the server
- installation public key stored on the server
- per-request installation signature validation

Local file integrity checks are only a convenience and safety measure.

## User And Activation Flow

### First-time flow

1. User starts the game with the mod installed.
2. The mod reads `player_account_id` and `player_username`.
3. The mod writes `player-observation.bpp`.
4. User opens installer.
5. Installer detects observation data and allows first-time registration.
6. User sets a password.
7. Installer calls the server to create the user keyed by the observed `player_account_id`.
8. Installer displays an explicit confirmation step for the currently observed game account.
9. After confirmation, installer generates a new installation keypair locally.
10. Installer registers the installation with the server by sending the public key.
11. Server creates an `installation_id`.
12. Installer writes `installation.bpp` and `installation.key`.
13. The mod starts using installation-signed runtime requests.

### Existing-user flow

1. User opens installer.
2. If there is no observation file, installer still allows viewing existing account data.
3. If there is an observation file, installer shows the observed `player_username`.
4. User logs in with `player_username + password`.
5. If the observed `player_account_id` matches the logged-in user, installer may refresh or recreate installation materials.
6. If the observed `player_account_id` differs, installer must clearly state that game account information has changed and re-login is required to generate new mod keys.

### Explicit confirmation requirement

Observation is automatic.

Binding/activation is explicit.

The installer must require a clear confirmation step before turning the observed identity into an activated installation context.

## Installer Behavior Rules

### Before any observation exists

On first launch, installer cannot complete registration or activation until the mod has written observed player identity data.

Allowed behavior:

- show onboarding
- explain that the user must launch the game/mod first

### Already logged in with no new observation

Installer should allow:

- viewing account data
- viewing stream profile fields
- other non-activation features

Installer should not require re-login in this state.

### Logged in and observed identity changed

Installer should:

- prominently state that the observed game identity has changed
- require a fresh login before generating new mod keys

Installer should still allow:

- viewing existing profile data
- other non-mod-auth-related functionality

Installer should block:

- installation activation
- installation key refresh
- other operations that would authorize the mod for the newly observed account

## Mod Runtime Online Layer

The mod should expose one unified runtime service layer, for example:

- `ModOnlineClient`

This is not upload-only. It is the mod's complete online communication layer.

Suggested responsibilities:

- read `installation.bpp`
- read `installation.key`
- sign requests
- upload run bundles or other artifacts
- query ghost battles
- create replay links
- download replay payloads
- publish player observations

Feature-specific sub-clients can sit under the shared layer:

- `RunBundleClient`
- `GhostBattleClient`
- `ReplayClient`
- `ObservationClient`

They should all reuse the same:

- installation metadata loader
- request signer
- route builder
- auth/session error handling

## Request Authentication

Every V2 authenticated request should include:

- `installation_id`
- timestamp
- content hash
- signature

Suggested verification flow:

1. resolve `installation_id`
2. confirm installation is active
3. load installation public key
4. verify timestamp window
5. verify body hash
6. verify request signature
7. resolve `player_account_id` from the installation row

The request auth model should not depend on:

- legacy `client_id`
- legacy `install_id`
- post-hoc `bind-player`

## Server API Surface

Suggested V2 routes:

- `POST /v2/register`
- `POST /v2/login`
- `POST /v2/installations`
- `POST /v2/installations/observations`
- `POST /v2/run-bundles`
- `GET /v2/ghost-battles`
- `POST /v2/ghost-battles/:battleId/replay-link`
- `GET /v2/replays/:token`

### `POST /v2/register`

Purpose:

- create a new user keyed by the currently observed `player_account_id`

Input should include:

- observed `player_account_id`
- observed `player_username`
- password
- optional stream profile fields

### `POST /v2/login`

Purpose:

- authenticate an existing user via `player_username + password`

The server resolves the matching `player_account_id` and returns a normal authenticated installer session.

### `POST /v2/installations`

Purpose:

- create a new activated installation for the currently logged-in and confirmed user

Input should include:

- current installer session
- current observed `player_account_id`
- installation public key

Output should include:

- `installation_id`
- active installation status

### `POST /v2/installations/observations`

Purpose:

- record mod-observed identity claims for auditing and installer UX

This route does not itself complete activation.

## Upload And Query Semantics

The mod still uploads directly.

Installer is not a request proxy.

Therefore:

- installer provides identity bootstrap and activation state
- mod provides runtime uploads and online requests

All V2 uploaded data should be keyed by:

- `player_account_id`
- `installation_id`

Do not preserve nullable uploader/player linkage fields in the V2 model.

If run bundle upload is adopted, it should be introduced directly on the new V2 service surface rather than retrofitted into legacy endpoints.

## Conflict Handling

V2 first release should use the simplest hard rule:

- one `player_account_id` belongs to one user
- no appeal flow
- no automatic transfer
- no takeover flow

If an observed `player_account_id` does not belong to the currently logged-in user, installer must refuse activation for mod operations that need a new installation key.

## Migration Strategy

Treat V2 as a clean-break service model.

Recommended rollout:

1. keep V1 service behavior for existing clients
2. add independent V2 routes and V2 tables
3. ship updated installer and mod that only use V2
4. migrate old data into V2 where ownership is reliable
5. keep unresolved legacy data marked as legacy instead of forcing incorrect attribution

Do not attempt to reinterpret the old `client_id / bind-player` flow as if it were already the V2 identity model.

## Non-Goals

This design does not include:

- third-party or official game account verification
- account appeal or transfer flow
- multiple streaming platforms per user
- installer acting as an online proxy for mod traffic
- backward-compatible reuse of the old authentication model

## Recommendation

Proceed with V2 as a new identity and upload model, not as an incremental modification of the current service.

The most important decisions in this design are:

- `player_account_id` is the business user primary key
- installer owns activation and key issuance
- mod owns runtime communication
- binding requires explicit installer confirmation
- installation request signing replaces the legacy client registration flow

These decisions remove the current ambiguity between request authenticity and business ownership and give future upload changes, including run-bundle upload, a stable base to build on.
