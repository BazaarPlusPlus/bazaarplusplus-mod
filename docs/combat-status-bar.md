# Combat Status Bar

## Scope

当前实现会在战斗期间显示一个底部 HUD。运行时职责分为两部分：

- `Game/CombatStatusBar/CombatStatusBar.cs`：UI、状态与输入
- `Game/CombatStatusBar/CombatStatusBarModule.cs`：消费 combat 事件并推进状态

## 当前行为

- 仅在 `BppRuntimeHost.RunContext.IsInGameRun` 且功能开启时显示。
- 展示逻辑战斗时间与已处理帧数。
- 支持暂停。
- 只支持离散倍速：`0.25x`、`0.33x`、`0.50x`、`1.00x`。
- 记住功能开关与默认倍速。

不支持：

- frame stepping
- rewind
- 自定义 replay 控件
- 高于原生路径的任意倍速覆盖

## 逻辑时间

逻辑时间定义为：

`ProcessedCombatFrames * 50ms`

因此显示值跟随模拟进度，而不是墙钟时间；暂停和倍速切换不会让时间标签失真。

## Runtime Flow

1. `Patches/Combat/CombatSimulationPatches.cs` 发布 `CombatSimObserved` 和 `CombatFrameAdvanced`。
2. `CombatStatusBarModule` 记录总帧数并同步处理进度。
3. `CombatStatusBar` 在 `Update()` 中刷新 HUD。

## 关键文件

- `Plugin.cs`
- `Core/Config/BppConfig.cs`
- `Game/CombatStatusBar/CombatStatusBar.cs`
- `Game/CombatStatusBar/CombatStatusBar.State.cs`
- `Game/CombatStatusBar/CombatStatusBar.Config.cs`
- `Game/CombatStatusBar/CombatStatusBarModule.cs`
- `Game/CombatStatusBar/CombatStatusBar.SettingsMenuBridge.cs`
- `Game/Settings/BppSettingsDockCatalog.cs`
- `Patches/Combat/CombatSimulationPatches.cs`
- `Patches/Combat/CombatSpeedPatch.cs`
