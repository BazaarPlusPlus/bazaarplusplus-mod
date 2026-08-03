# BazaarPlusPlus 对外网络层深化设计

状态：已实施并通过全量验证

日期：2026-08-03

基线：`master` @ `76759b9e`
范围：运行期出站网络、`BazaarPlusPlus.ModApi` 及其消费方、Remote Embedded Catalog 消费链、版本检查、构建期远端 seed 下载；不含 BazaarAgent 入站 `HttpListener`

## 1. 结论

这次不建立一个覆盖所有 host、所有结果、所有重试的“万能网络层”。九个深化点分别收紧真正共享的规则，并保留各 feature 的产品语义：

1. **Run Bundle Contract**：V5 replayability、manifest/payload 身份一致性归 `ModApi.Bundle`。
2. **Mod API Response**：V5/legacy error envelope、request ID、`Retry-After` 只有一个 parser；endpoint disposition 不统一。
3. **Mod API Session**：删除只发放裸 `HttpClient`/routes 的浅 `ModOnlineClient`，session 拥有 transport、routes、typed operations 与 dispose。
4. **MessagePack Gzip Framing**：只共享 gzip framing、受控解压循环和失败分类；V5 与本地 replay 的安全/错误语义仍由各 wrapper 拥有。
5. **Bundle Seal Convergence**：从 coordinator 抽出零 Unity/SQLite/I/O 的纯决策核心，并修正 naive-UTC deadline。
6. **Bundle Queue Store**：两张 V5 队列表的 SQL、行 DTO 和事务归 Storage concrete store；文件、codec、HTTP、删除策略留 Game。
7. **Supporter Catalog**：成为 Remote Embedded Catalog 的第三个消费方，删除第二套 cache/refresh 实现。
8. **Release Manifest Client**：版本 manifest 协议从 `MonoBehaviour` 中移入可独立测试的 outbound adapter。
9. **Build Seed Fetcher**：把内联 Roslyn MSBuild 下载任务变成可测试的 build tool；feature parser 仍是 seed 语义的最终 owner。

只有两条硬顺序：**Mod API Response → Mod API Session**，**Bundle Seal Convergence → Bundle Queue Store**。其余候选没有代码级前置关系。

## 2. 探索事实

### 2.1 全部出站点

| 出站 | owner / 构造 | timeout、UA、鉴权 | retry / cache | 证据 |
| --- | --- | --- | --- | --- |
| `POST /bundles` | 每个 `BundleUploadFeed.Session` 自建 factory client | 120s；`BundleUpload` UA；Digest/Content-Length；无 auth | client 不重试；feed 用 SQLite cadence 与 `Retry-After` 调度 | `ModApiUploadDefaults.cs:5-10`；`BundleUploadFeed.cs:39-54,114-128,231-296`；`BundleUploadClient.cs:10-57` |
| `GET /ghost-battles` | Plugin 的 `ModOnlineClient` | 120s；`OnlineClient` UA；无 auth | 429 交给 workflow 进程内 cooldown；结果写本地 repository | `Plugin.cs:230-248`；`GhostBattleClient.cs:21-48`；`GhostBattleSyncService.cs:175-208` |
| 服务端下发的 presigned bundle URL | 同一 Plugin client | 120s；继承 UA；无 mod auth；8,388,607 byte 上限 | 本地文件 cache；403/404 刷新 reference 后只重试一次 | `GhostBattleClient.cs:74-150`；`GhostBattleSyncService.cs:82-167` |
| `GET /health` | 同一 Plugin client | 120s；`OnlineClient` UA；无 auth | 无 retry/cache | `ModApiHealthClient.cs:19-90`；`Plugin.cs:230-248` |
| `POST https://bazaardb.gg/api/profile/link/redeem` | Plugin 独立 client | 30s；`BazaarDbLink` UA；无 auth | 无 retry/cache | `BazaarDbLinkClient.cs:42-103`；`Plugin.cs:214-228` |
| supporter JSON | `BPPSupporterCatalog` 静态 client | 10s；`BPPSupporterCatalog` UA；无 auth | temp cache 1h；无快照失败退避 5m | `BPPSupporterCatalog.cs:13-29,97-169,171-263` |
| voice-lines JSON | `VoiceLinesCatalogFactory` 静态 client | 10s；`VoiceSubtitlesRepository` UA；无 auth | Remote Embedded Catalog；game-root cache 20h | `VoiceLinesCatalogFactory.cs:11-42` |
| ten-win JSON | `TenWinBuildCatalogFactory` 静态 client | 10s；`TenWinBuildRepository` UA；无 auth | Remote Embedded Catalog；game-root cache 20h | `TenWinBuildCatalogFactory.cs:11-42` |
| `latest.json` | 每次 version controller 初始化新建 client | 5s；`VersionCheck` UA；无 auth | 无 retry/cache | `MainMenuVersionCheckController.cs:9-35,85-163` |
| sponsor URL | Unity `Application.OpenURL` | browser-owned | browser-owned | `BPPSupporterLinks.cs:6-14`；`BPPSupporterRow.cs:178-181` |
| V5 E2E `/bundles` | tool 直接 `new HttpClient` | 125s；无 BPP UA；无 auth | 工具显式重复/冲突场景 | `tools/BundleV5E2E/Program.cs:15-26,93-107` |
| voice/ten-win build seed | `RemoteEmbeddedData.targets` 内联 Roslyn task | 2m；无 UA/auth | 文件存在即跳过；force refresh 覆盖；临时文件替换 | `RemoteEmbeddedData.targets:3-38,60-99,105-179,186-203` |

普通项目无条件导入 build target（`BazaarPlusPlus.csproj:21`）；publish 先 fetch，再运行两个真实 seed gate（`run.sh:142-160,192-200`）。NuGet restore 是依赖管理，不属于本设计要重画的产品网络边界。

### 2.2 深度与删除测试

- `ModOnlineClient` 只有构造、两个公开 getter 和 `Dispose`（`ModOnlineClient.cs:4-22`）。删除后复杂度只搬到消费方，是明确浅模块。
- `ModApiJsonPost` 只有 BazaarDB 一个生产调用者；它公开的 generic 形状没有第二种协议消费方（`ModApiJsonPost.cs:13-35`）。其响应读取应并入统一 response owner，而不是保留第二条 public chain。
- `BppHttpClientFactory.Create` 一处隐藏 UA token 清洗并被多类 caller 使用（`BppHttpClientFactory.cs:6-22,24-91`）；删除会复制易错的 header 规则，保留。
- `BundleV5Codec` 用 5 个 public 操作隐藏约 690 行 container/segment/hash/manifest 规则，并在 build 后自开校验（`BundleV5Codec.cs:25-130`）；它是深模块，不拆。
- `IRemoteEmbeddedCatalog<T>` 只有 `TryGet/Warm/Refresh/Dispose` 四个操作，却隐藏 cache→embedded→remote、flight、generation、publish gate、cancel/dispose 语义（`RemoteEmbeddedCatalogContracts.cs:4-13`；`RemoteEmbeddedCatalog.cs:1-1054`）；现有 23 个 xUnit Fact 直接执行核心（`RemoteEmbeddedCatalogTests.cs:8-613`），不另包一层。
- `BundleSealCoordinator` 以三个生命周期成员承担 worker、SQLite、文件恢复、payload、截图、codec 与 publish（`BundleSealCoordinator.cs:15-47,50-113,142-395`），适合抽决策核心；I/O 仍留翻译层。
- V5 queue schema 在 Storage（`RunLogSchema.cs:185-223`），实际 SQL 却散在 coordinator/feed，且 reseal 事务重复（`BundleSealCoordinator.cs:669-705`；`BundleUploadFeed.cs:299-331`），适合 concrete Storage 深模块。

### 2.3 同一规则的分歧

1. 写侧 replayability 要求 `CardSets.Count == 4` 且均非 `Missing`，不查 label（`RunPayloadComposer.cs:395-405`）；读侧要求四个 label 各至少一项非 `Missing`，不查 count/唯一性（`GhostBattleSyncService.cs:372-380`）。本地 composer 固定写四个 label（`RunPayloadComposer.cs:206-214`），所以分歧主要暴露在远端/畸形 bundle。
2. `MessagePackGzipCodec` 与 `RunPayloadV5Codec` 都写 gzip+MessagePack，但 V5 有 64 MiB cap、`UntrustedData` 和 version gate；generic codec 无 cap并泄露异常字符串（`MessagePackGzipCodec.cs:15-18,54-72`；`RunPayloadV5Codec.cs:10-14,57-98`）。这是可共享 framing、不可共享完整协议的差异。
3. `BundleUploadClient` 与 `GhostBattleClient` 各自解析 `Retry-After`（`BundleUploadClient.cs:49-57`；`GhostBattleClient.cs:272-279`）。
4. error body 同时存在 top-level string、nested V5、完全忽略三套路径（`ModApiErrorFormatter.cs:36-63`；`BundleUploadClient.cs:153-186`；`ModApiHealthClient.cs:25-36`）。V5 设计给出 nested envelope（`docs/plans/2026-08-03-v5-data-pipeline-design.md:92-105`），Ghost 测试仍使用 top-level 429（`GhostBattleSync.Tests/Program.cs:79-96`）。
5. build fetch 只检查 HTTP success、最小字节和首个非空白字符（`RemoteEmbeddedData.targets:105-148`）；voice/ten-win runtime parser 才拥有结构语义（`VoiceLinesValidationCore.cs:38-127`；`TenWinBuildCorpus.cs:91-172`）。publish 的 seed gates 会执行真实 parser（`run.sh:150-160`），普通缺 seed build 则不会。

### 2.4 失败表示与覆盖真相

ModApi public/consumer seam 至少有七类“不成功”表示：nullable route、`bool + out reason`、typed exception、`Succeeded/Error` result、BazaarDB outcome enum、Bundle disposition result、取消抛异常而其他异常折为 result。Remote Embedded Catalog 另有 `CatalogIssue`/refresh result；supporter 使用 fallback-only；version controller 只发 state/log。

raw diagnostic 目前会进入部分用户文本：health 把 `Exception.Message` 放进 result（`ModApiHealthClient.cs:96-103`）后由 History formatter使用（`HistoryPanelServerHealth.cs:61-68`）；Ghost 下载错误进入 replay UI（`HistoryPanelReplayService.cs:209-220`）。新 response owner只提供 closed user code，原始 body/exception只允许进结构化诊断字段。

基线的 63 个测试工程中，21 个含 `Microsoft.NET.Test.Sdk`，42 个是 exe-runner；实施后为 68 个（21 xUnit、47 exe-runner）。`run.sh` 按 csproj 内容分派（`run.sh:305-321`）。基线 35 个工程使用显式 `<Compile Include>`；实施后为 38 个，且绝大多数属性折行，不能用单行 grep 计数。网络候选中被 Compile-Include 钉住的生产源主要是 Remote Embedded Catalog contracts/core（`RemoteEmbeddedCatalog.Tests.csproj:23-30`）；另有反射全名 pin `ModApiHealthProbeResult`、`BazaarDbLinkOutcome`（`HistoryPanelServerHealth.Tests/Program.cs:5-9,181-240`）。

关键空白：Bundle feed 真上传路径没有被执行；`StartupUploadRunner.Tests` 只做类型与 batch 常量断言（`StartupUploadRunner.Tests/Program.cs:5-17`）。现有 Bundle pipeline fixture 只覆盖一个已过期、无截图、无 battle 的 seal（`BundlePipeline.Tests/Program.cs:17-75`）。Build Roslyn task 只有 XML 形状测试（`CoreLayeringTests.cs:2655-2691`），没有下载/替换行为测试。

### 2.5 同主题既有记录的独立复核

独立枚举/删除测试后再对照仓库中已有同主题记录，稳定结论是：replayability规则应归ModApi、`ModOnlineClient`是浅handle bag、response parser应单一owner、seal core必须先于queue SQL迁移、supporter应复用Remote Embedded Catalog。这些均由上述活代码证据支持，而非沿用记录结论。

需要纠正/补足之处：

- 范围不能止于六项：两个gzip codec的非等价重复（`MessagePackGzipCodec.cs:15-18,54-72`；`RunPayloadV5Codec.cs:10-14,57-98`）、version controller内嵌协议（`MainMenuVersionCheckController.cs:85-197`）、build target内嵌下载（`RemoteEmbeddedData.targets:60-184`）也属于全部出站面。
- `BundleQueueStore`不应再配一个与concrete implementation等大的单实现interface；临时SQLite直接测试deep concrete store即可，Storage项目引用边界见`BazaarPlusPlus.Storage.csproj:1-18`。
- `ModApi.Tests`没有Test SDK，是exe-runner（`ModApi.Tests.csproj:1-17`）；命令必须是`dotnet run --project`。
- 基线测试工程是63个，不是61个；实施新增5个工程后为68个。分派事实见`run.sh:305-321`。
- supporter lifecycle不能留在static facade；ADR-0002要求composition registry owner（`docs/adr/0002-mountable-feature-registry.md:7-11`）。
- build seed不能在semantic gate前覆盖canonical文件；现有替换/存在即跳过窗口见`RemoteEmbeddedData.targets:82-88,122-164`。

## 3. 候选设计

### C1 — Run Bundle Contract

**现状**：见 §2.3.1；manifest/payload identity 又在 Ghost Game 层手写（`GhostBattleSyncService.cs:230-265`）。低层 `BundleV5Codec.Open` 必须继续允许 opaque run payload，因为 compatibility fixture 明确用单字节 payload（`BundleV5Codec.Tests/Program.cs:226-270`）。

**目标接口**：

```csharp
public static class RunBundleV5Contract
{
    public static bool IsReplayable(RunBattleV5 battle);
    public static RunBundleOpenResult Open(ReadOnlyMemory<byte> bundleBytes);
}

public enum RunBundleOpenFailureKind
{
    ContainerInvalid,
    PayloadInvalid,
    RunIdentityMismatch,
}

public sealed class RunBundleOpenResult
{
    public OpenedRunBundleV5? Value { get; }
    public RunBundleOpenFailureKind? FailureKind { get; }
}

public sealed class OpenedRunBundleV5
{
    public OpenedBundleV5 Bundle { get; }
    public RunPayloadV5 Payload { get; }
    public bool TryGetReplayableBattle(string battleId, out RunBattleV5? battle);
}
```

`Open` 只做 container open、payload decode、manifest/payload `RunId`/`PlayerAccountId` 一致性；typed failure 让 Ghost 保持 `PayloadInvalid → GhostArtifactInvalid`、`RunIdentityMismatch → GhostBattleMismatch`，不依赖临时异常约定。它不会因另一个不相关 battle 的坏 replay ID 拒绝整个 bundle。`TryGetReplayableBattle` 检查目标 ID membership、battle 唯一存在、三段 replay 非空，以及四个必需 label 各恰好一次且非 `Missing`。外部 discovery reference 的 bundle/uploader identity 留 Ghost workflow。

**步骤与验证**：

1. 先加contract与行为矩阵。断言低层仍开opaque payload，高层拒绝run identity mismatch、缺/重label、空replay phase；确切命令见§3.10 C1.1。
2. composer改用`IsReplayable`，补真实battle fixture。断言composer产出的replay ID可由contract取回；确切命令见§3.10 C1.2。
3. Ghost extraction改用高层contract，删除本地run identity/`HasCompleteSnapshots`并补可解码bundle。断言mismatch/incomplete仍映射既有reason；确切命令见§3.10 C1.3。
4. 加architecture fact与契约文字。断言Game两侧不再声明replayability predicate或比较manifest/payload run identity；确切命令见§3.10 C1.4。

**测试**：扩现有 `BundleV5Codec.Tests`、`BundlePipeline.Tests`、`GhostBattleSync.Tests`（均 exe-runner）与 `Architecture.Tests`（xUnit）；不新建工程。

**行为变化**：有意拒绝缺 label、重复 label、额外 card set；不改变 MessagePack keys/schema。历史远端 bundle 若含额外 set，过去读侧可能接受，今后拒绝。已采纳完整四组作为 canonical replayability 契约。

**touchesFiles**：

- `src/BazaarPlusPlus.ModApi/Bundle/RunBundleV5Contract.cs`（新增）
- `src/BazaarPlusPlus/Game/BundlePipeline/RunPayloadComposer.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`
- `tests/BundleV5Codec.Tests/Program.cs`
- `tests/BundlePipeline.Tests/Program.cs`
- `tests/GhostBattleSync.Tests/Program.cs`
- `tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs`
- `docs/contracts/run-payload-v5.md`
- `CONTEXT.md`
- `docs/adr/0012-outbound-network-ownership.md`

### C2 — Mod API Response

**现状**：见 §2.3.3-4。Bundle 只从 header 取 request ID，却忽略 nested envelope 的 `request_id`（`BundleUploadClient.cs:42-65,153-186`）。处置词汇确实不同：Bundle matrix（`BundleUploadClient.cs:126-150`）、Ghost cooldown（`GhostBattleSyncService.cs:179-188`）、BazaarDB mapping（`BazaarDbLinkClient.cs:105-123`），不得统一成 generic `Result<T>`。

**目标接口**：

```csharp
public readonly record struct ModApiErrorEnvelope(
    ModApiEnvelopeShape Shape,
    string? Code,
    string? Message,
    bool? Retryable,
    string? RequestId);

public readonly record struct ModApiBodyReadPolicy(int MaxBytes, string OverflowUserCode);

public sealed class ModApiResponse
{
    public int StatusCode { get; }
    public bool IsSuccess { get; }
    public string Body { get; }
    public ModApiErrorEnvelope Error { get; }
    public string? RequestId { get; }
    public int? RetryAfterSeconds { get; }
    public string UserCode { get; }
    public static Task<ModApiResponse> ReadAsync(
        HttpResponseMessage response,
        ModApiBodyReadPolicy bodyPolicy,
        CancellationToken cancellationToken = default);
}

public sealed class ModApiFailure
{
    public string UserCode { get; }
    public Exception? DiagnosticException { get; }
    public ModApiResponse? Response { get; }
}
```

支持 nested V5 与 legacy top-level；header request ID 优先、body fallback；`Retry-After` 同时支持 delta/date。`Shape` 必须保留：Bundle 的 409 `run_already_bundled`/`bundle_id_conflict` disposition 只信 nested V5 code，避免代理或 legacy top-level code 驱动本地删文件。send/read异常进入 closed `transport_error + DiagnosticException`，raw body/异常消息不进入 `UserCode`。

presigned bundle download不是 Mod API JSON response：成功二进制继续使用声明长度与 bounded stream，非2xx body使用 `MaxBytes = 16 * 1024`；overflow仍为 `bundle_too_large`（现状 `GhostBattleClient.cs:88-150`），绝不无界缓冲外部host响应。

**步骤与验证**：

1. 新增response/failure reader与tests。断言nested/top-level/malformed/blank、request ID优先级、两种Retry-After、body cap和transport sentinel分离；确切命令见§3.10 C2.1。
2. 迁移Bundle client并删私有envelope/header parser。断言disposition matrix不变、top-level 409保持transient、body request ID fallback可见；确切命令见§3.10 C2.2。
3. 迁移Ghost query与Mod API failure；presigned成功流保留原bounded binary path，非2xx只接有16 KiB policy的reader。断言429 cooldown不变、nested error可解析、声明/流/body三种超限都为`bundle_too_large`；确切命令见§3.10 C2.3。
4. 迁移BazaarDB；collapse `ModApiJsonPost`浅result；Health与Ghost UI只采用closed `UserCode`。用含唯一sentinel的异常/body断言UI不含sentinel而`DiagnosticException`仍到结构化日志；确切命令见§3.10 C2.4。
5. 加架构事实：clients不直接读Retry-After/request ID/JObject error；确切命令见§3.10 C2.5。

**测试**：扩 `ModApi.Tests`、`BundlePipeline.Tests`、`GhostBattleSync.Tests`、`HistoryPanelServerHealth.Tests`（exe-runner）与 Architecture.Tests（xUnit）；新增 `ModApiResponseTests.cs`，不新建工程。

**行为变化**：body `request_id` 现在可用于诊断；raw body/exception不再进入用户文本；transport失败统一显示closed通用码；endpoint retry/disposition与presigned资源上限保持。header/body request ID 冲突默认 header 权威，需服务端确认。服务端各 endpoint 的真实 envelope shape 仍需确认。

**touchesFiles**：

- `src/BazaarPlusPlus.ModApi/Http/ModApiResponse.cs`（新增）
- `src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs`（删除或收成 private helper）
- `src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs`（删除）
- `src/BazaarPlusPlus.ModApi/Clients/BundleUploadClient.cs`
- `src/BazaarPlusPlus.ModApi/Clients/GhostBattleClient.cs`
- `src/BazaarPlusPlus.ModApi/Clients/ModApiHealthClient.cs`
- `src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelDataService.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelReplayService.cs`
- `tests/ModApi.Tests/Program.cs`
- `tests/ModApi.Tests/ErrorFormatterTests.cs`（删除）
- `tests/ModApi.Tests/ModApiResponseTests.cs`（新增）
- `tests/ModApi.Tests/BazaarDbLinkClientTests.cs`
- `tests/ModApi.Tests/HealthClientTests.cs`
- `tests/BundlePipeline.Tests/Program.cs`
- `tests/GhostBattleSync.Tests/Program.cs`
- `tests/HistoryPanelServerHealth.Tests/Program.cs`
- `tests/HistoryPanelFactory.Tests/Program.cs`
- `tests/HistoryPanelOperationalLogging.Tests/HistoryPanelLogWriterTests.cs`
- `tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs`
- `docs/adr/0012-outbound-network-ownership.md`

### C3 — Mod API Session

**现状**：`ModOnlineClient` 是 handle bag（`ModOnlineClient.cs:4-22`）；Ghost/Health现场重建 endpoint client（`GhostBattleSyncService.cs:117-129,170-178`；`HistoryPanelServerHealth.cs:13-24`）；Plugin、upload feed、E2E tool 各自构造 transport/routes（`Plugin.cs:214-249`；`BundleUploadFeed.cs:27-55`；`tools/BundleV5E2E/Program.cs:15-26`）。

**目标接口**：

```csharp
public sealed class ModApiSessionOptions
{
    public string ApiBaseUrl { get; init; }
    public string ProductVersion { get; init; }
    public string UserAgentSuffix { get; init; }
    public TimeSpan Timeout { get; init; }
}

public sealed class ModApiSession : IDisposable
{
    public static ModApiSession? TryCreate(ModApiSessionOptions options,
        HttpMessageHandler? handler = null);
    public Task<BundleUploadResponse> UploadBundleAsync(...);
    public Task<GhostBattleQueryResult> QueryGhostBattlesAgainstMeAsync(...);
    public Task<GhostBundleDownloadResult> DownloadGhostBundleAsync(...);
    public Task<ModApiHealthProbeResult> ProbeHealthAsync(...);
}
```

session 不公开 `HttpClient`、routes 或 endpoint clients。Plugin History/Ghost/Health、每个 upload activation、E2E tool分别拥有 session，避免一个 dispose 关闭另一条链。BazaarDB 不在 session：不同 host、固定 URI、30s 生命周期（`Plugin.cs:214-228`）。handler 是真实 production/test adapter seam，不另加 `IModApiSession`。

新增零 Unity `HistoryPanelMountPlan.Resolve(hasReplayRuntime, hasOverlayHost, hasSession)`，输出 `DoNotMount / MountLocalOnly / MountWithOnline`。它使“无 session仍保留本地历史”成为可执行行为，而不是 Factory源码字符串断言；Mount只翻译plan。旧 `HistoryPanelMountDependency.OnlineClient`删除，远端能力缺席不再记成整个panel mount失败。

**步骤与验证**：

1. 新增session和handler行为测试，先保留`ModOnlineClient`。断言routes/headers/timeout/typed operation与dispose；确切命令见§3.10 C3.1。
2. 先加`HistoryPanelMountPlan`行为矩阵，再迁移Plugin/composition/History/Ghost/Health并删除`ModOnlineClient`；保持两个反射全名。删除旧early return/`OnlineClient`枚举。断言有replay+overlay、无session时plan为`MountLocalOnly`且实际Mount走AddComponent路径；确切命令见§3.10 C3.2。
3. upload feed改为自有session。断言UA仍为`BundleUpload`、120s，dispose不影响Plugin session；确切命令见§3.10 C3.3。
4. E2E tool迁移，测试不再直接构造endpoint clients。断言stored/duplicate/conflict matrix与byte-identical retry不变；确切命令见§3.10 C3.4。
5. endpoints/routes降为internal或吸收，删除第二条public chain并加架构事实；确切命令见§3.10 C3.5。

**测试**：扩现有 ModApi、Ghost、Bundle、HistoryPanel factory/health、StartupUploadRunner；Architecture xUnit。`HistoryPanelFactory.Tests` 的 source-string pin（`Program.cs:151-215`）必须同迁。

**行为变化**：base URL 无效不再移除整个 History Panel，只禁用远端能力；E2E增加标准 UA。运行期 timeout、上传/History transport隔离、async `ConfigureAwait(false)`位置与 teardown先后保持。保留各 owner suffix，避免改变可能存在的服务端指标/限流分组。

**touchesFiles**：

- `src/BazaarPlusPlus.ModApi/Clients/ModApiSession.cs`（新增）
- `src/BazaarPlusPlus.ModApi/Clients/ModOnlineClient.cs`（删除）
- `src/BazaarPlusPlus.ModApi/Clients/BundleUploadClient.cs`
- `src/BazaarPlusPlus.ModApi/Clients/GhostBattleClient.cs`
- `src/BazaarPlusPlus.ModApi/Clients/ModApiHealthClient.cs`
- `src/BazaarPlusPlus.ModApi/ModApiRoutes.cs`
- `src/BazaarPlusPlus.ModApi/Http/BppHttpClientFactory.cs`
- `src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs`（若 C2 未删除则此处删除）
- `src/BazaarPlusPlus/Plugin.cs`
- `src/BazaarPlusPlus/BppComposition.cs`
- `src/BazaarPlusPlus/Game/BundlePipeline/BundleUploadFeed.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelMountPlan.cs`（新增）
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelMount.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelFactory.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelLogOperations.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelServerHealth.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`
- `tests/ModApi.Tests/Program.cs`
- `tests/ModApi.Tests/RoutesTests.cs`
- `tests/ModApi.Tests/HealthClientTests.cs`
- `tests/GhostBattleSync.Tests/Program.cs`
- `tests/BundlePipeline.Tests/Program.cs`
- `tests/HistoryPanelFactory.Tests/Program.cs`
- `tests/HistoryPanelServerHealth.Tests/Program.cs`
- `tests/StartupUploadRunner.Tests/Program.cs`
- `tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs`
- `tools/BundleV5E2E/Program.cs`
- `CONTEXT.md`
- `docs/adr/0012-outbound-network-ownership.md`

### C4 — MessagePack Gzip Framing

**现状**：见 §2.3.2。两个 codec 有共同 framing，但安全、cap、version 和错误文字不等价。

**目标接口**：internal `MessagePackGzipFraming.Encode<T>(payload, options)` 与 `TryDecode<T>(bytes, options, int? max)`，返回 `FailureKind + Exception?`。两个 public wrapper各自映射：generic仍无 cap、保留现有 exception text；V5仍使用 64 MiB、`UntrustedData`、version 5与稳定 closed reasons。

**步骤与验证**：

1. 先扩generic/V5 malformed、wrong version、cap、stable byte fixture；确切命令见§3.10 C4.1。
2. 新framing core，只迁generic wrapper。断言无新size rejection且错误文字不变；确切命令见§3.10 C4.2。
3. 迁V5 wrapper。断言>64 MiB、非5、gzip bytes逐字节不变；确切命令见§3.10 C4.3。
4. architecture fact限制ModApi内只有core直接构造`GZipStream`；确切命令见§3.10 C4.4。

**测试**：扩 `ModApi.Tests` 与 `BundleV5Codec.Tests`（exe-runner）、Architecture xUnit。

**行为变化**：默认无。本地 replay 明确不增加 cap。

**touchesFiles**：

- `src/BazaarPlusPlus.ModApi/MessagePackGzipFraming.cs`（新增）
- `src/BazaarPlusPlus.ModApi/MessagePackGzipCodec.cs`
- `src/BazaarPlusPlus.ModApi/Bundle/RunPayloadV5Codec.cs`
- `tests/ModApi.Tests/CodecTests.cs`
- `tests/BundleV5Codec.Tests/Program.cs`
- `tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs`

### C5 — Bundle Seal Convergence

**现状**：两分钟常量未使用，SQL硬编码 `+2 minutes`（`BundleSealCoordinator.cs:17,277-299`）；同一 `deadlineReached` 穿过 replay/account/screenshot/payload 分支（`:147-175,204-205`）。SQL `datetime()` 产出无 offset 字符串，读取却直接 `DateTimeOffset.Parse`（`:288,445`）。机械执行 SQLite 得到 `2026-08-03 05:02:00`，无 `Z`/offset。

**目标接口**：纯 `BundleSealConvergence` 只接 `SealJobFacts + float secondsUntilInputDeadline + observations`，返回 `Continue/Wait/MarkScreenshotTimedOutAndContinue/MarkTerminal`。`SqliteUtcInstant`与coordinator先把持久化绝对deadline正规化，再把相对秒数送入core；`<= 0`表示到期。完整job row保持引用record并携带account/allocation等I/O字段；核心只接投影，绝不引用`DateTimeOffset`、clock、Storage/SQLite/Unity/ModApi/文件。

**步骤与验证**：

1. 新纯文件与零ManagedPath exe-runner，范式严格抄`SavedReplayLifecycle`（`SavedReplayLifecycle.cs:11-15`；`CombatReplayPlaybackLogging.Tests.csproj:20-50`）。断言相对秒数正/零/负边界、terminal、deadline前replay/account/screenshot、到期降级、payload超限矩阵；确切命令见§3.10 C5.1。
2. 四组gate逐段委托核心，I/O顺序/错误码/副作用不变。断言现有过期无截图run仍产生run-only pending bundle；确切命令见§3.10 C5.2。
3. 新`SqliteUtcInstant`并由coordinator正规化后计算相对float；补direct parser与“deadline未到不封包”fixture；确切命令见§3.10 C5.3。
4. 架构事实及ADR-0011追加“持久化绝对deadline在translation layer转相对float”；断言core没有`DateTimeOffset`/clock；确切命令见§3.10 C5.4。

**测试**：新 exe-runner `BundleSealConvergence.Tests`（Compile-Include、零 ManagedPath）；新 `BundleQueueSqliteStore.Tests`承载 instant；扩 BundlePipeline；新 Architecture xUnit 文件。

**行为变化**：UTC以东从“通常立即封包”恢复真实剩余两分钟；UTC以西从约 `|offset|` 小时收紧到两分钟；UTC无变化。未改持久化图，不 bump schema。

**touchesFiles**：

- `src/BazaarPlusPlus/Game/BundlePipeline/BundleSealConvergence.cs`（新增）
- `src/BazaarPlusPlus/Game/BundlePipeline/BundleSealCoordinator.cs`
- `src/BazaarPlusPlus.Storage/Sqlite/SqliteUtcInstant.cs`（新增）
- `tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj`（新增）
- `tests/BundleSealConvergence.Tests/Program.cs`（新增）
- `tests/BundlePipeline.Tests/Program.cs`
- `tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj`（新增）
- `tests/BundleQueueSqliteStore.Tests/Program.cs`（新增）
- `tests/Architecture.Tests/BundleSealConvergenceArchitectureTests.cs`（新增）
- `CONTEXT.md`
- `docs/adr/0011-pure-decision-cores-for-timing-invariants.md`

### C6 — Bundle Queue Store

**现状**：见 §2.2；feed destructive/recovery/cleanup 路径包括 allocation mismatch删除、orphan adopt、invalid pending reseal、retention/soft limit（`BundleSealCoordinator.cs:239-249,315-395`；`BundleUploadFeed.cs:333-405`），现有测试未执行 feed。

**目标接口**：`public sealed BundleQueueStore : SqliteStoreBase`，公开领域操作：ensure eligible jobs、read job、ensure allocation、freeze account、mark state、publish transaction、validate pending、fail+reseal transaction、list due、record outcome/transient、retention/reclaim candidates。records只含 string/number/`DateTimeOffset`；不接受 Game/ModApi 类型。不加只有一个实现且与实现等大的 queue interface；生产和测试都用 concrete store + 临时 SQLite。

文件系统留Game，但经窄 `IBundleOutboxFiles` port（`OpenRead/Exists/GetLength/Delete/Enumerate`）与生产 `SystemBundleOutboxFiles`；test spy可在`Delete`瞬间查询SQLite，确定性证明DB-before-delete。`BundleUploadFeed`保留parameterless生产构造，并增加internal test构造接session factory、queue store和files port；Session单独拥有/dispose其ModApiSession。soft-limit caller按真实删除逐项减running total并达到512 MiB即停，保持现状（`BundleUploadFeed.cs:376-405`）。`RunPayloadComposer`的runs/events裸SQL不在本候选（`RunPayloadComposer.cs:413-417`）。

coordinator另有非queue截图查询仍使用其`Open()`（`BundleSealCoordinator.cs:479-500`）。删除该连接前，扩 `RunScreenshotSqliteStore.TryGetLatestPrimaryForRun(runId)`，返回Storage DTO；coordinator只保留绝对路径约束与`File.Exists`。这不是扩大queue owner，而是消除删除`Open()`的编译blocker。

**步骤与验证**：

1. 先实现store/records/SQLite runner，复现eligibility/order/allocation idempotency/state/due/outcome/reseal事务/retention。断言fail outbox+reseal同事务；确切命令见§3.10 C6.1。
2. coordinator迁非破坏性SQL；确切命令见§3.10 C6.2。
3. 先补allocation mismatch/orphan adopt/invalid pending fixtures，再迁publish/recovery。断言删除窗口、adopt、atomic reseal；确切命令见§3.10 C6.3。
4. 先加internal Session构造、fake handler与spy `IBundleOutboxFiles` fixture，再迁due/outcome/transient/reseal。spy在`Delete`回调内查询SQLite并断言已uploaded；另断言transient从不Delete、session dispose只释放本activation transport；确切命令见§3.10 C6.4。
5. 单独迁cleanup。断言14天pending、7天permanent、uploaded cleanup、soft-limit early stop；确切命令见§3.10 C6.5。
6. 扩`RunScreenshotSqliteStore.TryGetLatestPrimaryForRun`与既有runner，迁截图读取；随后删除queue SQL和coordinator连接helper。architecture限制queue表名只在schema/store；确切命令见§3.10 C6.6。

**测试**：新 `BundleQueueSqliteStore.Tests` exe-runner；扩 BundlePipeline/StartupUploadRunner；Architecture xUnit。

**行为变化**：除 C5 UTC修复外无意改变；不改 schema/DTO graph，不 bump版本；outbox无 FK并在删除 run后保留的语义保持（`HistoryPanelRepository.Tests/Program.cs:29-42`）。

**touchesFiles**：

- `src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs`（新增）
- `src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueRecords.cs`（新增）
- `src/BazaarPlusPlus.Storage/Sqlite/SqliteUtcInstant.cs`
- `src/BazaarPlusPlus.Storage/RunScreenshot/RunScreenshotSqliteStore.cs`
- `src/BazaarPlusPlus.Storage/RunScreenshot/RunScreenshotRecord.cs`
- `src/BazaarPlusPlus/Game/BundlePipeline/BundleSealCoordinator.cs`
- `src/BazaarPlusPlus/Game/BundlePipeline/BundleUploadFeed.cs`
- `src/BazaarPlusPlus/Game/BundlePipeline/BundleOutboxFiles.cs`（新增）
- `tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj`
- `tests/BundleQueueSqliteStore.Tests/Program.cs`
- `tests/BundlePipeline.Tests/Program.cs`
- `tests/StartupUploadRunner.Tests/Program.cs`
- `tests/RunScreenshotSqliteStore.Tests/Program.cs`
- `tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs`
- `CONTEXT.md`
- `docs/adr/0012-outbound-network-ownership.md`

### C7 — Supporter Remote Embedded Catalog

**现状**：supporter自行实现同一条 seed/fallback→temp cache→remote链（`BPPSupporterCatalog.cs:11-37,76-169,171-263`），而 voice/ten-win 已用共享 catalog（`VoiceLinesCatalogFactory.cs:23-38`；`TenWinBuildCatalogFactory.cs:23-38`）。现有 fixed list 完全绕过远端（`BPPSupporterCatalog.cs:76-81`）；fallback永远至少五项（`:30-37,84-89`）；reset不清 cached entries/expiry/disk-load flag（`:64-73`）。共享 catalog的 cache duration只在 warm判断；一旦 `_warmCompleted`，不会自动每小时重取（`RemoteEmbeddedCatalog.cs:224,574-575,861-862`）。

**目标接口**：新增 `SupporterCatalogFactory.Create(dataRootPath)`、`SupporterCatalogParser`、observer、嵌入seed及 `SupporterCatalogModule : IBppFeature`。module由`BppComposition`构造/注册，独占lazy factory、`IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>`、warm/5m retry latch与dispose。`BPPSupporterCatalog`只保留config、fixed-list产品政策和同步snapshot投影，不持有HTTP/cache/task/`IDisposable`。`Stop`顺序为catalog dispose→projection reset，拒绝late publish。cache移到`<GameRoot>/BazaarPlusPlusV5/supporter-list.json`，不再用temp。继续通过`BPPSupporterListSourcePolicy.ResolveEntries`，避免配置策略变死代码。

fixed-list检查发生在lazy factory、`TryGet`、`WarmAsync`之前；fixed模式重复读取的factory/cache/embedded/remote计数都必须为0。默认明确接受：失去会话内每小时自动重取；展示型supporter在一次warm后会话稳定，下一次启动再按1h磁盘freshness刷新。无snapshot时module用5m最小重试闩锁，避免每次打开面板重启完整warm。若产品要求会话内小时刷新，应由显式scheduler/refresh owner实现，不能假设shared catalog自动做。

**步骤与验证**：

1. 新parser/seed，扩`Supporters.Tests` Compile-Include，覆盖过滤、空payload、seed至少五项；确切命令见§3.10 C7.1。
2. 新factory/observer/module与专用counting fake；覆盖lazy create、warm一次、无snapshot 5m闩锁、dispose→reset、late publish拒绝，fixed模式所有调用计数为0；确切命令见§3.10 C7.2。
3. `BppComposition`注册module，facade切换并删除旧static HTTP/temp cache/refresh task；更新旧路径断言（`V5DataPipelineArchitectureTests.cs:36-57`）；确切命令见§3.10 C7.3。
4. 扩architecture：第三consumer、dataRoot cache、composition唯一owner、dispose-before-reset、facade无`IDisposable`/refresh状态；确切命令见§3.10 C7.4。

**测试**：扩Supporters exe-runner；新`SupporterCatalogModule.Tests` exe-runner（ProjectReference main + InternalsVisible，测试内专用counting fake）；扩RemoteEmbeddedCatalog与Architecture xUnit。

**行为变化**：cache从 OS temp移到 game-root数据目录；会话内每小时自动刷新改为每次启动warm；无快照失败仍至少5m才重试；fixed list行为不变。开放产品问题：是否接受会话稳定语义；默认接受。嵌入 seed损坏会使 remote成功前无 supporter，故 seed gate不可省略。

**touchesFiles**：

- `src/BazaarPlusPlus/Game/Supporters/BPPSupporterCatalog.cs`
- `src/BazaarPlusPlus/Game/Supporters/SupporterCatalogFactory.cs`（新增）
- `src/BazaarPlusPlus/Game/Supporters/SupporterCatalogDocument.cs`（新增）
- `src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs`（新增）
- `src/BazaarPlusPlus/Game/Supporters/supporter-list.json`（新增）
- `src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `src/BazaarPlusPlus/BppComposition.cs`
- `src/BazaarPlusPlus/Plugin.cs`
- `src/BazaarPlusPlus/Properties/AssemblyAttributes.cs`
- `tests/Supporters.Tests/Supporters.Tests.csproj`
- `tests/Supporters.Tests/Program.cs`
- `tests/SupporterCatalogModule.Tests/SupporterCatalogModule.Tests.csproj`（新增）
- `tests/SupporterCatalogModule.Tests/Program.cs`（新增）
- `tests/RemoteEmbeddedCatalog.Tests/RemoteEmbeddedCatalogTests.cs`
- `tests/Architecture.Tests/RemoteEmbeddedCatalogArchitectureTests.cs`
- `tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs`
- `CONTEXT.md`
- `docs/adr/0013-remote-data-and-release-boundaries.md`

### C8 — Release Manifest Client

**现状**：Unity controller同时持有 URL、client、HTTP status、JSON DTO、cancel generation和UI state（`MainMenuVersionCheckController.cs:9-35,85-197`）；协议路径没有执行测试，只有 version comparer/label纯测试（`MainMenuVersionLabel.Tests/Program.cs:5-43`）。

**目标接口**：`Infrastructure/ReleaseManifest/ReleaseManifestClient` concrete adapter，构造接`HttpClient + Uri`，`FetchAsync`返回`Success(version)`或closed failure kind/status/exception；不引用Unity、BepInEx、UI state。另加零Unity `ReleaseManifestCheckLifecycle`，拥有generation/CTS lease replacement/late-result rejection/dispose决策；controller只负责MonoBehaviour Update、client组装、state/UI投影与日志。没有第二个生产adapter，不加client interface；test用fake handler与tracking disposable。

**步骤与验证**：

1. 新adapter/lifecycle与零ManagedPath runner，覆盖协议与两代TCS乱序、tracking dispose；确切命令见§3.10 C8.1。
2. controller委托adapter/lifecycle，保留5s、`ConfigureAwait(false)`和Update发布时机，删除本地generation/CTS replacement；确切命令见§3.10 C8.2。
3. architecture禁止controller直接`GetAsync`/JSON DTO/声明generation，并钉adapter/lifecycle零Unity；确切命令见§3.10 C8.3。

**测试**：新 `ReleaseManifestClient.Tests` exe-runner（Compile-Include adapter与必要通用源，零 ManagedPath）；现有 MainMenuVersionLabel；Architecture xUnit。

**行为变化**：无意改变。reentrant `Initialize` 当前会覆盖旧 `_httpClient` 而不 dispose（`MainMenuVersionCheckController.cs:22-35`）；是否实际重入仓库内不可证，迁移时应在启动新 client前 dispose旧 client，作为资源修复但不改变发布 generation。无服务端契约问题，除 `latest.json` 是否保证 `{version}`。

**touchesFiles**：

- `src/BazaarPlusPlus/Infrastructure/ReleaseManifest/ReleaseManifestClient.cs`（新增）
- `src/BazaarPlusPlus/Game/Lobby/ReleaseManifestCheckLifecycle.cs`（新增）
- `src/BazaarPlusPlus/Game/Lobby/MainMenuVersionCheckController.cs`
- `tests/ReleaseManifestClient.Tests/ReleaseManifestClient.Tests.csproj`（新增）
- `tests/ReleaseManifestClient.Tests/Program.cs`（新增）
- `tests/Architecture.Tests/OutboundNetworkArchitectureTests.cs`（新增）
- `CONTEXT.md`
- `docs/adr/0013-remote-data-and-release-boundaries.md`

### C9 — Build Seed Fetcher

**现状**：约百行 fetch/validate/atomic replace藏在 `.targets` CDATA（`RemoteEmbeddedData.targets:60-184`），只有 XML形状测试（`CoreLayeringTests.cs:2655-2691`）。普通 build缺文件会下载，但只做粗筛；publish另跑真实 feature parsers（`run.sh:142-160,192-200`）。

**目标接口**：`build/RemoteEmbeddedDataFetcher` console + 可Compile-Include的`RemoteEmbeddedDataFetch` core。core接`HttpClient`、request（URL/staging/min bytes）并负责response success、流式临时文件、最小字节、JSON首字符与失败清理；另有`PromoteSeedSet`先备份全部canonical文件，再逐项替换，任一失败回滚整组。Program负责参数/退出码。`.targets`只声明item metadata与调用tool，不再包含C#。UA设为`BazaarPlusPlusBuild/<version>`；无auth/retry；文件存在/force决策仍在MSBuild owner。

语义校验继续由feature parser/seed gates拥有；build fetcher只声明“传输完整性筛选”，不复制voice/ten-win schema。显式`fetch-data`与publish共用`Stage → SemanticValidate → Promote`：下载只写staging；seed gates通过`-p:RemoteEmbeddedDataDirectory=<stage> -p:RemoteEmbeddedDataPrepared=true`验证staged文件；两条全过后才整组提升。任一失败/第二项promote失败都恢复全部旧canonical bytes并清理staging。普通缺seed build仍可直接写canonical且只做传输筛选，这是兼容选择，不能声称完成schema validation。

**步骤与验证**：

1. 新core/CLI与pipeline runner，覆盖status、short、非JSON、staging/tmp cleanup、整组promote与第二项失败回滚；确切命令见§3.10 C9.1。
2. `.targets`改为Exec并删除inline task；loopback子进程覆盖missing/force/nonforce/退出码传播；确切命令见§3.10 C9.2。
3. `run.sh`让`fetch-data`/publish共用事务helper；坏schema fixture断言gate非零、旧canonical不变、staging清理；确切命令见§3.10 C9.3。
4. 收口验证不访问公网；确切命令见§3.10 C9.4。实际公网`./run.sh fetch-data`是有意覆盖动作，仅在波次报告后执行。

**测试**：新`RemoteEmbeddedDataPipeline.Tests` exe-runner（core fake + loopback MSBuild/run.sh子进程集成）；扩Architecture xUnit；现有VoiceSubtitles xUnit/LiveBuildRecommendations exe-runner作为semantic gate。

**行为变化**：build请求新增标准 UA；`fetch-data` 由“粗筛完成”变为“真实 seed parser也通过”；无 retry。接受 `dotnet run` build tool 的启动成本；若以后出现实测CI问题，保持core接口改为预编译task assembly。

**touchesFiles**：

- `build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj`（新增）
- `build/RemoteEmbeddedDataFetcher/Program.cs`（新增）
- `build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetch.cs`（新增）
- `src/BazaarPlusPlus/RemoteEmbeddedData.targets`
- `tests/RemoteEmbeddedDataPipeline.Tests/RemoteEmbeddedDataPipeline.Tests.csproj`（新增）
- `tests/RemoteEmbeddedDataPipeline.Tests/Program.cs`（新增）
- `tests/Architecture.Tests/CoreLayeringTests.cs`
- `tests/Architecture.Tests/OutboundNetworkArchitectureTests.cs`
- `run.sh`
- `CONTEXT.md`
- `docs/adr/0013-remote-data-and-release-boundaries.md`

## 3.10 逐步可执行验证矩阵

以下命令不使用省略号或“同上”。每个步骤按顺序逐条执行；对应 §3 的行为断言由其中的行为工程触发，build 仅是编译门槛。

### C1.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C1.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C1.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C1.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C2.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C2.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C2.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C2.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C2.5

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C3.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build tools/BundleV5E2E/BundleV5E2E.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C3.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build tools/BundleV5E2E/BundleV5E2E.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C3.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build tools/BundleV5E2E/BundleV5E2E.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C3.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build tools/BundleV5E2E/BundleV5E2E.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C3.5

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build tools/BundleV5E2E/BundleV5E2E.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/HistoryPanelFactory.Tests/HistoryPanelFactory.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet test tests/HistoryPanelOperationalLogging.Tests/HistoryPanelOperationalLogging.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C4.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C4.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C4.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C4.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BundleV5Codec.Tests/BundleV5Codec.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C5.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
./run.sh format-check
```

### C5.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
./run.sh format-check
```

### C5.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C5.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C6.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C6.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C6.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C6.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C6.5

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C6.6

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
dotnet run --project tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
dotnet run --project tests/StartupUploadRunner.Tests/StartupUploadRunner.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C7.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
dotnet test tests/RemoteEmbeddedCatalog.Tests/RemoteEmbeddedCatalog.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C7.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
dotnet run --project tests/SupporterCatalogModule.Tests/SupporterCatalogModule.Tests.csproj
dotnet test tests/RemoteEmbeddedCatalog.Tests/RemoteEmbeddedCatalog.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C7.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
dotnet run --project tests/SupporterCatalogModule.Tests/SupporterCatalogModule.Tests.csproj
dotnet test tests/RemoteEmbeddedCatalog.Tests/RemoteEmbeddedCatalog.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C7.4

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
dotnet run --project tests/SupporterCatalogModule.Tests/SupporterCatalogModule.Tests.csproj
dotnet test tests/RemoteEmbeddedCatalog.Tests/RemoteEmbeddedCatalog.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C8.1

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ReleaseManifestClient.Tests/ReleaseManifestClient.Tests.csproj
dotnet run --project tests/MainMenuVersionLabel.Tests/MainMenuVersionLabel.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C8.2

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ReleaseManifestClient.Tests/ReleaseManifestClient.Tests.csproj
dotnet run --project tests/MainMenuVersionLabel.Tests/MainMenuVersionLabel.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C8.3

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/ReleaseManifestClient.Tests/ReleaseManifestClient.Tests.csproj
dotnet run --project tests/MainMenuVersionLabel.Tests/MainMenuVersionLabel.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C9.1

```bash
dotnet build build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/RemoteEmbeddedDataPipeline.Tests/RemoteEmbeddedDataPipeline.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C9.2

```bash
dotnet build build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/RemoteEmbeddedDataPipeline.Tests/RemoteEmbeddedDataPipeline.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C9.3

```bash
dotnet build build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/RemoteEmbeddedDataPipeline.Tests/RemoteEmbeddedDataPipeline.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```

### C9.4

```bash
dotnet build build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/RemoteEmbeddedDataPipeline.Tests/RemoteEmbeddedDataPipeline.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh format-check
```


## 4. 跨候选裁定

### 4.1 唯一 owner

| 决策 | owner | 明确不属于 |
| --- | --- | --- |
| replayability、manifest/payload run identity | C1 Run Bundle Contract | Ghost workflow、composer private helper |
| envelope/request ID/Retry-After读取 | C2 Mod API Response | endpoint clients |
| Bundle 409 disposition、Ghost cooldown、BazaarDB outcome、Health text | 各 endpoint/workflow | C2 generic response |
| transport/routes/UA/timeout/dispose | C3 各 owner-scoped session | `ModOnlineClient` getter bag |
| gzip framing与受控循环 | C4 framing core | 两个 wrapper |
| V5 cap/security/version/error码 | `RunPayloadV5Codec` | generic framing/core |
| 本地 replay resolver/错误文字/无cap | `MessagePackGzipCodec` | V5 wrapper |
| 两分钟量值、wait/degrade/terminal | C5 convergence | Storage SQL |
| SQLite instant、queue rows/SQL/事务 | C6 store | pure convergence/Game endpoint |
| 文件删除/hash/codec/HTTP/upload policy | Game bundle workflow | Storage |
| supporter fixed-list与显示政策 | supporter feature | Remote Embedded Catalog |
| cache/embedded/remote flight | Remote Embedded Catalog；lifecycle由C7 module/composition | supporter静态facade |
| release manifest协议 | C8 adapter；generation/lease由lifecycle core | Unity controller |
| build传输完整性与整组promote/rollback | C9 fetcher | feature parser |
| seed语义 | voice/ten-win parser + seed gate | generic build fetcher |

### 4.2 文件冲突

**Hard**：

- C2/C3：三个 endpoint clients、`ModApiJsonPost`、ModApi tests同一区域；C2先统一响应体，C3再隐藏实现。
- C5/C6：`BundleSealCoordinator` 的 job/deadline/read路径；C5先修 UTC并抽决策，C6再搬 SQL。

**Soft**：

- C1/C3：`GhostBattleSyncService`，一项改 extraction、一项改 transport；串行提交。
- C1/C2/C4：`BundleV5Codec.Tests`、`ModApi.Tests`、`V5DataPipelineArchitectureTests`是共享测试文件的不同场景。
- C3/C6：`BundleUploadFeed`，session构造与 SQL搬迁邻近，分波避免 patch冲突。
- C3/C7：`Plugin.cs`/`BppComposition.cs`，一项迁online session，一项注册supporter module，方法区域不同但同一composition文件，按波次串行。
- C7/C8/C9：`CONTEXT.md`/ADR/Architecture共享文档与测试入口，代码区域不冲突。

### 4.3 项目引用与 DTO

main只向 ModApi/Storage/Localization引用（`BazaarPlusPlus.csproj:97-101`）；Storage项目不引用 main/ModApi（`BazaarPlusPlus.Storage.csproj:1-18`）。因此 Storage records不能接 Game enum或 ModApi result，Game负责映射。C1新增的 MessagePack DTO相关图保持全 public；C5/C6不改 serialized graph。所有候选均不改 `RunLogSchema` 图，所以不 bump版本。

### 4.4 ADR编号

- `0012-outbound-network-ownership.md`：C1-C4、C6的 owner/seam与拒绝 generic result/global transport；C2同时向 ADR-0009追加“不统一 endpoint disposition/cooldown”。
- `0013-remote-data-and-release-boundaries.md`：C7-C9的 runtime catalog、release manifest和build fetch边界。
- C5向既有 ADR-0011追加 Bundle Seal Convergence纯核心范式，不另开小 ADR。

### 4.5 波次

| 波次 | 候选 | 依据 |
| --- | --- | --- |
| W1 | C1 Run Contract、C2 Response、C4 Framing | 都先建立 ModApi规则owner；C2必须早于C3；三者只有测试文件soft overlap，按候选串行提交 |
| W2 | C3 Session | 吸收W1 response实现；在 queue迁移前先完成 feed构造迁移 |
| W3 | C5 Seal Convergence、C7 Supporter、C8 Release、C9 Build Fetcher | 四块代码独立；C5必须早于C6；每候选仍是单独可评审提交 |
| W4 | C6 Bundle Queue Store | 最后搬 coordinator/feed SQL，避免搬入旧 UTC/transport形状 |

每个候选的每一步都先跑其列出的 build、行为工程和 format-check；每波末运行 `./run.sh test`。若全量测试在隔离 worktree执行，先建立 installer/decompiled两个仓库规则所述 symlink。

## 5. 文档与词汇

`CONTEXT.md` 增加：

- **Run Bundle Contract**：V5 container与run payload合在一起消费时的自洽/replayability规则。_Avoid_：ghost replay rules、composer replay helper。
- **Mod API Response**：一次 Mod API/BazaarDB HTTP响应的统一协议视图；含 envelope、request ID、retry hint和 closed user code。_Avoid_：generic Result、error formatter、client-local parser。
- **Mod API Session**：一个 owner-scoped、可释放的 backend连接，封装 routes、transport与typed operations。_Avoid_：online client、handle bag、shared raw HttpClient。
- **Bundle Seal Convergence**：单次 seal pass的纯决策owner。_Avoid_：seal policy bag、SQLite state machine。
- **Bundle Queue Store**：`bundle_seal_jobs`/`bundle_outbox`的唯一持久化owner。_Avoid_：outbox repository interface、Game SQL helper。
- **Release Manifest**：安装器发布的 `{version}`更新检查文档。_Avoid_：mod API health、installer state。
- **Build Seed Fetch**：构建期下载 embedded seed的传输完整性动作。_Avoid_：catalog refresh、schema validation。

## 6. MEMORY待修订项

`MEMORY.md`仅由 consolidation维护，本任务不直接修改：

1. `MEMORY.md:15` 的 db=18/row=11/upload payload=6过期；实际 `LocalDatabaseSchemaVersion = 1`、`RowSchemaVersion = 1`（`RunLogSchema.cs:9-10`），payload version归 `BundleLimitsV5.RunFormatVersion`，RunLogSchema无该常量。
2. `MEMORY.md:69` 对 ADR-0011测试范式的概括过宽。真正零 ManagedPath的先例是 `SavedReplayLifecycle`及其 Compile-Include runner（`SavedReplayLifecycle.cs:11-15`；`CombatReplayPlaybackLogging.Tests.csproj:20-50`）；`CollectionViewState.cs:2`使用 game domain type，其测试需 ManagedPath。
3. 基线测试工程数是63（21 xUnit、42 exe-runner），不是61；本次新增5个 exe-runner 后为68（21 xUnit、47 exe-runner）。分类机制见 `run.sh:305-321`。

## 7. 已采纳的外部契约与产品决策

仓库内无法直接观察服务端实现；本次批准并实施以下兼容选择，后续若服务端契约变化再单独修订：

1. replayability要求四个 snapshot labels恰好一次且非 `Missing`，采用原写/读两侧规则的严格合取。
2. Mod API error envelope兼容 nested V5、legacy top-level、空体和非JSON；Cloudflare响应回退到HTTP状态码。
3. header与body `request_id`冲突时header权威。
4. 保留`BundleUpload`、`OnlineClient`等owner-specific UA suffix，避免改变后端指标/限流分组。
5. supporter一次warm后会话稳定；无快照失败仍有5分钟最小重试闩锁。
6. 本地replay不新增解压cap；V5继续保留64 MiB限制。
7. build fetcher采用`dotnet run`；如CI启动成本以后成为实测问题，可保持core接口改为预编译task assembly。
8. `latest.json`保持top-level `{version}`协议。

## 8. 红队与对抗验证

三名未参与设计的agent分别只审行为保持、ADR/钉死项、步骤完整性；全程review-only。共提15条：1 blocker、13 major、1 minor。随后由另外三名独立agent按“默认驳回”逐条亲读引用、检查后果与后续覆盖。最终**14条成立（1 blocker、12 major、1 minor），1条驳回，0条PLAUSIBLE，0条未验证**。

| ID | 原等级 | 对抗结论 | 证据与折入修订 |
| --- | --- | --- | --- |
| BHV-01 | major | **REJECT** | decode/identity现有映射确实不同（`GhostBattleSyncService.cs:248-265,308-314`），但C1.3已用可执行fixture钉住两类reason，后果被后续步骤覆盖。接口仍采用typed failure作可导航性澄清，不计确认缺陷。 |
| BHV-02 | major | **CONFIRMED major** | presigned非2xx现有16 KiB上限（`GhostBattleClient.cs:88-150`）；C2新增body policy并保持成功bounded stream，三种超限均有行为断言。 |
| BHV-03 | major | **CONFIRMED major** | transport `ex.Message`当前可流入UI（`GhostBattleClient.cs:64-71,121-128`；`HistoryPanelDataService.cs:213-237`）；C2拆closed user code与diagnostic exception，并加sentinel测试。 |
| BHV-04 | major | **CONFIRMED major** | fixed-list当前在任何refresh前return（`BPPSupporterCatalog.cs:76-89`）；C7改lazy factory并钉factory/catalog调用全0。 |
| BHV-05 | major | **CONFIRMED major** | 当前粗筛后先替换canonical（`RemoteEmbeddedData.targets:122-164`），存在即跳过（`:82-88`）；C9改Stage→SemanticValidate→整组Promote/rollback。 |
| RT-D1 | major | **CONFIRMED major** | ADR-0002要求非Unity lifecycle归composition registry（`docs/adr/0002-mountable-feature-registry.md:7-11`）；C7新增`SupporterCatalogModule : IBppFeature`，静态facade不持有lifecycle。 |
| RT-D2 | major | **CONFIRMED major** | ADR-0011要求caller-supplied相对时间、runtime翻译（`docs/adr/0011-pure-decision-cores-for-timing-invariants.md:9-16`）；C5改传`float secondsUntilInputDeadline`。 |
| RT-S01 | blocker | **CONFIRMED blocker** | coordinator截图查询仍依赖`Open()`（`BundleSealCoordinator.cs:479-500`），而截图store只有Save（`RunScreenshotSqliteStore.cs:7-12`）；C6先扩read seam再删除连接helper。 |
| RT-S02 | major | **CONFIRMED major** | Factory runner不执行真实factory/mount（`HistoryPanelFactory.Tests/Program.cs:8-11,127-215`）；C3新增可执行`HistoryPanelMountPlan`矩阵后才改变null-session行为。 |
| RT-S03 | major | **CONFIRMED major** | Supporters runner没有catalog fake（`Supporters.Tests.csproj:7-45`），TenWin stub又是private（`LiveBuildRecommendations.Tests/Program.cs:858-899`）；C7新增专用module runner/counting fake。 |
| RT-S04 | major | **CONFIRMED major** | 现有version测试只测comparer/formatter（`MainMenuVersionLabel.Tests.csproj:10-20`；`Program.cs:4-47`）；C8新增可执行generation/lease lifecycle与TCS乱序测试。 |
| RT-S05 | major | **CONFIRMED major** | 现有build验证只读XML（`CoreLayeringTests.cs:2655-2691`），已有seed不触Exec（`RemoteEmbeddedData.targets:186-200`）；C9新增loopback子进程MSBuild/run.sh集成runner。 |
| RT-S06 | major | **CONFIRMED major** | handler只能控HTTP，不能确定性观察`ApplyResponse`→`File.Delete`窗口（`BundleUploadFeed.cs:99-106`）；C6新增spy file port，在Delete瞬间查DB。 |
| RT-S07 | major | **CONFIRMED major** | 原步骤含不可执行省略号/“同命令”；§3.10现为每个步骤列完整项目路径、正确test/run分派、build与format-check。 |
| RT-S08 | minor | **CONFIRMED minor** | C3改变online缺席语义却漏旧enum文件（`HistoryPanelLogOperations.cs:20-25`）；touchesFiles已补并删除旧`OnlineClient` dependency词。 |

没有依赖服务端才能判断的红队finding；服务端/产品问题只保留在§7，未混入已验证结论。

## 9. 实施与验证结果

按 W1→W4 完成九个候选，旧实现均已删除，没有 fallback 或并行双链：

| 波次 | 落地结果 | 行为验证 |
| --- | --- | --- |
| W1 | `RunBundleV5Contract`、`ModApiResponse`、`MessagePackGzipFraming`成为唯一规则owner | replayability/identity、bounded body、envelope/request ID/Retry-After、gzip字节与64 MiB cap均有可执行fixture |
| W2 | `ModApiSession`替换`ModOnlineClient`；History增加local-only mount plan；每个upload activation自有session | session header/routes/dispose、无session仍挂载本地History、远端能力缺席和UI closed error均有fixture |
| W3 | seal纯核心与UTC正规化、supporter catalog module、release manifest adapter/lifecycle、build fetcher事务链 | deadline边界、fixed-list零warm、late callback、乱序release请求、loopback下载/坏seed/rollback均有fixture；真实`./run.sh fetch-data`也通过两条feature seed gate |
| W4 | queue SQL/事务归concrete Storage store，文件端口归Game，截图read归截图store | allocation幂等、orphan adoption/mismatch、invalid pending reseal、DB-before-delete、transient保留、retention/soft limit均有fixture |

实施与提交前独立审查中有九项声明或实现假设被真实代码证伪并就地修正：

1. `netstandard2.1`新增positional record需要`IsExternalInit`；Storage row改为等价的不可变class，未引入兼容shim。
2. bash `nounset`下零透传参数不能展开未初始化local array；`run.sh` helper改为直接转发`"$@"`。
3. shell函数位于`if`条件时，失败命令可能被后续成功命令覆盖；每层显式捕获并返回status。
4. Release配置下完整Voice测试包含与seed无关的Debug-only日志断言；production seed gate收窄为该工程中三个`Embedded_seed`真实parser测试，而全量Debug测试仍执行完整工程。
5. supporter迁移不能只保留snapshot发布；observer显式保留catalog/cache-write的degraded/recovered结构化日志。
6. 合法MessagePack可用`nil`覆盖非nullable集合初始化器；Run Bundle contract对battle列表、replay ID列表和card set列表均改为null-safe拒绝，并加入真实解码后的畸形DTO行为断言。
7. 仅构造TCS而不把它接入异步主路径不能证明乱序完成；Release lifecycle现在执行operation/publish gate并拥有每代request disposable，测试实际完成“新代先、旧代后”和destroy后完成，逐个断言dispose恰好一次。
8. 远端`error.code`虽来自已解析JSON，仍不是closed user code；response只暴露`http_<status>`，Bundle只allow-list两个nested V5 conflict code驱动处置，其余远端码只保留为协议诊断。
9. queue record若继续暴露`state`/`screenshot_state`字符串，持久化词汇仍会泄到Game；Storage新增typed state并独占字符串映射，pending validation projection删除无用status字段。

提交前独立实现审查分为Standards与Spec两轴：Standards提出1条hard violation和1条judgement call，Spec提出1条major和1条minor；四条均经主代理复核成立并按上面第6-9项修正，0条驳回、0条未验证。修正后定向build、六个行为工程、Architecture.Tests与format-check全部通过。

最终机械计数为68个测试工程：21个xUnit、47个exe-runner，38个工程含折行或单行`<Compile Include>`。最终门禁：

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
./run.sh test
./run.sh format-check
./run.sh restore-locked
```

主工程build为0 warning/0 error；`./run.sh test`遍历68个工程全部通过，Architecture.Tests为142/142；format-check检查1142个文件通过。未改MessagePack DTO图或SQLite持久化图，`RunLogSchema.LocalDatabaseSchemaVersion`与`RowSchemaVersion`保持1。

## 10. 实际 touchesFiles

以下为实施后的完整机械清单；已删除文件也保留在清单中：

```text
CONTEXT.md
build/RemoteEmbeddedDataFetcher/Program.cs
build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetch.cs
build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj
docs/ARCHITECTURE.md
docs/adr/0009-preserve-behavior-specific-boundaries.md
docs/adr/0011-pure-decision-cores-for-timing-invariants.md
docs/adr/0012-outbound-network-ownership.md
docs/adr/0013-remote-data-and-release-boundaries.md
docs/contracts/run-payload-v5.md
docs/drafts/2026-08-03-modapi-network-deepening-design.md
run.sh
src/BazaarPlusPlus.ModApi/Bundle/RunBundleV5Contract.cs
src/BazaarPlusPlus.ModApi/Bundle/RunPayloadV5Codec.cs
src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs
src/BazaarPlusPlus.ModApi/Clients/BundleUploadClient.cs
src/BazaarPlusPlus.ModApi/Clients/GhostBattleClient.cs
src/BazaarPlusPlus.ModApi/Clients/ModApiHealthClient.cs
src/BazaarPlusPlus.ModApi/Clients/ModApiSession.cs
src/BazaarPlusPlus.ModApi/Clients/ModOnlineClient.cs (deleted)
src/BazaarPlusPlus.ModApi/Http/BppHttpClientFactory.cs
src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs (deleted)
src/BazaarPlusPlus.ModApi/Http/ModApiResponse.cs
src/BazaarPlusPlus.ModApi/MessagePackGzipCodec.cs
src/BazaarPlusPlus.ModApi/MessagePackGzipFraming.cs
src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs (deleted)
src/BazaarPlusPlus.ModApi/ModApiRoutes.cs
src/BazaarPlusPlus.ModApi/Properties/AssemblyAttributes.cs
src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueRecords.cs
src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs
src/BazaarPlusPlus.Storage/RunScreenshot/RunScreenshotRecord.cs
src/BazaarPlusPlus.Storage/RunScreenshot/RunScreenshotSqliteStore.cs
src/BazaarPlusPlus.Storage/Sqlite/SqliteUtcInstant.cs
src/BazaarPlusPlus/BazaarPlusPlus.csproj
src/BazaarPlusPlus/BppComposition.cs
src/BazaarPlusPlus/Game/BundlePipeline/BundleOutboxFiles.cs
src/BazaarPlusPlus/Game/BundlePipeline/BundleSealConvergence.cs
src/BazaarPlusPlus/Game/BundlePipeline/BundleSealCoordinator.cs
src/BazaarPlusPlus/Game/BundlePipeline/BundleUploadFeed.cs
src/BazaarPlusPlus/Game/BundlePipeline/RunPayloadComposer.cs
src/BazaarPlusPlus/Game/HistoryPanel/AccountLink/AccountLinkLogRequest.cs
src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs
src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs
src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelFactory.cs
src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelLogOperations.cs
src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelMount.cs
src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelMountPlan.cs
src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelServerHealth.cs
src/BazaarPlusPlus/Game/Lobby/MainMenuVersionCheckController.cs
src/BazaarPlusPlus/Game/Lobby/ReleaseManifestCheckLifecycle.cs
src/BazaarPlusPlus/Game/Supporters/BPPSupporterCatalog.cs
src/BazaarPlusPlus/Game/Supporters/SupporterCatalogDocument.cs
src/BazaarPlusPlus/Game/Supporters/SupporterCatalogFactory.cs
src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs
src/BazaarPlusPlus/Game/Supporters/supporter-list.json
src/BazaarPlusPlus/Infrastructure/ReleaseManifest/ReleaseManifestClient.cs
src/BazaarPlusPlus/Plugin.cs
src/BazaarPlusPlus/Properties/AssemblyAttributes.cs
src/BazaarPlusPlus/RemoteEmbeddedData.targets
tests/Architecture.Tests/BundleSealConvergenceArchitectureTests.cs
tests/Architecture.Tests/CoreLayeringTests.cs
tests/Architecture.Tests/OutboundNetworkArchitectureTests.cs
tests/Architecture.Tests/RemoteEmbeddedCatalogArchitectureTests.cs
tests/Architecture.Tests/V5DataPipelineArchitectureTests.cs
tests/BundlePipeline.Tests/BundlePipeline.Tests.csproj
tests/BundlePipeline.Tests/Program.cs
tests/BundleQueueSqliteStore.Tests/BundleQueueSqliteStore.Tests.csproj
tests/BundleQueueSqliteStore.Tests/Program.cs
tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj
tests/BundleSealConvergence.Tests/Program.cs
tests/BundleV5Codec.Tests/Program.cs
tests/GhostBattleSync.Tests/Program.cs
tests/HistoryPanelFactory.Tests/Program.cs
tests/ModApi.Tests/BazaarDbLinkClientTests.cs
tests/ModApi.Tests/ErrorFormatterTests.cs (deleted)
tests/ModApi.Tests/HealthClientTests.cs
tests/ModApi.Tests/ModApiResponseTests.cs
tests/ModApi.Tests/Program.cs
tests/ModApi.Tests/SessionTests.cs
tests/ReleaseManifestClient.Tests/Program.cs
tests/ReleaseManifestClient.Tests/ReleaseManifestClient.Tests.csproj
tests/RemoteEmbeddedDataPipeline.Tests/Program.cs
tests/RemoteEmbeddedDataPipeline.Tests/RemoteEmbeddedDataPipeline.Tests.csproj
tests/RunScreenshotSqliteStore.Tests/Program.cs
tests/StartupUploadRunner.Tests/Program.cs
tests/SupporterCatalogModule.Tests/Program.cs
tests/SupporterCatalogModule.Tests/SupporterCatalogModule.Tests.csproj
tests/Supporters.Tests/Program.cs
tests/Supporters.Tests/Supporters.Tests.csproj
tools/BundleV5E2E/BundleV5E2E.csproj
tools/BundleV5E2E/Program.cs
```
