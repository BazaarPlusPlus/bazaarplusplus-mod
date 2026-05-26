# 设计文档反查审计 2026-05-27

## 范围和方法

本轮审计只读源码、反编译文档和现有设计文档，不编译、不运行测试、不修改项目代码。

已反查范围：

- `decompiled/reverse-docs/*.md`
- `docs/*.md`
- `docs/reference/*.md`
- `docs/superpowers/plans/*.md`
- `docs/superpowers/specs/*.md`
- 和联网口径直接相关的当前源码：`ModApi/*`、`Game/RunLogging/Upload/*`、`Game/HistoryPanel/Ghost/*`、`Game/AutoBazaar/*`、`Game/Screenshots/Upload/*`、`Game/MonsterPreview/*`

结论优先级：

1. 当前源码和反编译结果。
2. 顶层 `docs/` 与 `docs/reference/` 的当前说明。
3. `docs/superpowers/plans|specs` 作为历史计划或实现前规格，只能在和当前源码一致时采用。

## 总体结论复核

原反编译结论大方向成立：

- 官方游戏启动和账号/经济系统是 JSON REST，基地址来自 `Config.NetURL`。
- 官方 run 内玩法协议是 `HttpGameClient(Config.SocketURL)` 的 MessagePack HTTP：`POST /sessions`、`POST /commands`、`DELETE /sessions`。
- 静态数据来自 `Config.MaintenanceDataURL`、`Config.DataURL`、bundled `StreamingAssets` 和 Addressables 本地 catalog/cache。
- BazaarPlusPlus 当前云端 API 指向 `https://mod-api-v4.bazaarplusplus.com`，客户端不设置 bearer/API-key，身份主要来自 body/query 里的 `player_account_id`。
- AutoBazaar 是本地 loopback 控制层：默认 `http://127.0.0.1:47900`，只提供 `GET /v1/context` 和 `POST /v1/actions`，不是权威 run 引擎。
- 本地离线方案仍应保留客户端现有状态机和协议形状：本地 REST facade + 本地 MessagePack session server + 本地静态资源服务。

本轮发现需要收紧或修正的结论有两类：

1. `docs/run-upload.md` 仍有 V3 残留措辞和旧文件路径。
2. final-battle 标记的来源在设计文档之间存在冲突；当前 mod 源码没有发送 `is_final_battle` 字段。

## 发现的问题

### 1. `docs/run-upload.md` 的 V3 表述是残留

`docs/run-upload.md` 开头写：

- "上传协议是未认证的 V3 `run-bundle` 上传"
- 当前实现文件包含 `Game/Online/V3Routes.cs`、`Game/Online/RunBundleClient.cs`

但当前源码实际是：

- `ModApi/ModApiRoutes.cs` 定义 `/run-bundles`、`/ghost-battles`、`/bazaardb-screenshots`、`/bazaardb/manifest`。
- `ModApi/Clients/RunBundleClient.cs` 发送 `POST /run-bundles`。
- `ModApi/Clients/GhostBattleClient.cs` 发送 `GET /ghost-battles?player_account_id=...`、`POST /ghost-battles/{battleId}/replay-link`，并下载返回的 `download_url`。
- `ModApi/Http/BppHttpClientFactory.cs` 只设置 `User-Agent`，没有 auth header。

判断：

- "未认证"是对的。
- "V3 `run-bundle`" 是历史命名残留；当前 wire/base/docs 都是 V4。
- `Game/Online/V3Routes.cs`、`Game/Online/RunBundleClient.cs` 路径已经不是当前源码路径。

对离线方案的影响：

- 不影响离线本地 run server 方案，因为官方玩法协议仍是 `/sessions` 和 `/commands`。
- 如果要做本地 mod-cloud stub，应按 `ModApi/ModApiRoutes.cs` 的 V4 路由实现，不要按历史 `V3Routes` 路径找接口。

建议后续文档修正：

- 把 `docs/run-upload.md` 中 "V3 `run-bundle`" 改成 "V4 `run-bundle` / run-bundles 上传"。
- 把当前实现文件列表改成 `ModApi/ModApiRoutes.cs`、`ModApi/Clients/RunBundleClient.cs`、`ModApi/Clients/GhostBattleClient.cs` 等实际路径。

### 2. final-battle 标记来源存在冲突

设计文档中的说法不完全一致：

- `docs/mod-features-overview.md`：服务端把上传 bundle 中最后一场 battle 标记为 `is_final_battle`。
- `docs/run-upload.md` 第 20 行语义：服务端把 `battle_projections` 中最后一条 battle 当 final marker。
- `docs/run-upload.md` 第 45 行语义：`is_final_battle` 由 mod 客户端在上传 payload 里直接传。
- `docs/reference/sqlite-schema-reference.md`：服务端 treats incoming battle projection `is_final_battle = true` as sticky。
- `docs/reference/ghost-battle-data-flow.md`：ghost response 使用 V4 wire key `is_final_battle`，本地字段仍叫 `IsBundleFinalBattle` / `is_bundle_final_battle`。

当前 mod 源码实际情况：

- `ModApi/Models/RunBundleUploadRequest.cs` 的 `BattleProjection` 没有 `is_final_battle` 或 `is_bundle_final_battle` JSON 字段。
- `Game/RunLogging/Upload/RunBundleUploadStore.cs` 的 `BuildBattleProjection(PvpBattleManifest manifest)` 没有写 final-battle 字段。
- `ModApi/Clients/GhostBattleClient.cs` 确实会从 ghost battle 响应读取 `battle["is_final_battle"]`，写入 `GhostBattleImportRecord.IsBundleFinalBattle`。
- `Game/HistoryPanel/HistoryPanelRepository.cs` 本地 SQLite 字段仍是 `is_bundle_final_battle`，这只是本地 schema 名称，和 V4 wire rename 解耦。

判断：

- "ghost 查询响应带 `is_final_battle`，客户端解析为 `IsBundleFinalBattle`" 是当前源码支持的结论。
- "当前 mod 上传时直接发送 `is_final_battle`" 与当前源码不符。
- 若服务端当前能生成 final marker，它应当是服务端根据上传 bundle 的最后一条 battle projection 或 artifact 顺序推导；这一点需要在 `bazaarplusplus-server` 源码中最终确认。本仓库没有服务端源码，不能把 "客户端发送" 当成本仓库事实。

对离线方案的影响：

- 本地 ghost/import stub 可以继续返回 `is_final_battle`，因为客户端能解析。
- 本地 run-bundle 接收端不能指望当前 mod 请求体携带 `is_final_battle`。如果实现本地 V4-compatible upload stub，应在接收端按 `battle_projections` 顺序或 artifact battles 顺序推导最后一场。
- 如果未来要让客户端显式发送该字段，需要新增 `BattleProjection.IsFinalBattle` DTO 字段和 `RunBundleUploadStore` 填充逻辑；这属于代码改动，不在本次范围内。

### 3. BazaarDB V3 设计文档是历史文档

当前顶层文档和源码一致：

- 当前上传端点是 `POST /bazaardb-screenshots`。
- 当前 manifest 是 `GET /bazaardb/manifest?date=...&cursor=&limit=`，需要 `Authorization: Bearer <BAZAARDB_PULL_TOKEN>`。
- 当前 image bytes 通过公开 R2 custom domain `https://bazaardb-assets-v4.bazaarplusplus.com/{r2_key}` 获取，不再走 Worker image proxy。
- mod 上传端不附加鉴权 header，身份在 `BazaarDbScreenshotUploadRequest.PlayerAccountId`。

历史文档仍有旧说法：

- `docs/superpowers/specs/2026-05-24-bazaardb-upload-in-mod-design.md` 引用 `ModCFServerV3`、`mod-api-v3.bazaarplusplus.com`、`GET /bazaardb/image/{screenshot_id}`、Worker proxy R2。
- `docs/superpowers/plans/2026-05-24-bazaardb-upload-in-mod.md` 也包含 V3 server/route 计划。

判断：

- 顶层 `docs/bazaardb-screenshot-upload.md` 是当前口径。
- `docs/superpowers/...bazaardb...` 是历史计划，不能作为当前实现接口。

对离线方案的影响：

- 离线默认应关闭 `BazaarDbUploadEnabled`，或者把 `ModApiRoutes` 指向本地 stub。
- 不需要实现 `/bazaardb/image/{id}`；如果需要本地 BazaarDB 拉取体验，实现 manifest + 本地公开文件 URL 即可。

### 4. FFmpeg/R2 是 installer 侧能力，不是游戏运行时外发

`docs/combat-replay-video-recording.md` 提到 installer 可以从 R2 下载 FFmpeg minimal build。

当前 runtime 结论：

- mod runtime 只检测 `BazaarPlusPlusV4/tools/ffmpeg/ffmpeg(.exe)` 或 PATH 上的 `ffmpeg`。
- runtime 不下载 FFmpeg，不写入工具目录。

判断：

- 这不是游戏或 mod runtime 的联网请求。
- 离线游戏运行时只需要确保 FFmpeg 已本地存在；没有 FFmpeg 时视频录制静默禁用，不影响 replay。

### 5. AutoBazaar agent/analytics API 是未来设计，不是当前接口

`docs/superpowers/specs/2026-05-17-autobazaar-agent-design.md` 描述了 `/agent/build-priors`、`/agent/card-stats`、`/agent/synergy`、`/agent/recent-winning-builds`、`/agent/evaluate-actions` 等未来 analytics/local evaluation API。

当前源码和当前 reference docs 支持的接口只有：

- `GET /v1/context`
- `POST /v1/actions`

判断：

- agent analytics API 不应计入当前游戏联网面。
- 如果要做预定义对局的自动化体验，可以复用当前 AutoBazaar loopback 控制层；策略/训练 API 应作为额外本地工具接口，而不是替代权威 run session server。

### 6. HTTP rate-limit bypass 不是离线方案必需项

`docs/superpowers/specs/2026-05-17-bypass-http-rate-limit-design.md` 是绕过客户端 `HttpDataProvider` rate limit 的设计。

判断：

- 本地离线 facade 不应依赖这个设计来跑通流程；本地服务可以直接按客户端节奏返回。
- live server 的 429 不应被本地补丁吞掉，否则会掩盖真实服务端限流和状态不同步。
- `session-command-protocol.md` 里记录的 `/commands` 429 仍应作为 active failure mode 处理：客户端会触发乐观 undo callback。

## 设计文档覆盖清单

| 文档 | 反查结论 |
|---|---|
| `docs/mod-features-overview.md` | 当前总览基本可信；final marker 描述按"服务端从最后 battle 推导"理解时与源码最一致。 |
| `docs/run-upload.md` | 当前上传流程大体可信；V3 命名、旧文件路径、"客户端直接传 is_final_battle" 需要修正。 |
| `docs/bazaardb-screenshot-upload.md` | 当前 BazaarDB V4 口径可信；"复用 V3 JSON 上传管线"是实现沿革，不代表 V3 endpoint。 |
| `docs/run-logging.md` | 本地 SQLite/run logging 口径与上传/HistoryPanel 设计一致。 |
| `docs/reference/ghost-battle-data-flow.md` | ghost 查询、导入、投影、replay 不翻转的结论可信；final marker 的"set by uploader"应改成"server response contains"，避免暗示当前 mod 上传字段存在。 |
| `docs/reference/sqlite-schema-reference.md` | 本地 SQLite 部分可信；V4 server D1 部分来自外部 repo，本仓库只能交叉验证客户端 DTO 和 ghost 解析。`incoming battle projection is_final_battle` 与当前客户端 DTO 不一致。 |
| `docs/reference/auto-bazaar-http-api-v1.md` | 与当前 `Game/AutoBazaar/AutoBazaarHttpServer.cs` 一致。 |
| `docs/reference/auto-bazaar-decision-surface.md` | 与 AutoBazaar 是控制层的结论一致。 |
| `docs/reference/end-of-run-screenshot-flow.md` | 和 BazaarDB 上传前置截图采集职责一致，不引入额外网络。 |
| `docs/reference/combat-replay-recording.md` | 与本地 replay payload / ghost replay 下载职责一致。 |
| `docs/combat-replay-video-recording.md` | runtime 不下载 FFmpeg；installer 下载是另一个进程/项目的能力。 |
| `docs/superpowers/plans/2026-05-17-autobazaar-http-endpoint.md` | 历史实现计划；当前接口已落地为 `/v1/context` 和 `/v1/actions`。 |
| `docs/superpowers/specs/2026-05-17-autobazaar-agent-design.md` | 未来 agent/analytics 设计；不是当前 runtime 联网接口。 |
| `docs/superpowers/specs/2026-05-17-bypass-http-rate-limit-design.md` | 邻近补丁设计；不应作为离线 facade 的必要组件。 |
| `docs/superpowers/plans/2026-05-24-bazaardb-upload-in-mod.md` | 历史 V3 计划；由当前 `docs/bazaardb-screenshot-upload.md` 取代。 |
| `docs/superpowers/specs/2026-05-24-bazaardb-upload-in-mod-design.md` | 历史 V3/ModCFServerV3 规格；不要按 `/bazaardb/image/{id}` 实现当前本地方案。 |
| 其他 UI/HistoryPanel/Tooltip/Preview 设计文档 | 与联网面弱相关；没有发现会改变本地离线 run server 方案的接口要求。 |

## 修正后的接口口径

| 类别 | 当前接口 | 本地离线实现建议 |
|---|---|---|
| 官方启动 REST | `Config.NetURL` 下 `/api/time`、auth、bootstrap、profile、wallet、collection、store、season 等 JSON REST | 本地 JSON facade，返回稳定账号、库存、英雄、赛季和配置。 |
| 官方 run session | `Config.SocketURL` 下 `POST /sessions`、`POST /commands`、`DELETE /sessions`，MessagePack | 本地 session server，维持 `sid`/`rid`、命令处理、`INetMessage` 输出。 |
| 静态数据 | `{MaintenanceDataURL}/maintenance.json`、`{DataURL}/GameData.db.zip`、translations、Addressables | 优先 bundled/cache；需要 HTTP 时指向本地静态服务。 |
| mod run upload | `POST /run-bundles` JSON，无 auth | 离线默认关闭；若启用本地 stub，按当前 DTO 接收，不要期待 `is_final_battle` 字段。 |
| mod ghost sync | `GET /ghost-battles?player_account_id=...`、`POST /ghost-battles/{battleId}/replay-link`、`GET download_url` | 可选本地 ghost fixture 服务；response 可包含 `is_final_battle`，客户端会解析。 |
| BazaarDB upload | `POST /bazaardb-screenshots` JSON，无 auth；manifest 为外部 BazaarDB bearer 拉取 | 离线默认关闭；展示需要时写本地 manifest + 本地文件 URL。 |
| sponsor catalog | `https://bpp-static.bazaarplusplus.com/supporter-list.json` | 使用本地缓存或内置 fallback。 |
| final build recommendation | `https://bpp-metrics.bazaarplusplus.com/final_builds_for_mod.json` | 使用 20h 本地 cache 或 embedded `final-builds-top50.json`。 |
| AutoBazaar loopback | `GET /v1/context`、`POST /v1/actions` | 保留为外部控制入口；不代替 run session 权威逻辑。 |
| telemetry stub | `api/telemetry` 常量存在，但未确认发送调用 | 本地模式不挂载或只写本地日志。 |
| 旧/测试 UI helper | `MainMenuUIDataHandler` 可构造 `UnityWebRequest.Get` 和测试图片 URL | 本地模式用 allowlist 阻断未知外发。 |

## 全量文档清单

本轮实际枚举到 34 个 `docs/**/*.md` 文件，并逐一按"当前实现、参考文档、历史计划/规格、弱联网相关 UI 文档"分类：

| 文档 | 分类 | 网络/离线结论 |
|---|---|---|
| `docs/bazaardb-screenshot-upload.md` | 当前实现文档 | V4 BazaarDB 上传当前口径。 |
| `docs/combat-replay-video-recording.md` | 当前/邻近实现文档 | FFmpeg 下载是 installer 侧，runtime 不下载。 |
| `docs/combat-status-bar.md` | UI 功能文档 | 无新增网络面。 |
| `docs/history-panel-eliminated-row-indicator-plan.md` | UI 实施计划 | 依赖 `IsBundleFinalBattle`，不定义新接口。 |
| `docs/history-panel-eliminated-row-indicator.md` | UI 设计文档 | 依赖 ghost projection，不定义新接口。 |
| `docs/history-panel-known-issues.md` | UI 问题记录 | 不改变联网结论。 |
| `docs/mod-features-overview.md` | 当前总览 | V4 / AutoBazaar / BazaarDB 总体口径可信，final marker 需按本审计收紧。 |
| `docs/monster-preview-design.md` | 当前 UI/preview 文档 | 源码另有 supporter/final-build 推荐数据拉取，已计入接口表。 |
| `docs/run-logging.md` | 当前实现文档 | 本地 SQLite source-of-truth 口径可信。 |
| `docs/run-upload.md` | 当前实现文档但有残留 | V3 命名、旧路径、final marker 来源需修正。 |
| `docs/reference/auto-bazaar-decision-surface.md` | 当前参考 | AutoBazaar 是控制层，不是 run 引擎。 |
| `docs/reference/auto-bazaar-http-api-v1.md` | 当前参考 | `/v1/context`、`/v1/actions` 与源码一致。 |
| `docs/reference/combat-replay-recording.md` | 当前参考 | replay payload/ghost replay 职责与接口表一致。 |
| `docs/reference/end-of-run-screenshot-flow.md` | 当前参考 | 截图采集不直接外发。 |
| `docs/reference/ghost-battle-data-flow.md` | 当前参考但有措辞风险 | ghost response final marker 可信；不应暗示当前客户端上传该字段。 |
| `docs/reference/hotkeys-reference.md` | 当前参考 | 无新增网络面。 |
| `docs/reference/settings-and-debug-surfaces.md` | 当前参考 | 设置/调试表面，不改变接口口径。 |
| `docs/reference/sqlite-schema-reference.md` | 当前参考但含外部 server 事实 | 本地 SQLite 可信；server D1/final marker 需以服务端 repo 确认。 |
| `docs/reference/upgrade-tooltip-implementation.md` | 当前参考 | 无新增网络面。 |
| `docs/superpowers/plans/2026-05-17-autobazaar-http-endpoint.md` | 历史实施计划 | 已落地为当前 AutoBazaar loopback。 |
| `docs/superpowers/plans/2026-05-17-autobazaar-target-selection-and-hero-select.md` | 历史实施计划 | AutoBazaar 扩展计划，不定义当前云端接口。 |
| `docs/superpowers/plans/2026-05-22-autobazaar-mountable-and-encounter-decoupling.md` | 历史实施计划 | 架构/挂载拆分，不改变联网口径。 |
| `docs/superpowers/plans/2026-05-24-bazaardb-upload-in-mod.md` | 历史 V3 计划 | 被当前 V4 BazaarDB 文档取代。 |
| `docs/superpowers/plans/2026-05-26-mod-architecture-refactor.md` | 历史实施计划 | 架构重构，不定义新联网接口。 |
| `docs/superpowers/specs/2026-05-17-autobazaar-agent-design.md` | 未来规格 | `/agent/*` 不是当前 runtime 接口。 |
| `docs/superpowers/specs/2026-05-17-bypass-http-rate-limit-design.md` | 邻近补丁规格 | 不应作为离线方案必需项。 |
| `docs/superpowers/specs/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md` | 历史规格 | AutoBazaar 架构拆分，不改变当前 HTTP API。 |
| `docs/superpowers/specs/2026-05-22-combat-replay-sfx-silent-analysis.md` | 分析文档 | 音频/回放分析，不新增网络面。 |
| `docs/superpowers/specs/2026-05-23-combat-replay-sfx-impl.md` | 实施规格 | 音频/回放实现，不新增网络面。 |
| `docs/superpowers/specs/2026-05-24-arm-combat-replay-recording-design.md` | 实施规格 | replay/recording intent，不新增网络面。 |
| `docs/superpowers/specs/2026-05-24-bazaardb-upload-in-mod-design.md` | 历史 V3 规格 | 被当前 V4 BazaarDB 文档取代。 |
| `docs/superpowers/specs/2026-05-24-combat-replay-skill-showcase-design.md` | 未来/邻近规格 | supporter CDN 已计入；export 侧离线依赖 sidecar。 |
| `docs/superpowers/specs/2026-05-24-pedestal-aware-preview-display-design.md` | UI 规格 | 无新增网络面。 |
| `docs/superpowers/specs/2026-05-27-history-panel-native-rendering-migration.md` | UI 规格 | 无新增网络面。 |

## 对预定义对局和随机系统方案的影响

本轮审计不改变预定义对局的大架构：

1. 预定义对局入口仍应接管 `/sessions`，根据 selected fixture 创建 run state。
2. 玩家动作仍通过 `/commands` 进入，server 根据 fixture script、命令、RNG stream 生成 `NetMessageGameSim` / `NetMessageCombatSim` / `NetMessageGameStateSync`。
3. 随机系统仍应由本地权威 session server 管理，不依赖客户端 `System.Random`。
4. fixture 模式需要禁用或固定客户端侧展示随机：loadout randomize、random hero/skin、supporter random、VFX/UI random。
5. 如果同时启用 run upload/ghost 本地 stub，final marker 应由本地 stub 从完整 bundle 中推导，或在本地新增显式字段；不要假设当前客户端会上传 `is_final_battle`。

对战系统接口建议保持：

| 接口 | 作用 | 关键结构 |
|---|---|---|
| `POST /local/fixtures/{id}/select` | 选择预定义对局 | fixture id、hero、playMode、seed profile |
| `GET /local/fixtures` | 枚举可体验对局 | metadata、版本、hero、难度、描述、supported game data hash |
| `POST /sessions` | 创建官方协议兼容 session | `InitializeRunCommand` -> `sid`/`rid` + run init messages |
| `POST /commands` | 官方协议兼容动作入口 | `INetCommand` -> deterministic `INetMessage` aggregate |
| `GET /local/replay/{sessionId}` | 调试/复盘命令日志 | command log、rng log、state hash |

随机系统建议继续拆成三层：

- `FixtureSeed`：对局级 seed，决定同一 fixture 的全局身份。
- `RngStream`：按用途拆流，例如 shop、encounter、combat、loot、opponent、visual。
- `DeterminismLedger`：记录每次抽样的 stream、counter、input、output、state hash，便于多人复现同一对局。

## 建议的后续文档修正

当前 reverse docs 已经按当前源码口径记录了主要接口，不需要推翻。后续若允许修改 `docs/`，建议优先修正：

1. `docs/run-upload.md`：V3 残留命名、旧文件路径、final marker 来源。
2. `docs/reference/sqlite-schema-reference.md`：把 "incoming battle projection carries `is_final_battle`" 改成当前可验证事实，或明确该句来自服务端 repo。
3. `docs/reference/ghost-battle-data-flow.md`：把 "`is_final_battle` set by uploader" 改成 "server response contains `is_final_battle`; current mod client parses it but does not upload that field"。
4. 历史 V3 `docs/superpowers/...bazaardb...` 文件加显眼的 historical/superseded 标记，避免后来实现本地方案时误用 `/bazaardb/image/{id}`。
