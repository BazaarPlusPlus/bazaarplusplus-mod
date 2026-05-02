# Combat Status Bar

## Scope

当前实现会在战斗期间显示一个底部 HUD。运行时职责分为两部分：

- `Game/CombatStatusBar/CombatStatusBar.cs`：UI、状态、速度和输入
- `Game/CombatStatusBar/CombatStatusBarModule.cs`：消费 combat 事件并推进状态

## 当前行为

- 仅在 `IBppServices.RunContext.IsInGameRun` 且功能开启时显示。
- 展示逻辑战斗时间与当前 frame index。
- 支持暂停和 0.50x / 0.67x / 1.00x 速度档位。
- 记住功能开关和默认速度档位。

不支持：

- frame stepping
- rewind
- 自定义 replay 控件

## 逻辑时间

逻辑时间定义为：

`当前 zero-based frame index * 50ms`

因此显示值跟随模拟进度，而不是墙钟时间；暂停不会让时间标签失真。

## Runtime Flow

1. `Patches/Combat/CombatSimulationPatches.cs` 发布 `CombatSimObserved` 和 `CombatFrameAdvanced`。
2. `CombatStatusBarModule` 记录总帧数并同步处理进度；HUD frame 文本显示 zero-based 当前帧序号。
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
