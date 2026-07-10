# HistoryPanel 单一写者瘦身 设计备忘 · rev2

状态：grilling 决策完毕 + 红队快审回收（2026-07-11），待用户确认实施。
来源：2026-07-11 架构评审候选 6，经全量盘点 + 红队两轮**定性下调**，最终收敛为纯删除型零行为变化重构。

## 定性修正记录（重要：供未来架构评审引用，勿再重提以下命题）

1. **「HistoryPanel 状态两写者/矛盾不变量」证伪**：12 个代理属性的 setter 全部死代码（三个 partial 中对代理赋值零命中，主会话亲手 grep 复核）。C3 瘦身（f4536eb8）后 `HistoryPanelCoordinator` 已是唯一写者。`_statusMessage` setter 的清标志地雷（`HistoryPanel.cs:102`）真实但不可达。
2. **「coordinator-null 陈旧回退」证伪**：唯一 null 路径（ModApi base URL 非法，`Plugin.cs:197-202`）同时跳过 `AttachToOverlayHost`（`HistoryPanelMount.cs:60-68`），面板永不进入 Overlay Panel Host；且该态下 `_state` 列表恒空，回退臂 ≡ 空列表。
3. **「OnPanelShown 标志重置不对称 = 真 bug」证伪（红队 F3）**：a3690e9d（2026-07-04，Overlay Panel Host 集中化）后，重入打开被 `OverlayLifecycleCore.ExecuteOpenRequest` 的 `AlreadyInState` 挡住（`OverlayLifecycleCore.cs:164-165`，不发 Open 指令，`OnPanelShown` 不重跑）；`OnDisable` 路径实际经 `RequestClose → OnPanelHidden`（四标志全清）。卡标志场景在 HEAD 不可达。现状的「保留 GhostSync/ServerHealth」是 33cf5aa6 带测试钉住的深思熟虑决定（`GhostBattleSync.Tests/Program.cs:186-193` 显式断言 preserve）——其原始理由（防重入竞态）在 Overlay Panel Host 之后同样过时，但反转它零收益。**对称重置提案（原变更 4）经用户确认放弃。**若未来 overlay lifecycle 再变（历史上两个月改了三次），此项可重评；安全性论证已备好：`HistoryPanelSessionScope.Begin()` 先 End（bump 版本 + 取消旧 CTS）再装新 CTS，任何在飞旧操作必然 `!IsCurrent` 保释。
4. **H4（通用 RunGuardedAsync）否决**：四个异步处理器 6 轴互异——状态通道（main banner vs account-link banner）、redeem 自超时≠用户取消（`HistoryPanelCoordinator.cs:613-624`）、redeem 账号切换中途保释（`:642-648`）、replay 成功关面板、sync 终态二次写（`:797-801`）、前置检查链长短不一。通用化 = 6 委托参数的配置记录。**勿再提议。**

## 变更清单（纯删除，零行为变化）

1. **删 12 个代理属性**（`HistoryPanel.cs:64-122`），约 20 处读点机械改为直读 `_state.X`（与 `BuildUiModel` 既有直读风格统一）：
   - `_selectedRunIndex`→`HistoryPanel.UiToolkit.cs:211`；`_selectedGhostBattleIndex`→`UiToolkit.cs:214`；`_selectedBattleIndex`→`UiToolkit.cs:215`（注意：这三处在 UiToolkit partial，非 HistoryPanel.cs——红队 F4 勘误）；`_previewSelectionMode`→`HistoryPanel.cs:278`；`_battles`→`HistoryPanel.cs:289`、`UiToolkit.cs:104,172`；`_sectionMode`→`HistoryPanel.cs:58,281`、`UiToolkit.cs:102,120,166,170,203,213`；`_ghostBattleFilter`→`UiToolkit.cs:204`；`_statusMessage`→`UiToolkit.cs:207`；`_replayActionInProgress`→`UiToolkit.cs:114`；`_runs`/`_ghostBattles` 零读点直接删。
   - 实施时以 grep 复核为准（行号可能漂移），红队已确认清单完整、无 partial 外引用、测试反射只用 `HistoryPanelState` 属性名（不受影响）。
2. **回退臂改空集合**：`GetFilteredGhostBattles`（`HistoryPanel.cs:477`，必改——其回退臂读被删代理）与 `GetFilteredRuns`（`:482`，非必改但为一致性同改）改为 `_coordinator?.X() ?? Array.Empty<T>()`。行为等价已证（见定性修正 2）。`BuildUiModel` 内其余 `_coordinator?.` 三元回退**保留**（真实降级态防御）。
3. **删写-only 死字段** `HistoryPanelState.IsVisible`（`HistoryPanelState.cs:86`）及唯二写点（`HistoryPanelCoordinator.cs:69,75`）。红队确认零读者（含测试反射字符串）。面板 `static HistoryPanel.IsVisible` 是对外可见性事实源（`HistoryPanelSettingsDockEntry.cs:26` 在读），不动。

## 不做的事

- 原变更 4（OnPanelShown 对称重置）——放弃，理由见定性修正 3。**不碰 `GhostBattleSync.Tests` 的 preserve 断言。**
- H4 通用 RunGuardedAsync——否决入档。
- OnDisable 缺口、`BuildUiModel` 三元回退——均已证非问题/真实防御。

## 测试与验证

- **零新增测试**（纯删除无新行为可钉；引擎不存在）。现有测试全部原样存活：五个 HistoryPanel 测试项目 + `GhostBattleSync.Tests` 均经 ProjectReference + 反射（类型/属性名不变）；`HistoryPanelPreview.Tests` 的显式 Compile-Include 不含本次触碰文件。
- 验证：`dotnet run --project tests/HistoryPanelFiltering.Tests/...`、`tests/GhostBattleSync.Tests/...`（确认 preserve 断言仍绿）等受影响 exe-runner + `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`。
