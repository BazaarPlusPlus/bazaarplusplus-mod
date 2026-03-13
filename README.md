# BazaarPlusPlus

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 增强模组，专注于提供更清晰、更顺手的局内信息展示。  

BazaarPlusPlus is a BepInEx enhancement mod for *The Bazaar*, focused on making in-game information easier to read and act on.

## Features / 功能亮点

- 战斗状态条：提供更直观的战斗播放状态与节奏信息。  

Combat status bar: adds clearer playback and combat pacing information.
- 怪物预览优化：右键预览查看怪物的卡牌与技能信息。  

Monster preview improvements: right-click the preview to view enemy cards and skill information.
- 附魔与词条预览增强：补充物品附魔相关的可见信息。  

Enchant and item preview improvements: expands visible information for supported item and enchant views.

## Install / 安装

推荐优先使用发布包内附带的安装器。当前发布包主要提供 Windows 安装流程；macOS 仍在适配中。  

The recommended option is the installer included in the release package. The current release primarily supports installation on Windows; macOS support is still in progress, and the installer is not yet considered reliable there.

Windows 推荐安装流程：  

Recommended Windows installation flow:

1. 下载最新发布包。  
   Download the latest release package.
2. 运行安装器，并选择你的 *The Bazaar* 游戏目录。  
   Run the installer and select your *The Bazaar* game directory.
3. 安装完成后，重新启动 Steam 和游戏。  
   After installation finishes, restart Steam and the game.
4. 首次进入游戏后，BazaarPlusPlus 会自动生成配置文件。  
   BazaarPlusPlus will generate its configuration file automatically the first time you launch the game.

## FAQ / 常见问题

**配置文件在哪里？ / Where is the config file?**  

配置文件位于 `BepInEx/config/BazaarPlusPlus.cfg`。 
如果文件还不存在，请先启动一次游戏。你也可以直接在 installer 的设置页中调整相关配置。  

The config file is located at `BepInEx/config/BazaarPlusPlus.cfg`. If it does not exist yet, launch the game once first. You can also adjust the relevant options directly from the installer's Settings page.

**安装后没有生效怎么办？ / What if the mod does not seem active after install?**  

先确认安装目录是否指向正确的《The Bazaar》目录，然后检查 `BepInEx` 是否已正确放入游戏目录，再重新启动游戏。  

First verify that the selected game path is the correct *The Bazaar* directory, then check that `BepInEx` was placed into the game folder correctly, and restart the game.

## For Developers / 开发者

项目基于 C#、.NET 和 BepInEx 5。当前开发相关内容仍在整理中，后续会逐步补充更完整的源码与说明。  
  
The project is built with C#, .NET, and BepInEx 5. Developer-facing source structure and documentation are still being organized, and more complete code and setup notes will be added over time.

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
