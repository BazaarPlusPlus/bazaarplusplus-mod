# 架构深化批次设计记录（2026-07 评审卡 3–8）

状态：设计共识已与用户逐卡确认（grilling 完成），待红队评审 → 修订 → 最终开工确认。
来源：2026-07-27 架构评审（三个探索 agent 的 file:line 实证 + 逐卡盘问决议）。
词汇：模块/接口/深·浅/接缝/适配器/杠杆/局部性（/codebase-design）；领域词条已同步进 `CONTEXT.md`（Collection View State、Saved Replay Lifecycle、Upload Feed Session）。

共同根因：纯逻辑核心测试充分，而持有竞态安全与时序不变量的 MonoBehaviour 编排胶水没有可测接缝；极端症状是用源码文本断言 token 顺序代替行为测试。

---

## 卡 3 — CollectionViewState reducer

**问题**：`CollectionPanel.cs` 的 11 个筛选命令各自手写多步协议（改 `_filter` →（SetActiveTab/ToggleHero 先 `PruneInvisibleSourceSelections`，**Prune 紧跟 filter 变更、先于滚动重置与查询**——红队 R2-9 修正原描述）→ `_scrollY=0` → `ApplyFilters` → `RefreshView` → 三个命令再补 `ResetControlsScroll`，`CollectionPanel.cs:670-796`）；`ApplyFilters` 写回 `_currentRunDay` 并经 `AdoptNormalization` 写回 `_filter`（`:963-1012`）；`AvailableSourcesFor` 手工缓存（`:1077-1112`）。胶水零测试。

**设计**：
- 新纯类 `Game/CollectionPanel/CollectionViewState.cs`（无 Unity 类型，可 Compile-Include）。
- 13 个变更命令方法，统一返回 **`CollectionRenderOutcome?`**（R2-7：**null = 同值 no-op**——不取消防抖排期、不渲染、不重置滚动，对齐现行 SetActiveTab 同 tab `:678-679`、SetSortPriority 同值 `:776-777`、SetSearchQuery 同串 `:790-791` 的早退语义；防抖取消发生在**确认状态变更之后**）。Outcome 含 `(Model, ResetScroll, ResetControlsScroll)`。
- **hero 归一化不写偏好**（R2-4）：用户命令 `ToggleHero` 内 Save；`AcceptCatalog` 的归一化走无 Save 的内部路径（对齐现行 `:689` vs `:1189-1195` 的分野），否则"记住的英雄不在本次目录"场景会用 Common 覆盖玩家已存偏好。测试补"AcceptCatalog 归一化 → 偏好存储零写入"断言。
- 搜索全入：`SetSearchQuery`（只排期）/`ToggleSearch`/`TickSearch(dt, isComposing)`/`ResetSearchForLifecycle`；`CollectionSearchModeState`+`CollectionSearchRefreshGate` 内化。
- 生命周期：`BeginCatalogLoad`/`AcceptCatalog`/`CatalogUnavailable`/`ResetCatalog`/`ApplyOpenSelection(selection, supporters, **currentRunDay**)`（R2-8：开门探针的天数须随选择进入模型，否则冷启动多帧加载窗内 Day 徽标渲染旧残值）/`Rebuild`。facet 重算仍紧贴目录接受（ADR-0009）。
- 网格回读走注入端口 `ICollectionGridPort`，**契约显式定义三个窗口**（R2-5）：
  - `Publish(cards, tab, matches) → GridProjection?`——网格不可用（virtualizer null/已 Dispose）返回 null，reducer 保持现行早退语义：**跳过归一化写回与缓存失效**（对齐 `:966-967` 整体早退）；
  - `Current`——首次 Publish 前与适配器失效窗口返回显式 `GridProjection.Empty`（生产适配器包裹 Dispose 感知，不得吐出 VisibleCount=0 但 ContentHeight 残留的自相矛盾投影）；
  - `SetViewport` 引起的投影漂移由面板负责（现行 `:470-481` 已在视口变化时重发 spacer 高度）；假网格按同一契约实现。
- 依赖注入：`ICollectionGridPort`、拓宽后的 `ICollectionSourceCatalog`（新增 `For(kind, hero)` 花名册查询）、`IGameDataDayTierResolver`、`ICollectionPanelHeroPreferenceStore`。supporters 开面板时传值；`NativeTagTypography` 探针与自愈标志留面板。
- 视图侧 `ICollectionPanelCommands` 不动；`PanelCommands` 变单行转发 + 面板唯一消费点 `ApplyOutcome`（对 null outcome 不动作）。

**前置**：`CollectionPanelViewModel`/`CollectionSourceOptionViewModel` 从 `Ui/CollectionPanelView.cs:17,63` 拆到新纯文件（新建文件，不移动被 Compile-Include 钉住的文件）。

**测试**：新 exe-runner `tests/CollectionViewState.Tests`（Compile-Include + ManagedPath 枚举 DLL，复刻 CollectionFilterEngine.Tests 形状；入口须复刻 `GhostBattleSync.Tests/Program.cs:140-142` 的 `L.Install` 引导）。首发用例：ToggleHero 全链（Prune 先于查询生效+偏好 Save+控制栏滚动意图）、归一化写回、可用来源缓存命中/失效、挂起搜索被变更取消、**同值 no-op 保留 pending 且不重置滚动**（R2-7）、AcceptCatalog 三连（facet 重算+无 Save 归一化+prune+**偏好零写入**）。
**架构测试迁移**（R2-6）：`CoreLayeringTests.CollectionPanel_catalog_state_transitions_are_centralized`（`CoreLayeringTests.cs:227-319`）以方法源码抽取+字段赋值计数钉死 CollectionPanel.cs 内部结构——目录状态迁入 reducer 后必失败；改写该测试为对 `CollectionViewState` 校验"目录状态转移集中"的新不变量（新边界配架构测试，符合仓库规则）。

**已知语义微移**：负载诊断 `Filter`/`Refresh` 段测量口径（事件 schema 不变）；缓存命中路径少一次冗余查询与渲染。

## 卡 4 — NativeCardCellFitter + CollectionCardFitMath

**问题**：`CollectionGridVirtualizer.cs` 1250 行融合两个深模块；测量/拟合约 300 行（`:479-780`）零测试；滚动每帧对所有已实体化格子全量重测（`:218-226` → `:524`），Aspect 回退分支测量时变异 rect（`:597-598`）。

**设计**：
- `Grid/CollectionCardFitMath.cs`：纯（UnityEngine-free，float/自有 struct）。`ComputeScale(bounds, aspectRatio?, cellRect, gap, inset, frameHeightOverSocket)`、`ComputeAnchoredPosition(bounds, scale, cellRect, scrollY)`。`NativeVisualBounds` 提升为纯 `CardVisualBounds`。
- `Grid/NativeCardCellFitter.cs`：Unity 适配器。测量链（FrameContainer→RawImage→Aspect 变异回退→root.rect→sizeDelta→1×1）、`PrepareGridRect`、组件/游戏常量读取、rect 写入。
- 虚拟化器 4 个调用点（`:462-463, 224-225, 831-832, 953-954`）改调 fitter，删约 300 行。
- **逐调用行为保持**：每次 Reposition 仍重测。per-cell 边界缓存另开 issue（失效钩子：重 bind/`_scaleDirty`/艺术加载完成；风险点：Aspect 回退卡今天靠重复 SetSize 维持尺寸；需进游戏验证）。

**测试**：并入 `tests/CollectionGridLayout.Tests`。用例：Large 外扩钳制分支、退化 aspectRatio（NaN/Inf/≤0.01）、零/负尺寸守卫回 1f、中心对齐与 scroll 偏移。**冒烟清单**（R2-18：测量链依赖 Unity、纯测试覆盖不到调用时序）：进游戏滚动 Collection 网格，须明确覆盖 **Aspect 回退卡（无 FrameContainer/RawImage 可测边界的卡）滚动场景**，确认 `SetSizeWithCurrentAnchors` 变异行为不变、卡面尺寸无漂移。

## 卡 5 — Encounter Preview 死接口修剪

**删除**（grep 实证零读者）：
- `EncounterOption.{SourceKey, SourceKind, RepresentativeTemplateId, IsSourceMatch}` + `enum EncounterSourceKind`（仅活在定义文件）。
- `EncounterChoiceDetail.IsSourceMatch` + ctor 参数（5 个生产构造点全为 false：`EncounterEventDetailResolver.cs:115,133,555,605,636`）。

**合并**：`EncounterPreviewResult`/`EncounterStepPreviewResult`/`LevelUpPreviewResult`（`EncounterPreviewModule.cs:27-40`，结构全同）→ 单一 `EventPreviewResult`；patch 侧重复 `Available(...)` 解码帮手合一。零语义差异，非 ADR-0009 拒绝的配置化统一。**同步更新 `CoreLayeringTests.cs:2331-2342` 钉住三个方法签名的文本断言**（R2-16）。

**保留**：`TemplateId`/`DisplayName`/`ResultText`/`RewardFilter`（有读者或编译期再证；不越界扩删）。构造点收窄全部编译期暴露、纯机械——**计数修正（R2-17）**：测试侧 3 处 `EncounterOption` + 26 处 `isSourceMatch` 构造（`CollectionEncounterGameTooltipTextTests.cs` 23 + `CollectionEncounterOutcomeMergeTests.cs` 3），生产侧 `EncounterEventDetailResolver.cs:43-44` 的 `EncounterOption` 命名实参一并收窄；实施时以 grep 命名实参为准，不依赖本清单点数。

## 卡 6 — SavedReplayLifecycle

**问题**：存档回放的状态代数散在 `CombatReplayRuntime`（MonoBehaviour）6 处：`SavedReplayProgress` 四态两处转移（`:991-996, 1037-1042`）、15s 退出抑制锁存（`:77-90`）、`_returnToMenuAfterReplay`/`_bootstrappedReplayActive`、待定菜单返回轮询（`:1192-1251`）、启动中断接力（`:919-925 → 1049-1053`）、终结归属（`:1033-1042`）。现状"测试"= `ReplayPlaybackRuntimeOwnershipTests` 读源码断言 token 顺序。

**设计**（决策输出型纯核心，复刻 `EndOfRunCaptureWorkflowCore` 模式；**API 经红队 R2 两条 blocker 重设计**）：
- `Game/CombatReplay/SavedReplayLifecycle.cs`：纯，零 Unity/零委托持有，时间一律 `float now` 参数。
- **启动分阶段提交**（blocker R2-0：两旗标现分三个时点提交，是防 mid-start 退出竞态的关键，不可单点参数化）：
  - `OnStartBegun()`（`:876` 时点，progress=StartInProgress）
  - `OnBootstrapResolved(bool returnToMenuAfter)`（`:888-889`，bootstrap ensure 之后）
  - `OnInjectionCommitted()`（`:926`，注入成功且中断检查通过后才落 bootstrappedActive）
  - `OnStartFailed()`（`:930-934` catch 入口——**纯启动失败无状态退出也可达**，major R2-3；清 bootstrap 旗标、progress→StartFailureCleanup）
  - `OnStartFinished()`（`:991-996` finally 映射）、`TakeStartupInterruption()`
- **两段式退出决策**（blocker R2-1：publish 前后事实不可混于一次调用）：
  - `BeginReplayStateExit(now) → ExitOwnership { OwnsTerminalByStart }`——publish **前**查询，runtime 据此执行 `PublishEnded(failed: ownsTerminal)`；
  - `OnReplayStateExited(now, publishSucceeded, exception?) → StateExitDecision { LatchInterruption(reason)?, CompleteNow(failureReason) | BeginMenuReturn(failureReason) | Defer }`——publish **后**产出 latch reason（StartException vs EndedPublishFailed）与终结路由；
  - 补 `OnMenuReturnDispatchFailed()` 转移（`:1199-1210` 同步派发失败不置 pending，runtime 直接以 MenuReturnFailed 终结——否则 lifecycle 的 pending 会对已终结操作再发 CompleteTimeout）；
  - **pending 菜单返回与启动进度是并行状态**（`:225-248` 可在 pending 窗内开新启动）——核心分开建模，TickMenuReturn 只作用于其捕获的 operation。
- **抑制锁存三路径全覆盖**（major R2-2）：`TryContinueReplay` 在非 bootstrapped 路径也读写同一锁存（`:847-851, :860-861`）——补 `IsExitSuppressed(now)` 与 `NoteProgrammaticExitLatched(now)` 成员，锁存单一所有者覆盖 continue/bootstrapped-exit/native-exit 三条路径。
- `RequestBootstrappedExit(float now, bool inReplayState) → Suppressed|NotActive|Proceed`、`TickMenuReturn(float now, bool heroSelectLoaded) → Wait|CompleteConfirmed|CompleteTimeout`、`ObserveReplayStateGone()`。
- runtime 变翻译层：publish、portrait 清理、菜单调度、`CompletePlaybackOperation` 留在 runtime；两条退出路径各自清理顺序原样显式（ADR-0009）。
- **吸收删除** `ReplayPlaybackStateExitCoordinator`（近直通件）+ 其测试；try/observe 清理循环降为 runtime 私有助手。三个生产调用点迁移：`CombatReplayRuntime.cs:937,1044,1157`；同步移除 `tests/CombatReplayPlaybackLogging.Tests/CombatReplayPlaybackLogging.Tests.csproj:44-47` 对该源文件的 Compile-Include 钉（红队核实 R1）。
- **删** `ReplayPlaybackRuntimeOwnershipTests` 两条文本用例；新建 `SavedReplayLifecycleTests`（同工程 xUnit）：启动中途退出→latch+无双重终结、**纯启动失败（无状态退出）→StartFailureCleanup→Idle**（R2-3）、抑制窗内/窗后重复 Exit **及 continue 路径锁存**（R2-2）、菜单返回确认/超时/**同步派发失败**（R2-1）、pending 窗内新启动并行（R2-1）。

**守护栏**：ADR-0007/0008 —— `TryContinueReplay` 唯一编程退出；`TryExitBootstrappedSavedReplayToMenu` 的 bool 契约不变；15s 逃生舱语义逐字保留。

## 卡 7 — IUploadFeed 行为化

**问题**：`Activate` 返回 4 委托配置袋（`IUploadFeed.cs:100-118`），泵手工重接线；启用策略被劈两半（PTR 在泵 `BackgroundUploadPump.cs:34-48`，BazaarDb 启用在 feed `:69-74`）；静态 `CurrentByFeed` 注册表只为一个调用者活着（`BazaarDbSnapshotUploadSettingsDockEntry.cs:38`）；RunBundle 重复推导泵节奏参数只为打日志（`RunBundleUploadFeed.cs:28-30`）。泵/挂载零测试。

**设计**：
- `IUploadFeed.Activate(services, logState, cadence) → IUploadFeedSession?`；`UploadPumpCadence` 由泵传真实节奏值。
- `IUploadFeedSession : IDisposable { IsEnabled; RunAttemptAsync(ct); SubscribeArmSignals(arm) }`。RunBundle session 内部订 `CombatReplayPersistenceDrained`；BazaarDb 返 null。
- **释放两时点契约**（major R2-11，对齐现行 `BackgroundUploadPump.OnDestroy:95-134` 顺序）：`SubscribeArmSignals` 返回的 `IDisposable` 由**泵持有并在 OnDestroy 最先释放**（先退订→cancel）；`session.Dispose` 只覆盖 attempt 资源（httpClient/uploadService），**必须在排水回调完成后**才调用（沿用 `StartupUploadAttemptRunner.TryDrainPendingTaskOnShutdown` 的回调时点）；session 对订阅句柄的重复释放做幂等兜底。单一 Dispose 融合两者会导致在途 RunAttemptAsync 踩已释放资源或半拆泵仍被回调。
- PTR 渠道门留泵侧作激活前提（所有 feed 继承；与 store 行过滤双层防御口径一致）。
- 新事件 `Game/Upload/UploadArmRequested(UploadFeedKind)`（**不进 Core/Events**——`UploadFeedKind` 是 Game 层类型）；泵订阅；设置行 onChanged 改发事件；删 static `CurrentByFeed`/`ArmImmediate`。
- 删 `UploadFeedActivation`、`UploadArmHook`。

**测试迁移**：`BazaarDbScreenshotUploadService.Tests` 以 `GetMethod` 反射钉住 `BazaarDbSnapshotUploadFeed.IsEnabled` 静态方法签名（`Program.cs:103-119`，非 IsAssignableFrom——红队 R1 修正），迁移时改为驱动 `IUploadFeedSession.IsEnabled` 并同步更新反射。新增：BazaarDb 三条件启用矩阵（现有静态测试可搬）、泵侧 PTR 跳过不 Activate、arm 事件只命中同 kind。

## 卡 8 — HistoryPanel 装配链坍缩

**问题**：Mount→Runtime→Factory→Dependencies 每跳近直通；`IHistoryPanelRuntime` 8 成员中存活消费仅 2 个只读（coordinator `:316-317`）+ 面板一处路径（`HistoryPanel.cs:288`）；4 个望远镜构造器；路径 `Func` 包一次性只读字符串；`Dependencies.GhostSyncService` 零读者。

**设计**：
- 删 `HistoryPanelRuntime.cs`；`IHistoryPanelRuntime` → `IHistoryPanelRunState { IsInGameRun; CurrentServerRunId }`（只读；生产适配器 6 行包 `IRunContext`，不暴露 setter）。
- `HistoryPanelDependencies` 单构造器：`(runState, dataService, replayService, serverHealthProbe?, accountLinkClient?, isBazaarDbAccountLinkAvailable?, combatReplayDirectoryPath)`；删 GhostSyncService 属性与全部望远镜。
- `HistoryPanelReplayService` 三个路径 `Func` → 普通 string。
- Mount（null 探测+缺失日志）与 Factory（null 降级装配）保留。**修正（红队 R1，major）**：`HistoryPanelReplayService` 首参 `Func<CombatReplayRuntime?>` 不是路径、`services.Paths` 里没有——Mount 把 `() => combatReplayRuntime` 作为参数直传 `Factory.Create`（签名相应加参），Factory 再传给 ReplayService；路径值才从 `services.Paths` 取 string 直传。顺手补 Factory 首批测试（db 路径空 → repository null → 降级链）。
- `HistoryPanel.cs:288` 的 `_runtime?.CombatReplayDirectoryPath` 改读携带记录的 `CombatReplayDirectoryPath` 属性（红队 R1）。

**迁移注意**（红队 R2 补全）：
- `GhostBattleSync.Tests` 全名 Activator 构造须同步更新（arity、首参类型、删 ghostSyncService 位）；**新单构造器保持无守卫直赋值**（R2-14：测试按位传 null 且被 ADR-0009 钉为行为锚点，加 ArgumentNullException 守卫会在构造期炸掉钉住的用例）；
- `HistoryPanelFiltering.Tests/Program.cs:165,213` 的 `Construct` 按**参数个数精确匹配**构造 Dependencies（R2-13）——arity 变 7 后抛 MissingMethodException，一并迁移；
- coordinator 内 `ResolveGhostBattleOutcome` 反射兼容保留不动（超出本卡）。
**实施时核对**：`IsBazaarDbAccountLinkAvailable` 实际消费点（预计 UiToolkit partial 账号链接卡可见性）。

---

## 实施顺序建议（每卡一分支一 PR，按仓库 wrap-up 流程）

1. **卡 5**（纯删除，半天，零测试成本）
2. **卡 3**（首选：杠杆/风险比最高，构建+表格测试即可回归）
3. **卡 4**（同区跟进；与卡 3 无文件冲突——卡 3 动 CollectionPanel.cs，卡 4 动 CollectionGridVirtualizer.cs）
4. **卡 8**（独立区，中等）
5. **卡 7**（独立区，中等；含反射钉迁移）
6. **卡 6**（最重、触回放竞态，压轴；实施后需进游戏冒烟：存档回放退出、重复 Exit、菜单返回、录制中退出）

每卡验证：`./run.sh build` + 相关测试工程；卡 4/6 追加进游戏冒烟（Steam 启动，读 `BepInEx/LogOutput.log`）。

## 待核对项（实施前/中）

- [x] 卡 3：~~L 英文回退~~ **红队 R1 核实为否**：`L.Resolve` 未 Install 即抛 `InvalidOperationException`（`L.cs:30-34`），英文回退只在显式参数重载里——`CollectionViewState.Tests` 须复刻 `GhostBattleSync.Tests/Program.cs:142` 的 `L.Install` 引导
- [ ] 卡 3：`BPPSupporterSample` 纯度（进 ViewModel Compile-Include 闭包）
- [ ] 卡 8：`IsBazaarDbAccountLinkAvailable` 消费点
- [ ] 卡 7：`CompositionRuntime`/架构测试对 `Game/Upload` 新事件类型的分层校验通过

## 红队评审记录（2026-07-28，两轮，评审专用未改码）

- R1（2×sonnet 视角 + fable 核实）：7 裁决，6 证实 1 驳回；唯一 major（卡 8 `Func<CombatReplayRuntime?>` 来源错误）及 5 minor 已全部修入正文（标 "红队 R1"）。
- R2（2×fable 视角补跑行为保持/接口设计 + fable 重核全量）：19 裁决，17 证实 2 驳回。**卡 6 两条 blocker**（启动旗标分阶段提交、publish 前后事实不可单次决策）导致 SavedReplayLifecycle API 重设计；卡 6 另 2 major（抑制锁存第三读写者、纯启动失败入口）、卡 3 3 major（归一化不写偏好、网格端口三窗口契约、CoreLayeringTests 目录集中化测试迁移）、卡 7 1 major（释放两时点契约）及各 minor 已修入正文（标 "R2-n"）。
- 驳回 2 条：卡 8 "须同 PR 未标注"（每卡一 PR 流程已覆盖）；卡 7 "反射钉未说明迁移"（R1 修订版已说明）。
- 全裁决原文：workflow run `wf_22e7e6dc-4e0` journal。
