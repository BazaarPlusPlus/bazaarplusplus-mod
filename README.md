# BazaarPlusPlus

[English](README_en.md)

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 模组，提供战斗 UI 增强、怪物与 tooltip 预览、run logging、历史面板、本地战斗回放、终局自动截图，以及后台上传能力。

当前仓库只保留与现有实现仍然一致的说明文档；如果文档与代码冲突，以 `Plugin.cs`、`Core/`、`Game/`、`Patches/` 中的实际实现为准。

## 功能概览

- 战斗状态条：在底部 HUD 显示逻辑战斗时间、已处理帧数、暂停状态以及离散速度档（0.50x / 0.67x / 1.00x）。
- 怪物预览：默认走游戏原生怪物预览；Bazaar++ 在 tooltip 路径上做局部增强，并复用原生 `MonsterBoardTooltip` 展示自定义 board 内容。
- 附魔 / 升级预览：在原生 tooltip 路径上追加附魔文本，或在按住升级预览热键时进入原生 upgrade preview。
- Run Logging 与 HistoryPanel：活跃 run 写入 SQLite；游戏内可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- 战斗回放：本地保存 PVP replay payload；`HistoryPanel` 在条件满足时可回放已保存战斗。
- 终局自动截图：终局 `Continue` 前保存主截图和 SQLite 元数据。
- 后台上传：run / replay 后台上传，仅在未处于 live run 时执行。
- BazaarDB 截图上传：可选开关，启用后把终局截图与摘要 JSON 推到 ModCFServerV3，BazaarDB 再按天拉取（默认关闭）。
- Anonymous Mode：将本地玩家名替换为 `Anonymous`。
- **AutoBazaar HTTP 接口** — 本地回环 HTTP 服务（默认端口 47900），允许外部工具读取当前决策上下文（`GET /v1/context`）并发起动作（`POST /v1/actions`）。Mod 本身不做策略决策。详见 [docs/reference/auto-bazaar-http-api-v1.md](docs/reference/auto-bazaar-http-api-v1.md)。

## 安装与配置

- 运行前提：已安装《The Bazaar》与 BepInEx 5。
- 手动安装时，将构建输出中的 `BazaarPlusPlus.dll` 以及同目录下的 SQLite 运行时依赖复制到游戏的 `BepInEx/plugins/`。
- 首次运行后，配置文件会写入 `BepInEx/config/BazaarPlusPlus.cfg`。
- 与详细功能相关的配置项、热键和 debug 面板说明见 `docs/reference/`。

## 从源码构建

- 项目目标框架为 `netstandard2.1`，使用可构建 C# 12 项目的 .NET SDK。
- 主项目和大多数测试项目通过 `ManagedPath` 解析游戏程序集；如果自动识别不到本地安装目录，请在构建时显式传入它。
- 常用命令：

```bash
dotnet build
dotnet build -p:ManagedPath=/path/to/TheBazaar_Data/Managed
./run.sh build
./run.sh all
```

- 构建行为：
  - Debug 构建会在识别到本地游戏目录时自动复制插件到 `BepInEx/plugins/`。
  - Release 构建在相邻 `../bazaarplusplus-installer` 仓库存在时，会把产物复制到安装器资源目录。

## 数据与网络行为

- run logging、战斗回放和终局截图会在本地保存 SQLite 数据、replay payload 与截图文件；玩家观察数据写到 `BazaarPlusPlus/Identity/observation.v1.json`，云同步本身不携带任何鉴权凭证。
- 后台上传会在非 live run 状态下执行上传扫描。
- `ModCFServerV3/` 目录包含当前上传、ghost battles、replay 下载相关的 Cloudflare Worker 后端实现。

## 仓库结构

- `Plugin.cs`：BepInEx 运行时入口。
- `Core/`、`Game/`、`Patches/`：主要功能实现。
- `Data/`：内嵌资源（如 build 推荐 JSON）。
- `tests/`：按特性拆分的测试项目。
- `run.sh`：本地构建、测试、格式化和反编译入口。
- `ModCFServerV3/`：云同步后端。

## 文档入口

- `docs/mod-features-overview.md`：当前功能总览。
- `docs/run-logging.md`：run logging、history panel、ghost battles。
- `docs/run-upload.md`：run bundle 上传行为、信任模型和边界。
- `docs/bazaardb-screenshot-upload.md`：BazaarDB 截图上传开关、sidecar 表与服务端契约。
- `docs/reference/end-of-run-screenshot-flow.md`：终局截图触发、存储和读取契约。
- `docs/reference/`：热键、设置表面、SQLite schema、tooltip 实现等参考文档。

## License

本项目使用 MIT License，见 `LICENSE`。

## 致谢

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
