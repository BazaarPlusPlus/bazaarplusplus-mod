# BazaarPlusPlus

BazaarPlusPlus 是一个面向《The Bazaar》的 BepInEx 5 模组：战斗 UI 增强、附魔 / 升级预览、run 记录与历史面板、本地战斗回放、终局自动截图与阵容推荐，以及可选的后台云同步。

## 功能一览

### 对局界面

- **战斗状态条**：底部 HUD 显示逻辑战斗时间、已处理帧数、暂停状态与离散速度档。
- **附魔 / 升级预览**：三档可视性模式（Off / AutoOnPedestalChoice / Always，默认 Always），按住 Ctrl / Shift 可手动覆盖。
- **卡牌图鉴**：Tab 键或大厅 dock 按钮打开的全屏 Item / Skill 图鉴，支持英雄、品质、体型、商人与训练师来源、运行天数筛选。
- **终局阵容面板**：局内 CapsLock 开关；展示实时 shop / board / stash，并按选定候选物品推荐匹配的十胜终局 build。

### 记录与回放

- **Run 记录与历史面板**：活跃 run 写入本地 SQLite；游戏内可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- **战斗回放**：本地保存 PVP replay payload；历史面板在条件满足时可回放已保存战斗。
- **终局自动截图**：终局总结页动画稳定后自动保存截图与元数据，再放行 `Continue`。

### 云同步（可选）

- **后台上传**：run / replay 后台上传到 V4 后端，仅在未处于 live run 时执行。
- **BazaarDB 截图上传**：默认关闭；启用后把终局截图快照推到 V4 后端，由 BazaarDB 队列拉取。
- **Anonymous Mode**：将本地玩家名替换为 `Anonymous`。

### 外部集成（默认不安装）

- **BazaarAgent HTTP 接口与活动浏览器**：独立的 host BepInEx 插件，在本地回环 `127.0.0.1:47900` 提供 HTTP 服务，允许外部工具读取当前决策上下文并发起动作；浏览器访问 `http://127.0.0.1:47900/` 可实时查看 Host/Agent 的结构化协议活动。mod 本身不做策略决策。按需用 `./run.sh build --with-bazaaragent` 构建，默认构建不产出（并主动清除）host dll；已构建的前端资源受版本控制，位于 `src/BazaarPlusPlus.BazaarAgent/Dashboard/dist/` 并嵌入 DLL。详见 [docs/ARCHITECTURE.md#bazaaragent-optional-host](docs/ARCHITECTURE.md#bazaaragent-optional-host)。

## 安装与配置

- 运行前提：已安装《The Bazaar》与 BepInEx 5。
- 手动安装时，将构建输出中的 `BazaarPlusPlus.dll`、`BazaarPlusPlus.ModApi.dll`、`BazaarPlusPlus.Storage.dll`、`BazaarPlusPlus.Localization.dll` 以及同目录下的 SQLite 原生运行时依赖复制到游戏的 `BepInEx/plugins/`。
- 首次运行后，配置文件写入 `BepInEx/config/BazaarPlusPlus.cfg`。

## 从源码构建

目标框架为 `netstandard2.1`（C# 12）。构建通过 `ManagedPath` 解析游戏程序集；自动识别不到本地安装目录时显式传入：

```bash
./run.sh build          # Debug 构建（识别到游戏目录时自动复制到 BepInEx/plugins/）
./run.sh test           # 默认离线测试（不部署游戏、不下载种子）
./run.sh test-compat    # 显式运行本机条件满足的兼容性前提测试
./run.sh test-corpus /path/to/replays  # 显式运行 replay 证据语料分析
./run.sh publish        # 生产发布：刷新远端数据、种子门禁、安装器打包
```

自动识别不到游戏目录时，用 `-p:ManagedPath=/path/to/TheBazaar_Data/Managed` 显式指定。运行 `./run.sh` 查看全部子命令及说明。

- 默认本地构建从 `src/BazaarPlusPlus/obj/remote-data/` 嵌入 `voice-lines.json`（缺失时自动获取），并使用仓库内的 `builds.json` 基线；`./run.sh fetch-data` 可手动刷新远端种子，发布流程会在语义门禁通过后再将新种子提升为构建输入。
- 普通 Release 只编译；只有 `./run.sh publish` 会写入相邻 installer 仓库并生成 `BepInEx.zip`。

## 数据与网络行为

- run 记录、战斗回放与终局截图均保存在本地（SQLite、replay payload、截图文件）；云同步不携带任何鉴权凭证，且只在非 live run 状态下执行上传扫描。
- 语音字幕与终局 build 种子由构建管线嵌入，运行时在本地缓存过期后后台刷新。
- 云端后端（上传、ghost battles、replay 链接、BazaarDB 快照投递）在独立仓库 `bazaarplusplus-server`，部署于 `mod-api-v4.bazaarplusplus.com`；mod 侧 HTTP 客户端在 `src/BazaarPlusPlus.ModApi/`。

## 仓库结构

- `src/BazaarPlusPlus/`：主插件工程。`Plugin.cs` 为 BepInEx 入口，feature wiring 走 `BppComposition.cs` 组合根；其下按 `Core/`、`GameInterop/`、`Game/`、`Patches/`、`Infrastructure/`、`Data/` 分层，职责详见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。
- `src/BazaarPlusPlus.ModApi/`、`src/BazaarPlusPlus.Storage/`、`src/BazaarPlusPlus.Localization/`：HTTP 客户端、本地持久化、本地化引擎三个独立程序集（零 game/Unity/BepInEx 依赖）。
- `src/BazaarPlusPlus.BazaarAgent/`、`src/BazaarPlusPlus.BazaarAgentHost/`：可选的 BazaarAgent 纯核心与 host 插件。
- `tests/`：12 个默认 xUnit 测试宿主、兼容性清单，以及由 `ScenarioRunner.Tests` 逐子进程执行的源码影子场景 capsule。
- `tools/PeriodicEffectAttribution.Corpus/`：需要显式提供 replay corpus 的离线证据工具，不属于默认测试。
- `decompiled/`：游戏 DLL 的 ILSpy 反编译输出，只读参考。
- `run.sh`：本地构建、测试、格式化和反编译入口。

## 文档

文档与代码冲突时，以 `src/BazaarPlusPlus/` 下的实际实现为准。

- [docs/README.md](docs/README.md)：文档索引与生命周期说明。
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)：当前实现的 living architecture（按主题组织，带代码证据）。
- [docs/adr/](docs/adr/)：设计决策记录。
- [GitHub Issues](https://github.com/BazaarPlusPlus/bazaarplusplus-mod/issues)：后续工作、需求与 bug 追踪。

## License

本项目使用 MIT License，见 `LICENSE`。
