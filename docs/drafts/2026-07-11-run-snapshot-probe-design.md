# IRunSnapshotProbe 运行快照探针 设计稿 · rev2

状态：红队评审已回收并修订（3 视角均 sound-with-revisions，1 blocker 4 major 已全部吸收），待用户确认实施。
来源：2026-07-11 架构评审候选 4 + grilling 决策 + 红队修订（同日）。

## 红队修订记录（rev2 变更）

| # | 发现 | 采纳的修订 |
|---|---|---|
| R1 (blocker) | 「两个 builder 委托改必传」触发 CS1737——`utcNow = null` 位于其前 | **`utcNow` 一并改必传**（两个 ctor 参数顺序不变——`RunLoggingModule.Tests` 的 Activator 按位传全部 9 参，顺序不变则不破）；`RunLoggingController` 接线处显式传 `utcNow: () => DateTime.UtcNow` + 两个 builder 闭包。5 参转发 ctor（`RunLoggingModule.cs:36-58`）实现时核查调用方：无生产调用方则删除，有则同步获得必传 builder 参数 |
| R2 (major) | `PlayerStatsSnapshot` 若按 `ChoicePedestalSnapshot` 先例设 internal，公开的 `RunLogSessionManager` ctor 暴露它会 CS0051 | **`PlayerStatsSnapshot` 与 `RankSnapshot` 设 `public sealed class`**（先例：`EncounterIdsSnapshot`/`EncounterTargetingSnapshot` 已因跨程序集测试同因公开）；`RunBasicsSnapshot` 保持 internal（仅 internal 映射器消费）；`IRunSnapshotProbe` 保持 internal |
| R3 (major) | `SettingsDockRegistry.Tests` 的 `ContractTestServices`（`:77-92`）实现真 `IBppServices`，新成员令其 CS0535 | 同一提交为其加 `public IRunSnapshotProbe RunSnapshot => null!;` 桩；显式记录 `CombatStatusBarState.Tests` 的 shim **不受影响**（本地重定义接口，无 ProjectReference） |
| R4 (major) | 接口混用两种返回风格（快照 vs 散 out），且「按读取成本」说辞夸大——basics/stats 都是廉价字段读 | rank 升格 Core DTO：`TryGetRankSnapshot(out RankSnapshot rank)`；rationale 改写为诚实版本：**一条「廉价字段 vs 反射」成本边界 + 消费者子集切分**；排行榜名次保持单 `out int?`（单值不造 DTO） |
| R5 (fidelity) | 「null → 属性字段全 null」是**错误的保真声明**——旧 SaveCheckpoint 读取失败时**保留 last-known 值**（session 字段不动，checkpoint 读 session.*） | 更正：保留 `if (stats != null) { session.* = stats.*; }` 守卫，provider 返回 null ⟹ checkpoint 携带 last-known 值，与今日完全一致 |
| R6 (major) | `RunLoggingSession.Tests/Program.cs:8` 用精确双类型数组 `GetConstructor` 反射取 ctor——加第三参后返回 null 直接炸 | 列入必改：类型数组补 `typeof(Func<PlayerStatsSnapshot?>)`，两处 `ctor.Invoke` 补第三实参；`statsProvider` **固定追加在 `utcNow` 之后**（按位绑定约束） |
| R7 (minor) | 只改一个读取器名字造成不对称；且 rev1 错称 CoreLayeringTests 锚定截图读取器文件名（实际锚定的是 controller 文件名） | 两者对称改名：`RunScreenshotMetadataReader → RunScreenshotRecordMapper`；更正锚定声明（`EndOfRunScreenshotController.cs` 文件名不可动，读取器可自由改名） |
| R8 (minor) | `docs/ARCHITECTURE.md:15` 引用将被删除的 `RunLoggingGameDataReader.cs:23-25` | 实现任务追加：改指向新的 `RunLoggingController` BuildChannel 组装点 |

另：全部闭包必须**在调用时**读取（`RunContext.LastRunExitKind`、探针值在闭包体内取，不得在接线时捕获值）——rev1 已隐含，rev2 明示。

## 背景与问题

「给当前 run 拍快照」被三处平行实现（本轮只收前两处）：

- `Game/RunLogging/RunLoggingGameDataReader.cs`：有状态静态单例（`Install(IRunContext, IGameBuildInfo)`/`Reset`，`Plugin.cs:167/178` 装卸），读 `Data.Run`/`Data.SelectedPlayMode`/`BppClientCacheBridge`，产出 Storage DTO。其 `TryGetPlayerRankSnapshot`（`:66-82`）冗余重实现了 bridge 2-out 重载已有的 try/catch 包装。
- `Game/Screenshots/RunScreenshotMetadataReader.cs`：无状态静态，一个 `CreateRecord`，唯一调用点 `EndOfRunScreenshotController.cs:369`，零 try/catch 纯 null 传播。
- `RunLogSessionManager.SaveCheckpoint`（`RunLogSessionManager.cs:83`）暗中直调静态读取器——构造函数看似全注入，实为隐藏依赖；两个构造过它的测试套件都从未调用过 `SaveCheckpoint`。
- `RunLoggingModule` 的 `Func<string, RunLogCompletion>`/`Func<string, RunLogAbandonment>` 委托缝在唯一生产调用点从未被替换（`RunLoggingController.cs:55-64` 传入的就是默认静态）。

重叠矩阵：共享 Day/Victories/rank/rating；RunLog 独有 Hour/Losses/GameMode/5 玩家属性；Screenshots 独有排行榜名次。

## 已确认决策（grilling，2026-07-11）

1. **单接口按读取成本分方法**（对齐 `IEncounterStateProbe`/ADR-0001 先例），适配器无帧缓存（调用频率低，同 `GameStateProbe`）。
2. **SaveCheckpoint 注入窄委托**：`Func<PlayerStatsSnapshot?>? statsProvider = null`，**无静态默认**（null → checkpoint 不带属性）；生产接线由 `RunLoggingController` 组装。
3. **杀掉两个静态读取器的静态形态**：`Install`/`Reset` 删除（`Plugin.cs` 同步去提），退化为纯映射器；`IRunContext`/`IGameBuildInfo` 读取上移到已持有 `services` 的调用方。
4. **范围与保真**：`PvpBattleSnapshotCollector`（第三份实现）与其他 `Data.Run` 散户不收（记为探针后续采用候选）；rank 读取统一到 bridge 2-out 语义（行为等价——双方最终都落到同一个 3-out + catch）；hero 双读（controller 缓冲 `:423` + reader fallback）保真保留；`RunLoggingModule` 两个委托改**必传**、静态默认删除。

## 新模块

**接口 + 快照 DTO**（`Core/GameState/`，架构约束：GameInterop 禁 import `Game.*`，快照类型必须落 Core）：

```csharp
// Core/GameState/IRunSnapshotProbe.cs
internal interface IRunSnapshotProbe
{
    bool TryGetRunBasics(out RunBasicsSnapshot basics);      // false ⟺ Data.Run == null
    bool TryGetPlayerStats(out PlayerStatsSnapshot stats);   // false ⟺ Data.Run?.Player == null（保真旧 TryBuildRunLogPlayerStats 语义）
    bool TryGetRankSnapshot(out RankSnapshot rank);          // ≡ bridge 2-out（R4：升格快照，与先例统一）
    bool TryGetLeaderboardPosition(out int? position);       // ≡ bridge（单值不造 DTO）
}

// Core/GameState/RunBasicsSnapshot.cs — internal sealed POCO（仅 internal 映射器消费）
// Day:int? Hour:int? Victories:int? Losses:int? Hero:string? GameMode:string?
// （Victories/Losses 用 unchecked((int)uint) 保真转换；Hero null ⟺ Player null；GameMode 来自 Data.SelectedPlayMode）

// Core/GameState/PlayerStatsSnapshot.cs — public sealed POCO（R2：公开 ctor 参数类型，CS0051）
// MaxHealth/Prestige/Level/Income/Gold : int?（取代并删除 Game/RunLogging 的 RunLogPlayerStatsSnapshot）

// Core/GameState/RankSnapshot.cs — public sealed POCO（R4）
// Rank:string? Rating:int?
```

拆分依据（R4 诚实版）：一条「廉价 `Data.Run` 字段读 vs `ClientCache` 反射读」的成本边界（basics/stats vs rank/leaderboard），叠加消费者子集切分（checkpoint 只要 stats、abandonment 只要 basics）。

**适配器**：`GameInterop/RunSnapshot/RunSnapshotProbe.cs`（子目录，`EncounterStateProbe` 先例）。唯一读游戏全局的地方：`Data.Run`、`Data.SelectedPlayMode`、`BppClientCacheBridge` 2-out 两个方法。无帧缓存。**逐方法保真现有 catch 语义**：basics/stats 无 try/catch（null 三元/前置守卫，与旧读取器一致——不趁机加软失败硬化）；rank/leaderboard 沿用 bridge 自身的 catch。

**组合根**：`BppComposition` 字段初始化 `new RunSnapshotProbe()`；`IBppServices` 增 `IRunSnapshotProbe RunSnapshot { get; }`（`IBppServices.cs`/`BppRuntimeServices.cs` 已在 CoreLayeringTests 允许名单，无需新增豁免）。

## 消费方改造

1. **`RunLogSessionManager`**：ctor 追加第三参 `Func<PlayerStatsSnapshot?>? statsProvider = null`（**位置固定在 `utcNow` 之后**，R6 按位绑定约束）；`SaveCheckpoint` 的 `:83` 改为 `var stats = _statsProvider?.Invoke(); if (stats != null) { session.MaxHealth = stats.MaxHealth; ... }`——**保留非空守卫**：provider 返回 null ⟹ session 字段不动，checkpoint 携带 last-known 值（R5 更正：今日 `TryBuildRunLogPlayerStats` 返回 false 的真实语义就是保留 last-known，不是置 null）。**不再有任何静态触达。**
2. **`RunLoggingGameDataReader` → 纯映射器**（类改名 `RunLogRecordMapper`，同文件路径；无测试反射钉住该名，已核）：
   - `TryCreateRunLogCreateRequest(RunBasicsSnapshot? basics, RankSnapshot? rank, string? serverRunId, string? buildChannel, out RunLogCreateRequest)` — 旧守卫等价：`basics == null || basics.Hero == null || serverRunId 空白` → false（旧 `Data.Run?.Player == null` ⟺ basics 缺 Hero）。
   - `BuildRunLogCompletion(string reason, RunExitKind lastExitKind, RunBasicsSnapshot? basics, PlayerStatsSnapshot? stats, RankSnapshot? rank)` — Status 由 `lastExitKind == Interrupted ? "abandoned" : "completed"`（原从 `RunContext` 读，现作参数，**闭包体内调用时取值**）；`EndedAtUtc = DateTimeOffset.UtcNow` 保留；stats null → completion 字段 null（保真：旧完成构造器丢弃 bool 后 `stats?.X` 本就得 null——注意与 checkpoint 的 last-known 语义不同，二者各自保真）。
   - `BuildRunLogAbandonment(string reason, RunBasicsSnapshot? basics)`。
3. **`RunLoggingController`** 成为组合点（已持有 `IBppServices`）：
   - `_sessionManager = new RunLogSessionManager(_store, statsProvider: () => probe.TryGetPlayerStats(out var s) ? s : null)`；
   - `EnsureActiveRunFromGame`（`:107`）改为 probe + `services.RunContext.CurrentServerRunId` + `services.GameBuild.Channel.ToString()` 组装后调映射器（BuildChannel 从 Install 时缓存改为每次调用现读——同一来源，run 中不可变，行为等价，记为微差异 D1）；
   - `RunLoggingModule` 构造处两个委托改必传闭包：`reason => RunLogRecordMapper.BuildRunLogCompletion(reason, services.RunContext.LastRunExitKind, Basics(), Stats(), Rank()...)` 等。
4. **`RunLoggingModule`**：`buildRunLogCompletion`/`buildRunLogAbandonment` **与 `utcNow` 三者一并改必传**（R1，参数顺序不变），删除对旧静态的两处 fallback（`:86-89`）；5 参转发 ctor 按 R1 处置。闭包一律在体内取值（探针 + `services.RunContext.LastRunExitKind`）。
5. **`RunScreenshotMetadataReader` → `RunScreenshotRecordMapper`**（R7 对称改名；无任何测试/ratchet 钉住旧名）：`CreateRecord(ScreenshotCaptureResult capture, RunBasicsSnapshot? basics, RankSnapshot? rank, int? position, bool isPrimary, string? buildChannel)`；hero 逻辑保真（优先 `capture.HeroName`，fallback `basics?.Hero`）。调用方 `EndOfRunScreenshotController.PersistCaptureAsync` 组装（已持有 `_services`）。**注意**：CoreLayeringTests 锚定的是 `EndOfRunScreenshotController.cs` **文件名**（`:1122-1124`），该文件不得改名/移动；映射器文件本身可自由改名。
6. **`Plugin.cs`**：删 `:167` `Install` 与 `:178` `Reset` 两行；`Game/RunLogging/RunLogCaptureService.cs` 中的 `RunLogPlayerStatsSnapshot` 删除（被 Core `PlayerStatsSnapshot` 取代）。

## 行为保真承诺与已知微差异

- 全部字段映射、null 降级、守卫语义、`EndedAtUtc=now`、hero 双读、uint→int unchecked 转换逐一保真。
- **D1**：RunLog 侧 BuildChannel 从「启动时缓存」改「每次现读 `services.GameBuild.Channel`」——同一来源、run 内不可变（`GameBuildInfoResolver` 启动即定），行为等价。
- **D2**：rank 读取删除冗余外层 try/catch（bridge 2-out 已含同语义 catch）——异常路径行为等价。

## 测试计划

- **新增（解锁两块盲区）**：
  - `RunLoggingSession.Tests`：`SaveCheckpoint` 首次可测——注入假 `statsProvider` → 断言 checkpoint 行携带 5 属性；再测 null provider → 属性全 null。
  - 映射器纯函数直测：RunLog 三个映射进 `RunLoggingCapture.Tests`（该套件已是纯映射器测试之家）；`CreateRecord` 进 `EndOfRunScreenshotGate.Tests`（伪造快照 → 断言 `RunScreenshotRecord` 字段）。
- **机械改写（R1/R3/R6 全量清单）**：
  - `RunLoggingSession.Tests/Program.cs:8`：`GetConstructor` 类型数组补 `typeof(Func<PlayerStatsSnapshot?>)`；两处 `ctor.Invoke` 补第三实参（精确双类型数组在三参 ctor 上返回 null——红队已编译复证）。
  - `RunLoggingModule.Tests/Program.cs:82-108, 153-179`：Activator 按位传全部 9 参且顺序不变 → **无需改动**（必传化不影响全参按位调用）；`:23` 直接 `new RunLogSessionManager(store, () => now)` 不受可选第三参影响。
  - `SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:77-92`：`ContractTestServices` 加 `public IRunSnapshotProbe RunSnapshot => null!;`（真接口的第二实现者）；`CombatStatusBarState.Tests` 的 shim 不受影响（本地重定义，无 ProjectReference）。
- **无 csproj 陷阱**：受影响测试项目全部 ProjectReference，已核。
- **文档修正**：`docs/ARCHITECTURE.md:15` 的 BuildChannel 引用改指向 `RunLoggingController` 新组装点（R8）。
- 验证：`dotnet run --project` 受影响 exe-runner（RunLoggingSession/RunLoggingModule/RunLoggingCapture/RunScreenshotSqliteStore/EndOfRunScreenshotGate）+ `dotnet test tests/SettingsDockRegistry.Tests tests/Architecture.Tests` + `dotnet build`。

## CONTEXT.md 新词条（实现时一并提交）

> **Run Snapshot Probe（运行快照探针）**：对「当前 run 的可记录事实」（天数/小时/胜负/英雄/模式、玩家五属性、段位、排行榜名次）的按需拉取读取，由 `IRunSnapshotProbe`（Core）+ `GameInterop/RunSnapshot` 适配器承载，按读取成本分方法。RunLogging 与 Screenshots 的记录构造是消费快照的纯映射器，不再直读游戏全局。

## 不做的事

- `PvpBattleSnapshotCollector` 与 CombatReplay/CollectionPanel/Tooltips 等 `Data.Run` 散户的探针化 → 后续候选。
- 软失败硬化（给 basics/stats 加 try/catch）→ 保真优先，本轮不做。
- 探针帧缓存 → 调用频率低，不需要。
