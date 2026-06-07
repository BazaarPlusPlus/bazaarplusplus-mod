# BazaarPlusPlus

[English](README_en.md)

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 模组，提供战斗 UI 增强、怪物与 tooltip 预览、run logging、历史面板、本地战斗回放、终局自动截图，以及后台上传能力。

当前仓库只保留与现有实现仍然一致的说明文档；如果文档与代码冲突，以 `src/BazaarPlusPlus/` 下的 `Plugin.cs`、`Core/`、`Game/`、`Patches/` 等实际实现为准。

## 功能概览

- 战斗状态条：在底部 HUD 显示逻辑战斗时间、已处理帧数、暂停状态以及离散速度档（0.50x / 0.67x / 1.00x）。
- 怪物预览：完全走游戏原生怪物预览，Bazaar++ 不做修改。
- 附魔 / 升级预览：附魔预览有可视性模式（Off / AutoOnPedestalChoice / Always，默认 Always）；Auto 模式会在选择屏遇到对应 pedestal 时自动显示预览；按住 Ctrl / Shift 仍可手动覆盖。
- Run Logging 与 HistoryPanel：活跃 run 写入 SQLite；游戏内可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- 战斗回放：本地保存 PVP replay payload；`HistoryPanel` 在条件满足时可回放已保存战斗。
- 终局自动截图：终局 `Continue` 前保存主截图和 SQLite 元数据。
- 后台上传：run / replay 后台上传，仅在未处于 live run 时执行。
- BazaarDB 截图上传：可选开关，启用后把终局截图快照 DTO 推到 V4 mod 后端（`bazaarplusplus-server` 仓库，部署 `mod-api-v4.bazaarplusplus.com`），BazaarDB 通过 peek/confirm 队列拉取（默认关闭）。
- 卡牌图鉴（Collection Panel）：全屏 Item/Skill 图鉴，Tab 键或大厅 dock 按钮打开；支持英雄 / 品质 / 体型 / 商人与训练师来源 / 运行天数筛选。
- 终局阵容面板（Live Build Panel）：局内 CapsLock 开关；展示实时 shop / board / stash，选择候选物品后给出匹配的十胜终局 build 推荐（数据来自云端 analyzer-v4 `tenwin_builds.json`，本地缓存后台刷新，并内嵌一份种子数据用于冷启动兜底）。
- Anonymous Mode：将本地玩家名替换为 `Anonymous`。
- **BazaarAgent HTTP 接口**（可选 host 插件，默认不安装）— 本地回环 HTTP 服务（固定 `127.0.0.1:47900`），允许外部工具读取当前决策上下文（`GET /v1/context`）并发起动作（`POST /v1/actions`）。Mod 本身不做策略决策。**host 是独立的 BepInEx 插件**，按需用 `./run.sh build --with-bazaaragent` 构建；host dll 安装后会自动启动，默认构建只产出主插件并主动清除两个 host dll。详见 [docs/features/bazaar-agent.md](docs/features/bazaar-agent.md)。

## 安装与配置

- 运行前提：已安装《The Bazaar》与 BepInEx 5。
- 手动安装时，将构建输出中的 `BazaarPlusPlus.dll`、`BazaarPlusPlus.ModApi.dll`、`BazaarPlusPlus.Storage.dll`、`BazaarPlusPlus.Localization.dll` 以及同目录下的 SQLite 原生运行时依赖复制到游戏的 `BepInEx/plugins/`。
- 首次运行后，配置文件会写入 `BepInEx/config/BazaarPlusPlus.cfg`。
- 与详细功能相关的配置项、热键和 debug 面板说明见 [docs/reference/](docs/reference/)；完整文档索引见 [docs/README.md](docs/README.md)。

## 从源码构建

- 项目目标框架为 `netstandard2.1`，使用可构建 C# 12 项目的 .NET SDK。
- 主项目和依赖游戏程序集的测试项目通过 `ManagedPath` 解析游戏程序集；如果自动识别不到本地安装目录，请在构建时显式传入它。
- 常用命令：

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj -p:ManagedPath=/path/to/TheBazaar_Data/Managed
./run.sh build
./run.sh all
```

- 构建行为：
  - Debug 构建会在识别到本地游戏目录时自动复制插件到 `BepInEx/plugins/`。
  - Release 构建在相邻 `../bazaarplusplus-installer` 仓库存在时，会把产物复制到安装器资源目录。

## 数据与网络行为

- run logging、战斗回放和终局截图会在本地保存 SQLite 数据、replay payload 与截图文件；云同步本身不携带任何鉴权凭证。
- 后台上传会在非 live run 状态下执行上传扫描。
- 云端后端（上传、ghost battles、replay 链接、BazaarDB 快照投递）现在在独立仓库 `bazaarplusplus-server`，部署于 `mod-api-v4.bazaarplusplus.com`。mod 侧的 HTTP 客户端在 `src/BazaarPlusPlus.ModApi/` 里。

## 仓库结构

- `src/BazaarPlusPlus/`：主插件工程。`Plugin.cs` 为 BepInEx 运行时入口（精简，feature wiring 走 `BppComposition` 的 `IBppMountable`/`ISettingsDockEntry` 注册表）；其下 `Core/` 纯抽象（配置、事件总线、路径、运行时服务接口，零 game DLL 引用），`GameInterop/` 游戏 DLL 耦合层（`BppClientCacheBridge`、`BppStaticDataAccess`、`GameStateProbe`、`RunContextStore`、`IRunContext` 接口、带 game type 的事件 `CombatSimObserved`/`NetMessageObserved`），`Game/`、`Patches/` 主要功能实现 + Harmony 补丁，`Infrastructure/` 跨切面工具（日志、字体、UI design token），`Data/` 内嵌资源（如 build 推荐 JSON）。
- `src/BazaarPlusPlus.ModApi/`、`src/BazaarPlusPlus.Storage/`、`src/BazaarPlusPlus.Localization/`：HTTP 客户端、本地持久化、本地化引擎三个独立程序集（零 game/Unity/BepInEx 依赖），由 `BppComposition.cs` 装配进 mod。
- `src/BazaarPlusPlus.BazaarAgent/`、`src/BazaarPlusPlus.BazaarAgentHost/`：可选 BazaarAgent 纯核心与 host 插件（默认不安装，按需 `./run.sh build --with-bazaaragent` 构建）。
- `tests/`：按特性拆分的测试项目。
- `run.sh`：本地构建、测试、格式化和反编译入口。

## 文档入口

- [docs/README.md](docs/README.md)：**文档总索引**（按受众与生命周期组织所有文档）。
- [docs/mod-features-overview.md](docs/mod-features-overview.md)：当前功能总览（按功能的深入目录）。
- [docs/features/](docs/features/)：各功能的现行文档（run logging 与上传、combat replay、history panel、screenshots、tooltip preview 等）。
- [docs/reference/](docs/reference/)：稳定契约 / 清单（热键、设置表面、SQLite schema、BazaarAgent HTTP API）。
- [docs/adr/](docs/adr/)：设计决策记录（ADR）。

## License

本项目使用 MIT License，见 `LICENSE`。

## 致谢

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
