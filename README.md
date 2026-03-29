# BazaarPlusPlus

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 模组。当前仓库只保留仍然对应现有代码的说明文档；代码本身始终是最终事实来源。

## 当前功能

- 战斗状态条：底部 HUD，显示逻辑战斗时间、已处理帧数、暂停与离散倍速。
- 怪物预览：右键锁定怪物或遭遇牌时显示敌方物品与技能面板，优先使用 `MonsterDatabase`，缺失时回退到运行时遭遇缓存。
- 附魔 / 升级预览：在原生 tooltip 路径上追加附魔文本，或在按住升级预览热键时进入原生 upgrade preview。
- Run Logging 与 HistoryPanel：活跃 run 写入 SQLite；游戏内可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- 战斗回放：本地保存 PVP replay payload；`HistoryPanel` 和 debug 面板可在条件满足时回放已保存战斗。
- 后台上传：可选的 run / replay 后台上传，仅在未处于 live run 时执行。
- Anonymous Mode：将本地玩家名替换为 `Anonymous`。

## 构建行为

- Debug 构建会在识别到本地游戏目录时复制插件到 `BepInEx/plugins`。
- Release 构建在相邻 `../bazaarplusplus-installer` 仓库存在时，会把产物复制到安装器资源目录。
- 目标框架为 `netstandard2.1`。

## 代码入口

- 运行时入口：`Plugin.cs`
- 核心目录：`Core/`、`Game/`、`Patches/`、`Data/`
- 脚本与测试：`scripts/`、`tests/`
- 文档入口：`docs/README.md`

## 致谢

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
