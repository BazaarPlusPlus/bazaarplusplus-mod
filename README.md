# BazaarPlusPlus

BazaarPlusPlus 是一个面向 **《The Bazaar》** 的 **BepInEx 增强模组**，专注于提供更清晰、更顺手的局内信息展示，提升游戏过程中信息获取与决策的效率。

## 功能

**战斗状态条**

提供更直观的战斗播放状态与节奏信息，帮助玩家更清楚地理解战斗节奏。

**怪物预览优化**

在怪物预览界面支持右键查看敌方卡牌与技能信息，补充游戏内默认未展示的内容。

**附魔与词条预览增强**

扩展物品附魔相关的可见信息，使部分原本隐藏或不完整的词条更加直观。

**战斗录像保存与回放（Debug）**

在调试面板中保存并重放最近捕获到的战斗。录像基于原始 `GameSim -> CombatSim -> GameSim` 三件套消息保存，重启游戏后仍可从 Debug Panel 的 `Replays` 区域重新播放。已保存录像的 bootstrap 不依赖正常 run start，只允许在没有 active run 的 lobby 中启动，并通过本地日志与代码路径检查进行验证。

## 安装

使用发布包内附带的 **安装器（Installer）**。

当前仅发布了 **Windows 版本**，macOS 仍在适配中。

对于本地开发构建，依赖 SQLite 的功能当前只支持 **macOS arm64 / Apple Silicon**；**Intel Mac (macOS x64)** 不在支持范围内。

### Windows 安装步骤

1. 下载最新发布包
2. 运行安装器并选择你的 **The Bazaar 游戏目录**
3. 安装完成后重新启动 Steam 和游戏
4. 首次进入游戏时，BazaarPlusPlus 会自动生成配置文件。
5. 如果需要倍速功能，请在游戏内选项菜单中启用 Combat Status Bar，然后重启游戏。

## 开发者

项目基于 **C#、.NET 和 BepInEx 5**。

项目相关代码仍在整理中，后续会逐步补充源码，架构说明与开发指南。

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

BazaarPlusPlus is a **BepInEx enhancement mod for *The Bazaar*** focused on providing clearer, more convenient in-run information displays, improving the efficiency of information gathering and decision-making during gameplay.

## Features

**Combat Status Bar**

Provides more intuitive combat playback status and pacing information, helping players understand the flow of battle more clearly.

**Monster Preview Improvements**

Supports right-clicking in the monster preview interface to inspect enemy cards and skill information, supplementing content not shown by default in the game.

**Enchant & Modifier Preview Enhancements**

Expands the visible information related to item enchants, making some originally hidden or incomplete modifiers more intuitive.

**Saved Combat Replay (Debug)**

Captures and persists raw `GameSim -> CombatSim -> GameSim` combat triplets, then exposes them in the debug panel so a saved fight can be replayed after restarting the game. Saved replay bootstrap no longer depends on a normal run start, remains restricted to the lobby with no active run, and is verified through local logs plus code-path review.

## Installation

Use the **installer included in the release package**.

Currently, only the **Windows version** has been released, while macOS support is still being adapted.

For local development builds, SQLite-backed features currently support only **macOS arm64 / Apple Silicon**. **Intel Mac (macOS x64)** is out of scope.

### Windows Installation Steps

1. Download the latest release package
2. Run the installer and select your **The Bazaar game directory**
3. Restart Steam and the game after installation
4. BazaarPlusPlus will automatically generate its configuration file the first time you enter the game.
5. If you need the speed-up feature, enable Combat Status Bar from the in-game options menu, then restart the game.

## For Developers

The project is built using **C#, .NET, and BepInEx 5**.

The project code is still being organized, and the source code, architecture notes, and development guide will be added gradually.

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
