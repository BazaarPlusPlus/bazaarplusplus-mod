---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# FFmpeg 从 installer 子系统迁移为随 mod 分发的兄弟二进制

- 日期：2026-05-30
- 状态：**IMPLEMENTED(已实现并合并,历史归档)**
- 影响仓库：`bazaarplusplus-mod`（主要）、`bazaarplusplus-installer`（删除 FFmpeg 子系统）

## 背景与动机

Combat Replay 的可选 MP4 录制功能（默认关闭，`CombatReplayVideo/Enabled=false`）在 mod 运行时调用一个外部 `ffmpeg` 可执行文件做编码。当前 FFmpeg 的**部署**完全由 installer 负责，而**消费**在 mod，归属割裂：

- installer 侧把 FFmpeg 当成一个独立子系统：`resources/FfmpegSource/{windows,macos}/ffmpeg.zip` 作为 Tauri resource 打包，配套 4 个 Rust tauri command（`detect/install/uninstall/repair_ffmpeg`）、完整 staging→probe→原子 swap 安装流水线、`version.json`、独立向导步骤与整套前端 UI。
- mod 侧（[`FfmpegLocator`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Video/FfmpegLocator.cs)）只做"检测 + 调用"，按契约在 `<GameRoot>/BazaarPlusPlusV4/tools/ffmpeg/` 与系统 `PATH` 两级查找。

目标：让"录制功能所依赖的 FFmpeg"归 mod 所有，删除 installer 侧整个 FFmpeg 子系统，优化整体简单度。

### 关键前提（已核实）

1. **一份 `BazaarPlusPlus.dll` 同时服务两平台**：Release 构建把同一个托管 DLL 拷进 `SourceForBuild/{windows,macos}` 两棵树。
2. **仓库已有逐平台 native 依赖的既定做法**：SQLite 的 native 二进制是作为**兄弟文件**摆在 `BepInEx/plugins/` 里逐平台分发的（Windows `e_sqlite3.dll`、macOS `libe_sqlite3.dylib`），**不是**嵌进 DLL。FFmpeg 与之同构。
3. **mod payload 落地路径**：installer 的 `install_bepinex` 把 `BepInExSource/{platform}/BepInEx.zip` 直接解压进 `<GameRoot>`，故 plugins 落到 `<GameRoot>/BepInEx/plugins/`。
4. **payload 解压不设可执行位**：`bepinex::zip_archive::extract_zip` 只 `fs::write`；macOS 上唯一被 chmod 的是 `run_bepinex.sh`（`vdf.rs` 在 Steam 启动项设置时单独 `| 0o111`）。没有对 payload 统一 chmod 的步骤。
5. **许可证不是约束**：本项目是公开 GPL 源码项目，接受 GPL；FFmpeg 的 LGPL/GPL 分发顾虑从设计中移除。（注：现有 Windows 二进制本就是 BtbN 的 GPL 变体，旧文档"LGPL+OpenH264 避开 GPL"的说法与实际产物不符。）
6. **裸二进制体积**：解压后 Windows `ffmpeg.exe` ≈ 131MB、macOS `ffmpeg` ≈ 47MB；对应 zip 为 50MB / 20MB。

## 决策

把 FFmpeg 改造成与 SQLite native 完全同构的"随 mod 分发的逐平台兄弟二进制"：

1. **形态**：ffmpeg 二进制随 mod 的 `BepInEx.zip` 落到 `<GameRoot>/BepInEx/plugins/`，与 `BazaarPlusPlus.dll` 同目录；运行时由 mod 相对自身定位、自己负责 POSIX 可执行位。
2. **二进制源头**：保留在 installer 仓库（以现有 zip 形式，git 内容一致、几乎零新增体积），由 mod 构建用一个 `<Unzip>` 步骤展开进 payload。
3. **installer 对 FFmpeg 彻底无感知**：删除 FfmpegSource 资源映射、4 个 Rust command、整套前端、uninstall 清理 hook。

## 详细设计

### A. mod 运行时：定位逻辑（`bazaarplusplus-mod`）

**A1. path provider：加 plugins 目录、删除已死的 tools 目录**

- [`IPathProvider`](../../../../src/BazaarPlusPlus.Storage/Paths/IPathProvider.cs) 新增 `string? PluginsDirectoryPath { get; }`。
- [`BepInExPathProvider`](../../../../src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs) 在 `Initialize()` 里用 `BepInEx.Paths.PluginPath` 填充（与现有 `BepInEx.Paths.GameRootPath` 用法一致，保持 locator 不直接耦合 BepInEx、可测试）。
- **删除 `ToolsDirectoryPath`**：去掉 legacy 兜底后，FFmpeg/locator 是它的唯一消费者，成为死代码。实现时先 `grep -ri ToolsDirectoryPath` 确认无其他消费者，然后从 `IPathProvider`、`BepInExPathProvider`（含 `tools` 路径拼接）、以及所有测试桩（`RunLoggingSqliteRecovery.Tests`、`Storage.Tests`、`RunLoggingSqliteStore.Tests` 等里的 `ToolsDirectoryPath => null`）一并移除。测试桩补上 `PluginsDirectoryPath => null`。

**A2. `FfmpegLocator` 检测顺序**

改为（[`FfmpegLocator.Resolve`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Video/FfmpegLocator.cs)）：

1. **bundled（主路径）**：`<PluginsDirectoryPath>/ffmpeg(.exe)`
2. **PATH**：fallback

不保留 `tools/ffmpeg/` legacy 兜底——本功能尚未发布、无老用户需要兼容，删除以保持简单。明确拍定：签名简化为 `Resolve(string? pluginsDirectoryPath)`，按 1→2 顺序探测。session 缓存、2s 探活（`ffmpeg -version`、`ExitCode==0`）、找不到则静默禁用并打一行 Info log —— 全部保持不变。

**A3. macOS 可执行位（自包含）**

在非 Windows 上、probe 之前：若候选文件存在且无执行位，`chmod 0755`（owner rwx / group rx / others rx，与原 installer `install.rs` 一致）再 probe。这样 mod 完全自包含解决 macOS +x，不依赖构建机或 installer 解压行为。

**A4. 调用点**

[`CombatReplayVideoRecorder`](../../../../src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs#L110) 改为把 `PluginsDirectoryPath` 传给 locator（不再传 `ToolsDirectoryPath`）。

### B. 构建 / 打包（`BazaarPlusPlus.csproj` + installer 仓库）

**B1. 二进制源头存放**

- 保留现有 `bazaarplusplus-installer/src-tauri/resources/FfmpegSource/{windows,macos}/ffmpeg.zip` 与 `LICENSE.txt` 作为**构建输入**（不再作为 Tauri 打包资源）。
- 从 Tauri 配置（`tauri.windows.conf.json` / `tauri.macos.conf.json`）的 `resources` 列表中**移除** FfmpegSource 映射，使其不再被打进 installer 应用本身。

**B2. mod 构建展开进 payload**

在 [`BazaarPlusPlus.csproj`](../../../../src/BazaarPlusPlus/BazaarPlusPlus.csproj) 已有 target 中加 `<Unzip>`（位置：DLL/native 拷贝之后、`<ZipDirectory>` 之前），沿用现有 `$(BPPInstallerSourcePath)` 与 `Exists(...)` 条件守卫：

- **Release**（`CopyToInstallerSource`）：把 `FfmpegSource/windows/ffmpeg.zip` 展开到 `SourceForBuild/windows/BepInEx/plugins/`，`FfmpegSource/macos/ffmpeg.zip` 展开到 `SourceForBuild/macos/BepInEx/plugins/`；随后现有 `ZipDirectory` 自动把它打进各自的 `BepInEx.zip`。同时把对应 `LICENSE.txt` 一并拷入 plugins（GPL 许可随二进制分发，正确且零成本）。
- **Debug**（`CopyToBepInExPlugins`）：把当前**宿主平台**对应的 `ffmpeg.zip` 展开到 `$(GamePath)/BepInEx/plugins/`，方便本地直接测试录制。

> 落地位置选 plugins 根目录（与 `e_sqlite3.dll` 一致）。131MB 的 `ffmpeg.exe` 摆在 plugins 根不影响 BepInEx——其插件扫描只认托管 DLL，会忽略 `.exe`。

### C. installer 侧删除清单（`bazaarplusplus-installer`）

- **Rust**：删除整个 `src-tauri/src/commands/ffmpeg/`（`mod.rs`/`install.rs`/`probe.rs`/`state.rs`/`uninstall.rs`）；删除 `lib.rs` 中 4 个 ffmpeg command 的注册；删除 `commands/mod.rs` 中的 `ffmpeg` 模块导出；删除 `bepinex/mod.rs` 中 `uninstall_bpp` 里的 `remove_ffmpeg_dir` 调用（`bepinex/mod.rs:168`）。
- **Tauri 资源**：从两个平台 conf 的 resources 列表移除 FfmpegSource（见 B1）。
- **前端**：删除 FFmpeg 向导步骤与相关 UI/逻辑——`InstallerFfmpegStep.svelte`、`controllers/ffmpeg-controller.ts`、`ffmpeg-errors.ts`、`selectors/ffmpeg.ts`、`ffmpeg-step-bundle.ts`、`bridge/commands.ts` 与 `installer/api.ts`/`storage.ts` 中的 ffmpeg 部分、`InstallerStatusSteps.svelte` / `InstallerPageContent.svelte` / `install/+page.svelte` / `install-controller.ts` 中引用 ffmpeg 的分支，以及 `scripts/prebuild-check.mjs` / `scripts/build.*` 中的 ffmpeg 校验。
- **ts-rs 导出**：删除随之失效的 `FfmpegStatus` / `FfmpegDetectResult` / `FfmpegInstallProgress` 绑定。

> 实现阶段以 `grep -ri ffmpeg` 为准逐个清理并确保两侧编译/类型检查通过；上面是基于当前约 20 处命中的清单。

### D. 文档同步

- [`docs/features/combat-replay.md`](../../features/combat-replay.md)：更新 FFmpeg 检测路径（第 49 行附近）；删除"installer 负责部署 / LGPL 避开 GPL"职责切分段（第 52 行）；更新"当前状态/未落地"（第 69 行，"installer 侧 FFmpeg 自动部署"作废）。
- [`Core/Config/BppConfig.cs`](../../../../src/BazaarPlusPlus/Core/Config/BppConfig.cs)：`Enabled` 帮助文案里的 `tools/ffmpeg/` 路径更新为"随 mod 安装、位于 BepInEx/plugins"。
- 删除 installer 的 `docs/combat-replay-ffmpeg-deployment.md`（整篇作废）。

## 非目标（Out of Scope）

- 不改抓帧/编码链路（`ReplayVideoCaptureSession`、`FfmpegRawVideoEncoder`）与录制配置项。
- 不引入运行时下载 / 自动更新 FFmpeg。
- 不拆逐平台 DLL 构建，不把二进制嵌入 DLL。
- 不新增 macOS x86_64 支持（沿用现状：windows-x86_64 + darwin-aarch64）。

## 验证

- **mod**：`FfmpegLocator` / `CombatReplayVideoRecorder` 相关测试项；因动了打包逻辑，跑 `BuildAll`，并人工确认产出的 `BepInEx.zip` 内含 `BepInEx/plugins/ffmpeg(.exe)`。
- **installer**：`npm run prebuild-check`（动了 bundled resources / Tauri 配置）、`cargo test`（删 command 后确认编译与注册一致）、`npm run check`。
- **端到端冒烟**：Windows 安装后开启录制能正常产出 MP4；macOS 上确认首次定位触发 chmod、可执行。

## 风险 / 待确认

- **`BepInEx.Paths.PluginPath` 可用性**：mod 已使用 `BepInEx.Paths.GameRootPath`，PluginPath 为 BepInEx 5 标准 API，风险低；若个别场景为空，回退到基于 `Assembly.GetExecutingAssembly().Location` 的目录。
- **`<Unzip>` 任务可用性**：.NET SDK / 现代 MSBuild 自带，`dotnet build` 环境可用。
- **installer git 历史**：旧 `FfmpegSource` zip 的历史 blob 不会被删除回收（与方案无关），新增几乎为零。

## 实现记录（2026-05-30）

经 Workflow（6 agent：并行实现 → 并行验证 → 对抗式 review）落地，并人工补两处后验证通过：

- 改动量：mod 8 文件（A1–A4 + B2）；installer 18 改 / 12 删（C + B1）；docs 3 处（D）。
- 验证（本机实跑）：mod `dotnet build` 的 Debug 与 Release 均 0 error（4 个为既存 warning）；ffmpeg 已确认进入两平台 `BepInExSource/<plat>/BepInEx.zip` 的 `BepInEx/plugins/`（Win `ffmpeg.exe` 131MB / mac `ffmpeg` 47MB，各带 `ffmpeg-LICENSE.txt`，与 `BazaarPlusPlus.dll` 同级）。installer `cargo check` 通过、`npm run check` svelte-check 427 文件 0 error、`prebuild-check: ok`、代码区 `grep -i ffmpeg` 零命中、两个 `ffmpeg.zip` 二进制保留在仓库。
- 设计外补充两处：(1) `BazaarPlusPlus.csproj` 的 Release `<Unzip>` 与 LICENSE `<Copy>` 各自加 `Exists(...)` 守卫，与 Debug 步骤对齐；(2) installer `.gitignore` 忽略 `SourceForBuild/*/BepInEx/plugins/ffmpeg{,.exe,-LICENSE.txt}`——解压出的裸二进制是构建产物，仓库只提交 `FfmpegSource/*/ffmpeg.zip`（已 `git check-ignore` 确认生效）。
- 已知无关项：installer `cargo test` 有 1 个既存失败（`stream::records::image` 在 Windows 上的 POSIX 路径断言），与本次改动无关，另行跟踪。
