---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Combat Status Bar

## Scope

当前实现会在战斗期间显示一个底部 HUD。运行时职责分为两部分：

- `Game/CombatStatusBar/CombatStatusBar.cs`：UI、状态、速度和输入
- `Game/CombatStatusBar/CombatStatusBarModule.cs`：消费 combat 事件并推进状态

## 当前行为

- 仅在功能开启时显示；live run（`RunContext.IsInGameRun` 缓存为 true）与录制回放（缓存为 false 时回退 `GameStateProbe.ComputeIsInGameRun()`，把 `ReplayState` 也报告为 in-game-run）均会显示，lobby / 主菜单不显示（`CombatStatusBar.State.cs:111-133`）。
- 展示逻辑战斗时间（elapsed time）。
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
2. `CombatStatusBarModule` 记录总帧数并同步处理进度；`CombatStatusBar.State` 将帧号转换为逻辑耗时（`frameIndex * 50ms`）供 HUD 显示。
3. `CombatStatusBar` 在 `Update()` 中刷新 HUD。

## 关键文件

- `Plugin.cs`
- `BppComposition.cs`
- `Core/Config/BppConfig.cs`
- `Game/CombatStatusBar/CombatStatusBar.cs`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs`
- `Game/CombatStatusBar/CombatStatusBar.State.cs`
- `Game/CombatStatusBar/CombatStatusBar.Config.cs`
- `Game/CombatStatusBar/CombatStatusBar.RoundedSprite.cs`
- `Game/CombatStatusBar/CombatStatusBar.SettingsMenuBridge.cs`
- `Game/CombatStatusBar/CombatStatusBar.SettingsMenuLabel.cs`
- `Game/CombatStatusBar/CombatStatusBarModule.cs`
- `Game/CombatStatusBar/CombatStatusBarSettingsDockEntry.cs`
- `Game/Settings/BppSettingsDockCatalog.cs`
- `Patches/Combat/CombatSimulationPatches.cs`
