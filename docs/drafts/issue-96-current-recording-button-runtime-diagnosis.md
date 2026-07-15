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
