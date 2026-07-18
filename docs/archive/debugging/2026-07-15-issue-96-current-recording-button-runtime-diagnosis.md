---
status: implemented
archived: 2026-07-18
superseded-by: code (PR #105, CurrentReplayRecordingButtonController) + docs/MEMORY.md gotcha
---

# Issue #96 战后录制按钮二次实机失败诊断

## 背景

#96 为当前战斗的原生 replay 增加一次点击录制入口。录制状态机以当前 artifact 的稳定 battle ID 为主键，按钮只应在 `ReplayState` 且已锁定 battle ID 时可见（`src/BazaarPlusPlus/Game/CombatReplay/CurrentReplayRecordingState.cs:222`）。视觉入口按用户要求放在右下角设置按钮组，原生 Replay 按钮只提供点击命令（`src/BazaarPlusPlus/Patches/Combat/CurrentReplayRecordingButtonPatch.cs:13`、`:35`）。

首次实机失败后，按钮从顶部 Replay 容器迁移到 `FightMenuDialog.SettingButton`。2026-07-15 17:49 的第二次实机仍未显示按钮；截图版本为 `BPP 4.5.0.t20260715.172837.dev`，确认运行的是修订后的部署。

下文对旧 controller 行号的引用用于描述当时实机运行的 `f00a59f3`；修复后的当前源码已把 lifecycle owner 移到 settings button，并由本文件的验证项约束。

## 2026-07-15 第三次实机结果

`4.5.0.t20260715.180656.dev` 已证明主流程可用：按钮进入 `ready`、点击后进入 `recording`，最终产生 40.392 秒、106,368,564 字节的视频并写入数据库。不过实机同时暴露两个 UI 缺陷：

1. 按钮所有状态均无图标。`LogOutput.log` 精确记录 `CreateGlyph` 调用 `NativeGameTypography.OwnedTextPreparation.Apply` 时传入了 null。实现把 `TextMeshProUGUI` 加到已经承载原生 `Image` 的同一个 GameObject；第二个 Unity UI `Graphic` 没有成功创建，但代码仍继续应用字体。修复为保留原生 icon 的 RectTransform 作为布局槽，并在其下创建独立 glyph 子对象，同时对 `AddComponent` 结果做 null guard。
2. Tooltip 被夹到屏幕最左侧。实现传入了 `_cloneRect.position + offset`，而游戏的 `AuxiliaryTooltipController.WorldToScreenPositionAfterAFrameCoroutine` 又计算 `targetObject.position + offset`，导致按钮世界坐标被累加两次。修复为只传入由 `_cloneRect.TransformVector(...)` 得到的相对世界空间偏移。

用户还反馈视频结束略早。当前录像严格在 `CombatReplayPlaybackEnded` 发布时停止，没有 post-roll；这是共享录制器的时间窗口策略，不属于 #96 的战后入口/UI 接线，留作独立后续讨论。

## 2026-07-15 第四次实机失败：入口整体消失与并发部署冲突

### 已确认事实

1. 日志记录 `phase=ready snapshot_visible=true layout_available=false layout_reason_code=target_footprint_unavailable clone_active=false native_replay_bound=true`。状态机和原生 replay 绑定均正常，入口被布局 fail-closed 隐藏。
2. `d7305f40` 把新建的 TMP glyph 设为 `Button.targetGraphic`，布局又从 `targetGraphic.rectTransform` 测量按钮 footprint。嵌套 glyph 的 RectTransform 无法提供有效屏幕尺寸，因此该改动本身会稳定触发 `target_footprint_unavailable`。
3. 同时存在另一个 Codex task 在同一主 worktree 构建/部署。#96 在 18:47 部署的 DLL 为 4,332,544 字节、SHA-256 `d67fcfc…`；安装目录中的 DLL 在 19:18 被替换为 4,457,984 字节、SHA-256 `277403ee…`，但外置 `BazaarPlusPlus.version` 仍停留在 `4.5.0.t20260715.184633.dev`。因此截图版本标签不能证明实际加载 DLL 的来源，并发 task 确实造成了部署污染。

### 方案决策

- 不再使用 TMP glyph；复用书本按钮既有的 `BppDockButtonVisuals` / `Image sprite` 视觉链路，保留原生 settings button clone 的根 `Image` 作为 `Button.targetGraphic`。
- 为导出、录制、查看、重试四种状态增加内嵌 PNG sprite；按钮状态变化只替换原生 icon `Image.sprite`，不改变按钮层级和 footprint。
- current-recording UI observation 增加 `icon_available`，下一次实机可直接区分资源加载失败和布局失败。
- 构建、测试和提交只在独立持久 worktree 中进行；部署前后校验 DLL hash，并把 DLL 来源与版本文件同时核对。

### 验证方法

- Architecture.Tests 强制禁止 current replay controller 引入 TMP/`NativeGameTypography` 或把 glyph 设为 `targetGraphic`，并要求使用现有 dock sprite pipeline。
- Release build 后检查五个 dock PNG 的 manifest resource name，部署前后核对产物与安装 DLL 的 SHA-256。
- 用户实机验收：入口可见、四种状态图标可见、Tooltip 位于按钮附近、完成后可查看视频；日志中应为 `layout_available=true icon_available=true`。

## 2026-07-15 第五次实机失败：圆形按钮出现白色方形底板

### 已确认根因

`4.5.0.t20260715.215542.dev` 中入口和导出图标均已显示，但截图明确出现一个大于圆形按钮的白色正方形底板。该白色区域不是 PNG 自带背景，而是 controller 为了稳定 footprint 执行 `clone.AddComponent<Image>()` 后留下的默认 Unity `Image`：默认 color 为不透明白色，且随后被设为 `Button.targetGraphic`。这条手写路径没有复用书本按钮的完整 native visual state 恢复流程，因此把用于点击/布局的透明 fallback frame 渲染成了可见底板。

### 候选方案与决策

1. **仅把新 root Image 设为透明**：可以消除静态白底，但若它继续作为 `ColorTint` 的 `targetGraphic`，Unity `Selectable` 状态转换仍可能把它改回不透明颜色，不能作为稳定方案。
2. **采用书本按钮的完整流程（选定）**：在剥离原生 controller 前捕获 `BppDockButtonVisualState`，新建 root fallback Image 时设为透明，再调用 `BppDockButtonVisuals.Apply(...)` 恢复原生 target graphic、transition、sprite state 和圆形视觉；状态 PNG 只替换原生 icon Image。
3. **继续手写 frame/color 状态**：会复制 `BppNativeSettingsButtonClone` 已解决的逻辑，且已连续造成 footprint 与白底两个回归，不采用。

### 验证方法

- 源码约束 current replay controller 必须捕获 `BppDockButtonVisualState` 并调用 `BppDockButtonVisuals.Apply`，禁止直接把新 root Image 赋给 `targetGraphic`。
- 构建部署后，用户在当前战后界面验证：只显示与书本/齿轮同族的圆形 frame，不存在白色方形底板；导出 icon 居中，hover 不改变方形背景。

## 2026-07-15 第六次实机失败：Tooltip 仍被定位到屏幕左侧

### 已确认根因

继续对照现有自定义按钮和反编译原生实现后，确认书本 dock 按钮本身没有 tooltip；current replay 是唯一手写 `IPointerEnterHandler + ShowAuxiliaryTooltipController` 的 dock 按钮。此前虽然把“绝对坐标 + offset”改成了 `TransformVector`，但仍在调用错误的定位入口：`AuxiliaryTooltipController.WorldToScreenPositionAfterAFrameCoroutine` 会用 `Camera.main` 把 `target.position + offset` 当作**世界坐标**转成屏幕坐标，而 settings dock 的 `RectTransform.position` 已处于 screen-space canvas。把 screen-space UI transform 送进 world-space API 会产生错误屏幕点，最终被 `KeepTooltipWithinBounds` 推到边缘。

反编译的同一原生 controller 已提供专门的 `PositionOverUI(Transform uiTransform)`，它通过 tooltip rect 的本地坐标系对齐 screen-space UI，但 `TooltipParentComponent.ShowAuxiliaryTooltipController` 没有暴露该分支。

### 方案决策

- 保留原生 tooltip 的内容、frame、显示/隐藏生命周期；调用 Show 后在主路径 coroutine 等待原生 controller 生成并完成一帧布局。
- 使用原生 `AuxiliaryTooltipController.PositionOverUI` 进入 UI 坐标路径，再根据 button/tooltip 的 world corners 将 tooltip 排到圆形按钮左侧，最后调用原生 `KeepTooltipWithinBounds`。
- 不再向 world-space Show API 传入推算后的 UI offset，也不复制一套 tooltip prefab。

### 验证方法

- 架构测试要求使用 `PositionOverUI`，并禁止 `TransformVector` tooltip offset。
- 实机 hover 时 tooltip 应紧邻圆形按钮左侧；窗口尺寸变化或按钮处于屏幕右下角时仍在边界内，pointer exit 后正常隐藏。

### 2026-07-15 预览复测：UI 定位被原生 coroutine 覆盖

`4.5.0.t20260715.231647.dev` 中首次 hover 的 Tooltip 仍落到左侧；再次 hover 后虽然移动到按钮附近，却覆盖按钮和鼠标。实现虽调用了 `PositionOverUI`，但还有两个时序错误：首次加载时原生 tooltip 异步生成可能超过 BPP 固定等待的 8 帧，BPP coroutine 先退出，错误 world-space 位置无人纠正；后续加载时 BPP 只在 fade/zoom 动画早期按较小 rect 计算一次左侧间距，随后 tooltip 放大却不再重新布局，最终向右覆盖按钮。

修订为整个 hover 生命周期持续等待与布局：不设固定帧数上限；原生 `_coroutine` 活跃时等待，idle 后每个 `WaitForEndOfFrame` 都重新执行 `PositionOverUI + 相对布局 + bounds clamp`，直到 pointer exit。这样既覆盖首次异步 spawn，也跟随 fade/zoom 后的实际 tooltip corners。

用户进一步明确最终位置应在被 hover 的圆形按钮**正上方**，不能覆盖 icon/button。布局因此改为读取原生 `_contentForWorldBounds` 的真实视觉边界，将 tooltip 的 bottom edge 放到 button top edge 之上并保留间距；水平方向先中心对齐，再交给原生 bounds clamp 处理右侧越界。

## 2026-07-18 录制与 Recap 状态不同步

### 已确认根因

反编译确认，`ReplayState.Replay()` 播放完毕只恢复 Replay/Recap/Continue 按钮并触发 `Events.ReplayEnded`，不会自动进入 Recap（`decompiled/TheBazaarRuntime/TheBazaar/ReplayState.cs:246-284`）；进入 Recap 的完整原生链路由 `BoardRecapReplayButtonsController.Recap()` 发出事件，再依次调用 `ReplayState.Recap()` 与 `BoardManager.ShowRecapView()`（`decompiled/TheBazaarRuntime/TheBazaar/BoardRecapReplayButtonsController.cs:159-166`、`decompiled/TheBazaarRuntime/BoardManager.cs:3656-3660`）。另一方面，原生 Replay 入口只调用 `ReplayState.Replay()` 并隐藏按钮（`decompiled/TheBazaarRuntime/BoardManager.cs:3649-3653`），不会先走 Back 所调用的 `HideRecapView()` / `ReplayState.RecapBack()`（`decompiled/TheBazaarRuntime/BoardManager.cs:3630-3637`）。于是 current-recording 直接复用 Replay 按钮时存在两个确定行为缺口：录完停留在普通回放面；从 Recap 内起录会让 Recap 的翻板协程与 Replay 的翻板流程重叠。

### 修复决策与验证

- 继续复用游戏的原生按钮动作：patch 同时绑定 Replay、Recap、Back 三个按钮，不复制 `ShowRecapView` / `HideRecapView` 的私有实现。
- 普通状态起录仍同步触发 Replay；Recap 状态起录先触发原生 Back，再等待 `IsRecapViewOpen == false && StorageMoving == false`，之后才触发 Replay，避免两套翻板动画并发。
- current-native 录制收到 `Events.ReplayEnded` 时先触发原生 Recap，继续捕获 3 秒后再发布 recorder 的 ended 事件；这 3 秒作为固定 post-roll，明确包含 Recap 切换与停留画面。post-roll 期间程序化 Continue 会被拒绝，避免自动流程提前退出 ReplayState、截断视频尾部。
- Architecture.Tests 固定三按钮接线、Recap 关闭等待条件和录制结束后的原生 Recap 调用；实机验证需覆盖「普通状态录制后自动进 Recap」与「Recap 状态点击录制时先完整翻回、再开始回放」两条路径。

## 当前问题与已确认事实

1. 该场战斗不是 PvE/教程边界：SQLite 最新记录为 `combat_kind=PVPCombat`、`has_local_payload=1`，exact battle payload 已落盘。
2. 游戏 `Player.log` 显示 17:49:44 从 `PVPCombatState` 进入 `ReplayState`；运行时监听该事件并调用 `EnterReplayState()`（`src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs:779`）。
3. 截图中右下角书本按钮正常显示，证明同一个 `FightMenuDialog.Start` 上的 CollectionPanel patch 和 settings-dock 基础布局能够运行。
4. BepInEx 日志没有 `settings.patch.degraded`，说明当前录制 patch 没有抛出已捕获异常；但 `Attach` 的提前返回、snapshot 状态和布局拒绝目前都没有 Release 可观测性。
5. **已确认根因**：clone 创建后立即 `SetActive(false)`，控制器却被添加在 clone 自身（`CurrentReplayRecordingButtonController.cs:42-46`），因此它不会再收到 `LateUpdate()`。原生 `ReplayState.OnEnter()` 先调用 `TransitionIn.OnReplayStateEnter()`，后者同步显示战后按钮；`AppState.SetState()` 要等 `OnEnter()` 返回后才触发 `Events.StateChanged`。于是 Show postfix 的唯一一次手动 `Refresh()` 发生在 `_replayStateActive=false` 时，clone 保持 inactive；随后状态机进入 ReplayState，但 inactive clone 上的 controller 已没有机会刷新。这个时序完整解释了两次“捕获和 ReplayState 正常、按钮始终不出现”。
6. 当前控制器还把最终可见性写成 `snapshot.Visible && _layoutAvailable`（`CurrentReplayRecordingButtonController.cs:194`），而 `SyncLayout` 丢弃了布局返回的 blocker reason（`:160`）。任一布局测量失败也会静默隐藏按钮，是修复 lifecycle 后必须同时消除的次级不可观测风险。
7. clone 隐藏后又停用原生按钮 icon GameObject（`:99`）；新建 TMP glyph 则铺满 clone 根 RectTransform（`:119`）。屏幕布局需要从目标 Button 的可见 Graphic 计算非零 footprint（`src/BazaarPlusPlus/Game/Settings/BppDockButtonScreenLayout.cs:16`、`:43`）。如果原生设置按钮的非零几何主要来自 icon 子 RectTransform，停用 icon 会令目标 footprint 不可用。这是独立的高风险布局缺陷，应与 lifecycle 根因一并修复。

## 候选原因与排序

1. **inactive clone 同时拥有 lifecycle controller（已确认）**：Show 刷新早于 ReplayState event，之后 controller 随 clone inactive，不再运行。
2. **目标 footprint 被移除（高风险次因）**：TMP glyph 没有复用 icon 的非零 RectTransform；修复 lifecycle 后可能继续导致布局 fail-closed。
3. **current snapshot 未进入 Visible（低）**：已有 artifact 与 ReplayState 两个外部证据，但仍保留 Release observation 以验证内部组合。
4. **按钮已激活但被 Canvas/层级遮挡（低）**：当前 `SetAsLastSibling()` 理论上降低该概率；若日志显示 clone active 再检查屏幕 bounds / canvas。

## 修订方案

- 把 `CurrentReplayRecordingButtonController` 挂到始终活动的原生 settings button，而不是临时 clone；clone 只承载视觉。这样即使按钮当前隐藏，owner 仍会在 `LateUpdate()` 观察 ReplayState、artifact persistence 和 recorder preflight 的后续变化。
- 保留设置按钮 icon 的 GameObject 和 RectTransform，只禁用原 `Image` 渲染；将 TMP 分享 glyph 直接挂到同一个非零 icon RectTransform，并把它设为 Button 的 `targetGraphic`。这样布局和点击都使用已经由原生 prefab 验证过的视觉 footprint。
- 为 current-recording UI 增加低频结构化主路径日志：controller attach、native replay bind、snapshot visible、phase、layout available、layout reason、clone active。只在 observation 变化时记录，避免逐帧刷屏。
- 可见性仍保持 fail-closed，避免布局未知时覆盖原生设置/书本按钮；但布局失败必须有明确 reason，不能继续静默。
- 增加源码架构约束：禁止停用 native icon GameObject；要求 glyph 复用 native icon RectTransform，并要求布局 reason 被保存/记录。

## 验证方法

- 静态与自动化：运行 CSharpier、Architecture.Tests、SettingsDockRegistry.Tests、CombatReplayRecording.Tests 和 Release build；保持 0 build warning/error。
- 部署：只从 clean detached worktree 构建并在确认游戏进程退出后复制 DLL；不启动游戏、不创建或推进新对局。
- 下次用户实机：进入任一 PvP 战后 `ReplayState`，预期右下角书本按钮上方出现圆形分享按钮。
- 若仍失败：直接读取 BepInEx Release 日志中的 current-recording UI observation。验收必须能区分 `controller missing`、`snapshot_visible=false`、`layout_available=false + reason`、`clone_active=true` 四类，不再依赖截图猜测。
- 完整功能：点击按钮后原生 replay 启动；结束后生成 MP4、`combat_replay_videos` exact battle ID 行更新为 `COMPLETED`，按钮切换为查看视频。
