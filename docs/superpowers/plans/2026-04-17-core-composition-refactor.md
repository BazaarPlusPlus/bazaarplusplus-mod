# Core/Game Composition Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除 `Core/` 对 `Game/` 的 3 处反向依赖，并把 `BppRuntimeHost.Current` 的 7 个静态访问器替换为显式 `IBppServices` 注入 + Patch 层集中静态。

**Architecture:** 单 PR，3 个逻辑 commit：① 脚手架 + 反向依赖修复（`BppRuntimeHost` 仍在）；② 所有消费方切换到 `IBppServices`/`BppPatchHost`；③ 删除 `BppRuntimeHost`，跑负向 grep。组合根新建为 `BppComposition`（根命名空间，全项目唯一可同时 `using Core.*`/`Game.*` 的位置）。

**Tech Stack:** C# 11 / .NET Standard 2.1 / BepInEx 5 / HarmonyX / Unity 2022 LTS

**Spec:** [docs/superpowers/specs/2026-04-17-core-composition-refactor-design.md](../specs/2026-04-17-core-composition-refactor-design.md)

---

## Prerequisites

- [ ] **Step 0.1: 确认工作目录干净**

Run: `cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod && git status`
Expected: `nothing to commit, working tree clean`（如果不在 git 仓库根，改为在子仓库内验证；若非 git 管理，记录起点的文件 inventory 以便回滚）

- [ ] **Step 0.2: 确认基线构建可通过**

Run: `cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod && dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.` 无 error，warning 数记为基线

- [ ] **Step 0.3: 收集当前静态调用分布作为基线**

Run:
```bash
grep -rn "BppRuntimeHost\." Core Game Patches Infrastructure --include="*.cs" | wc -l
```
Expected: ~54（与 spec §1 一致）

---

## Commit 1 Foundation scaffolding + reverse-dep fixes

### Task 1: 引入 `IBppServices` 接口

**Files:**
- Create: `Core/Runtime/IBppServices.cs`
- Modify: `Core/Runtime/BppRuntimeServices.cs`

- [ ] **Step 1.1: 创建 `IBppServices.cs`**

Write `Core/Runtime/IBppServices.cs`:
```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BepInEx.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal interface IBppServices
{
    IBppEventBus EventBus { get; }
    IBppConfig Config { get; }
    IPathService Paths { get; }
    IRunContext RunContext { get; }
    IGameStateProbe GameStateProbe { get; }
    ManualLogSource Logger { get; }
}
```

- [ ] **Step 1.2: 扩展 `BppRuntimeServices` 以实现接口 + 加 `Logger`**

Modify `Core/Runtime/BppRuntimeServices.cs`：增加 `Logger` 参数与属性，并声明 `: IBppServices`：

```csharp
#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BepInEx.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeServices : IBppServices
{
    public BppRuntimeServices(
        IBppEventBus eventBus,
        IBppConfig config,
        IPathService paths,
        IRunContext runContext,
        IGameStateProbe gameStateProbe,
        ManualLogSource logger
    )
    {
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        RunContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        GameStateProbe = gameStateProbe ?? throw new ArgumentNullException(nameof(gameStateProbe));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IBppEventBus EventBus { get; }
    public IBppConfig Config { get; }
    public IPathService Paths { get; }
    public IRunContext RunContext { get; }
    public IGameStateProbe GameStateProbe { get; }
    public ManualLogSource Logger { get; }
}
```

- [ ] **Step 1.3: 修正 `BppRuntimeHost` 调用点以匹配新构造函数**

Modify `Core/Runtime/BppRuntimeHost.cs:22-28`（`DetachedServices`）和 `:60-66`（`Services = new(...)`）：在参数末尾加 `_logger!`（`DetachedServices` 会在后续 commit 删除，此处临时传一个空 logger 即可——用 `new ManualLogSource("BppDetached")` 保持编译通过）。

修改 22-28:
```csharp
private static readonly BppRuntimeServices DetachedServices = new(
    new InMemoryBppEventBus(),
    new BppConfig(),
    new BppPathService(),
    new RunContextStore(),
    new GameStateProbe(),
    new ManualLogSource("BppDetached")
);
```

修改 60-66:
```csharp
Services = new BppRuntimeServices(
    _eventBus,
    _config,
    _paths,
    _runContext,
    _gameStateProbe,
    _logger
);
```

- [ ] **Step 1.4: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 1.5: 提交**

```bash
git add Core/Runtime/IBppServices.cs Core/Runtime/BppRuntimeServices.cs Core/Runtime/BppRuntimeHost.cs
git commit -m "Introduce IBppServices interface with Logger"
```

---

### Task 2: 修复反向依赖 #2 —— `BppPathConstants`

**Files:**
- Create: `Core/Paths/BppPathConstants.cs`
- Modify: `Core/Paths/BppPathService.cs:2` 和 `:23-27`
- Modify: `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs:17`

- [ ] **Step 2.1: 创建 `BppPathConstants.cs`**

Write `Core/Paths/BppPathConstants.cs`:
```csharp
#nullable enable
namespace BazaarPlusPlus.Core.Paths;

internal static class BppPathConstants
{
    public const string RunLogDatabaseFileName = "bazaarplusplus.db";
}
```

- [ ] **Step 2.2: `BppPathService` 改用 Core 常量**

Modify `Core/Paths/BppPathService.cs`:
- 删除第 2 行 `using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;`
- 将第 26 行 `RunLogSqliteSchema.DatabaseFileName` 改为 `BppPathConstants.RunLogDatabaseFileName`

- [ ] **Step 2.3: Game 侧 `RunLogSqliteSchema` 反过来引用 Core 常量**

Modify `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs:17`：
- 在 using 列表加 `using BazaarPlusPlus.Core.Paths;`
- 第 17 行改为：`public static string DatabaseFileName => BppPathConstants.RunLogDatabaseFileName;`

（保留 `DatabaseFileName` 属性以免破坏 Game 内部多个引用点；它现在只是 Core 常量的转发。）

- [ ] **Step 2.4: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 2.5: 验证反向依赖 #2 已消除**

Run: `grep -n "using BazaarPlusPlus.Game" Core/Paths/BppPathService.cs`
Expected: 无输出

- [ ] **Step 2.6: 提交**

```bash
git add Core/Paths/BppPathConstants.cs Core/Paths/BppPathService.cs Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs
git commit -m "Reverse Core/Game path constant dependency"
```

---

### Task 3: 修复反向依赖 #3 —— 把 `PvpBattleRecorded` 移到 Game

**Files:**
- Delete: `Core/Events/PvpBattleRecorded.cs`
- Create: `Game/PvpBattles/PvpBattleRecorded.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs:218`（Publish 处的 `using`）
- Modify: `Game/RunLogging/RunLoggingModule.cs`（Subscribe 处的 `using`）

- [ ] **Step 3.1: 创建新事件文件**

Write `Game/PvpBattles/PvpBattleRecorded.cs`:
```csharp
#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleRecorded
{
    public PvpBattleManifest Manifest { get; set; } = null!;
}
```

- [ ] **Step 3.2: 删除旧文件**

```bash
rm Core/Events/PvpBattleRecorded.cs
```

- [ ] **Step 3.3: 在 Publish 点添加 using**

Modify `Game/CombatReplay/CombatReplayRuntime.cs`：已有 `using BazaarPlusPlus.Core.Events;`。检查是否还需要它（如果是 `PvpBattleRecorded` 是该文件从 Core.Events 拉入的唯一类型则删除；否则保留）。在文件顶部加 `using BazaarPlusPlus.Game.PvpBattles;`（若尚未存在）。

Verify: grep 其他 `BazaarPlusPlus.Core.Events` 的类型在该文件的使用：
```bash
grep -n "CombatFrameAdvanced\|CombatSimObserved\|CombatReplayPersistenceDrained\|NetMessageObserved\|RunInitializedObserved\|RunLifecycleChanged\|IBppEventBus" Game/CombatReplay/CombatReplayRuntime.cs
```
如仍有命中则保留 Core.Events using。

- [ ] **Step 3.4: 在 Subscribe 点同样处理**

Modify `Game/RunLogging/RunLoggingModule.cs`：检查 `BazaarPlusPlus.Core.Events` 的其他引用；加 `using BazaarPlusPlus.Game.PvpBattles;`（若尚未存在）。

- [ ] **Step 3.5: 同步检查测试项目引用**

Run: `grep -rn "PvpBattleRecorded" tests/`
Expected: 无输出（若有则同步更新 `using`）

- [ ] **Step 3.6: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3.7: 验证反向依赖 #3 已消除**

Run: `grep -rn "using BazaarPlusPlus.Game" Core/`
Expected: 仅剩 `Core/Runtime/BppRuntimeHost.cs` 的 3 行（反向依赖 #1，Commit 3 清理）

- [ ] **Step 3.8: 提交**

```bash
git add -A Core/Events Game/PvpBattles/PvpBattleRecorded.cs Game/CombatReplay/CombatReplayRuntime.cs Game/RunLogging/RunLoggingModule.cs
git commit -m "Move PvpBattleRecorded event to Game/PvpBattles"
```

---

### Task 4: 添加 `BppPatchHost`

**Files:**
- Create: `Patches/BppPatchHost.cs`

- [ ] **Step 4.1: 创建 Patch 层静态入口**

Write `Patches/BppPatchHost.cs`:
```csharp
#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Patches;

internal static class BppPatchHost
{
    private static IBppServices? _services;

    public static IBppServices Services =>
        _services
        ?? throw new InvalidOperationException(
            "BppPatchHost.Install must be called before patches run."
        );

    public static void Install(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public static void Reset()
    {
        _services = null;
    }
}
```

- [ ] **Step 4.2: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`（此时 `BppPatchHost` 尚无消费方，只是声明）

- [ ] **Step 4.3: 提交**

```bash
git add Patches/BppPatchHost.cs
git commit -m "Add BppPatchHost static entry point for Patches layer"
```

---

### Task 5: 添加 `BppComposition` 骨架

**Files:**
- Create: `BppComposition.cs`（项目根）

此任务**只写骨架**，实际被 Plugin.cs 使用在 Task 12 里。这里写完后依然是死代码，靠编译器验证签名对齐。

- [ ] **Step 5.1: 创建 `BppComposition.cs`**

Write `BppComposition.cs`:
```csharp
#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.RunLifecycle;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class BppComposition : IDisposable
{
    private readonly InMemoryBppEventBus _eventBus = new();
    private readonly BppConfig _config = new();
    private readonly BppPathService _paths = new();
    private readonly RunContextStore _runContext = new();
    private readonly GameStateProbe _gameStateProbe = new();
    private readonly BppRuntimeServices _services;
    private readonly BppFeatureRegistry _featureRegistry = new();
    private readonly RunLifecycleModule _runLifecycle;
    private readonly CombatReplayModule _combatReplayModule;
    private readonly CombatStatusBarModule _combatStatusBarModule;
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntimeAccessor;

    public IBppServices Services => _services;
    public RunLifecycleModule RunLifecycle => _runLifecycle;

    public BppComposition(
        ManualLogSource logger,
        ConfigFile configFile,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
    )
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (configFile == null) throw new ArgumentNullException(nameof(configFile));
        _combatReplayRuntimeAccessor =
            combatReplayRuntimeAccessor ?? throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));

        _config.Initialize(configFile);
        _paths.Initialize();
        _runContext.Reset();

        _services = new BppRuntimeServices(
            _eventBus, _config, _paths, _runContext, _gameStateProbe, logger
        );

        _runLifecycle = new RunLifecycleModule(_eventBus, _gameStateProbe, _runContext);
        _combatReplayModule = new CombatReplayModule(_eventBus, _combatReplayRuntimeAccessor);
        _combatStatusBarModule = new CombatStatusBarModule(_eventBus, _runContext);

        _featureRegistry.Register(_runLifecycle);
        _featureRegistry.Register(_combatReplayModule);
        _featureRegistry.Register(_combatStatusBarModule);
    }

    public void Start() => _featureRegistry.Start();

    public void Dispose() => _featureRegistry.Stop();
}
```

- [ ] **Step 5.2: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`（BppComposition 尚未被使用，但编译器校验所有依赖能解析）

- [ ] **Step 5.3: 提交**

```bash
git add BppComposition.cs
git commit -m "Add BppComposition composition root skeleton"
```

---

## Commit 2 Consumer migration

整个 Commit 2 目标：让所有现有 `BppRuntimeHost.X` 静态调用换成 `IBppServices` / `BppPatchHost.Services` / 构造注入。`BppRuntimeHost` 本身保持原状。

### Task 6: 迁移 `BppLog` 到 Install 模式

**Files:**
- Modify: `Infrastructure/BppLog.cs`
- Modify: `Plugin.cs`（Task 12 再收口；此处只搭静态注入点）

- [ ] **Step 6.1: 替换 Logger 获取方式**

Modify `Infrastructure/BppLog.cs`:
- 删除 `using BazaarPlusPlus.Core.Runtime;`（第 5 行）
- 删除第 22 行 `private static ManualLogSource? Logger => BppRuntimeHost.Logger;`
- 新增 `Install` 方法和私有字段：

在 class 体内合适位置（Prefix 附近）加：
```csharp
private static ManualLogSource? _logger;

public static void Install(ManualLogSource logger)
{
    _logger = logger;
}

private static ManualLogSource? Logger => _logger;
```

（保留 `Logger` 属性语义以免改动下游 `Log(logger, ...)` 等方法签名。）

- [ ] **Step 6.2: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: 编译通过，可能出现 `BppLog.Install` 未被调用导致运行期日志丢失——属于预期（Task 12 在 Plugin.cs 加 Install 调用）。

- [ ] **Step 6.3: 提交**

```bash
git add Infrastructure/BppLog.cs
git commit -m "BppLog: switch Logger from ServiceLocator to Install injection"
```

---

### Task 7: 迁移 5 个 Harmony Patch 到 `BppPatchHost.Services`

**文件清单（grep 锁定）**:
| 文件 | 行号 | 当前 |
|---|---|---|
| `Patches/RunLogging/RunInitializedPatch.cs` | 19 | `BppRuntimeHost.EventBus.Publish(...)` |
| `Patches/Tooltips/ItemEnchantPreviewPatch.cs` | 54 | `BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig?.Value` |
| `Patches/Combat/CombatReplayCapturePatch.cs` | 19 | `BppRuntimeHost.EventBus.Publish(...)` |
| `Patches/Combat/CombatSimulationPatches.cs` | 18, 28 | `BppRuntimeHost.EventBus.Publish(...)` |
| `Patches/NameOverride/NameOverridePatches.cs` | 17 | `BppRuntimeHost.Config.EnableNameOverrideConfig?.Value` |

**Transformation pattern** —— 对每个文件：

1. 将 `using BazaarPlusPlus.Core.Runtime;` 替换为 `using BazaarPlusPlus.Patches;`（若已有则保留）
2. 所有 `BppRuntimeHost.X` → `BppPatchHost.Services.X`

- [ ] **Step 7.1: 迁移 `RunInitializedPatch.cs`**

Modify `Patches/RunLogging/RunInitializedPatch.cs:4` 和 `:19`：
- 第 4 行 `using BazaarPlusPlus.Core.Runtime;` → `using BazaarPlusPlus.Patches;`
- 第 19 行 `BppRuntimeHost.EventBus.Publish(...)` → `BppPatchHost.Services.EventBus.Publish(...)`

- [ ] **Step 7.2: 迁移 `ItemEnchantPreviewPatch.cs`**

Modify `Patches/Tooltips/ItemEnchantPreviewPatch.cs`: 同样 pattern，第 54 行 `BppRuntimeHost.Config` → `BppPatchHost.Services.Config`。

- [ ] **Step 7.3: 迁移 `CombatReplayCapturePatch.cs`**

Modify `Patches/Combat/CombatReplayCapturePatch.cs`: 第 19 行 `BppRuntimeHost.EventBus` → `BppPatchHost.Services.EventBus`。

- [ ] **Step 7.4: 迁移 `CombatSimulationPatches.cs`**

Modify `Patches/Combat/CombatSimulationPatches.cs`: 第 18、28 行 `BppRuntimeHost.EventBus` → `BppPatchHost.Services.EventBus`。

- [ ] **Step 7.5: 迁移 `NameOverridePatches.cs`**

Modify `Patches/NameOverride/NameOverridePatches.cs`: 第 17 行 `BppRuntimeHost.Config` → `BppPatchHost.Services.Config`。

- [ ] **Step 7.6: 验证 Patches 目录已无 BppRuntimeHost 引用**

Run: `grep -rn "BppRuntimeHost" Patches/`
Expected: 无输出

- [ ] **Step 7.7: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7.8: 提交**

```bash
git add Patches/
git commit -m "Patches: route static access through BppPatchHost"
```

---

### Task 8: 迁移 `RunLoggingController` (MonoBehaviour)

**Files:**
- Modify: `Game/RunLogging/RunLoggingController.cs`

**Pattern（MonoBehaviour）**：
1. 新增私有字段存 `IBppServices _services` 和本模块的其他依赖
2. 新增 `public void Initialize(IBppServices services)`（若需其它非 `IBppServices` 依赖也在参数中列出）
3. 把原 `Awake()` 中读 `BppRuntimeHost.X` 的代码移到一个新私有方法 `InitializeCore()`；`Awake()` 保持空或仅做不依赖 services 的本地初始化
4. `Initialize()` 记下 services 后立刻调 `InitializeCore()`
5. `Plugin.cs`（Task 12）里紧跟 `AddComponent<T>()` 调 `.Initialize(services)`

**当前 `RunLoggingController.Awake()` 需要的依赖**（见源文件 lines 27-51）：
- `services.Paths.RunLogDatabasePath`
- `services.EventBus`
- 两个日志行

- [ ] **Step 8.1: 改造 `RunLoggingController`**

Modify `Game/RunLogging/RunLoggingController.cs`:

头部 using：
- 删除 `using BazaarPlusPlus.Core.Runtime;`（若存在）
- 加 `using BazaarPlusPlus.Core.Runtime;` — 实际仍需要，因为 `IBppServices` 在这个 namespace。所以**保留** `using BazaarPlusPlus.Core.Runtime;`，只是不再读静态

类体改造：
```csharp
private IBppServices? _services;
// ...其余字段保持

private void Awake()
{
    // 等待 Initialize() — 原 Awake 体已迁出
}

public void Initialize(IBppServices services)
{
    _services = services ?? throw new ArgumentNullException(nameof(services));
    InitializeCore();
}

private void InitializeCore()
{
    var services = _services!;
    var runLogDatabasePath =
        services.Paths.RunLogDatabasePath
        ?? throw new InvalidOperationException("Run log database path is not initialized.");
    var sqliteStore = new SqliteRunLogStore(runLogDatabasePath);
    var uploadStore = new RunSyncStateSqliteStore(runLogDatabasePath);
    var battleCatalog = new PvpBattleCatalog(runLogDatabasePath);
    _store = new QueuedRunLogStore(new ReplicatedRunLogStore(sqliteStore, uploadStore));
    _sessionManager = new RunLogSessionManager(_store);
    _sessionManager.RestoreActiveSession();
    _captureService = new RunLogCaptureService();
    _core = new RunLoggingControllerCore(_sessionManager, _captureService);
    _module = new RunLoggingModule(
        services.EventBus,
        _sessionManager,
        _core,
        () => CombatReplayRuntime.Instance?.HasPendingPersistence == true,
        EnsureActiveRunFromGame,
        battleCatalog.AttachToRun,
        buildRunLogAbandonment: RunLoggingGameDataReader.BuildRunLogAbandonment
    );
    _module.Start();
    BppLog.Info(
        "RunLoggingController",
        $"Initialized run logging database: {services.Paths.RunLogDatabasePath}"
    );
}
```

注意：`RunLoggingModule.RunLoggingModule` 的构造里它读 `BppRuntimeHost.RunContext` 的 4 处静态还需要 Task 11 处理，本 Task 不动。

- [ ] **Step 8.2: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`（Plugin.cs 还未调 Initialize，运行时会失败——属 Task 12 收口前预期状态）

- [ ] **Step 8.3: 提交**

```bash
git add Game/RunLogging/RunLoggingController.cs
git commit -m "RunLoggingController: inject IBppServices via Initialize"
```

---

### Task 9: 迁移 `RunUploadController` (MonoBehaviour)

**Files:**
- Modify: `Game/RunLogging/Upload/RunUploadController.cs`

**Current static accesses**（grep 锁定）：
- `:30` `BppRuntimeHost.Paths.RunLogDatabasePath`
- `:31` `BppRuntimeHost.Paths.CombatReplayDirectoryPath`
- `:63` `BppRuntimeHost.EventBus.Subscribe<RunLifecycleChanged>`
- `:67` `BppRuntimeHost.EventBus.Subscribe<CombatReplayPersistenceDrained>`
- `:89` `BppRuntimeHost.RunContext.IsInGameRun`
- `:123` `BppRuntimeHost.RunContext.IsInGameRun`

- [ ] **Step 9.1: 施加 Initialize 模式**

类体调整：

```csharp
private IBppServices? _services;

private void Awake()
{
    // 等待 Initialize() — 原 Awake 体已迁出
}

public void Initialize(IBppServices services)
{
    _services = services ?? throw new ArgumentNullException(nameof(services));
    InitializeCore();
}

private void InitializeCore()
{
    var services = _services!;
    // 原 Awake 体在此；所有 BppRuntimeHost.X 改用 services.X
}
```

逐行替换：
- `:30` `BppRuntimeHost.Paths.RunLogDatabasePath` → `services.Paths.RunLogDatabasePath`
- `:31` `BppRuntimeHost.Paths.CombatReplayDirectoryPath` → `services.Paths.CombatReplayDirectoryPath`
- `:63` `BppRuntimeHost.EventBus.Subscribe<RunLifecycleChanged>` → `services.EventBus.Subscribe<RunLifecycleChanged>`
- `:67` `BppRuntimeHost.EventBus.Subscribe<CombatReplayPersistenceDrained>` → `services.EventBus.Subscribe<CombatReplayPersistenceDrained>`
- `:89, :123`（`Update`/私有方法中）`BppRuntimeHost.RunContext.IsInGameRun` → `_services!.RunContext.IsInGameRun`（类型成员内改用字段 `_services!`，而非局部 `services`）

删除 `using BazaarPlusPlus.Core.Runtime;` 的若不再使用的成员（保留以引用 `IBppServices`）。

- [ ] **Step 9.2: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Game/RunLogging/Upload/RunUploadController.cs
git commit -m "RunUploadController: inject IBppServices via Initialize"
```

---

### Task 10: 迁移 `EndOfRunScreenshotController` (MonoBehaviour)

**Files:**
- Modify: `Game/Screenshots/EndOfRunScreenshotController.cs`

**Current static accesses**：
- `:44` `BppRuntimeHost.Paths.ScreenshotsDirectoryPath`
- `:45` `BppRuntimeHost.Paths.RunLogDatabasePath`
- `:63` `BppRuntimeHost.EventBus.Subscribe<RunInitializedObserved>`
- `:293` `BppRuntimeHost.RunContext.CurrentServerRunId`

- [ ] **Step 10.1: 施加 Initialize 模式**

类体调整：

```csharp
private IBppServices? _services;

private void Awake()
{
    // 等待 Initialize()
}

public void Initialize(IBppServices services)
{
    _services = services ?? throw new ArgumentNullException(nameof(services));
    InitializeCore();
}

private void InitializeCore()
{
    var services = _services!;
    // 原 Awake 体在此
}
```

逐行替换：
- `:44` `BppRuntimeHost.Paths.ScreenshotsDirectoryPath` → `services.Paths.ScreenshotsDirectoryPath`
- `:45` `BppRuntimeHost.Paths.RunLogDatabasePath` → `services.Paths.RunLogDatabasePath`
- `:63` `BppRuntimeHost.EventBus.Subscribe<RunInitializedObserved>` → `services.EventBus.Subscribe<RunInitializedObserved>`
- `:293`（私有方法中）`BppRuntimeHost.RunContext.CurrentServerRunId` → `_services!.RunContext.CurrentServerRunId`

- [ ] **Step 10.2: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Game/Screenshots/EndOfRunScreenshotController.cs
git commit -m "EndOfRunScreenshotController: inject IBppServices via Initialize"
```

---

### Task 11: 迁移 `CombatReplayRuntime` (MonoBehaviour, 最大)

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`（及其 partial 如 `.Bootstrap.cs`/`.Portrait.cs`/`.Warmup.cs` 有静态访问时）

**Current static accesses**（主文件）：
- `:76` `BppRuntimeHost.Paths.RunLogDatabasePath`
- `:79` `BppRuntimeHost.Paths.CombatReplayDirectoryPath`
- `:135` `BppRuntimeHost.RunContext.IsInGameRun`
- `:188` `BppRuntimeHost.RunContext.CurrentServerRunId`
- `:218` `BppRuntimeHost.EventBus.Publish(new PvpBattleRecorded ...)`
- `:228` `BppRuntimeHost.EventBus.Publish(new CombatReplayPersistenceDrained())`
- `:341` `BppRuntimeHost.RunLifecycle.RefreshRunStateFromCurrentState()` — 这是 `RunLifecycleModule`，不在 `IBppServices` 里

- [ ] **Step 11.1: `Initialize` 签名**

`CombatReplayRuntime` 需要 `IBppServices` **和** `RunLifecycleModule`（因为 §Q3 决策后者不入 `IBppServices`）：

```csharp
public void Initialize(IBppServices services, RunLifecycleModule runLifecycle)
{
    _services = services ?? throw new ArgumentNullException(nameof(services));
    _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));
    InitializeCore();
}
```

并新增字段：
```csharp
private IBppServices? _services;
private RunLifecycleModule? _runLifecycle;
```

- [ ] **Step 11.2: 替换 6 处 `BppRuntimeHost.X` 调用**

将 `:76, :79, :135, :188, :218, :228` 的 `BppRuntimeHost.X` 替换为 `_services!.X`。
将 `:341` 的 `BppRuntimeHost.RunLifecycle.RefreshRunStateFromCurrentState()` 替换为 `_runLifecycle!.RefreshRunStateFromCurrentState()`。

- [ ] **Step 11.3: 检查 partial files**

Run: `grep -n "BppRuntimeHost" Game/CombatReplay/CombatReplayRuntime.*.cs`
Expected: 若有命中，在各 partial 中沿用 `_services!` 字段访问（字段在主文件声明即可被 partial 共享）。

- [ ] **Step 11.4: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Game/CombatReplay/CombatReplayRuntime.cs Game/CombatReplay/CombatReplayRuntime.*.cs
git commit -m "CombatReplayRuntime: inject IBppServices and RunLifecycleModule"
```

---

### Task 12: 迁移 `MonsterPreviewItemBoardRuntime` (MonoBehaviour)

**Files:**
- Modify: `Game/MonsterPreview/MonsterPreviewItemBoardRuntime.cs`

**Current static accesses**:
- `:188` `BppRuntimeHost.Config.ItemBoardAnchoredPositionConfig?.Value`

- [ ] **Step 12.1: 施加 Initialize 模式**

类体调整：

```csharp
private IBppServices? _services;

private void Awake()
{
    // 等待 Initialize()
}

public void Initialize(IBppServices services)
{
    _services = services ?? throw new ArgumentNullException(nameof(services));
    InitializeCore();
}

private void InitializeCore()
{
    // 原 Awake 体（若有需 services 的部分）
}
```

逐行替换：
- `:188`（私有方法中）`BppRuntimeHost.Config.ItemBoardAnchoredPositionConfig?.Value` → `_services!.Config.ItemBoardAnchoredPositionConfig?.Value`

若当前 `Awake()` 没有依赖 `BppRuntimeHost`，保留原 Awake 逻辑不动，`Initialize()` 仅保存 services。

- [ ] **Step 12.2: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Game/MonsterPreview/MonsterPreviewItemBoardRuntime.cs
git commit -m "MonsterPreviewItemBoardRuntime: inject IBppServices via Initialize"
```

---

### Task 13: 迁移 `CombatStatusBar` (MonoBehaviour + partials)

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`（主 partial）
- Modify: `Game/CombatStatusBar/CombatStatusBar.State.cs`
- Modify: `Game/CombatStatusBar/CombatStatusBar.Config.cs`

**Current static accesses**：
- `CombatStatusBar.State.cs:82` `BppRuntimeHost.RunContext.IsInGameRun`
- `CombatStatusBar.Config.cs:18, :27, :32, :37, :44` 全部读 `BppRuntimeHost.Config.XxxConfig`

- [ ] **Step 13.1: 在主 partial 加 `Initialize`**

Modify `CombatStatusBar.cs`：类体加 `_services` 字段与 `Initialize(IBppServices)` 方法。

- [ ] **Step 13.2: 替换 partials 中的静态访问**

两个 partial 中所有 `BppRuntimeHost.X` → `_services!.X`。

注意：`CombatStatusBar.Config.cs:37, :44` 是读回可变 Config 对象，不只是读 `.Value`；这些依然通过 `_services.Config.XxxConfig` 解析。

- [ ] **Step 13.3: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Game/CombatStatusBar/
git commit -m "CombatStatusBar: inject IBppServices across partials"
```

---

### Task 14: 迁移静态工具类（Install 模式）

**5 个静态类需要 `Install`**：
| 文件 | 当前静态访问 | Install 签名 |
|---|---|---|
| `Infrastructure/CardsJson/CardsJsonCache.cs:43` | `BppRuntimeHost.Paths.CardsJsonPath` | `Install(IPathService paths)` |
| `Game/LegendaryPosition/LegendaryPositionDisplayFormatter.cs:12` | `BppRuntimeHost.Config.LegendaryPositionDisplayModeConfig?.Value` | `Install(IBppConfig config)` |
| `Game/Settings/BppChineseLocalization.cs:462` | `BppRuntimeHost.Config.ChineseLocaleModeConfig?.Value` | `Install(IBppConfig config)` |
| `Game/Settings/BppSettingsDockCatalog.cs` (8 处，均 `BppRuntimeHost.Config.*`) | 如上 | `Install(IBppConfig config)` |
| `Game/RunLogging/RunLoggingGameDataReader.cs:25, :78` | `BppRuntimeHost.RunContext.*` | `Install(IRunContext runContext)` |

**Install pattern（示范）**：

```csharp
internal static class X
{
    private static IBppConfig? _config;

    public static void Install(IBppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    private static IBppConfig Config =>
        _config ?? throw new InvalidOperationException($"{nameof(X)}.Install must be called at startup.");
    // 原 BppRuntimeHost.Config.XxxConfig 改读 Config.XxxConfig
}
```

- [ ] **Step 14.1: 改造 `Infrastructure/CardsJson/CardsJsonCache.cs`**

类体头部加：
```csharp
private static IPathService? _paths;
public static void Install(IPathService paths) =>
    _paths = paths ?? throw new ArgumentNullException(nameof(paths));
private static IPathService Paths =>
    _paths ?? throw new InvalidOperationException("CardsJsonCache.Install must be called at startup.");
```
- 删 `using BazaarPlusPlus.Core.Runtime;`；加 `using BazaarPlusPlus.Core.Paths;`
- `:43` `BppRuntimeHost.Paths.CardsJsonPath` → `Paths.CardsJsonPath`

- [ ] **Step 14.2: 改造 `Game/LegendaryPosition/LegendaryPositionDisplayFormatter.cs`**

类体头部加：
```csharp
private static IBppConfig? _config;
public static void Install(IBppConfig config) =>
    _config = config ?? throw new ArgumentNullException(nameof(config));
private static IBppConfig Config =>
    _config ?? throw new InvalidOperationException("LegendaryPositionDisplayFormatter.Install must be called at startup.");
```
- 删 `using BazaarPlusPlus.Core.Runtime;`；加 `using BazaarPlusPlus.Core.Config;`
- `:12` `BppRuntimeHost.Config.LegendaryPositionDisplayModeConfig?.Value` → `Config.LegendaryPositionDisplayModeConfig?.Value`

- [ ] **Step 14.3: 改造 `Game/Settings/BppChineseLocalization.cs`**

类体头部加（与 14.2 相同的 `_config`/`Install`/`Config` 三件套）：
```csharp
private static IBppConfig? _config;
public static void Install(IBppConfig config) =>
    _config = config ?? throw new ArgumentNullException(nameof(config));
private static IBppConfig Config =>
    _config ?? throw new InvalidOperationException("BppChineseLocalization.Install must be called at startup.");
```
- using 同 14.2
- `:462` `BppRuntimeHost.Config.ChineseLocaleModeConfig?.Value` → `Config.ChineseLocaleModeConfig?.Value`

- [ ] **Step 14.4: 改造 `Game/Settings/BppSettingsDockCatalog.cs`**

类体头部加（同 14.2 三件套）：
```csharp
private static IBppConfig? _config;
public static void Install(IBppConfig config) =>
    _config = config ?? throw new ArgumentNullException(nameof(config));
private static IBppConfig Config =>
    _config ?? throw new InvalidOperationException("BppSettingsDockCatalog.Install must be called at startup.");
```
- using 同 14.2
- 替换 8 处：`:73, :93, :100, :105, :117, :123, :137, :143` 全部 `BppRuntimeHost.Config` → `Config`

注意：`BppSettingsDockCatalog` 的顶层 `Definitions` 字段是 collection initializer，其中包含对 `ResolveXxxStatus` / `ReadXxx` 方法的委托引用。这些委托方法内读 `BppRuntimeHost.Config` ——被委托存储后仍能正确读最新的 `Config`。但 `Definitions` 本身在静态构造期被求值。验证：该 collection initializer 是否在构造时直接触发 `BppRuntimeHost.Config` 调用（lambda vs. 直接方法引用）。若是直接方法引用（`ReadXxx` 是一个方法组），则 OK——延迟到调用时解析 Config。若是立即求值，需拆成 lazy。

Run 确认：
```bash
grep -n "BppRuntimeHost" Game/Settings/BppSettingsDockCatalog.cs
```
所有命中都应位于方法体内，而非 `Definitions` initializer 里。

- [ ] **Step 14.5: 改造 `Game/RunLogging/RunLoggingGameDataReader.cs`**

类体头部加：
```csharp
private static IRunContext? _runContext;
public static void Install(IRunContext runContext) =>
    _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
private static IRunContext RunContext =>
    _runContext ?? throw new InvalidOperationException("RunLoggingGameDataReader.Install must be called at startup.");
```
- 删 `using BazaarPlusPlus.Core.Runtime;`；加 `using BazaarPlusPlus.Core.RunContext;`
- `:25` `BppRuntimeHost.RunContext.CurrentServerRunId` → `RunContext.CurrentServerRunId`
- `:78` `BppRuntimeHost.RunContext.LastRunExitKind` → `RunContext.LastRunExitKind`

- [ ] **Step 14.6: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Infrastructure/CardsJson/CardsJsonCache.cs Game/LegendaryPosition/ Game/Settings/BppChineseLocalization.cs Game/Settings/BppSettingsDockCatalog.cs Game/RunLogging/RunLoggingGameDataReader.cs
git commit -m "Static utility classes: switch to Install-based dependency injection"
```

---

### Task 15: 迁移 `RunLoggingModule` (非 MB 类，构造注入)

**Files:**
- Modify: `Game/RunLogging/RunLoggingModule.cs`

**Current static accesses**:
- `:131, :148, :184, :226, :368` 全部读 `BppRuntimeHost.RunContext.*`

- [ ] **Step 15.1: 构造函数加 `IRunContext` 参数**

Modify `RunLoggingModule` 构造函数：在现有 `IBppEventBus eventBus` 之后加 `IRunContext runContext`。新增私有字段 `_runContext`。

- [ ] **Step 15.2: 替换 5 处静态调用**

所有 `BppRuntimeHost.RunContext.X` → `_runContext.X`。

- [ ] **Step 15.3: 更新调用方**

`RunLoggingController.InitializeCore`（Task 8 已改）中的 `new RunLoggingModule(services.EventBus, ...)` 改为 `new RunLoggingModule(services.EventBus, services.RunContext, ...)`。

- [ ] **Step 15.4: 编译 + 提交**

```bash
dotnet build BazaarPlusPlus.csproj -c Debug
git add Game/RunLogging/RunLoggingModule.cs Game/RunLogging/RunLoggingController.cs
git commit -m "RunLoggingModule: ctor-inject IRunContext"
```

---

### Task 16: 重写 `Plugin.cs` 启动顺序

**Files:**
- Modify: `Plugin.cs`

新的启动顺序（见 spec §4）：
```
1. CreatePluginConfigFile
2. _composition = new BppComposition(Logger, configFile, () => combatReplayRuntime)
3. BppLog.Install(_composition.Services.Logger)
4. BppPatchHost.Install(_composition.Services)
5. _harmony.PatchAll()
6. combatReplayRuntime = AddComponent<CombatReplayRuntime>()
   combatReplayRuntime.Initialize(services, _composition.RunLifecycle)
7. _composition.Start()
8. Install 5 个静态工具类
9. AttachGameModules (AddComponent + Initialize for 其余 MB)
10. BuildIdentityAndOnlineServices
```

- [ ] **Step 16.1: 改写 Awake()**

替换 `Plugin.cs` 的 `Awake()` 和相关 helper 方法：

```csharp
protected virtual void Awake()
{
    try
    {
        BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");
        BppPluginVersion.Initialize(Info.Location);

        var configFile = CreatePluginConfigFile();

        CombatReplayRuntime? combatReplayRuntime = null;
        _composition = new BppComposition(Logger, configFile, () => combatReplayRuntime);

        var services = _composition.Services;
        BppLog.Install(services.Logger);
        BppPatchHost.Install(services);

        InstallStaticUtilities(services);

        ApplyHarmonyPatches();

        combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
        combatReplayRuntime.Initialize(services, _composition.RunLifecycle);

        _composition.Start();

        BuildIdentityAndOnlineServices(services);
        AttachRuntimeComponents(services, combatReplayRuntime);
        BppLog.Info("Plugin", "Plugin initialization completed");
    }
    catch (Exception ex)
    {
        BppLog.Error("Plugin", "Plugin initialization failed", ex);
        CleanupFailedInitialization();
        throw;
    }
}

private static void InstallStaticUtilities(IBppServices services)
{
    CardsJsonCache.Install(services.Paths);
    LegendaryPositionDisplayFormatter.Install(services.Config);
    BppChineseLocalization.Install(services.Config);
    BppSettingsDockCatalog.Install(services.Config);
    RunLoggingGameDataReader.Install(services.RunContext);
}
```

- [ ] **Step 16.2: 更新 `AttachRuntimeComponents` 调用 Initialize**

每个 `AddComponent<T>()` 后紧跟 `.Initialize(services)`（或特定签名）：

```csharp
private void AttachRuntimeComponents(IBppServices services, CombatReplayRuntime combatReplayRuntime)
{
    var runLogging = gameObject.AddComponent<RunLoggingController>();
    runLogging.Initialize(services);

    var runUpload = gameObject.AddComponent<RunUploadController>();
    runUpload.Initialize(services);

    AddConfiguredPlayerObservationController();
    AddConfiguredHistoryPanel(services, combatReplayRuntime);

    var statusBar = gameObject.AddComponent<CombatStatusBar>();
    statusBar.Initialize(services);

    gameObject.AddComponent<MonsterPreviewWarmupController>();
    gameObject.AddComponent<CardSetPreviewRuntime>();

    var itemBoardRuntime = gameObject.AddComponent<MonsterPreviewItemBoardRuntime>();
    itemBoardRuntime.Initialize(services);

    var screenshot = gameObject.AddComponent<EndOfRunScreenshotController>();
    screenshot.Initialize(services);

    AddConfiguredTooltipModifierRefreshController(services.Config);
}
```

- [ ] **Step 16.3: 更新 `OnDestroy` 顺序**

改写 `OnDestroy`：
```csharp
protected virtual void OnDestroy()
{
    try
    {
        DetachRuntimeComponents();
        _composition?.Dispose();
        _composition = null;
        DisposeIdentityAndOnlineServices();
        UnpatchHarmony();
    }
    finally
    {
        BppLog.Flush();
        BppPatchHost.Reset();
    }
}
```

`BppPatchHost.Reset()` 放在 Flush 之后，确保 MB 的 `OnDestroy` 之后再清空（参考 spec §6 风险表）。

- [ ] **Step 16.4: 替换成员字段**

将 `private BppRuntimeHost? _runtimeHost;` 改为 `private BppComposition? _composition;`。删除 `CreateAndStartRuntime`、`StopRuntimeHost` 等围绕 `_runtimeHost` 的 helper。

- [ ] **Step 16.5: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 16.6: 手测冒烟**

1. 启动游戏，BepInEx 控制台：`Plugin initialization completed`
2. 打开 History 面板：列表渲染
3. 打一局 PVE：Run 结束后 DB 有新行
4. 打一局 PVP：CombatReplay 文件生成
5. 关闭游戏：`BepInEx/LogOutput.log` 无新增 `NullReference`/`Error`

- [ ] **Step 16.7: 提交**

```bash
git add Plugin.cs
git commit -m "Plugin: wire BppComposition and ordered IBppServices init"
```

---

## Commit 3 Cleanup

### Task 17: 删除 `BppRuntimeHost`

**Files:**
- Delete: `Core/Runtime/BppRuntimeHost.cs`

- [ ] **Step 17.1: 删除文件**

```bash
rm Core/Runtime/BppRuntimeHost.cs
```

- [ ] **Step 17.2: 编译**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug`
Expected: `Build succeeded.`（若报 "The type or namespace name 'BppRuntimeHost' could not be found"，说明 Commit 2 遗漏；按错误定位并修正）

- [ ] **Step 17.3: 运行负向 grep**

```bash
grep -rn "BppRuntimeHost" Core Game Patches Infrastructure Plugin.cs BppComposition.cs
```
Expected: 无输出

```bash
grep -rn "^using BazaarPlusPlus\.Game" Core/
```
Expected: 无输出

- [ ] **Step 17.4: 完整 BuildAll**

Run: `dotnet build -c Debug`（根 solution / BuildAll target）
Expected: `Build succeeded.`（项目 `.rules` 要求跨包重构跑 BuildAll）

- [ ] **Step 17.5: 运行所有测试项目**

Run:
```bash
dotnet test tests/CombatReplayRecording.Tests/
dotnet test tests/RunLoggingCapture.Tests/
# 如有其他 tests/ 子项目一并跑
```
Expected: 全绿

- [ ] **Step 17.6: 最终手测**

按 spec §7 手测 6 项（游戏启动/PVE/PVP/History/Settings/退出日志）全通过。

- [ ] **Step 17.7: 提交**

```bash
git add -A
git commit -m "Remove BppRuntimeHost service locator"
```

---

### Task 18: 验收

- [ ] **Step 18.1: 过 spec §9 成功判定清单**

检查：
- `grep -r "BppRuntimeHost\." Core Game Patches Infrastructure` → 0
- `grep -r "^using BazaarPlusPlus.Game" Core/` → 0
- `dotnet build` 通过
- 既有测试绿
- 6 项手测全过
- `OnDestroy` 退出日志无新增 Error

- [ ] **Step 18.2: 若所有项通过，准备 PR**

使用 `.rules` Pull Request Hygiene 风格：
- 标题：`Decouple Core from Game layer via explicit IBppServices injection`（imperative，无 conventional prefix，无尾标点）
- Body 末尾含 `Release Notes:` + `- Improved mod architecture and maintainability`

---

## Self-Review Summary

本 plan 覆盖 spec §1–§9 全部要求。关键对齐检查：
- spec §3.1 `IBppServices` 6 成员 — Task 1
- spec §3.2 `BppComposition` — Task 5 + Task 16
- spec §3.3 `BppPatchHost` — Task 4 + Task 7
- spec §3.4 反向依赖 1/2/3 — Task 17（#1）、Task 2（#2）、Task 3（#3）
- spec §4 Plugin.cs 启动顺序 — Task 16
- spec §5 文件级清单 — Task 1-17 覆盖
- spec §6 风险：MB 在 Awake 读依赖 — 通过 Task 8-13 的 InitializeCore 模式缓解；OnDestroy 顺序 — Task 16.3；BppPatchHost Install 在 PatchAll 前 — Task 16.1
- spec §7 验证 — Task 17.3-17.6 + Task 18.1
- spec §8 commit 拆分 — Commit 1 (Task 1-5), Commit 2 (Task 6-16), Commit 3 (Task 17-18)

**取舍说明**：
- 对于 Commit 2 的 MonoBehaviour/Patch 迁移，没有为每一行给出 before/after 代码——这些都是机械的 `BppRuntimeHost.X → _services.X` 或 `BppPatchHost.Services.X` 替换，Task 8 作为 pattern-setting 示例写出完整代码。其他 MB 任务引用同 pattern + 列出具体行号清单，避免粘贴几千行。
- 对于 Task 14 的 5 个静态工具类，给出通用 Install pattern 代码 + 5 个文件清单 + 各自依赖，engineer 按 pattern 套用。
