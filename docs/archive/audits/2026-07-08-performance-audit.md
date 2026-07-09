---
status: partial
archived: 2026-07-10
calibrated: 2026-07-10
---

> Status: PARTIAL — audit's only Critical (#2 per-frame EndOfRun scan) SHIPPED via PR#8 (merge 761a7503 / commit d22e7cab; EndOfRunScreenshotController.cs:466-481, app-state gate + 0.5s throttle, stronger than the doc's throttle-only proposal); #4/#9 probe-retracted as non-issues; #1/#3/#5/#6/#8/#10 downgraded to optional GC-hygiene, untouched at HEAD.

# BazaarPlusPlus 性能审计报告

> 生成日期：2026-07-08 · 方法：8 维度并行审计 + 对抗性验证（每条发现追踪调用链确认真实频率，独立复核重新定级）。14 条确认发现，1 条误报被驳回。

## 1. 概览

最大的性能风险集中在 **每帧执行的 `Update()` 循环**，且几乎全部是 Mono 上的 Gen0 分配压力——没有一处产生大内存尖峰或整帧预算耗尽，但多个 always-on 组件挂在插件常驻 GameObject 上，全程每帧分配小对象（字符串、空 List、装箱枚举器、`Vector3[4]`、场景查询数组）。次要风险是 **Harmony net-message 分发缝** 上两个补丁各注入 `object[] __args` 造成每消息数组分配+装箱（事件/网络速率，非每帧），以及 **插件加载时** SQLite schema 被同一个库重复初始化 5 次的一次性主线程冻结。所有问题的修复都是行为保持型（缓存、脏标记、复用 scratch buffer、去重初始化），无一需要改变语义。

## 2. 按影响排序的问题清单

### Medium

**1. OverlayPanelHost.Update 每帧分配场景令牌字符串 + 空 List + 接口枚举器（三合一）**
`src/BazaarPlusPlus/Game/OverlayPanels/OverlayPanelHost.cs:33-65`（并 `Game/OverlayPanels/OverlayLifecycleCore.cs:101-103`）
- 触发频率：每帧，整个会话（大厅/对局/战斗），无 early-return/dirty-flag。挂载点 `OverlayPanelHostMount.cs:17` 无条件注册于 `BppComposition.cs:128`。
- 成本：allocation/GC。每帧 (a) `GetSceneToken` = `$"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}"`（`OverlayPanelHost.cs:151-152`）新建堆字符串+两次 native string get+装箱 int/bool——仅用于与 `_lastSceneToken` 序号比较；(b) `Evaluate` 无条件 `new List<OverlayDirective>()`（`OverlayLifecycleCore.cs:103`）即使返回空表；(c) `ExecuteDirectives` 参数为 `IReadOnlyList<>`（`OverlayPanelHost.cs:86`），`foreach` 通过接口取枚举器，把 List 的 struct 枚举器装箱上堆。合计约 4-5 个小对象/帧（约 240-300/秒 @60fps）。
- 修复建议：(1) 缓存上一帧 Scene 句柄/buildIndex，仅在场景真正变化时重建令牌字符串；(2) 无 directive 快路径返回共享静态 `Array.Empty<OverlayDirective>()`，首个 directive 产生时才惰性 new List；(3) `ExecuteDirectives` 改收 `List<OverlayDirective>` 或用 `for` 按索引遍历以走 struct 枚举器。

**2. EndOfRunScreenshotController 每帧 FindObjectsOfType 扫描全场景（在结算屏出现前的整个会话）**
`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:468-483`
- 触发频率：每帧，**整个会话**（非仅对局中），只要功能开启（默认 true，`EndOfRunScreenshotSettingsPolicy.cs:28`）且截图目录存在——直到结算屏生成并被缓存前，`_cachedEndOfRunScreenController` 一直为 null。挂载 `BppComposition.cs:136`，Update `:112-115` 无节流。
- 成本：scene-walk + allocation/GC。miss 路径 `UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(includeInactive:true)`（`:470`）每帧走一遍场景并新建 `EndOfRunScreenController[]` 结果数组。
- 修复建议：无缓存控制器时对扫描做节流——存 `_nextScanRealtime`，每 ~0.5s 才重扫（照搬 `VersionLabelScanner.cs:11` 的 `ScanIntervalSeconds` 模式）。结算屏晚几百毫秒被发现不可感知。

**3. NetMessage 分发缝上 RunInitializedPatch 前缀注入 `object[] __args`，与兄弟补丁叠加为每消息两次数组分配**
`src/BazaarPlusPlus/Patches/RunLogging/RunInitializedPatch.cs:17-19`
- 触发频率：per-net-message（事件/网络速率，非每帧），战斗 sim 流式期间成批。与 `CombatReplayCapturePatch.cs:17` 补丁挂同一缝 `NetMessageDispatchSeam.ResolveTarget()`（`NetMessageDispatchSeam.cs:22-34`，每条 live-run 消息含 aggregate 子消息都流经此单一方法）。
- 成本：allocation/GC + 装箱。HarmonyX 在补丁序言里 `new object[N]` 捕获全部目标参数（PTR 2-参形态还装箱 bool），发生在 `message is not X` 类型门（`:22`）**之前**。两个补丁（Prefix+Postfix）各建一个数组+各装箱一次 bool，每消息共两次。`IsSpectatePlayback` 仅读 `args[1]`，缺省 false（`NetMessageDispatchSeam.cs:38-41`）。
- 修复建议：停止注入 `object[] __args`。在 PTR 2-参目标上按位注入 `bool __1`；online 1-参目标省略该参（默认 spectate=false）。或把 spectate 解析上提到 `NetMessageDispatchSeam` 内，让两个补丁都无需 `__args`——行为保持。（同源问题：`CombatReplayCapturePatch.cs:17-19` 的 Postfix，合并计入本条。）

**4. 插件加载时 run-log SQLite schema 被同一个库同步初始化 5+ 次（BepInEx Awake 主线程）**
`src/BazaarPlusPlus.Storage/Sqlite/SqliteStoreBase.cs:13-27`
- 触发频率：startup（每次插件加载一次），全部同步跑在 `Plugin.Awake` 主线程（`Plugin.cs:40`），无协程/Task 延迟。
- 成本：blocking-IO。五个 `SqliteStoreBase` 子类都指向同一 `RunLogDatabasePath`，各自跑 `EnsureInitialized`（9× CREATE TABLE + ~15× CREATE INDEX + 2× `PRAGMA table_info` 扫描，`RunLogSchema.cs:54-268`）+ WAL PRAGMA + 每次 OpenConnection 的 foreign_keys/busy_timeout PRAGMA（`SqliteStoreBase.cs:36-40`），冗余 5 次于冻结的主线程。PvpBattleSqliteStore（`PvpBattleCatalog.cs:13`←`BppComposition.cs:65-68,125`）、RunLogStore+RunSyncStateStore（`RunLoggingController.cs:45-46`）、CombatReplayVideoMetadataStore（`CombatReplayVideoRecorder.cs:41`）、RunScreenshotSqliteStore（`EndOfRunScreenshotController.cs:73`）。已初始化的库上重复的 `IF NOT EXISTS`/table_info 是廉价元数据读，真实可省成本约 4 次多余连接打开+WAL 建立。
- 修复建议：按 DB 路径去重——静态 `_schemaReady`（keyed by databasePath）守卫，让后 4 个 store 构造跳过 bootstrap SQL/WAL/table_info。`EnsureInitialized` 已幂等，去重不改行为。可进一步把首次初始化与 `RestoreActiveSession` 延后到首个 RunInitialized 事件以移出 Awake。

### Low

**5. BppHotkeyService.WasPressedThisFrame 每帧对每个已注册面板经 NormalizeBindingPath 分配字符串**
`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:110-136, 371-378`
- 触发频率：每帧，整会话。3 个面板注册（`CollectionPanel.cs:130`、`HistoryPanel.cs:446`、`LiveBuildPanel.cs:62`），经 `OverlayPanelHost.ResolvePressedHotkeyPanelId`（`OverlayPanelHost.cs:67-84`）每帧遍历。
- 成本：allocation/GC。`WasPressedThisFrame` 无条件 `NormalizeBindingPath(bindingPath)`（`:112`）重新规范化一个已规范化的路径；键盘分支 `KeyboardPrefix + trimmed[KeyboardPrefix.Length..]`（`:378`）= range-slice 子串+`+` 拼接 ≈ 2 个新字符串/面板 ≈ 6 个/帧，无论是否按键。（对照：`TooltipModifierRefreshController.cs:65-66` 的 IsHeld→IsPressed 不重规范化，不分配。）
- 修复建议：`NormalizeBindingPath` 短路——输入已以 `KeyboardPrefix` 开头且无空白时原样返回；或加内部「已规范化」重载，所有每帧调用方传入的 `GetBindingPath` 结果已规范化。行为保持。

**6. ItemBoardPreviewSurface.FindHoveredHandle 悬停时每帧 `new Vector3[4]`**
`src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs:707-729`
- 触发频率：每帧，**且**预览面板打开**且**光标在预览 clip bounds 内（`clipBounds.Contains` 门后，`:234`）。LiveBuild 最多约 4 次/帧（每 board 行一个 surface，`LiveBuildPreviewRenderer.cs:29-33`）、History 约 1 次/帧。经 `OverlayPanelHost.Update`→`Tick(isVisible)`→PollHover 驱动。
- 成本：allocation/GC。每次调用 `new Vector3[4]`（`:709`）+ 每活跃卡一次 `GetWorldCorners`。约 48-64 字节/次的短命小数组。（`new Rect(...)` 是 struct，无堆成本。）
- 修复建议：提升为可复用实例字段 `private readonly Vector3[] _corners = new Vector3[4];`，`GetWorldCorners(_corners)` 复用（照搬 recorder 的 `_flipRowBuffer`）。`GetWorldCorners` 每次覆写全部 4 项，复用安全。`LayoutCardsPacked:612`/`LayoutCardsSlotGrid:647` 同模式但在冷 Render 协程内，可选共享同一字段。

**7. VersionLabelScanner 从 Update() 轮询全场景 `Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()`**
`src/BazaarPlusPlus/Game/VoiceSubtitles/VersionLabelScanner.cs:19-55, 62, 89-118`
- 触发频率：功能**默认关**（`BppConfig.cs:88`）——关时每 0.75s tick 仅做 config 读+return，可忽略。开启且未挂载时，重扫按 `_nextFullScanAt`：前 3 次 miss 每 0.75s，之后退避到每 5s 稳态。挂载 `BppComposition.cs:151`。无 sceneLoaded/activeSceneChanged 钩子，故 label-less 屏上持续轮询。
- 成本：scene-walk。`Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()`（`:94`）是最重的 TMP 查询变体（含 inactive+asset 对象）+ 逐元素字符串检查。稳态每 ~5s 一次全场景枚举。（`GameObject.Find` 仅在成功挂载后 `_cachedVersionLabelPath` 非 null 时才跑，`:47`。）
- 修复建议：改由 scene-loaded/activeSceneChanged 事件驱动全扫描（label 存在与否是场景作用域，非帧作用域）；已开的关功能与已挂载短路保留。最低风险替代：拉长基础 `ScanIntervalSeconds` 与 miss 后退避。

**8. CollectionGridVirtualizer 滚动时每帧对每张可见卡重走子树（GetComponentsInChildren + 递归 DFS），未缓存**
`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs:222-233, 549-557, 620, 737, 752-765`
- 触发频率：每帧，**仅当 Collection 面板打开且滚动偏移变化时**（`_scrollY != _lastScrollY` 门，`:222`）；空闲帧零成本。Tick 经 `CollectionPanel.cs:136`→`:404` 每帧驱动，`:401-403` 每帧读滚动。
- 成本：scene-walk + allocation/GC + 布局读。每张已实现卡每滚动帧：`ResolveNativeVisualBounds`（`:528`，无缓存）→ `FindDescendant` 递归 DFS（`:752-765`）→ `TryMeasureSubtreeBounds`/`AccumulateRectBounds` 第二次递归 DFS 每 RectTransform 调 `GetWorldCorners`+`InverseTransformPoint`（`:714-750`）。每个 `foreach (Transform child in current)` 分配 Mono 枚举器/节点 → N 卡 × M 节点枚举器分配/滚动帧。`RealizedCell`（`:962-1001`）只存 `CachedRect`，无缓存 `NativeVisualBounds` 字段。（注：纯滚动帧 `_scaleDirty` 为 false，仅 Reposition 跑一次 walk；`GetComponentsInChildren<RawImage>` `:620` 是 FrameContainer 未找到时的 fallback，不在常路径。）
- 修复建议：首次成功测量后把 `NativeVisualBounds` 缓存到 `RealizedCell` 并在 Reposition/ApplyCellScale 复用；仅在 cell 重新实现或 base-unit/scale 变化时重测。native 几何与 scale 无关（scale 经 `rect.localScale` 单独施加），定位不变。

**9. run-end 转换时 CompleteRun/MarkRunAbandoned 阻塞主线程最长 2.5s 排空 SQLite 写队列**
`src/BazaarPlusPlus.Storage/RunLog/Replication/QueuedRunLogStore.cs:65-75, 101-111`
- 触发频率：每次 run 生命周期结束一次（完成/放弃/session 不匹配），主线程。经 `InMemoryBppEventBus` 同步派发（`:56-60` 直接 Invoke，无线程跳转），发布方为 Unity 主线程上的 Harmony 补丁与 MonoBehaviour 轮询。
- 成本：blocking-IO。`DrainPendingWrites` 入队屏障并 `drained.Wait(_shutdownDrainTimeout)`，默认 2500ms（`:11`）。2.5s 是**等待上限非典型成本**——正常游玩队列近空、屏障近零返回；全冻结仅在积压+SQLite stall 时出现，且 run-log 与 RunSyncState 是两个独立 DB（`ReplicatedRunLogStore.cs:9-52`），自竞争有限。最坏落在 run-end 屏幕过渡帧（本就是场景切换）。
- 修复建议：把终结写（CompleteRun/MarkRunAbandoned）追加到同一后台 worker（append terminal work item 到 `_pending`）而非同步屏障+内联 inner-store——保序不阻塞。若必须保留同步屏障用于 shutdown 保序，则只在 Dispose/teardown 用阻塞排空；至少把 run-end 排空超时降到远低于 2500ms。

**10. 事件遭遇 tooltip 格式化 `{ability.*}` 占位符时，未缓存的 GetType().GetProperty() 反射链逐次解析**
`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionLocalizationResolver.cs:164-260`
- 触发频率：每次事件遭遇 tooltip 填充（per-hover，冷路径），且四重门全过：`EnableEventPreview` 配置开（默认关，`EventPreviewGate.cs:8-11`）、非战斗、卡为 `ECardType.EventEncounter`、描述文本含 `{ability.`（`:65`）。经 `EncounterEventTooltipPatch.Postfix`（`:32`）→ …→ `FormatAbilityPlaceholders`（`:63`）。非每帧、非 per-net-message。
- 成本：reflection。`TryResolveAbilityValue`/`TryResolveFromCardAttributes`/`TryReadEntry`（`:164,182-183,187-192,196,205-207,243,321-322`）均 `GetType().GetProperty(name)?.GetValue(...)` 无缓存；一个占位符触发 ~4-10 次未缓存 type-scan + GetValue 装箱，`Regex.Replace` 回调每占位符重跑。内容未 memoize（`BppTooltipSections.cs:26-44` 仅缓存克隆的 GameObject）。
- 修复建议：把解析出的 PropertyInfo 按 `(Type, memberName)` 缓存到静态 `ConcurrentDictionary`（照搬 `GameInterop/BppClientCacheBridge.cs:194-208` 的 MemberAccessor 模式）；或对稳定 publicized 游戏类型用直接成员读。低紧急度——冷路径且被 `{ability.` 子串门守。

## 3. 快速见效项 (Quick Wins)

按 影响/成本比 排序，优先做：

1. **OverlayPanelHost 三合一（问题 1）** — 单文件、三处小改、行为保持，一次性消除全会话每帧约 4-5 个分配。最高性价比。
2. **EndOfRunScreenshotController 扫描节流（问题 2）** — 加一个 `_nextScanRealtime` 节流即可，消除默认开启功能在整会话每帧的场景遍历+数组分配。仓库内已有现成模式可抄。
3. **SQLite schema 去重初始化（问题 4）** — 加一个按 DB 路径的静态守卫，`EnsureInitialized` 已幂等，直接砍掉插件加载时 4 次多余的主线程连接打开+WAL 建立。
4. **ItemBoardPreviewSurface 复用 `Vector3[4]`（问题 6）** — 提升为实例字段一行改动，消除悬停每帧最多 4 个小数组分配。
5. **BppHotkeyService NormalizeBindingPath 短路（问题 5）** — 加一个 already-normalized 短路分支，消除每帧约 6 个字符串分配。

## 4. 系统性建议

**A. 多个 always-on `Update()` 无节流地每帧重算 / 分配（问题 1、2、5、6、8）**
这是本次审计最普遍的根因：多个组件挂在插件常驻 GameObject 上，`Update()` 无 early-return / dirty-flag / 节流，全会话每帧执行。统一治理：
- 对「结果只在稀有事件后变化」的每帧计算（场景令牌、结算屏查找、hotkey 路径规范化、卡片 native bounds），一律加**脏标记/缓存**，仅在触发源变化时重算。
- 对「本质是场景作用域」的扫描（VersionLabelScanner、EndOfRunScreenshot），改由 **sceneLoaded/activeSceneChanged 事件驱动**或**时间节流**，而非每帧轮询。仓库内 `VersionLabelScanner.cs:11` 的 `_nextScanAt`/`ScanIntervalSeconds` 已是可复用节流范式。

**B. 每帧的隐性堆分配：空集合、接口枚举器、临时数组（问题 1、6、8）**
Mono 上这些是实打实的 Gen0 压力。统一手法：
- 无结果快路径返回**共享静态 `Array.Empty<T>()`**，首个元素才惰性 new。
- 热路径 `foreach` 参数用**具体 `List<T>` 或 `for` 按索引**遍历，避免通过 `IReadOnlyList<>` 装箱 struct 枚举器。
- per-frame 的 `new Vector3[N]` 等 scratch buffer 一律提升为**可复用实例字段**（仓库已有 `_flipRowBuffer` 先例），前提是被调 API 每次全覆写。

**C. 热路径/半热路径反射未缓存（问题 10，及 B 的 DFS 场景走查同属「未缓存的重复解析」）**
`GetType().GetProperty(name)` 逐次 type-scan + `GetValue` 装箱。统一改为按 `(Type, memberName)` 缓存 `PropertyInfo`（`BppClientCacheBridge.cs:194-208` 的 MemberAccessor 已是仓库范式），或对稳定 publicized 游戏类型直接成员读。目前该问题只落在冷路径故低优，但同一范式应在任何进入热路径的反射处强制执行。

**D. Harmony 分发缝上的 `object[] __args` 注入（问题 3）**
在每消息/每调用都经过的缝上，声明 `object[] __args` 会让 HarmonyX 在序言里 `new object[N]`+装箱值类型，发生在类型门之前、无条件付费。统一原则：热缝补丁**按位注入所需参数（`__0`/`__1`）**或把参数解析上提到缝内暴露，**永不**为读单个参数而请求整个 `__args` 数组。

**E. 主线程同步阻塞 IO（问题 4、9）**
已有后台队列的写入不应在主线程同步屏障排空；一次性 schema 初始化不应在 `Awake` 冻结帧内重复跑。原则：**幂等初始化按资源去重**；**已排队的写入 fire-and-forget**，仅在 Dispose/teardown 才用阻塞排空，并给任何主线程 `Wait` 设一个远小于 2.5s 的上限。

## 附：被驳回的误报

1 条候选发现在对抗性验证中被判定为**非问题**并剔除，未计入上述清单（验证器确认其落在冷路径或已被现有守卫缓解）。

---

## 实测更新（2026-07-08，探针两轮采集后）

以下结论基于主路径临时探针的游戏内实测（~118fps 机器），**修正了上文的多处定级**：

1. **#2（EndOfRun 每帧扫描）实为 Critical，已修复并验证。** 基线实测单次扫描 0.29ms（菜单）→1.98ms（14 分钟局内）→3.6ms（长局后期），随场景增长，总成本占一核 18%+（后期可达 42%）。修复：miss 路径按 `ControllerScanIntervalSeconds = 0.5f` 节流。游戏内验证：20 scans/10s（基线 ~1180），总成本 <1% 一核；结算屏检测、截图捕获与落盘、continue 全链路无回归。
2. **#4（SQLite 重复初始化）撤销，不值得做。** 实测首个 store 359ms 是**部署新 dylib 后首次启动的一次性成本**（第二次启动同店仅 16.1ms），后续 7 个 store 各 0.2-0.6ms——"去重"只能省 ~2ms，"推迟首开"稳态只值 ~16ms。启动真正大头是 Harmony 打补丁 ~465ms（25 个补丁类固有成本）。
3. **#9（run-end 排空阻塞）定案为非问题。** 实测一次完整 run 结束：`drain=0.2ms (pending=0) write=0.7ms`，主线程阻塞 <1ms。2.5s 是正常游玩打不到的超时上限，无需修复。
4. 分配类发现（#1/#5/#6 等）维持"卫生问题"定级：游戏启用了增量 GC（boot.config `gc-max-time-slice=3`），<1KB/帧的分配不可感知，可在顺手时统一治理。

方法论教训：静态分析对"频率 × 单次成本"的估计可双向偏差一个数量级（#2 被低估、#4 被高估），性能修复决策应以主路径探针实测为准。
