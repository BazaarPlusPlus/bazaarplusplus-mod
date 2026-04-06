# RunBundle Upload Design

## Goal

Replace the current two-lane upload model:

- one run summary upload per run
- one battle artifact upload per saved PvP replay

with a single upload unit:

- one RunBundle upload per completed run

This change is driven by upload amplification and storage pressure. The current design scales object count and request count with PvP battle count, which is no longer acceptable at higher user volume.

## Current Problems

### Request amplification

Today a completed run can generate:

- 1 `POST /runs`
- N `POST /battles`

For runs with multiple PvP combats, this multiplies client requests, Worker invocations, signature checks, and object-store writes.

### Object-store amplification

The server currently stores:

- one run summary object
- one replay object per uploaded battle

This creates excessive object count and metadata overhead in R2.

### Payload inefficiency

Current battle replay payloads are JSON and encode replay messages as Base64 strings:

- `spawn_message_base64`
- `combat_message_base64`
- `despawn_message_base64`

This inflates data size before compression. Even with gzip on the server side, Base64 adds avoidable overhead.

### Upload logic is fragmented

Upload logic currently lives in multiple feature-specific modules:

- `Game/RunLogging/Upload/`
- `Game/CombatReplay/Upload/`
- `Game/ModApi/`

That fragmentation makes it harder to evolve protocol, retry policy, compression, routing, and state tracking.

## Proposed Direction

Introduce a dedicated upload subsystem centered on a single run-level protocol:

- local gameplay modules keep writing local state
- upload state is tracked only at the run level
- upload packaging builds one `RunBundleV2` from local persisted data
- upload transport handles registration, signing, compression, and HTTP

The server receives one bundle, stores one bundle object, and still projects indexed `runs` and `battles` metadata into D1 for query and replay discovery.

## High-Level Architecture

### Local persistence remains feature-owned

The following modules keep their current local responsibilities:

- `Game/RunLogging/` persists run facts into SQLite
- `Game/CombatReplay/` persists battle manifests and replay payloads locally
- `Game/PvpBattles/` remains the local manifest catalog

These modules should not contain network upload logic after the refactor.

### Upload becomes a dedicated subsystem

Add a new upload subtree:

```text
Game/
  Upload/
    UploadCoordinatorController.cs
    UploadCoordinator.cs
    UploadAttemptGate.cs
    UploadAttemptRunner.cs

    State/
      RunUploadStateStore.cs
      RunUploadStateRecord.cs

    Auth/
      UploadIdentityStore.cs
      UploadClientStateStore.cs
      UploadKeyStore.cs
      UploadAuthenticatedSession.cs
      UploadRegistrationClient.cs
      UploadRequestSigner.cs

    Protocol/
      UploadRoutes.cs
      UploadContentTypes.cs
      UploadErrorFormatter.cs

    RunBundle/
      RunBundleUploadService.cs
      RunBundleUploadApiClient.cs
      RunBundleBuilder.cs
      RunBundleEncoder.cs
      EncodedRunBundle.cs
      RunBundleBuildResult.cs

      Sources/
        IRunBundleSource.cs
        SqliteRunBundleSource.cs

      Models/
        RunBundleV2.cs
        RunBundleRunSummaryV2.cs
        RunBundleBattleEntryV2.cs
        RunBundleBattleManifestV2.cs
        RunBundleBattleParticipantsV2.cs
        RunBundleBattleOutcomeV2.cs
        RunBundleReplayPayloadV2.cs
        RunBundleUploadApiResult.cs
        RunBundleUploadCycleResult.cs
```

## Responsibility Boundaries

### RunLogging

Owns:

- live run capture
- run SQLite persistence
- run completion and abandonment facts

Does not own:

- upload transport
- upload API clients
- upload retry logic
- upload payload format

### CombatReplay

Owns:

- replay capture
- replay payload persistence
- battle manifest persistence
- replay persistence queueing

Does not own:

- standalone replay upload
- battle upload retry state
- battle API clients

### Upload subsystem

Owns:

- upload scheduling
- upload state tracking
- bundle construction
- bundle encoding and compression
- client registration
- request signing
- HTTP upload
- upload retries and failure recording

## RunBundle Protocol

## Top-level model

The upload unit is `RunBundleV2`.

```text
RunBundleV2
- schema_version: int
- install_id: string
- client_id: string?
- plugin_version: string
- submitted_at_utc: string
- run: RunBundleRunSummaryV2
- battles: RunBundleBattleEntryV2[]
```

### Run summary

```text
RunBundleRunSummaryV2
- run_id: string
- status: string
- hero_id: string?
- hero_name: string?
- started_at_utc: string?
- ended_at_utc: string
- final_day: int?
- final_wins: int?
- final_losses: int?
- mmr: int?
```

### Battle entry

```text
RunBundleBattleEntryV2
- battle_id: string
- manifest: RunBundleBattleManifestV2
- replay: RunBundleReplayPayloadV2
```

### Battle manifest

```text
RunBundleBattleManifestV2
- battle_id: string
- run_id: string?
- recorded_at_utc: string
- day: int?
- hour: int?
- encounter_id: string?
- combat_kind: string
- participants: RunBundleBattleParticipantsV2
- outcome: RunBundleBattleOutcomeV2
```

### Battle participants

```text
RunBundleBattleParticipantsV2
- player_name: string?
- player_account_id: string?
- player_hero: string?
- player_rank: string?
- player_rating: int?
- player_level: int?
- opponent_name: string?
- opponent_account_id: string?
- opponent_hero: string?
- opponent_rank: string?
- opponent_rating: int?
- opponent_level: int?
```

### Battle outcome

```text
RunBundleBattleOutcomeV2
- result: string?
- winner_combatant_id: string?
- loser_combatant_id: string?
```

### Replay payload

```text
RunBundleReplayPayloadV2
- version: int
- spawn_message: byte[]
- combat_message: byte[]
- despawn_message: byte[]
```

## Encoding and Compression

### Format choice

Use:

- MessagePack for bundle serialization
- raw `byte[]` for replay messages
- gzip for whole-bundle compression

### Why this format

This design targets the real size drivers:

- replay messages are already binary
- Base64 creates avoidable inflation
- one whole-bundle compression pass is more efficient than many per-battle uploads

MessagePack helps reduce metadata overhead, but the larger savings come from:

- removing Base64
- compressing the full bundle as one unit

### Upload body

Client upload body should be:

- MessagePack-encoded `RunBundleV2`
- then gzip-compressed

Suggested request content metadata:

- `Content-Type: application/x-bpp-run-bundle`
- `Content-Encoding: gzip`

Signature and body hash should be computed from the final transmitted byte array, not from any pre-compression intermediate representation.

## Local Upload State Model

Upload state should become run-scoped only.

### Keep

Continue using a run-level upload state table derived from current `run_sync_state`.

### Remove from active use

Battle-level upload tracking should no longer drive upload behavior:

- `replay_dirty`
- `replay_last_attempt_at_utc`
- `replay_last_uploaded_at_utc`
- `replay_retry_count`
- `replay_last_error`

These fields can remain temporarily for migration compatibility, but they should become dead state.

### New run-level fields

Suggested additions:

- `uploaded_payload_hash`
- `uploaded_bundle_schema_version`
- `last_uploaded_object_key`

## Client Flow

### Triggering

The upload coordinator runs only when:

- community contribution is enabled
- the player is not in a live run

It should still reuse the existing delayed-start and retry cadence logic currently implemented by the startup attempt gate/runner pattern.

### Dirtying

The run should be marked dirty whenever persisted run data changes in a way that affects the final bundle:

- run created
- run event appended
- checkpoint saved
- run completed
- run abandoned
- replay persistence drained for a battle associated with that run

The replay module should no longer mark battle upload state dirty. Instead, once replay persistence drains, the owning run should be re-armed for upload.

### Bundle build flow

For each pending completed run:

1. load run summary from local SQLite
2. load local battles for that run
3. load local replay payload files for those battles
4. decode Base64 replay messages back into raw bytes
5. construct `RunBundleV2`
6. encode with MessagePack
7. gzip the encoded bytes
8. upload once

### Failure policy

If a completed run bundle cannot be constructed because required local replay payload is missing:

- mark the run upload as failed
- keep it dirty for retry unless the failure is known terminal

Terminal conditions should be rare and explicit. Do not silently drop replay-bearing runs.

## Server Flow

## New upload endpoint behavior

The server should accept a new run-bundle upload path or repurpose `/runs` for the new binary bundle protocol. Either way, protocol versioning must be explicit.

Recommended server flow:

1. verify client signature
2. read transmitted bytes
3. gunzip bytes
4. decode MessagePack bundle
5. validate bundle integrity and required fields
6. store the original compressed bundle as one R2 object
7. upsert `runs` projection
8. upsert `battles` projection for each embedded battle

## Object storage layout

Recommended R2 object key:

```text
run-bundles/<client_id>/<run_id>/<payload_hash>.mpack.gz
```

Only one object should be written per uploaded run bundle.

## D1 projection model

### Runs

The `runs` table should store:

- current run summary projection
- bundle object metadata

Suggested additions:

- `bundle_schema_version`
- `bundle_object_key`
- `bundle_codec`
- `bundle_size_bytes`

### Battles

The `battles` table should continue storing query-facing battle metadata, but replay lookup should point back to the containing run bundle, not to a per-battle object.

Suggested fields:

- `replay_container_object_key`
- `replay_container_codec`

The old `replay_object_key` can remain during migration, but new writes should use the container-based lookup model.

## Replay Download Flow

The replay download API should continue to authorize replay access per battle id, but object retrieval changes.

### Old path

Current model:

1. lookup battle row
2. fetch `replay_object_key`
3. read one stored replay object
4. return replay JSON

### New path

New model:

1. lookup battle row by `battle_id`
2. read `replay_container_object_key`
3. fetch the run bundle object
4. gunzip and decode the bundle
5. find the requested battle entry
6. return the replay payload

### Compatibility recommendation

For download responses, keep the existing client-facing JSON replay shape at first:

```text
{
  battle_id,
  version,
  spawn_message_base64,
  combat_message_base64,
  despawn_message_base64
}
```

That keeps replay playback compatibility stable while upload and storage internals change underneath.

In other words:

- storage format changes now
- download response format can lag and remain backward-compatible

## Class Design

## Coordinator layer

### `UploadCoordinatorController`

Purpose:

- Unity-facing lifecycle entrypoint
- owns subscriptions and shutdown token
- ticks the upload attempt runner

Methods:

- `Awake()`
- `Update()`
- `OnDestroy()`
- `OnRunLifecycleChanged(RunLifecycleChanged change)`
- `OnCombatReplayPersistenceDrained(CombatReplayPersistenceDrained drained)`

### `UploadCoordinator`

Purpose:

- orchestrates one upload cycle
- delegates actual run bundle upload to the service

Methods:

- `Task ProcessPendingRunsAsync(CancellationToken cancellationToken)`
- `void ArmImmediateAttempt()`

## State layer

### `RunUploadStateStore`

Purpose:

- owns run-level upload dirty state and retry state

Methods:

- `void MarkRunDirty(string runId)`
- `IReadOnlyList<string> GetPendingCompletedRunIds(int limit)`
- `bool HasMorePendingCompletedRuns()`
- `void MarkUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error)`
- `void MarkUploaded(string runId, DateTimeOffset uploadedAtUtc, string payloadHash, int bundleSchemaVersion)`
- `RunUploadStateRecord? TryGetState(string runId)`

## Bundle source layer

### `IRunBundleSource`

Purpose:

- abstraction for building a full run bundle from persisted local data

Methods:

- `RunBundleBuildResult? TryBuildRunBundle(string runId, string installId, string? clientId = null)`

### `SqliteRunBundleSource`

Purpose:

- reads local SQLite rows and replay payload files
- converts persisted local state into `RunBundleV2`

Methods:

- `RunBundleBuildResult? TryBuildRunBundle(string runId, string installId, string? clientId = null)`
- `RunBundleRunSummaryV2? TryLoadRunSummary(SqliteConnection connection, string runId, string installId, string? clientId)`
- `IReadOnlyList<RunBundleBattleEntryV2> LoadBattleEntries(string runId)`
- `RunBundleBattleEntryV2? TryLoadBattleEntry(PvpBattleManifest manifest)`
- `byte[]? LoadReplayMessageBytes(string base64)`
- `bool HasMissingReplayPayload(string runId)`

## Bundle packaging layer

### `RunBundleBuilder`

Purpose:

- assembles a final bundle object from already loaded parts

Methods:

- `RunBundleV2 Build(string installId, string? clientId, RunBundleRunSummaryV2 run, IReadOnlyList<RunBundleBattleEntryV2> battles)`

### `RunBundleEncoder`

Purpose:

- MessagePack serialization
- gzip compression
- optional local decode utilities for testing and tooling

Methods:

- `EncodedRunBundle Encode(RunBundleV2 bundle)`
- `RunBundleV2 Decode(byte[] payloadBytes)`
- `byte[] SerializeMessagePack(RunBundleV2 bundle)`
- `byte[] Compress(byte[] rawBytes)`
- `byte[] Decompress(byte[] compressedBytes)`

## Upload transport layer

### `RunBundleUploadService`

Purpose:

- owns per-cycle upload loop for pending runs

Methods:

- `Task<RunBundleUploadCycleResult> UploadPendingRunBundlesAsync(CancellationToken cancellationToken)`
- `RunBundleUploadApiClient CreateApiClient()`
- `UploadAuthenticatedSession CreateAuthenticatedRouteClient()`

### `RunBundleUploadApiClient`

Purpose:

- sends one encoded bundle to the server

Methods:

- `Task<RunBundleUploadApiResult> UploadRunBundleAsync(EncodedRunBundle bundle, string clientId, string installId, string runId, CancellationToken cancellationToken)`

## Auth and signing layer

### `UploadAuthenticatedSession`

Methods:

- `Task<UploadAuthenticatedRequestResult<TResult>> SendAsync<TResult>(string installId, Func<string, CancellationToken, Task<TResult>> sendAsync, CancellationToken cancellationToken)`

### `UploadRegistrationClient`

Methods:

- `Task<string?> EnsureClientRegistrationAsync(string installId, CancellationToken cancellationToken)`

### `UploadRequestSigner`

Methods:

- `HttpRequestMessage CreateSignedBinaryUploadRequest(string uploadEndpoint, byte[] bodyBytes, string clientId, string installId, string runId, string contentType, string? contentEncoding, IReadOnlyDictionary<string, string?>? extraHeaders = null)`
- `HttpRequestMessage CreateSignedRequest(HttpMethod method, string endpoint, byte[]? bodyBytes, string clientId, string installId, string? contentType, string? contentEncoding, IReadOnlyDictionary<string, string?>? extraHeaders = null)`
- `static string ComputeBodyHash(byte[] bytes)`
- `static string BuildCanonicalRequest(string method, string absolutePath, string clientId, string installId, string timestamp, string bodyHash)`

## Existing File Changes

### `Plugin.cs`

Replace:

- `RunUploadController`
- `BattleUploadController`

with:

- `UploadCoordinatorController`

### `RunLoggingController.cs`

Change the replicated persistence path so it marks run upload dirty through the new run-level upload state store instead of the old run-summary-specific upload store.

### `CombatReplayRuntime.cs`

Remove battle upload concerns:

- remove battle upload store ownership
- remove `MarkReplayDirty`

Keep:

- replay persistence result draining
- `CombatReplayPersistenceDrained` event publication

The upload subsystem should react to the drain event and re-arm run-level upload.

### `RunLogSqliteSchema.cs`

Keep the run upload state table but evolve it for bundle tracking:

- add `uploaded_payload_hash`
- add `uploaded_bundle_schema_version`
- add `last_uploaded_object_key`

Battle replay upload status fields should stop driving behavior and can be removed in a later cleanup migration.

## Files to Retire

The following old upload files should be deleted once the new path is live:

- `Game/RunLogging/Upload/RunUploadController.cs`
- `Game/RunLogging/Upload/RunSummaryUploadService.cs`
- `Game/RunLogging/Upload/RunSummaryUploadApiClient.cs`
- `Game/RunLogging/Upload/RunSummaryUploadPayload.cs`
- `Game/RunLogging/Upload/RunUploadSqliteStore.cs`
- `Game/CombatReplay/Upload/BattleUploadController.cs`
- `Game/CombatReplay/Upload/BattleArtifactUploadService.cs`
- `Game/CombatReplay/Upload/BattleArtifactUploadApiClient.cs`
- `Game/CombatReplay/Upload/BattleArtifactUploadRequestSigner.cs`
- `Game/CombatReplay/Upload/BattleUploadSqliteStore.cs`

## Migration Strategy

Implement in this order.

### Phase 1: Add the new path

- add `RunBundleV2` models
- add bundle source, builder, encoder, and upload service
- add server support for run bundle uploads
- keep old server endpoints working

### Phase 2: Switch the client

- replace old run upload controller with upload coordinator
- disable standalone battle upload
- start writing only run bundle uploads

### Phase 3: Compatibility bridge

- keep replay download API compatible
- server supports replay lookup from both:
  - legacy `replay_object_key`
  - new `replay_container_object_key`

### Phase 4: Cleanup

- remove legacy battle upload client path
- remove dead battle upload state handling
- remove or migrate obsolete schema fields

## Minimum First Implementation

The smallest useful vertical slice is:

1. add bundle models
2. implement `SqliteRunBundleSource`
3. implement `RunBundleEncoder`
4. implement `RunUploadStateStore`
5. implement `RunBundleUploadApiClient`
6. implement `RunBundleUploadService`
7. add `UploadCoordinatorController`
8. switch `Plugin.cs` to the new controller
9. stop creating standalone battle upload controllers

That gives a working single-upload path before deeper cleanup.

## 设计分析

### 问题根源

当前每次完成的跑局（run）会产生：

- 1 个 `POST /runs`（跑局摘要）
- N 个 `POST /battles`（每场 PvP 战斗各一个）

导致请求数、R2 对象数、Worker 调用数都随战斗场数线性放大。同时 replay 消息用 Base64 编码存 JSON，在压缩前额外膨胀数据体积。

### 核心设计

用一个 `RunBundleV2` 打包整跑的所有数据，一次上传：

```
RunBundleV2
├── run: RunBundleRunSummaryV2         # 跑局摘要
└── battles: RunBundleBattleEntryV2[]  # 所有战斗（含 manifest + replay）
    └── replay: RunBundleReplayPayloadV2
        ├── spawn_message:  byte[]     # 原始二进制，不再 Base64
        ├── combat_message: byte[]
        └── despawn_message: byte[]
```

编码方式：MessagePack 序列化 → gzip 整包压缩，一次传输。

### 职责边界重划

| 模块 | 保留 | 移除 |
|---|---|---|
| `RunLogging` | 本地 SQLite 持久化 | 上传逻辑 |
| `CombatReplay` | 回放捕获、本地持久化、drain 事件 | 独立上传、battle 上传状态 |
| 新 `Upload/` 子系统 | 调度、打包、编码、签名、HTTP、重试 | — |

### 客户端上传流程

1. 跑局/replay drain 时，标记 run 为 dirty
2. 协调器空闲时（不在跑局中）触发
3. 从 SQLite 加载 run summary + battles + replay 文件
4. 解 Base64 → 原始 bytes → 构建 `RunBundleV2`
5. MessagePack 编码 → gzip → 一次 POST

### 服务端变化

- 一个 bundle 存一个 R2 对象：`run-bundles/<client_id>/<run_id>/<hash>.mpack.gz`
- D1 仍然维护 `runs` / `battles` 表的索引投影
- **replay 下载**：从 bundle 对象解包后提取单场战斗，但下载响应格式暂时保持老 JSON 格式（向后兼容）

### 迁移四阶段

1. **Phase 1**：加新路径（保留旧端点）
2. **Phase 2**：切换客户端，停掉旧独立上传
3. **Phase 3**：兼容桥接（两种 replay 查找都支持）
4. **Phase 4**：清理旧代码

### 总结

整体思路是**把 N 次上传压缩成 1 次，把 N 个对象压缩成 1 个**，以解决规模增长后的放大问题。设计比较清晰，主要 trade-off 是 replay 下载时需要从整包里解出单条，但文档认为这个代价可接受，等到真成热路径再优化。

---

## Open Questions

These need explicit decisions during implementation.

### Endpoint shape

Choose one:

- reuse `/runs` with content-type-based protocol switch
- add a new route such as `/run-bundles`

Recommendation:

- add a dedicated new route for clarity and rollback safety

### Server-side replay extraction cost

The new model trades fewer writes for more work on replay download, because a single battle replay must be extracted from a run bundle.

Recommendation:

- accept that tradeoff first
- optimize later only if replay download becomes hot

### Maximum bundle size

Large runs with many stored PvP battles may create larger upload bodies.

Recommendation:

- instrument uncompressed and compressed bundle sizes from day one
- add hard limits only after observing real data

### Download response format

Recommendation:

- keep the old JSON replay response contract initially
- postpone client replay protocol changes until upload/storage migration is stable
