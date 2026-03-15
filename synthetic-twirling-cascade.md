# BazaarPlusPlus：v1.1.0 → HEAD 代码审查与修复方案

---

## 一、RunLogging — Session 生命周期

### 1. Session 串局 + run_resumed 事件丢失（CRITICAL）

**问题：**
`RunLoggingController.EnsureActiveRunFromGame()`（L116）在 `HasActiveSession == true` 时直接短路返回，不进入 `EnsureRunStarted()`——后者是写 `run_started`/`run_resumed` 的唯一路径。结果：
- 上局崩溃后残留的 active session，新局所有事件继续写进旧 `run_id`（两局数据合并）
- 恢复的 run 缺少 `run_resumed` 事件，无法判断该 session 是否经历过中断

**修改文件：**

`Game/RunLogging/Models/RunLogSessionState.cs`
```csharp
public string? Hero { get; set; }
public string? GameMode { get; set; }
```

`Game/RunLogging/Persistence/SqliteRunLogStore.cs`（`TryResumeActiveRun` SELECT 补充）
```sql
r.hero, r.game_mode
-- 并填充到 RunLogSessionState.Hero / GameMode
```

`Game/RunLogging/RunLogSessionManager.cs:32`（`EnsureActiveSession`）
```csharp
public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
{
    if (ActiveSession != null)
    {
        if (string.Equals(ActiveSession.Hero, request.Hero, StringComparison.Ordinal)
            && string.Equals(ActiveSession.GameMode, request.GameMode, StringComparison.Ordinal))
            return ActiveSession;

        BppLog.Warn("RunLogSessionManager",
            $"Session mismatch: active={ActiveSession.Hero}/{ActiveSession.GameMode}, " +
            $"new={request.Hero}/{request.GameMode}. Abandoning stale session.");
        _store.MarkRunAbandoned(ActiveSession.RunId, new RunLogAbandonment { Reason = "session_mismatch" });
        ActiveSession = null;
    }
    ActiveSession = RestoreActiveSession() ?? _store.CreateRun(request);
    return ActiveSession;
}
```

`Game/RunLogging/RunLoggingController.cs:116`（`EnsureActiveRunFromGame` 移除短路）
```csharp
private RunLogSessionState? EnsureActiveRunFromGame()
{
    if (!GameDataReader.TryCreateRunLogCreateRequest(out var request))
        return null;
    return RequireCore().EnsureRunStarted(request); // 始终走 EnsureRunStarted
}
```

> `EnsureRunStarted()` 内：`_runStartedEventWritten == false` 时，按 `LastSeq == 0` 写 `run_started`，`LastSeq > 0` 写 `run_resumed`，同时触发 session 校验。

---

### 2. run_state_exit 硬编码 Status = "completed"（HIGH）

**问题：**
`GameDataReader.BuildRunLogCompletion()`（L218）无条件写 `Status = "completed"`。`ModState` 订阅了 `RunInterrupted`（L86）但只把 `IsInGameRun` 拨成 false，`PollRunState()` 检测到后走同一条 completion 路径。中断/掉线/回大厅全被记为"完成"，run history 失真。

**修改文件：**

`Models/ModState.cs`
```csharp
public enum RunExitKind { Normal, Interrupted }
public static RunExitKind LastRunExitKind = RunExitKind.Normal;

private static void OnRunEnded()
{
    LastRunExitKind = RunExitKind.Normal;
    SetInGameRun(false, "Run ended");
}
private static void OnRunInterrupted()
{
    LastRunExitKind = RunExitKind.Interrupted;
    SetInGameRun(false, "Run interrupted");
}
```

`Game/GameDataReader.cs:218`
```csharp
public static RunLogCompletion BuildRunLogCompletion(string reason)
{
    var status = ModState.LastRunExitKind == RunExitKind.Interrupted ? "abandoned" : "completed";
    return new RunLogCompletion { Status = status, Reason = reason, /* ... */ };
}
```

---

### 3. RunLogInferenceService 未接入调用链（MEDIUM）

**问题：**
`InferenceService` 在 `Awake()` 实例化但零调用（L33）。`PendingSelectionSeq` 写入 session（SessionManager.cs:91）但从未传给 `InferenceService`。`choice_made` 事件从不产生。

**修改文件：**

`Game/RunLogging/Models/RunLogSessionState.cs`
```csharp
public IList<RunLogOptionSnapshot>? LastSelectionOptions { get; set; }
```

`Game/RunLogging/RunLogSessionManager.cs`（`AppendEvent`，`selection_seen` 分支）
```csharp
if (string.Equals(entry.Kind, "selection_seen", StringComparison.Ordinal))
{
    session.PendingSelectionSeq = entry.Seq;
    session.LastSelectionOptions = entry.Options; // 缓存 options 供推断用
}
```

`Game/RunLogging/RunLoggingController.cs`（`PollRunState` 的 state snapshot 分支）
```csharp
if (GameDataReader.TryBuildRunLogStateSnapshot(out var stateInput))
{
    var prevPendingSeq = _sessionManager.ActiveSession?.PendingSelectionSeq;
    RequireCore().AcceptStateSnapshot(stateInput);

    var session = _sessionManager.ActiveSession;
    if (prevPendingSeq != null && session != null && _inferenceService != null)
    {
        var choiceInput = new RunLogChoiceInferenceInput
        {
            SelectionSeq = prevPendingSeq.Value,
            TransitionedAway = true,
            Options = session.LastSelectionOptions ?? new List<RunLogOptionSnapshot>(),
            ResultingInstanceIds = Data.Entities?.Keys
                .Select(k => k.ToString()).ToList() ?? new List<string>(),
        };
        var choiceEvent = _inferenceService.InferChoice(choiceInput);
        if (choiceEvent != null)
        {
            _sessionManager.AppendEvent(choiceEvent);
            session.PendingSelectionSeq = null;
            session.LastSelectionOptions = null;
        }
    }
}
```

---

## 二、RunLogging — SQLite 稳健性

### 4. OpenConnection() 连接泄漏（CRITICAL）

**问题：**
`SqliteRunLogStore.OpenConnection()`（L402）：连接 Open 后，PRAGMA `ExecuteNonQuery()` 若抛异常，连接已开启但从未 dispose，持续累积。

**修改文件：** `Game/RunLogging/Persistence/SqliteRunLogStore.cs:402`
```csharp
private SqliteConnection OpenConnection()
{
    var connection = new SqliteConnection($"Data Source={_databasePath}");
    connection.Open();
    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
    catch
    {
        connection.Dispose();
        throw;
    }
}
```

---

### 5. AppendEvent JSON 序列化异常导致 Unity Update 崩溃（HIGH）

**问题：**
`SqliteRunLogStore.AppendEvent()`（L190）在连接已开启后调用 `JsonConvert.SerializeObject()`，序列化失败的异常会上冒穿过 `RunLogSessionManager` → `RunLoggingControllerCore` → `PollRunState()`，在 Unity Update 中未被捕获，导致整个更新循环崩溃。

**修改文件：** `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
```csharp
public void AppendEvent(string runId, RunLogEvent entry)
{
    // 先序列化，失败则不开连接，异常由调用方处理
    var payloadJson = JsonConvert.SerializeObject(entry, SerializerSettings);

    using var connection = OpenConnection();
    using var command = connection.CreateCommand();
    // ... 使用 payloadJson 变量而非内联序列化
}
```

---

### 6. SQLite 命令无超时，主线程可能卡死（MEDIUM）

**问题：**
所有 SQLite `command.ExecuteNonQuery()` 没有设置 `CommandTimeout`。若数据库被其他进程锁定，命令无限阻塞，Unity 主线程冻结。

**修改文件：** `Game/RunLogging/Persistence/SqliteRunLogStore.cs`（所有 `CreateCommand()` 后）
```csharp
command.CommandTimeout = 2; // seconds
```
同时在 `RunLoggingController.PollRunState()` 的调用路径包裹 try/catch `SqliteException`，记录 error 日志后不再上抛。

---

### 7. TryResumeActiveRun 不校验 checkpoint 一致性（MEDIUM）

**问题：**
`TryResumeActiveRun()`（L44）信任 checkpoint 的 `last_seq`，但不校验该 seq 对应的事件实际存在于 `run_events`。崩溃后可能产生序号空洞。

**修改文件：** `Game/RunLogging/Persistence/SqliteRunLogStore.cs:44`
```csharp
// 读取 checkpoint last_seq 后，再查实际最大 seq
var actualMaxSeq = /* SELECT MAX(seq) FROM run_events WHERE run_id = $runId */;
var safeLastSeq = Math.Min(checkpointLastSeq, actualMaxSeq ?? 0);
// 用 safeLastSeq 填充 RunLogSessionState.LastSeq
```

---

## 三、构建与配置

### 8. Debug 构建 Windows 缺 e_sqlite3.dll（HIGH）

**问题：**
`BazaarPlusPlus.csproj` Debug target（L127-176）只处理 macOS 的 `libe_sqlite3.dylib`，没有 `WindowsSqliteNativeFile` 和 `e_sqlite3.dll` 的 Delete/Copy 步骤。Release target（L196）有。Windows 开发者 Debug 构建后，`Plugin.cs:29` 无条件初始化 SQLite store，缺少原生库直接崩溃。

**修改文件：** `BazaarPlusPlus.csproj`，在 Debug target 的 `FilesToDelete` 和 Copy 步骤补充：
```xml
<FilesToDelete Include="$(GamePath)\BepInEx\plugins\e_sqlite3.dll" />

<WindowsSqliteNativeFile
  Include="$(NuGetPackageRoot)sqlitepclraw.lib.e_sqlite3/*/runtimes/win-x64/native/e_sqlite3.dll" />

<Copy
  SourceFiles="@(WindowsSqliteNativeFile)"
  DestinationFolder="$(GamePath)\BepInEx\plugins"
  SkipUnchangedFiles="true"
  Condition="'@(WindowsSqliteNativeFile)' != ''" />
```

---

### 9. EnchantPreview.AlwaysShow 功能入口消失（MEDIUM）

**问题：**
`ModState.cs:52` 的 `EnchantPreviewAlwaysShowConfig` 和逻辑代码还在，但安装器设置页已删除，新的 native settings patch 只覆盖了 `CombatStatusBar` 和 `NameOverride`，`EnchantPreview` 没有游戏内设置桥接，只能手改 cfg 文件。

**修改文件（新建，参照现有模式）：**
- `Game/ItemEnchantPreview/EnchantPreview.SettingsMenuBridge.cs`
- `Game/ItemEnchantPreview/EnchantPreview.SettingsMenuLabel.cs`
- `Patches/Tooltips/EnchantPreviewSettingsPatch.cs`（参照 `CombatStatusBarSettingsPatch.cs`）
- `Plugin.cs`：注册新 patch

---

## 四、Settings Patches

### 10. Anchor toggle 找不到时静默失败（MEDIUM）

**问题：**
两个 patch（`CombatStatusBarSettingsPatch.cs:72-76`、`NameOverrideSettingsPatch.cs:73-77`）用 `AccessTools.Field(..., "_fastForwardFirstFight")` 定位锚点 toggle。游戏更新改名后返回 null，功能静默失效，无任何日志输出。

**修改文件：** 两个 patch 的 `GetAnchorToggle()` 方法
```csharp
private static Toggle? GetAnchorToggle(OptionsDialogController instance)
{
    var field = AccessTools.Field(typeof(OptionsDialogController), "_fastForwardFirstFight");
    if (field == null)
    {
        BppLog.Error("Settings", "Could not find _fastForwardFirstFight in OptionsDialogController");
        return null;
    }
    var toggle = field.GetValue(instance) as Toggle;
    if (toggle == null)
        BppLog.Error("Settings", "Field _fastForwardFirstFight is not a Toggle or is null");
    return toggle;
}
```

---

### 11. NameOverride UI refresh 在 toggle OFF 时无谓触发（LOW）

**问题：**
`NameOverride.SettingsMenuBridge.cs:32` 无条件调用 `_refreshUi?.Invoke()`，toggle OFF 时也触发 banner 刷新，浪费性能。

**修改文件：** `Game/NameOverride/NameOverride.SettingsMenuBridge.cs`
```csharp
_config.Value = value;
if (value) _refreshUi?.Invoke(); // 只在开启时主动刷新
```

---

## 五、已确认无问题（参考）

| 疑点 | 结论 |
|------|------|
| SQLite transaction rollback | `using` on `SqliteTransaction` 隐式回滚，数据完整性有保证 |
| `AppendEvent` session 状态先于持久化被修改 | **误判**：L75 store write 在前，L77-91 session 更新在后，顺序正确 |
| Settings 加载时不生效 | `ShouldDraw()` 每帧检查 `IsEnabled()`，无需额外初始化路径 |
| dedup `string.Equals(x, null)` | 返回 false，逻辑正确 |
| Harmony Postfix 选择 | 两个 patch 均正确 |
| UI 刷新线程安全 | toggle listener 在 Unity 主线程触发，安全 |

---

## 六、执行优先级

| 优先级 | # | Fix 简述 |
|--------|---|----------|
| P0 | 1 | Session 串局 + run_resumed（数据正确性） |
| P0 | 2 | Status 区分 completed/abandoned（数据正确性） |
| P0 | 4 | 连接泄漏（稳定性） |
| P1 | 8 | Windows Debug 缺 DLL（构建可用性） |
| P1 | 5 | JSON 先序列化（防 Update 崩溃） |
| P1 | 6 | SQLite 命令超时（防主线程卡死） |
| P2 | 3 | InferenceService 接入（choice_made 功能） |
| P2 | 7 | Checkpoint 一致性校验 |
| P2 | 9 | EnchantPreview 设置桥接（功能入口） |
| P3 | 10 | Anchor toggle 错误日志 |
| P3 | 11 | NameOverride refresh 优化 |

---

## 七、验证方式

- **#1**：正常开局 → 有 `run_started`；强杀后重启 → 有 `run_resumed`；强杀后换英雄 → 旧 session 被 `abandoned`，新 session 独立
- **#2**：中途回大厅 → DB `run_status.status = 'abandoned'`；正常完赛 → `status = 'completed'`
- **#3**：完成遭遇选择 → DB 出现 `choice_made` 事件，字段含 `inferred_from`
- **#4**：在 `OpenConnection()` 人为 throw → 无连接累积
- **#8**：Windows Debug 构建后 → 插件目录有 `e_sqlite3.dll`，游戏正常启动
