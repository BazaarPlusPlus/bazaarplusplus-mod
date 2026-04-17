# Core/Game 分层与依赖注入重构设计

**日期**: 2026-04-17
**范围**: `bazaarplusplus-mod/`（不含 `ModCFServerV3/`、`decompiled/`）
**PR 策略**: 一把梭（单 PR，3 个逻辑 commit）

## 1. 背景

架构评估发现两个根因问题：

1. **Core 层反向依赖 Game 层**（3 处 `using BazaarPlusPlus.Game.*` 出现在 `Core/` 下）
2. **`BppRuntimeHost.Current` ServiceLocator 反模式**：7 个静态访问器，被 21 个文件、54 处隐式消费

这两个问题互相交织：静态访问器让消费方不需要经过依赖图，导致反向依赖不易察觉，也让单元测试与生命周期管理变脆。解决它们是后续拆分三个巨型类（`HistoryPanelRepository` 1523 行、`HistoryPanelUiToolkitView` 1725 行、`CombatReplayRuntime` 701 行）的前置工作。

## 2. 目标与非目标

### 目标

- 消除 Core→Game 反向 `using`（3 处）
- 删除 `BppRuntimeHost` 的 7 个静态访问器及 `DetachedServices` 兜底
- 所有 21 个消费方切到 `IBppServices`
  - MonoBehaviour 用 `Initialize(IBppServices, ...)` 显式注入
  - Harmony Patch（5 个静态类）用 `BppPatchHost.Services`，静态残留被限定在 `Patches/` 目录
- 新建组合根 `BppComposition`，作为全项目**唯一**允许同时引用 Core 与 Game 的位置

### 非目标

- 不拆分 `HistoryPanelRepository` / `CombatReplayRuntime` / `HistoryPanelUiToolkitView`
- 不调整 Patch 层塞入的 UI 业务逻辑
- 不动 `ModCFServerV3`
- 不引入外部 DI 框架（VContainer/Zenject）

## 3. 架构

### 3.1 `IBppServices` 接口

`Core/Runtime/IBppServices.cs`（新增）：

```csharp
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

现有 `BppRuntimeServices` record 扩展第 6 个成员 `Logger`，实现此接口。

**不加入** `RunLifecycleModule`——它只有 1 个消费方（`CombatReplayRuntime:341`），作为 `Initialize` 参数直接传入更干净。

### 3.2 组合根 `BppComposition`

`/BppComposition.cs`（新增，根命名空间）——全项目唯一同时 `using Core.*` 和 `Game.*` 的位置：

```csharp
internal sealed class BppComposition : IDisposable
{
    public IBppServices Services { get; }
    public RunLifecycleModule RunLifecycle { get; }
    public CombatReplayRuntime CombatReplayRuntime { get; }

    public static BppComposition BuildAndStart(GameObject host, ManualLogSource logger, ConfigFile configFile);
    public void AttachGameModules(GameObject host); // AddComponent + Initialize
    public void Dispose();                           // 原 BppRuntimeHost.Stop 逻辑
}
```

### 3.3 Patch 层静态入口

`Patches/BppPatchHost.cs`（新增）：

```csharp
internal static class BppPatchHost
{
    public static IBppServices Services { get; private set; } = null!;
    public static void Install(IBppServices services) => Services = services;
}
```

`Plugin.cs` **必须在** `_harmony.PatchAll()` 之前调用 `BppPatchHost.Install(services)`。

### 3.4 Core→Game 反向依赖的 3 处修复

| # | 当前位置 | 当前 | 修复 |
|---|---|---|---|
| 1 | `Core/Runtime/BppRuntimeHost.cs:11-13` | Core 里 new Game Module | **删除 `BppRuntimeHost`**，Module 构造迁入 `BppComposition` |
| 2 | `Core/Paths/BppPathService.cs:2` | 读 `Game.RunLogging.Persistence.Sqlite.RunLogSqliteSchema.DatabaseFileName`（= `"bazaarplusplus.db"`）| 新增 `Core/Paths/BppPathConstants.RunLogDatabaseFileName = "bazaarplusplus.db"`，Game 侧 `RunLogSqliteSchema` 反向引用 Core 常量 |
| 3 | `Core/Events/PvpBattleRecorded.cs:2` | 持有 `Game.PvpBattles.PvpBattleManifest` 具体类 | **修正方案**：将 `PvpBattleRecorded` **事件类型本身移动到 `Game/PvpBattles/`**（而非移动整个 DTO 簇）。原因：`PvpBattleManifest` 的传递依赖图包含 `PvpBattleCardSetCapture` → `CombatReplayCardSnapshot`（跨 Game 模块），迁移整簇 DTO 到 Core 会跨越多个 Game 边界、得不偿失。事件类型与其 payload 同住更内聚。只影响 2 个订阅方（`RunLoggingModule`、自身 Publish 点在 `CombatReplayRuntime`），都已 `using Game.PvpBattles` |

Q3 决策：保持 `RunLifecycleModule` 不进 `IBppServices`（其唯一消费方 `CombatReplayRuntime` 走显式 `Initialize` 参数）。

## 4. Plugin.cs 启动顺序

```
1. CreatePluginConfigFile()
2. _composition = BppComposition.BuildAndStart(gameObject, Logger, configFile)
3. BppLog.Install(_composition.Services.Logger)        // 先让日志可用
4. BppPatchHost.Install(_composition.Services)         // 必须在 PatchAll 前
5. _harmony.PatchAll()
6. _composition.AttachGameModules(gameObject)          // AddComponent + Initialize
7. BuildIdentityAndOnlineServices(_composition.Services)
```

`OnDestroy` 对称：DetachComponents → `_composition.Dispose()` → DisposeIdentity → UnpatchHarmony → `BppLog.Flush()`。

## 5. 文件级改动清单

### 新增（4）

- `BppComposition.cs`（根）
- `Core/Runtime/IBppServices.cs`
- `Core/Paths/BppPathConstants.cs`
- `Patches/BppPatchHost.cs`

### 删除（1）

- `Core/Runtime/BppRuntimeHost.cs`

### 移动（1）

- `Core/Events/PvpBattleRecorded.cs` → `Game/PvpBattles/PvpBattleRecorded.cs`（仅事件类型本身，不动 DTO 簇）

### 实质修改（约 12）

- `Core/Runtime/BppRuntimeServices.cs`：加 `Logger`，实现 `IBppServices`
- `Core/Paths/BppPathService.cs`：改读 `BppPathConstants`
- `Core/Events/PvpBattleRecorded.cs`：更新引用
- `Plugin.cs`：按 §4 启动顺序重写
- `Infrastructure/BppLog.cs`：加 `Install(ManualLogSource)`，删 `BppRuntimeHost.Logger` 读
- `Infrastructure/CardsJson/CardsJsonCache.cs`：接收 `IPathService` 入参
- 19 个 MonoBehaviour：新增 `Initialize(...)`，删除静态调用，字段改存注入的依赖
  - `CombatReplayRuntime` 额外接收 `RunLifecycleModule`
- 5 个 Harmony Patch：`BppRuntimeHost.X` → `BppPatchHost.Services.X`
- Game 侧 `RunLogSqliteSchema.cs`：反向读 `Core/Paths/BppPathConstants`

### 机械 `using` 更新

- 2 个 PvpBattleRecorded 订阅方同步更新 `using`
- 分布在 21 文件的 54 处静态调用（`BppRuntimeHost.X` 替换）

**总文件影响**: ~35

## 6. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Harmony `PatchAll` 在 `BppPatchHost.Install` 之前执行 → Patch 访问 null Services | Plugin.cs 明确顺序 + 在 `BppPatchHost.Services` getter 加早失败断言 |
| MessagePack DTO 在 Mono 运行时要求 `public` 可见性（项目 `.rules` 第 15 条）| PvpBattle DTO 簇留在 Game/PvpBattles/ 原位，可见性不变 |
| MonoBehaviour `Awake()` 先于 `Initialize()` 被调 → 字段为 null | 规则：MB 禁止在 `Awake` 读注入依赖，迁到 `Start`；或确保 `AddComponent` 同一帧紧跟 `Initialize`。实施时 grep 所有 19 个 MB 的 `Awake` 检查 |
| `OnDestroy` teardown 顺序：MB 的 `OnDestroy` 里调用已被置 null 的 `BppPatchHost.Services` | 先 `DetachRuntimeComponents` 再 `_composition.Dispose()`；保留 `BppPatchHost.Services` 直到所有 MB 被销毁后再清空 |
| `tests/` 下测试项目引用 `PvpBattleRecorded`？| grep 确认；若有同步更新 `using` |

## 7. 验证计划

### 自动

- `dotnet build`（`BuildAll` target）通过
- 既有测试全绿：`tests/CombatReplayRecording.Tests`、`tests/RunLoggingCapture.Tests` 等
- 负向 grep（可作为 lint）：
  - `grep -rn "BppRuntimeHost\." Core Game Patches Infrastructure` → 0 结果
  - `grep -rn "^using BazaarPlusPlus.Game" Core/` → 0 结果

### 手测（游戏内冒烟）

1. 启动 BepInEx，插件加载无 `NullReference`/`Error` 日志
2. 打一局 PVE → Run 完成 → 检查 RunLog DB 写入
3. 打一局 PVP → Battle 完成 → 检查 CombatReplay 文件 + PvpBattleManifest 序列化
4. 打开 History 面板 → 过往 Run 列表显示正常
5. 打开 Settings 面板 → Keybind 行正常渲染
6. **关闭游戏 → 打开 `BepInEx/LogOutput.log`，搜 `Error`/`Exception`/`NullReference`，无新增错误**

### 不加新测试

遵循 `.rules`：重构无新增行为，不为覆盖率造测试。

## 8. 实施顺序（单 PR 内 3 commit）

**Commit 1 · 脚手架 + 反向依赖修复**
- 新增 4 个文件（`IBppServices`/`BppComposition`/`BppPathConstants`/`BppPatchHost`）
- 移动 `PvpBattleRecorded` 到 `Game/PvpBattles/`；更新 2 处 `using`
- 反向修正 Game 的 `RunLogSqliteSchema.DatabaseFileName` 引用 Core 常量
- `BppRuntimeHost` **仍在**，静态访问器仍可用
- **期望**: 编译通过，行为不变

**Commit 2 · 消费端切换**
- 19 个 MonoBehaviour 加 `Initialize`，切到本地字段
- 5 个 Harmony Patch 切到 `BppPatchHost.Services`
- `BppLog` / `CardsJsonCache` 注入改造
- `Plugin.cs` 按 §4 启动顺序重写
- **期望**: 编译通过，手测通过

**Commit 3 · 清理**
- 删除 `Core/Runtime/BppRuntimeHost.cs` + `DetachedServices`
- **期望**: 负向 grep 通过，手测再跑一遍

## 9. 成功判定

- [ ] `grep -r "BppRuntimeHost\." Core Game Patches Infrastructure` 零结果
- [ ] `grep -r "^using BazaarPlusPlus.Game" Core/` 零结果
- [ ] `dotnet build`（BuildAll）通过
- [ ] 既有测试项目全绿
- [ ] §7 手测 6 项全过
- [ ] `OnDestroy` 退出日志无新增 Error/Exception

## 10. 后续（不在本 spec）

完成后可进入下一轮：

- TOP 3：拆分 `HistoryPanelRepository` / `CombatReplayRuntime` / `HistoryPanelUiToolkitView`
- TOP 4：Patch 层 UI 业务外移
- TOP 5：`BppLog` 进一步抽象为 `ILogger` 接口
