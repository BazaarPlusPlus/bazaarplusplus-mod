# Monster Preview

## Scope

本文只描述当前 shipped 的怪物预览实现，不再保留旧调研和一次性设计过程。

## Runtime Entry

`Plugin.cs` 当前挂载：

- `MonsterPreviewController`
- `MonsterPreviewWarmupController`
- `MonsterLockShowcaseRuntime`

主流程：

```text
CardTooltipController.LockTooltipToggle()
  -> ShowcaseTooltipPatches
  -> MonsterLockShowcaseRuntime
  -> MonsterPreviewController
  -> MonsterPreviewOverlayCoordinator
  -> PreviewBoardSession
  -> MonsterPreviewBoardRenderTarget
  -> MonsterPreviewBoard
```

## 当前行为

- 右键锁定怪物 / 遭遇牌时，Bazaar++ 可拦截原生 lock-toggle，展示自定义怪物预览。
- `MonsterLockShowcaseRuntime` 先尝试从 `MonsterDatabase` 构建预览数据。
- 若静态数据缺失，则回退到 `EncounterTracker` + `EncounterPreviewSpecConverter` 的运行时缓存。
- 重复触发同一目标时会走隐藏 / 下一次点击关闭的控制逻辑。
- `UseNativeMonsterPreview` 设置开启时，运行时会让回原生预览路径。

## 关键文件

- `Plugin.cs`
- `Patches/Showcase/ShowcaseTooltipPatches.cs`
- `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`
- `Game/MonsterPreview/MonsterPreviewController.cs`
- `Game/MonsterPreview/MonsterPreviewWarmupController.cs`
- `Game/MonsterPreview/MonsterPreviewModeSwitchCoordinator.cs`
- `Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs`
- `Game/MonsterPreview/Architecture/PreviewBoardSession.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`
- `Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs`
- `Game/EncounterTracker.cs`
- `Game/EncounterPreviewSpecConverter.cs`

## Debug

- 当前 debug build 不再单独挂载 `MonsterPreviewDebugController`。
- 与怪物预览相关的 debug 可见性主要通过 `DebugPanel` 的 `Encounters` 区域和 `Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs` 协助排查。
