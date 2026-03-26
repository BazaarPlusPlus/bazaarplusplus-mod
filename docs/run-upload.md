# Run Upload

## Scope

Run upload is an opt-in background feature layered on top of local SQLite persistence.

- Local SQLite remains the source of truth.
- Upload is disabled by default.
- Upload only runs when `RunUpload.Enabled = true`.
- Upload only runs while the player is not in a live run.

For the full identity, binding, and dual-backend design, see
`run-upload-identity-design.md`.

## Client Flow

1. The mod writes run data to local SQLite as usual.
2. `ReplicatedRunLogStore` marks the completed run as dirty in `run_sync_state`.
3. `RunUploadController` wakes up after startup delay and scans dirty completed runs.
4. The client loads or creates:
   - `install-id.txt`
   - `run-upload-client.json`
   - `run-upload-rsa.json`
5. If no `client_id` exists yet, the client registers its install and public key.
6. Completed run snapshots are uploaded with signed request headers.

## Config

`BazaarPlusPlus.cfg`

```ini
[RunUpload]
Enabled = false
Mode = Auto
RegistrationEndpoint =
Endpoint =
RegistrationEndpointGlobal =
EndpointGlobal =
RegistrationEndpointCn =
EndpointCn =
StartupDelaySeconds = 20
IntervalSeconds = 180
BatchSize = 3
GlobalFailureThreshold = 2
PreferredRouteCacheMinutes = 1440
```

Users who do not want upload should leave `Enabled = false`.

Route semantics:

- `Off`: disable upload even if `Enabled = true`
- `Global`: use only the Global endpoint pair
- `CN`: use only the CN endpoint pair
- `Auto`: try Global first; if registration or upload fails repeatedly, fall back to CN

Legacy `RegistrationEndpoint` and `Endpoint` are still accepted as the Global endpoint pair when the
new `...Global` settings are empty.

During ICP setup, `RegistrationEndpointCn` and `EndpointCn` may temporarily point at HTTPS IP
endpoints. After ICP is complete, switch those settings to the final CN domain without changing
client logic.

## Register Endpoint

`POST /clients/register`

Request body:

```json
{
  "install_id": "string",
  "plugin_version": "string",
  "requested_at_utc": "2026-03-27T00:00:00.000Z",
  "public_key": {
    "algorithm": "rsa-pkcs1-sha256",
    "modulus_b64": "base64",
    "exponent_b64": "base64",
    "fingerprint": "base64"
  }
}
```

Success response:

```json
{
  "client_id": "string"
}
```

Server rules:

- first registration for an `install_id` may create a new client
- repeated registration with the same public key may return the existing `client_id`
- repeated registration with a different key for the same `install_id` should be rejected

## Upload Endpoint

`POST /runs/upload`

Request body is the full run snapshot payload:

- `schema_version`
- `install_id`
- `client_id`
- `plugin_version`
- `submitted_at_utc`
- `run_id`
- `meta`
- `events`
- `checkpoint`
- `status`
- `pvp_battles`

Required headers:

- `X-BPP-Client-Id`
- `X-BPP-Install-Id`
- `X-BPP-Plugin-Version`
- `X-BPP-Run-Id`
- `X-BPP-Timestamp`
- `X-BPP-Nonce`
- `X-BPP-Content-SHA256`
- `X-BPP-Signature-Alg`
- `X-BPP-Signature`

## Signature Canonical String

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

Where:

- `POST` is uppercase HTTP method
- `/runs/upload` is the request path only
- `body_sha256` is base64-encoded SHA-256 of the raw UTF-8 request body
- `signature` is base64-encoded RSA PKCS#1 v1.5 SHA-256 signature

## Server Verification Rules

The server should:

1. Load the client public key by `client_id`
2. Check `install_id` matches the registered client
3. Recompute `body_sha256` from the raw body
4. Rebuild the canonical string exactly
5. Verify the RSA signature
6. Reject stale timestamps outside a short window such as 5 minutes
7. Reject reused nonces inside that window
8. Upsert run data idempotently by `run_id` and `(run_id, seq)`

## Privacy

- Upload is opt-in only.
- Users can disable it with one config flag.
- `account_id` is not used for authentication.
- This phase authenticates the install instance, not the real game account.
