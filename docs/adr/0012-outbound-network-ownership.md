# ADR-0012: Outbound Mod API rules have protocol and persistence owners

Status: Accepted, 2026-08-03

## Context

The V5 protocol migration replaced the wire format but left shared rules distributed across Game
workflows and thin handle bags. Bundle composition and Ghost import used different replayability
predicates; endpoint clients parsed the same error envelope, request correlation, and retry hints;
`ModOnlineClient` exposed concrete clients without hiding policy; Bundle queue SQL lived in both the
seal coordinator and upload feed.

The shared rules are real, but endpoint dispositions are intentionally different. A Bundle conflict,
a Ghost cooldown, a BazaarDB link outcome, and health presentation must not become one configurable
generic result (see ADR-0009).

## Decision

### Wire contract

`RunBundleV5Contract` is the shared reader/writer boundary for a V5 Bundle carrying a Run payload. It
opens the container, decodes the payload, verifies Run and player identity, and owns the exact
replayability predicate
([`RunBundleV5Contract.cs:83`](../../src/BazaarPlusPlus.ModApi/Bundle/RunBundleV5Contract.cs#L83)).
The low-level `BundleV5Codec` remains able to open opaque Run bytes; Ghost import selects a requested
Battle through `OpenedRunBundleV5.TryGetReplayableBattle`
([`RunBundleV5Contract.cs:46`](../../src/BazaarPlusPlus.ModApi/Bundle/RunBundleV5Contract.cs#L46)).

`ModApiResponse` is the single bounded JSON-response reader. It owns nested V5 and legacy envelope
recognition, header-over-body request-ID precedence, `Retry-After`, and a closed user code
([`ModApiResponse.cs:52`](../../src/BazaarPlusPlus.ModApi/Http/ModApiResponse.cs#L52)). Raw body and
exceptions remain diagnostic material. Arbitrary remote `error.code` values also remain protocol
metadata: endpoint clients must explicitly allow-list any code that drives product behavior and
otherwise expose only the HTTP-status code. Endpoint clients retain their disposition, cooldown,
expiry, and presentation mappings.

`MessagePackGzipFraming` owns only gzip framing, bounded decompression, and framing failure
classification
([`MessagePackGzipFraming.cs:43`](../../src/BazaarPlusPlus.ModApi/MessagePackGzipFraming.cs#L43)).
`RunPayloadV5Codec` continues to own V5 limits, untrusted-data options, version checks, and V5 error
codes. The local replay codec keeps its own compatibility and user-text behavior.

### Transport lifetime

`ModApiSession` owns one normalized route set, one configured `HttpClient`, and typed Bundle, Ghost,
and health operations; it exposes neither the transport nor endpoint clients and disposes the
transport once
([`ModApiSession.cs:6`](../../src/BazaarPlusPlus.ModApi/Clients/ModApiSession.cs#L6)). Plugin History,
each Upload Feed activation, and tools own independent sessions. BazaarDB remains separate because it
is a different host and product workflow.

Absence of a valid Mod API session removes only online History capabilities. The local History panel
continues to mount through the explicit mount decision; transport absence is not a panel-wide mount
failure.

### Durable Bundle queue

The concrete `BundleQueueStore` is the only owner of `bundle_seal_jobs` and `bundle_outbox` SQL, row
materialization, and multi-row transactions
([`BundleQueueStore.cs:9`](../../src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs#L9)).
Its public records expose typed seal and screenshot states; translation to persisted string values
remains inside Storage.
Allocation is idempotent, publish verifies the durable allocation and atomically inserts the outbox
row before deleting the seal job
([`BundleQueueStore.cs:71`](../../src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs#L71),
[`BundleQueueStore.cs:144`](../../src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs#L144));
invalid pending artifacts are failed and reseal is scheduled in one transaction
([`BundleQueueStore.cs:250`](../../src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs#L250)).

Storage does not own Bundle files, codecs, HTTP, or upload policy. Game uses `IBundleOutboxFiles` as
the narrow root-confined file seam
([`BundleOutboxFiles.cs:4`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleOutboxFiles.cs#L4)).
For an accepted upload, durable outcome is committed before file deletion
([`BundleUploadFeed.cs:94`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleUploadFeed.cs#L94),
[`BundleUploadFeed.cs:220`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleUploadFeed.cs#L220)); a
transient outcome never deletes the artifact. Startup recovery adopts an orphan only when its Bundle
allocation matches the durable seal job and otherwise applies the existing retention window
([`BundleSealCoordinator.cs:354`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleSealCoordinator.cs#L354)).

There is deliberately no same-shape `IBundleQueueStore`: tests execute the concrete Storage module
against temporary SQLite databases. Interfaces are reserved for effects that need substitution, such
as file deletion timing.

## Consequences

- Game coordinators contain orchestration and mappings, not SQL or response parsing.
- The ModApi and Storage assemblies remain independent of game/Unity/BepInEx/Harmony assemblies.
- MessagePack DTO visibility and the persisted schema graph are unchanged; `RunLogSchema` stays at
  database version 1 and row version 1
  ([`RunLogSchema.cs:7`](../../src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs#L7)).
- Replayability now rejects missing, duplicate, or extra snapshot labels. This is an intentional
  behavioral tightening and is documented in the V5 wire contract.
- User-facing failure text no longer receives raw remote bodies or exception messages. Structured
  diagnostics retain the exception and request correlation.

## Rejected alternatives

- A process-global `HttpClient` or public endpoint-client bag: it obscures ownership and makes one
  consumer's disposal affect another.
- One generic endpoint `Result<T>`: it turns distinct product semantics into configuration and is
  rejected by ADR-0009.
- Moving Bundle file policy into Storage: Storage cannot depend on Game or ModApi and should not own
  codec, HTTP, or filesystem retention behavior.
- Keeping old and new paths in parallel: recovery and retry would have two state owners.
