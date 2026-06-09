# LiveBuildPanel 阵容拉取与 HistoryPanel 连通性操作归位方案

Status: Implemented (2026-06-07)
Date: 2026-06-07

实现期间确认的增量：拉取成功的反馈不止 `十胜阵容已更新。`，还追加当前 corpus 的
`generatedAt`（本地时间）与规模统计（阵容数 / 英雄数），数据来自
`TenWinBuildCorpus.GeneratedAtUtc` / `BuildCount` / `HeroCount`，经
`BuildRecommendationRepository.GetCorpusSummary()` 提供。建议补充测试中的
"HistoryPanel 不再包含 FinalBuildRefresh" 源码断言未采纳（与 no-coverage-theater
约定冲突；跨面板 import 边界已有 `CoreLayeringTests` 覆盖），其余建议测试已落地。

## 背景

当前十胜阵容推荐已经由 `LiveBuildPanel` 承载，但手动拉取阵容的操作入口仍放在 `HistoryPanel`。这导致一个明显的产品边界错位：用户是在 Caps 打开的终局阵容面板里选择局内候选物品、浏览十胜推荐，却需要去历史面板触发远端阵容包刷新。

连通性检测也在 `HistoryPanel`，但 UI 现在把本地 DB 状态 chip 与远端健康检查按钮分成两行。用户看到 `DB Connected / DB 已连接` 后，自然会把旁边的服务连通性检测理解为同一组“数据是否可用”的诊断动作；把它们放在同一行是合理的。但两者不能合并为一个状态，因为代码里它们检查的是两个不同系统。

## 代码事实

- `LiveBuildPanel` 已经持有 `BuildRecommendationRepository`，并在候选物品变化时调用 `FindRecommendations(...)` 生成十胜推荐：`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:31`、`src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:202`。
- `LiveBuildPanel` 的右侧 rail 已经有候选数量、推荐状态、上一条/下一条导航，是承载“拉取阵容”按钮的自然位置：`src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:265`、`src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs:293`。
- `BuildRecommendationRepository` 明确负责加载 analyzer-v4 十胜阵容 corpus、缓存、远端刷新，并说明推荐查询只读本地 corpus、不打远端：`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs:19`。
- 手动远端刷新实际入口在 LiveBuildPanel recommendation backend：`BuildRecommendationRepository.TryRefreshFinalBuildsFromRemote(...)`，位置是 `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs:290`。
- HistoryPanel 目前只是把共享仓库刷新包进 `HistoryPanelDataService.RefreshFinalBuildsAsync(...)`：`src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelDataService.cs:177`。
- HistoryPanel UI 当前把 `Pull Builds / 拉取阵容` 按钮放在 operation rail 的 tool row：`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:316`、`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:330`。
- HistoryPanel 本地 DB chip 来自 `_dataService.IsAvailable` 与 `_dataService.DatabaseExists`，语义是本地 SQLite/repository 可用性：`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs:460`、`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs:216`。
- 远端连通性检测实际调用 `ModApiHealthClient.ProbeAsync(...)`，它请求 `ModApiRoutes.Health`：`src/BazaarPlusPlus.ModApi/Clients/ModApiHealthClient.cs:23`、`src/BazaarPlusPlus.ModApi/ModApiRoutes.cs:11`。
- 当前 HistoryPanel 的 `statsChipRow` 已经容纳 count、battle、database 三个 chip：`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:298`。
- 架构测试禁止 `HistoryPanel` 与 `LiveBuildPanel` 互相 import 内部 namespace：`tests/Architecture.Tests/CoreLayeringTests.cs:238`。因此迁移不能让 LiveBuildPanel 调用 HistoryPanel coordinator/data service。
- `BppComposition` 里 `HistoryPanelMount` 与 `LiveBuildPanelMount` 是两个独立 mountable：`src/BazaarPlusPlus/BppComposition.cs:108`、`src/BazaarPlusPlus/BppComposition.cs:114`。

## 目标

1. 把“拉取阵容”入口从 HistoryPanel 移到 LiveBuildPanel。
2. 保持十胜阵容刷新逻辑在 `Game/LiveBuildPanel/Recommendations`，避免 LiveBuildPanel 依赖 HistoryPanel，也避免误把 recommendation backend 表达成跨 feature shared module。
3. 删除 HistoryPanel 里的阵容刷新状态、按钮、文案和测试假设。
4. 把 HistoryPanel 的“检测连通”按钮移动到 DB chip 同一行。
5. 保持本地 DB 状态与远端 server health 状态分离：同一行展示，不合并语义。
6. 不改变十胜推荐算法、不改 analyzer JSON schema、不改服务端 endpoint、不新增设置项。

## 非目标

- 不重新设计 LiveBuildPanel 的四行 item-board 布局。
- 不改变 `BuildRecommendationRepository.FindRecommendations(...)` 的匹配/排序语义。
- 不把 server health 自动轮询化；本次仍是用户显式点击检测。
- 不把 BazaarDB 截图上传状态并入 HistoryPanel 的 DB chip。
- 不引入 HistoryPanel 与 LiveBuildPanel 之间的新引用关系。

## 最终 UX

### LiveBuildPanel

右侧 rail 顺序调整为：

1. 标题、关闭按钮、赞助者 attribution。
2. 候选数量 chip。
3. `Pull Builds / 拉取阵容` 按钮。
4. 推荐状态区。
5. 上一条 / 下一条。

按钮行为：

- 默认文案：`Pull Builds` / `拉取阵容`。
- 点击后文案：`Working...` 或 `Pulling...`，按钮 disabled。
- 刷新期间保持当前推荐可见，不清空 `_matches`。
- 成功后：
  - 更新本地 corpus/cache。
  - 重新执行 `RefreshRecommendations()`。
  - 重绘当前 LiveBuildPanel。
  - 推荐状态继续显示匹配结果与证据，例如当前已有的 `1/N · 十胜 X · rate · Dn`。
  - 额外显示一次短成功反馈：`Ten-win builds updated.` / `十胜阵容已更新。`
- 失败后：
  - 当前推荐保持旧 corpus 的结果。
  - 状态区显示失败原因：`Couldn't pull ten-win builds: <details>` / `拉取十胜阵容失败：<details>`。
- 面板关闭或销毁时：
  - 允许后台请求完成并更新共享 corpus。
  - 不再触碰已销毁/隐藏的 UITK view。

### HistoryPanel

右侧 rail 的 overview 区变为：

- 第一行：runs count chip、battle count chip、DB chip、`Check Server / 检测连通` 按钮。
- 不再出现 `Pull Builds / 拉取阵容`。
- `Check Server` 的结果仍写入 HistoryPanel 的 status banner，不写入 DB chip。

DB chip 语义保持：

- Connected：本地 run log DB 可读。
- Missing：DB 文件还不存在，通常是新安装/无历史数据，不是错误。
- Unavailable：repository/path 不可用。

Server health 语义保持：

- 点击按钮时检查游戏到 Bazaar++ 服务端 `/health` 的连通性。
- 成功/失败状态显示在 status banner 中，使用已有 `ServerHealthConnected(...)` / `ServerHealthFailed(...)` 文案。

## 实施设计

### 1. 在 LiveBuildPanel/Recommendations 保留刷新服务

保留 `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRefreshService.cs`：

```csharp
internal sealed class BuildRecommendationRefreshService
{
    public Task<BuildRecommendationRefreshResult> RefreshAsync(CancellationToken cancellationToken);
}

internal readonly struct BuildRecommendationRefreshResult
{
    public bool Succeeded { get; }
    public string? Error { get; }
}
```

内部使用 `Task.Run(...)` 包装 `BuildRecommendationRepository.TryRefreshFinalBuildsFromRemote(out error)`。这样 async/session guard 不再挂在 HistoryPanel，LiveBuildPanel 可以直接消费共享服务。注意：现有 repository 刷新是同步 HTTP 读取，`CancellationToken` 只能阻止任务启动或阻止 stale continuation 更新 UI，不保证中断已经发出的网络请求；本次不改 repository 的 HTTP 实现。

保留 `TryRefreshFinalBuildsFromRemote(...)` 的现有签名，因为测试已经通过反射验证这个手动刷新入口：`tests/LiveBuildRecommendations.Tests/Program.cs:867`。

### 2. 给 LiveBuildPanel 增加刷新状态与动作

`LiveBuildPanel` 增加字段：

- `_refreshService = new BuildRecommendationRefreshService()`
- `_buildRefreshInProgress`
- `_buildRefreshStatusText`
- `_buildRefreshStatusSeverity`
- `_buildRefreshOperationVersion`

新增方法：

- `TryRefreshFinalBuilds()`
- `RefreshFinalBuildsAsync(int operationVersion)`
- `SetBuildRefreshStatus(...)`

关键规则：

- 点击时如果 `_buildRefreshInProgress == true`，只更新状态为 already running，不发起第二个请求。
- 成功后必须调用现有 `RefreshRecommendations()` 与 `RefreshViewAndPreview()`。
- 异步返回时检查 operation version、`_isVisible`、`_view != null`，避免面板关闭后刷新 UI。
- `OnDestroy()` 增加版本递增/标记，避免 stale continuation 更新 UI。

### 3. 扩展 LiveBuildPanelSnapshot 与 View

`LiveBuildPanelSnapshot` 增加：

- `FinalBuildRefreshButtonText`
- `FinalBuildRefreshButtonEnabled`
- `BuildRefreshStatusText`
- `BuildRefreshStatusSeverity`

`LiveBuildPanelView` 构造函数增加 `Action refreshFinalBuilds`，并在 rail 里创建 `_finalBuildRefreshButton` 与 `_buildRefreshStatus`。

样式要求：

- 按钮固定高度沿用 `Sizes.ButtonStandardHeight`。
- 宽度不挤压导航按钮；放在 nav 上方。
- 状态文本使用独立 label，不复用 row empty text，避免触发 item-board row 布局抖动。此前 LiveBuildPanel row 文案会影响几何回调与 preview 重绘，这类状态不要放进 board row。

### 4. 把 LiveBuildPanel 文案补齐

在 `LiveBuildPanelText` 中增加：

- `RefreshFinalBuilds()`
- `RefreshingFinalBuilds()`
- `FinalBuildRefreshAlreadyRunning()`
- `FinalBuildRefreshSucceeded()`
- `FinalBuildRefreshFailed(string details)`
- 可选：`Working()`，如果不想复用 HistoryPanelText。

并把这些字符加入 `FontAtlasSample()`，避免 CJK glyph 首次渲染缺字。

### 5. 删除 HistoryPanel 的阵容刷新路径

删除/调整：

- `HistoryPanelDataService.RefreshFinalBuildsAsync(...)`
- `HistoryPanelCoordinator.TryRefreshFinalBuildsAsync()`
- `HistoryPanelState.FinalBuildRefreshInProgress`
- `HistoryPanelController.TryRefreshFinalBuilds()`
- `HistoryPanelUiToolkitView` 构造参数 `refreshFinalBuilds`
- `_finalBuildRefreshButton`
- `HistoryPanelUiToolkitModel.FinalBuildRefreshButtonText`
- `HistoryPanelUiToolkitModel.FinalBuildRefreshButtonEnabled`
- `HistoryPanelText.RefreshFinalBuilds*` 相关文案
- `ClearTransientStatus()` 中对 final-build refresh flag 的判断
- `OnPanelHidden()` / `OnPanelShown()` 中对该 flag 的测试假设

注意：只删除阵容刷新相关路径，不碰 ghost sync、replay、delete、server health。

### 6. 移动 HistoryPanel 连通性按钮

改 `HistoryPanelUiToolkitView.Tree.cs`：

- 在 `statsChipRow` 里，`_databaseChip` 后创建并添加 `_checkServerHealthButton`。
- 删除原 `toolRow`，因为迁走 `Pull Builds` 后它没有其他职责。
- 按钮保留 `ServerHealthButtonText` / `ServerHealthButtonEnabled` model 字段。
- 如果同一行空间不足，按钮允许 wrap 到下一行，但仍归属 `statsChipRow`。不要让 status banner 或 action footer 承载它。

不改：

- `HistoryPanelServerHealthFormatter`
- `HistoryPanelCoordinator.TryCheckServerHealthAsync()`
- status banner result flow

这是有意控制范围：健康探针本身已经在 `BazaarPlusPlus.ModApi`，当前只是 HistoryPanel 的 UI wrapper；本次没有其他面板需要共享 server health UI，因此不先做跨功能重构。

## 测试计划

### 必跑

```bash
dotnet run --project tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
```

`GhostBattleSync.Tests` 需要删除或改写对 `FinalBuildRefreshInProgress` 的反射断言，因为该状态不再属于 HistoryPanel。

### 建议补充

- 在 `LiveBuildPanel.Tests` 增加反射/源码测试，断言 `LiveBuildPanelText` 暴露 `RefreshFinalBuilds` 相关文案，且 `FontAtlasSample()` 包含这些文案。
- 在 `Architecture.Tests` 增加源码断言：HistoryPanel 不再包含 `RefreshFinalBuilds` / `FinalBuildRefresh`，LiveBuildPanel 不 import `BazaarPlusPlus.Game.HistoryPanel`。
- 若测试 harness 足够轻，给 `BuildRecommendationRefreshService` 加一个小测试，验证成功/失败 result 包装不会吞掉 error。

### 手测

必须通过 Steam 启动游戏：

```bash
open "steam://run/1617400"
```

验证步骤：

1. 进入非 combat 的 run 场景。
2. Caps 打开 LiveBuildPanel。
3. 选择 shop/board/stash 物品候选。
4. 点击 `拉取阵容`。
5. 确认按钮 disabled、状态显示刷新中。
6. 成功后推荐列表基于新 corpus 重新计算，上一条/下一条仍工作。
7. 断网或阻断远端后点击，确认失败状态显示但旧推荐不清空。
8. 打开 HistoryPanel，确认 `拉取阵容` 不存在。
9. 确认 `DB 已连接` 与 `检测连通` 在同一 overview 行。
10. 点击 `检测连通`，确认结果仍显示在 status banner，DB chip 不被远端健康结果覆盖。

## 风险与处理

### 异步刷新返回时 UI 已关闭

风险：LiveBuildPanel 现在没有 HistoryPanel 的 `HistoryPanelSessionScope`，直接 await 后刷新 UI 可能触碰已销毁 view。

处理：使用 `_buildRefreshOperationVersion` 或 `CancellationTokenSource` 做 stale continuation guard。即使请求完成并更新共享 corpus，也只有当前面板仍可见且版本匹配时才刷新 UI。

### 手动刷新与后台刷新重叠

风险：`BuildRecommendationRepository.EnsureLoaded()` 可能因 stale/missing cache 排队后台刷新；用户同时手动刷新会有两个远端请求。

处理：本次不改变 repository 的全局并发模型。两个请求都会写入同一 corpus/cache，结果等价；LiveBuildPanel 只防止同一面板按钮重复点击。若以后要优化，可把 `_backgroundRefreshInProgress` 与 manual refresh 合并为全局刷新锁，但这不是本次迁移必须条件。

### HistoryPanel rail 横向空间不足

风险：count、battle、DB chip 加按钮后，小宽度下可能拥挤。

处理：`statsChipRow` 已经 `flexWrap = Wrap.Wrap`，允许按钮随 chip wrap。必要时把 health button 设为 compact 宽度或 `fixedWidth: false`，但仍保持它属于 DB/overview 行。

### 误把 DB 与 server health 合并

风险：用户看到同一行后，后续实现可能把 server health 结果写到 DB chip。

处理：保留 `DatabaseChipText` / `DatabaseChipSeverity` 与 `ServerHealthButtonText` / status banner 两条 model 路径。DB chip 只读本地 repository/database state；server health 只更新 status banner。

## 文件改动清单

预计修改：

- `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRefreshService.cs`
- `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`
- `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanelText.cs`
- `src/BazaarPlusPlus/Game/LiveBuildPanel/Data/LiveBuildPanelSnapshot.cs`
- `src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelDataService.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelController.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelState.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs`
- `tests/GhostBattleSync.Tests/Program.cs`
- 可选：`tests/LiveBuildPanel.Tests/Program.cs`
- 可选：`tests/Architecture.Tests/CoreLayeringTests.cs`

不预计修改：

- `BuildRecommendationRepository.FindRecommendations(...)`
- `TenWinBuildCorpus`
- `GameInterop/ItemBoardPreview`
- `ModApiHealthClient`
- `BppComposition`
- server / analyzer / installer repos

## 验收标准

- LiveBuildPanel 里有 `拉取阵容` 操作，HistoryPanel 里没有。
- `拉取阵容` 成功后当前 LiveBuildPanel 推荐立即重新计算。
- `拉取阵容` 失败不清空旧推荐。
- HistoryPanel 的 `DB 已连接` 与 `检测连通` 显示在同一 overview 行。
- 远端连通性结果仍显示在 status banner，DB chip 只表示本地 DB 状态。
- `HistoryPanel` 与 `LiveBuildPanel` 仍不互相 import 内部 namespace。
- 上述必跑测试全部通过。
