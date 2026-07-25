# 导入 Replay 的原生 PVP 展示态不完整：根因与验收

## 背景

导入保存的 PVP 战斗时，插件会从普通游戏状态直接 push 原生 `ReplayState`，而不是先进入原生 `PVPCombatState`。2026-07-23 的实机截图显示：

- 对手头像使用商店态的大尺寸位置，压住原生对手生命条；
- 对手 stash / bank / board / carpet 没有稳定加载，部分回放看不到对手箱子；
- 对手钱袋仍处于激活状态，盖在原生 `Continue` / Replay 控件上。

这不是 HTML Viewer、报告素材导出或单个 `sortingOrder` 的问题。此前“技能图标轮流在画面中央闪现”属于另一条素材物化生命周期问题；本截图中的箱子、钱袋、头像和水晶均是原生战斗板对象，必须沿 Replay 状态链修复，不能混为一谈。

## 已验证根因

### 1. 对手头像在错误的锚点下实例化

`CombatReplayRuntime.StartReplayAsync` 在进入 `ReplayState` 前调用
`EnsureTemporaryOpponentPortraitAsync`。此时 `BoardManager.GetAnchor(Opponent, Portrait)`
仍返回商店态锚点，所以 `BoardBuilder.LoadHeroPortraitAsync` 把临时头像挂在商店态。

`TransitionInComponent.ProcessReplayStateState` 随后调用
`BoardManager.SetPortraitFrame(true)`，但该方法只切换
`opponentPortraitAnchor` 变量和战斗框，不会把已经实例化的头像重新挂到新锚点。
因此头像继续使用商店态的 transform，尺寸和纵向位置都不对，并覆盖生命条。

### 2. Replay 控件按 PVE 分支切换

原生 `ReplayState.OnEnter` 会缓存
`_wasPVPCombat = AppState.PreviousState is PVPCombatState`；
`BoardManager.ShowReplayAndRecapButtons` 也用同一判断决定钱袋和默认按钮的显隐。

插件从普通状态直接 push `ReplayState`，所以 `PreviousState` 不是
`PVPCombatState`。原生逻辑因而：

- 战斗阶段没有按 PVP 语义隐藏遗留的默认 `Continue`；
- Replay / Recap 控件出现时没有隐藏对手钱袋。

钱袋和按钮只是占用了同一原生位置；提高或降低其中一个的层级只会把错误换成另一种遮挡。

### 3. 对手收藏品初始化被跳过

`TransitionInComponent.ProcessPVPCombatState` 是原生唯一调用
`BoardManager.LoadOpponentCollectibles` 的入口。保存 Replay 直接进入
`ReplayState`，因此不会执行这一步。

此外，插件原先看到 spawn message 已有 `PvpOpponent` 时会直接从
`EnsureOpponentIdentity` 返回，却没有先把该对象写入 `Data.SimPvpOpponent`。
临时头像和收藏品准备因而只能看到空 loadout，丢失准确的英雄皮肤、stash、bank、
board 和 carpet 身份。

### 4. 第一次补丁仍在错误的生命周期阶段加载

第一次补丁在 `ReplayState` 入栈前调用 `LoadOpponentCollectibles`，并把该方法的
“内部缓存非空时直接返回”误当成了足够的幂等保证。这仍然不等价于原生
`ProcessPVPCombatState`：

- 加载时当前状态还不是战斗态，board / portrait 仍可能按商店态初始化；
- `BoardManager.opponentCollectables` 可能仍指向上一段原生展示留下的对象，
  因而本次调用成功返回却没有为当前录像重新物化；
- 临时头像也在战斗 frame 切换前创建，后续只能依赖补挂锚点修正。

因此验收不能只看“调用了原生加载函数”或“没有异常”，必须同时验证当前录像的
loadout 数据、stash / bank 实例、激活状态和 portrait parent。

## 修复策略

把保存 Replay 的 PVP 展示恢复拆成一次“进入战斗态时的重建”和可重复执行的
“控件显隐归一化”：

1. 强制 `BoardManager.SetPortraitFrame(true, force: true)`；
2. 先让原生 spawn handler 恢复完整 `Data.SimPvpOpponent`，并在
   `ReplayState` 已入栈后清理遗留的 `opponentCollectables`，跨一帧后按当前
   loadout 重新调用 `BoardManager.LoadOpponentCollectibles`；
3. 在战斗 frame 已生效后创建插件临时对手头像；补挂锚点只保留为防御性归一化；
4. Replay 控件隐藏（战斗播放）时：显示对手钱袋、隐藏默认商店/战斗按钮；
5. Replay 控件显示（播放结束）时：隐藏对手钱袋、继续隐藏默认按钮；
6. 对手 stash 在播放中和播放结束后都保持可见；
7. 输出 Debug 结构化观测：loadout 是否携带 stash / bank ID、
   `PlayerCollection` 数量、对应原生实例是否存在及是否激活，以及 portrait
   是否位于战斗锚点；
8. 不修改 `AppState.PreviousState`，不伪造或提前触发
   `PVPCombatState` / `ReplayStarted`，避免把视觉修复扩散成状态语义修改。

## 验证方法

- 用同一场 imported ghost 从 F8 启动回放，截取战斗开始后 1–3 秒：
  - 对手头像应与正常 PVP Replay 使用同一尺寸和纵向位置；
  - 对手生命条与数值不得被头像遮挡；
  - 对手 stash、bank、board 与 carpet 应来自该对手的完整 loadout；
  - 对手钱袋可见，但钱袋下不得出现 `Continue` 或其他默认按钮文字。
- 播放结束后：
  - Replay / Recap 控件完整可点击；
  - 对手钱袋不得遮挡这些控件。
- 点击原生 Replay 再播放一次，以上显隐关系必须保持，不得只修复第一次。
- 回归普通 PVE、正常在线 PVP 和非导入 Replay，补丁必须完全不生效。

## 修复接受条件

- 保存 Replay 的头像 parent 等于
  `BoardManager.GetAnchor(Opponent, Portrait)` 返回的战斗态锚点。
- 对手收藏品在 `ReplayState` 入栈后通过原生 `LoadOpponentCollectibles` 从当前
  loadout 重建；后续按钮显隐归一化不得再次物化。
- Debug 观测中，载荷有 stash / bank ID 时，对应实例必须存在；无自定义 ID 时也
  必须由原生默认资产生成可见实例。
- 战斗播放时默认 Board buttons 全部隐藏，对手钱袋可见。
- 播放结束时默认 Board buttons 仍隐藏，对手钱袋隐藏。
- 正常游戏路径不受影响。
