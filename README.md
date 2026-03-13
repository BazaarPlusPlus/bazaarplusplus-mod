# BazaarPlusPlus

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 增强模组，专注于提供更清晰、更顺手的局内信息展示。  

BazaarPlusPlus is a BepInEx enhancement mod for *The Bazaar*, focused on making in-game information easier to read and act on.

## Features / 功能亮点

- 战斗状态条：提供更直观的战斗播放状态与节奏信息。  

Combat status bar: adds clearer playback and combat pacing information.
- 怪物预览：在合适的场景下展示更完整的怪物预览信息。  

Monster preview: shows richer enemy preview information in supported flows.
- 遭遇信息补强：让遭遇相关的预览信息更连贯、更容易理解。  

Encounter clarity improvements: keeps encounter-related preview data more consistent and readable.
- 附魔与词条预览增强：补充部分物品与附魔相关的可见信息。  

Enchant and item preview improvements: expands visible information for supported item and enchant views.

## Install / 安装

普通玩家建议优先使用发布包内提供的安装器。当前 Windows 是更明确、体验更完整的安装路径；macOS 支持仍在持续完善中。  

For most players, the recommended path is the installer included with a release package. Windows is the clearest and most complete installation path today, while macOS support is still being improved.

基本流程如下：  

Recommended flow:

1. 下载最新发布版本。  

  Download the latest release package.
2. 运行安装器，并选择你的《The Bazaar》安装目录。  

  Run the installer and select your *The Bazaar* installation directory.
3. 安装完成后，重新启动 Steam 与游戏。  

  Restart Steam and the game after installation completes.
4. 首次进入游戏后，BazaarPlusPlus 会自动生成配置文件。  

  BazaarPlusPlus will generate its config file automatically after the first in-game launch.

## FAQ / 常见问题

**配置文件在哪里？ / Where is the config file?**  

配置文件位于 `BepInEx/config/BazaarPlusPlus.cfg`。如果文件还不存在，请先启动一次游戏。  

The config file is located at `BepInEx/config/BazaarPlusPlus.cfg`. If it does not exist yet, launch the game once first.

**安装后没有生效怎么办？ / What if the mod does not seem active after install?**  

先确认安装目录是否指向正确的《The Bazaar》目录，然后检查 `BepInEx` 是否已正确放入游戏目录，再重新启动游戏。  

First verify that the selected game path is the correct *The Bazaar* directory, then check that `BepInEx` was placed into the game folder correctly, and restart the game.

## For Developers / 开发者

项目基于 C#、.NET 和 BepInEx 5。源码正在整理，将在不远的将来开放。  
  
The project is built with C#, .NET, and BepInEx 5. The source tree is being cleaned up for open development, and this README intentionally keeps the developer entry point short.

## Credits / 致谢

**Inspired By / 灵感来源**  

以下项目为 BazaarPlusPlus 提供了重要灵感与参考：  

These projects provided important inspiration and reference for BazaarPlusPlus:

- [Duangi/BazaarHelper](https://github.com/Duangi/BazaarHelper)
- [oceanseth/BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)

**Data Source / 数据来源**  

部分怪物与相关展示信息整理参考了 BazaarDB。  

Some monster and related display data is informed by BazaarDB.

- [BazaarDB](https://bazaardb.gg)

**Core Dependency / 核心依赖**  

BazaarPlusPlus 基于 BepInEx 运行。  

BazaarPlusPlus runs on top of BepInEx.

- [BepInEx](https://github.com/BepInEx/BepInEx)

