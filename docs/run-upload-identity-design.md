# Run Upload Identity And Binding Design

## Scope

This document defines the end-state design for BazaarPlusPlus run upload identity, signed
requests, optional account binding, and dual-backend routing.

This is a design for the current repository shape, not a historical plan. The code under
`Game/RunLogging/Upload/` is the source of truth for the implemented client baseline, while this
document defines the intended server contract and the account-binding model that sits above it.

## Goals

- Keep local SQLite as the source of truth.
- Keep upload fully opt-in and disabled by default.
- Avoid any blocking network dependency inside live gameplay.
- Authenticate the install instance with a client-held private key.
- Support anonymous upload before account binding exists.
- Bind one or more install clients to a user account `uid` through a server-controlled flow.
- Support `Global` and `CN` backends with `Auto` mode trying `Global` first and falling back to `CN`.
- Allow `CN` endpoints to temporarily use HTTPS IP endpoints before ICP/domain cutover is ready.

## Non-Goals

- No in-game password login flow.
- No direct trust of client-supplied `uid`.
- No guarantee that upload always succeeds.
- No anti-cheat guarantees. This system authenticates install identity, not data truthfulness.

## Identity Model

There are three separate identity layers:

1. `install_id`
   - Generated and persisted locally by the client.
   - Represents one local installation.

2. `client_id`
   - Issued by the server during client registration.
   - Bound to a public key.
   - Represents one authenticated install client on the server side.

3. `uid`
   - The platform account identifier.
   - Represents the user account.

The required relationship is:

```text
uid
  -> client_id A
  -> client_id B

client_id
  -> install_id
  -> keypair
```

Private keys must never be treated as direct proof of `uid`. They only prove that the caller holds
the private key for a specific registered `client_id`.

## Core Principle

Private key ownership proves:

- "I am this client."

It does not prove:

- "I am this user account."

Therefore:

- private key -> `client_id`
- server binding flow -> `client_id -> uid`

## Client Baseline

The client already supports:

- local run persistence into SQLite
- dirty run tracking in `run_sync_state`
- background upload only outside live runs
- install id persistence
- route-scoped client registration
- signed upload requests
- route selection with `Global` first and `CN` fallback in `Auto` mode

Relevant source files:

- `Game/RunLogging/Persistence/ReplicatedRunLogStore.cs`
- `Game/RunLogging/Upload/RunUploadController.cs`
- `Game/RunLogging/Upload/RunUploadService.cs`
- `Game/RunLogging/Upload/RunUploadRouting.cs`
- `Game/RunLogging/Upload/RunUploadRegistrationClient.cs`
- `Game/RunLogging/Upload/RunUploadRequestSigner.cs`
- `Game/RunLogging/Upload/RunUploadApiClient.cs`
- `Game/RunLogging/Upload/RunUploadKeyStore.cs`
- `Game/RunLogging/Upload/RunUploadIdentityStore.cs`

## Recommended Server Data Model

### `users`

```text
uid PK
display_name
created_at
updated_at
```

### `clients`

```text
client_id PK
install_id
route_scope
key_algorithm
public_key_json
key_fingerprint
bound_uid NULL
status
created_at
last_seen_at
```

Notes:

- `route_scope` can be `global` / `cn` if you keep registration separate per backend.
- `bound_uid` is nullable so anonymous upload is supported.

### `client_bind_codes`

```text
bind_code PK
client_id
status
expires_at
used_at NULL
created_at
```

### `client_bind_challenges`

Only needed if you implement the optional challenge-confirmation step:

```text
challenge_id PK
client_id
challenge
expires_at
used_at NULL
created_at
```

### `runs`

```text
run_id PK
client_id
uid NULL
install_id
route_kind
schema_version
started_at_utc
ended_at_utc NULL
status
meta_json
checkpoint_json NULL
status_json NULL
created_at
updated_at
```

### `run_events`

```text
run_id
seq
payload_json
PRIMARY KEY (run_id, seq)
```

### `pvp_battles`

```text
battle_id PK
run_id
payload_json
```

### `request_nonces`

```text
client_id
nonce
seen_at
expires_at
PRIMARY KEY (client_id, nonce)
```

## Registration

The client must first register itself before signed upload is accepted.

### Endpoint

`POST /clients/register`

### Request

```json
{
  "install_id": "abc123",
  "plugin_version": "1.9.0",
  "requested_at_utc": "2026-03-27T08:00:00.000Z",
  "public_key": {
    "algorithm": "rsa-pkcs1-sha256",
    "modulus_b64": "...",
    "exponent_b64": "...",
    "fingerprint": "..."
  }
}
```

### Response

```json
{
  "client_id": "client_001",
  "status": "registered"
}
```

### Registration Rules

- First registration for an `install_id` may create a new `client_id`.
- Repeated registration with the same key may return the existing `client_id`.
- Repeated registration with a different key for the same install should be rejected by default.
- The server must persist the public key and key fingerprint.

## Signed Upload

### Endpoint

`POST /runs/upload`

### Required Headers

- `X-BPP-Client-Id`
- `X-BPP-Install-Id`
- `X-BPP-Run-Id`
- `X-BPP-Plugin-Version`
- `X-BPP-Timestamp`
- `X-BPP-Nonce`
- `X-BPP-Content-SHA256`
- `X-BPP-Signature-Alg`
- `X-BPP-Signature`

### Request Body

The request body is the full run snapshot:

```json
{
  "schema_version": 1,
  "install_id": "abc123",
  "client_id": "client_001",
  "plugin_version": "1.9.0",
  "submitted_at_utc": "2026-03-27T08:00:00.000Z",
  "run_id": "run-001",
  "meta": {},
  "events": [],
  "checkpoint": {},
  "status": {},
  "pvp_battles": []
}
```

### Canonical String

The client signs this exact newline-joined string:

```text
POST
/runs/upload
{client_id}
{install_id}
{timestamp}
{nonce}
{body_sha256}
```

### Verification Rules

The server must:

1. Load the client public key by `client_id`.
2. Check `install_id` belongs to that client.
3. Recompute the SHA-256 of the raw UTF-8 body.
4. Rebuild the canonical string exactly.
5. Verify the RSA PKCS#1 SHA-256 signature.
6. Reject stale timestamps outside a short window such as 5 minutes.
7. Reject reused nonces within the replay-protection window.
8. Store upload idempotently by `run_id` and `(run_id, seq)`.

## Account Binding

The client must never directly declare its `uid` and expect the server to trust it.

The recommended binding model is:

- client proves install identity with private key
- user proves account ownership by authenticating on the website
- server links the two

### Recommended Flow: Device Code Binding

This is the preferred flow for a game mod.

#### Step 1

Client is already registered and has `client_id`.

#### Step 2

Client requests a bind code.

`POST /clients/{client_id}/bind-code`

Request:

```json
{
  "requested_at_utc": "2026-03-27T08:05:00.000Z"
}
```

Response:

```json
{
  "bind_code": "ABCD-1234",
  "expires_at_utc": "2026-03-27T08:15:00.000Z",
  "verification_url": "https://your-site.example/device"
}
```

#### Step 3

User opens `verification_url`, logs in, and enters `bind_code`.

#### Step 4

Server associates the logged-in `uid` with the `client_id`.

#### Step 5

Client polls:

`GET /clients/{client_id}/binding-status?bind_code=ABCD-1234`

Pending response:

```json
{
  "status": "pending"
}
```

Bound response:

```json
{
  "status": "bound",
  "uid": "user_123",
  "bound_at_utc": "2026-03-27T08:07:00.000Z"
}
```

Expired response:

```json
{
  "status": "expired"
}
```

### Optional Hardening: Challenge Confirmation

If stronger proof is needed after device-code entry, add:

`POST /clients/{client_id}/bind/start`

Response:

```json
{
  "challenge_id": "challenge_001",
  "challenge": "random_base64",
  "expires_at_utc": "2026-03-27T08:10:00.000Z"
}
```

Then:

`POST /clients/{client_id}/bind/complete`

```json
{
  "challenge_id": "challenge_001",
  "signature": "base64"
}
```

The server verifies the signature with the stored public key before finalizing the binding.

## Binding Rules

- One `uid` may bind multiple `client_id`s.
- One `client_id` may bind only one `uid` at a time.
- Rebinding should require explicit server-side confirmation or an unbind flow.
- Bind, unbind, and migration operations should be auditable.

## Anonymous Upload

Anonymous upload is supported by design.

- If a client is not yet bound to a `uid`, uploads still succeed.
- Such uploads remain associated only with `client_id`.
- Once a binding exists, new uploads may be attributed to `uid`.
- Historical runs may optionally be backfilled to the bound `uid` asynchronously.

## Dual Backend Routing

The client supports these modes:

- `Off`
- `Global`
- `CN`
- `Auto`

### Mode Semantics

- `Off`
  - Disable upload completely.
- `Global`
  - Use only the Global endpoint pair.
- `CN`
  - Use only the CN endpoint pair.
- `Auto`
  - Try Global first.
  - If Global registration or upload fails enough times, fall back to CN.

### Current Auto Policy

The intended policy is deterministic:

1. Prefer Global.
2. If Global fails, try CN.
3. Cache a CN preference temporarily after repeated Global failures.
4. After the cache window expires, try Global first again.

### China Deployment Transition

Before ICP/domain cutover is ready:

- `RegistrationEndpointCn` and `EndpointCn` may point to HTTPS IP endpoints.

After ICP/domain cutover:

- Replace those config values with the final CN domain.
- No client logic change is required.

## Cross-Backend Identity Strategy

There are two viable models:

### A. Route-Scoped Clients

- Global and CN maintain separate `client_id`s.
- Client state is stored per route.
- Simpler to deploy initially.

### B. Shared Central Identity

- One shared identity service issues `client_id`s.
- Global and CN backends trust the same identity layer.
- Better for long-term account coherence.

Recommended rollout:

- Start with A.
- Move to B later if unified identity becomes necessary.

## Privacy And User Control

The system must preserve the following guarantees:

- Upload is disabled by default.
- Upload begins only after explicit user opt-in.
- Users can disable upload at any time.
- Account binding is optional.
- Anonymous upload remains possible.

This must be reflected in both configuration and documentation.

## Security Limits

This system can prevent:

- unauthenticated request forgery
- forged use of another client's public identity without the private key
- replay of previously captured signed requests within the nonce window
- direct impersonation of arbitrary `uid` values

This system cannot fully prevent:

- a local user altering run content before signing it
- device compromise or private key theft
- malicious clients submitting false but validly signed data

This is install authentication, not anti-cheat.

## Idempotency Rules

### Register

- repeated register with same install + same key must be safe

### Upload

- repeated upload of same run must be safe
- `run_events` must deduplicate on `(run_id, seq)`

### Bind Code

- a new bind code may invalidate older pending codes for the same client

### Bind Complete

- duplicate bind completion for the same unresolved challenge must be safe

## Recommended Rollout

### Phase 1

- local SQLite remains primary
- background upload only
- install id + signed upload
- dual backend routing

### Phase 2

- implement `register`
- implement `upload`
- add nonce store and signature verification

### Phase 3

- add device-code binding
- add binding-status polling

### Phase 4

- add optional challenge confirmation
- add historical run backfill to `uid`
- add multi-device account views

## Minimal Required Endpoints

Recommended minimum server API:

- `POST /clients/register`
- `POST /runs/upload`
- `POST /clients/{client_id}/bind-code`
- `GET /clients/{client_id}/binding-status?bind_code=...`

Optional hardening endpoints:

- `POST /clients/{client_id}/bind/start`
- `POST /clients/{client_id}/bind/complete`

## Acceptance Criteria

The design is considered complete when:

- upload can remain fully disabled by config
- upload can work anonymously before binding exists
- signed requests authenticate clients correctly
- `Auto` route mode prefers Global and falls back to CN
- `CN` can temporarily use HTTPS IP endpoints
- account binding never depends on trusting a client-supplied `uid`
- one `uid` can aggregate multiple install clients
