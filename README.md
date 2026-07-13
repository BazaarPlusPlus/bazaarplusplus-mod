# BazaarPlusPlus

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 5 模组：战斗 UI 增强、附魔 / 升级预览、run 记录与历史面板、本地战斗回放、终局自动截图与阵容推荐，以及可选的后台云同步。

## 功能一览

### 对局界面

- **战斗状态条**：底部 HUD 显示逻辑战斗时间、已处理帧数、暂停状态与离散速度档（0.50x / 0.67x / 1.00x）。
- **附魔 / 升级预览**：三档可视性模式（Off / AutoOnPedestalChoice / Always，默认 Always）；Auto 模式在选择屏出现对应 pedestal 时自动显示预览，按住 Ctrl / Shift 可手动覆盖。
- **卡牌图鉴（Collection Panel）**：Tab 键或大厅 dock 按钮打开的全屏 Item / Skill 图鉴，支持英雄、品质、体型、商人与训练师来源、运行天数筛选。
- **终局阵容面板（Live Build Panel）**：局内 CapsLock 开关；展示实时 shop / board / stash，选定候选物品后推荐匹配的十胜终局 build（数据来自云端 analyzer-v4 的 `tenwin_builds.json`，本地缓存后台刷新，并内嵌种子数据用于冷启动兜底）。

### 记录与回放

- **Run Logging 与 HistoryPanel**：活跃 run 写入本地 SQLite；游戏内可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- **战斗回放**：本地保存 PVP replay payload；HistoryPanel 在条件满足时可回放已保存战斗。
- **终局自动截图**：终局总结页的卡牌与技能展示动画稳定后，自动保存主截图和 SQLite 元数据，再放行 `Continue`。

### 云同步（可选）

- **后台上传**：run / replay 后台上传到 V4 后端（部署于 `mod-api-v4.bazaarplusplus.com`，源码在 `bazaarplusplus-server` 仓库），仅在未处于 live run 时执行。
- **BazaarDB 截图上传**：默认关闭；启用后把终局截图快照 DTO 推到 V4 后端，BazaarDB 通过 peek/confirm 队列拉取。
- **Anonymous Mode**：将本地玩家名替换为 `Anonymous`。

### 外部集成（默认不安装）

- **BazaarAgent HTTP 接口**：独立的 host BepInEx 插件，提供本地回环 HTTP 服务（固定 `127.0.0.1:47900`），允许外部工具读取当前决策上下文（`GET /v1/context`）并发起动作（`POST /v1/actions`）；mod 本身不做策略决策。按需用 `./run.sh build --with-bazaaragent` 构建，host dll 安装后自动启动；默认构建只产出主插件并主动清除两个 host dll。详见 [docs/ARCHITECTURE.md#bazaaragent-optional-host](docs/ARCHITECTURE.md#bazaaragent-optional-host)。

## 安装与配置

- 运行前提：已安装《The Bazaar》与 BepInEx 5。
- 手动安装时，将构建输出中的 `BazaarPlusPlus.dll`、`BazaarPlusPlus.ModApi.dll`、`BazaarPlusPlus.Storage.dll`、`BazaarPlusPlus.Localization.dll` 以及同目录下的 SQLite 原生运行时依赖复制到游戏的 `BepInEx/plugins/`。
- 首次运行后，配置文件写入 `BepInEx/config/BazaarPlusPlus.cfg`。

## 从源码构建

项目目标框架为 `netstandard2.1`，需要可构建 C# 12 项目的 .NET SDK。主项目和依赖游戏程序集的测试项目通过 `ManagedPath` 解析游戏程序集；如果自动识别不到本地安装目录，构建时显式传入。

```bash
./run.sh build          # Debug 构建
./run.sh test           # 全部测试
./run.sh fetch-data     # 手动强制刷新远端嵌入数据
./run.sh publish        # 生产发布：刷新数据、种子门禁、安装器资源与 BepInEx.zip
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj -p:ManagedPath=/path/to/TheBazaar_Data/Managed
```

构建行为：

- `run.sh` 默认为其启动的 .NET 进程设置 `DOTNET_SYSTEM_NET_DISABLEIPV6=1`，避免系统中失效的 IPv6 隧道路由阻塞远端数据下载；调用方可显式设置该环境变量来覆盖默认值。
- `voice-lines.json` 与 `tenwin_builds.json` 不存入仓库，构建从共享的 `src/BazaarPlusPlus/obj/remote-data/` 副本嵌入。普通构建已有副本时完全不联网；全新工作区缺失副本时自动下载一次，失败信息会提示恢复网络或运行 `./run.sh fetch-data`。
- `./run.sh fetch-data` 只强制刷新并校验两份远端数据，不编译、不测试、不写安装器目录；`./run.sh publish` 会强制刷新一次，并在打包前运行语音字幕与终局 build 的嵌入种子门禁。
- Debug 构建在识别到本地游戏目录时自动复制插件到 `BepInEx/plugins/`。
- 普通 Release 只编译；只有 `./run.sh publish` 会写入相邻 installer 仓库并生成 `BepInEx.zip`。

## 数据与网络行为

- run logging、战斗回放和终局截图在本地保存 SQLite 数据、replay payload 与截图文件；云同步本身不携带任何鉴权凭证。语音字幕目录和 analyzer-v4 终局 build 种子由构建管线从各自发布端获取并嵌入，运行时仍会在本地缓存过期后后台刷新。
- 后台上传只在非 live run 状态下执行上传扫描。
- 云端后端（上传、ghost battles、replay 链接、BazaarDB 快照投递）在独立仓库 `bazaarplusplus-server`，部署于 `mod-api-v4.bazaarplusplus.com`；mod 侧 HTTP 客户端在 `src/BazaarPlusPlus.ModApi/`。

## 仓库结构

- `src/BazaarPlusPlus/`：主插件工程。`Plugin.cs` 为 BepInEx 入口，feature wiring 走 `BppComposition.cs` 组合根；其下按 `Core/`（纯抽象）、`GameInterop/`（游戏 DLL 耦合层）、`Game/`（功能实现）、`Patches/`（Harmony 补丁）、`Infrastructure/`（跨切面工具）、`Data/`（内嵌资源）分层，分层职责详见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。
- `src/BazaarPlusPlus.ModApi/`、`src/BazaarPlusPlus.Storage/`、`src/BazaarPlusPlus.Localization/`：HTTP 客户端、本地持久化、本地化引擎三个独立程序集（零 game/Unity/BepInEx 依赖），由 `BppComposition.cs` 装配进 mod。
- `src/BazaarPlusPlus.BazaarAgent/`、`src/BazaarPlusPlus.BazaarAgentHost/`：可选的 BazaarAgent 纯核心与 host 插件。
- `tests/`：按特性拆分的测试项目。
- `decompiled/`：游戏 DLL 的 ILSpy 反编译输出，只读参考。
- `run.sh`：本地构建、测试、格式化和反编译入口。

## 文档

文档与代码冲突时，以 `src/BazaarPlusPlus/` 下的实际实现为准。

- [docs/README.md](docs/README.md)：文档索引与生命周期说明。
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)：当前实现的 living architecture（按主题组织，带代码证据）。
- [docs/adr/](docs/adr/)：设计决策记录。
- [GitHub Issues](https://github.com/cauyxy/bazaarplusplus-mod/issues)：后续工作、需求与 bug 追踪。
- [docs/archive/](docs/archive/)：历史参考文档，不再作为当前实现说明。

## License

本项目使用 MIT License，见 `LICENSE`。
