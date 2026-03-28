# BazaarPlusPlus

BazaarPlusPlus 是一个面向 **《The Bazaar》** 的 **BepInEx 增强模组**。当前仓库里的实现主要围绕战斗 HUD、怪物预览、tooltip 增强、run 记录，以及若干调试 / 设置集成展开。

## 功能

**战斗状态条**

底部 HUD，显示逻辑战斗时间、已处理帧数，并提供暂停与离散倍速控制。功能通过游戏内 Gameplay Settings 开关控制。

**怪物预览**

右键锁定怪物 / 遭遇牌时显示敌方物品与技能面板。数据优先来自 `MonsterDatabase`，缺失时回退到 `EncounterTracker` 的运行时缓存。

**附魔 / 升级预览**

在原生主 tooltip 上追加附魔预览，或在按住升级预览热键时复用游戏原生 upgrade-preview 路径。两种 tooltip 动作都支持在设置菜单里改绑键位，支持键盘和鼠标按钮。

**Run Logging 与历史面板**

活跃 run 会持续写入 SQLite；游戏内可通过 `HistoryPanel` 浏览最近 runs、关联的 PVP battles，以及保存的战斗快照预览；离线可用 `scripts/export_run_log.py` 导出。

**战斗录像保存 / 调试回放**

`CombatReplayRuntime` 会持续持久化 PVP replay payload；常规 UI 里的 `HistoryPanel` 会在选中 battle 且 payload 仍存在时启用 `Replay`。已保存的 replay 可以在 lobby 中重新 bootstrap 到原生 replay 流程。

**匿名模式**

可将本地玩家名在 `HeroBannerController` 上替换为 `Anonymous`，并通过游戏内 Gameplay Settings 开关控制。

## 构建与安装

发布包使用配套安装器；本仓库只描述代码里可见的构建行为，不声明最新发布范围。

- Debug 构建会在检测到常见 Windows Steam 安装目录或 macOS 默认 Steam 目录时，把插件复制到游戏的 `BepInEx/plugins`。
- Release 构建在相邻 `../bazaarplusplus-installer` 仓库存在时，会把 DLL、`.version` 文件和托管的 SQLite 运行时程序集复制到 installer 资源目录。
- 项目目标框架是 `netstandard2.1`，依赖 Bazaar 游戏程序集、`Microsoft.Data.Sqlite`、`Newtonsoft.Json` 和 BepInEx 5。

## 开发者

当前代码入口和活跃模块主要在 `Plugin.cs`、`Core/`、`Game/`、`Patches/`、`Data/`、`scripts/`、`tests/`。文档入口见 `docs/README.md`。

## 致谢

### 灵感来源

以下项目为 BazaarPlusPlus 提供了重要的参考：

* [https://github.com/Duangi/BazaarHelper](https://github.com/Duangi/BazaarHelper)
* [https://github.com/oceanseth/BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)

### 数据来源

部分怪物与相关展示信息参考自：

* [https://bazaardb.gg](https://bazaardb.gg)

### 核心依赖

BazaarPlusPlus 基于以下项目运行：

* [https://github.com/BepInEx/BepInEx](https://github.com/BepInEx/BepInEx)

---

# BazaarPlusPlus

BazaarPlusPlus is a **BepInEx enhancement mod for *The Bazaar***. The current repository is centered on combat HUDs, monster preview overlays, tooltip enhancements, run logging, and a small set of debug / settings integrations.

## Features

**Combat Status Bar**

Bottom HUD with logical combat time, processed-frame count, pause, and discrete speed control. The feature is exposed through the in-game gameplay settings.

**Monster Preview**

Right-clicking a monster / encounter card locks an overlay that shows the enemy item and skill board. Data comes from `MonsterDatabase` first, then falls back to live `EncounterTracker` cache when static coverage is missing.

**Enchant / Upgrade Preview**

Adds enchant preview lines to the native primary tooltip, or reuses the game's native upgrade-preview path while the upgrade modifier is held. Both tooltip actions are rebindable from the settings menu and support keyboard plus mouse buttons.

**Run Logging And History Panel**

Active runs are captured into SQLite; the in-game `HistoryPanel` browses recent runs, linked PVP battles, and stored battle snapshot previews; `scripts/export_run_log.py` handles offline export.

**Optional Background Run Upload**

Run upload is opt-in and disabled by default. When enabled, completed runs are registered and uploaded in the background outside live gameplay; local SQLite remains the source of truth. See `docs/run-upload.md`.

**Saved Replay / Debug Playback**

`CombatReplayRuntime` continuously persists PVP replay payloads; the normal `HistoryPanel` enables `Replay` when the selected battle still has a saved payload. Saved replays can be bootstrapped from the lobby back into the native replay pipeline.

**Anonymous Mode**

Optionally replaces the local player's hero-banner name with `Anonymous`, with an in-game gameplay setting toggle.

## Build And Install

Release packages are installed through the companion installer; this repository only documents build behavior that is visible in source.

- Debug builds auto-copy the plugin into `BepInEx/plugins` when a common Windows Steam install or the default macOS Steam install path is detected.
- Release builds copy the DLL, `.version` file, and managed SQLite runtime assemblies into the adjacent `../bazaarplusplus-installer` resources when that repository exists.
- The project targets `netstandard2.1` and depends on Bazaar game assemblies, `Microsoft.Data.Sqlite`, `Newtonsoft.Json`, and BepInEx 5.

## For Developers

Current entry points and active modules live under `Plugin.cs`, `Core/`, `Game/`, `Patches/`, `Data/`, `scripts/`, and `tests/`. The doc index is `docs/README.md`.

## Credits

### Inspiration

These projects provided important inspiration and references:

* [https://github.com/Duangi/BazaarHelper](https://github.com/Duangi/BazaarHelper)
* [https://github.com/oceanseth/BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)

### Data Source

Some monster and related display information references:

* [https://bazaardb.gg](https://bazaardb.gg)

### Core Dependency

BazaarPlusPlus runs on top of:

* [https://github.com/BepInEx/BepInEx](https://github.com/BepInEx/BepInEx)
