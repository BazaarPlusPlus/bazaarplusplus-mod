# 二轮反查审查记录

## 范围

本轮按“只写文档、不编译”的约束，重新审查以下内容：

- `decompiled/reverse-docs/*.md` 中的结论。
- 反编译源码中的网络请求、URL、endpoint、随机相关代码。
- 当前仓库 `docs/` 下和联网/上传/AutoBazaar/ghost battle/run logging/BazaarDB 相关的设计文档。

没有运行 build、test、decompiler 或任何编译命令。

## 结论复核

| 原结论 | 二轮状态 | 证据/说明 |
|---|---|---|
| 官方启动链路依赖 REST、维护状态、静态数据、Addressables、登录和 bootstrap。 | 保持。 | `AppLoader`、`ServersHealthService`、`DataDownloader`、`DataProvider` 仍是关键启动面。 |
| 进入 run 后玩法命令走 `/sessions`、`/commands`、`DELETE /sessions`，body 是 MessagePack。 | 保持。 | `HttpGameClient` 只有这些 session/command 端点；命令和消息 union 已在 `session-command-protocol.md` 记录。 |
| 本地离线方案推荐保留客户端协议，在本机实现 REST facade + MessagePack session server。 | 保持。 | 现有 `AppState`、`Cmd`、`NetMessageProcessor` 和 UI 都围绕原协议工作；替换协议比替换 UI 风险低。 |
| `Magic` 不是额外托管代码目录。 | 保持。 | 安装目录和 Addressables 搜索只发现 `MagicMirror`、`MagicSatchel`、`KitchenMagic` 等资源名；托管 DLL 已由现有 decompile 集覆盖。 |
| BazaarPlusPlus mod API 是外部网络面，但不是游戏启动必要条件。 | 保持，补充。 | `RunBundleClient`、`GhostBattleClient`、`BazaarDbScreenshotClient` 不设置 auth header；身份来自 payload/query。离线模式应默认禁用上传/同步。 |
| AutoBazaar 是本地控制层，不是 run 引擎。 | 保持。 | `AutoBazaarHttpServer` 只暴露 `/v1/context` 和 `/v1/actions`，dispatcher 仍调用原 `AppState`/`Cmd` 路径。 |
| 当前客户端不是官方玩法随机的权威执行方。 | 保持，补充。 | `BazaarGameClient.Domain.Models.Run.GetSeedManager()` 返回 null；`BazaarBattleService` 旧实现里 `seedManager` 才承担选择/目标/战斗随机。补充了客户端侧随机隔离要求。 |

## 二轮发现并修正的点

| 问题 | 影响 | 文档修正 |
|---|---|---|
| 旧/测试 `MainMenuUIDataHandler` 的 sale item JSON 和 `picsum.photos` 图片 URL 初版没有单独列出。 | 属于潜在联网面，虽然当前主流程不依赖。 | 已补进 `network-interface-inventory.md` 和 `data-structure-catalog.md`。 |
| `AnalyticsManager` 定义了 `api/telemetry` 和 `https://localhost:7291/` local base，但当前文件未发现实际发送调用。 | 容易被误判为当前活跃 telemetry 请求。 | 已作为 “potential telemetry stub” 记录，离线方案建议不挂载或只写本地日志。 |
| Mod API 设计文档强调当前 V4 端点无鉴权，身份来自 `player_account_id`。初版文档没有把这一点写清楚。 | 离线/隐私方案需要明确禁用上传和同步。 | 已补充 `RunBundleUploadRequest.PlayerAccountId`、`QueryAgainstMeAsync(playerAccountId)`、`BazaarDbScreenshotUploadRequest.PlayerAccountId` 是身份输入。 |
| `docs/superpowers` 中存在历史 V3 计划/规格，包含 `ModCFServerV3`、`mod-api-v3`、`/bazaardb/image` 等旧说法。 | 如果把历史计划当成现行实现，会和当前 V4 文档/源码冲突。 | 本审查明确：以当前源码、顶层 docs 和 `docs/reference` 为准；`docs/superpowers/plans|specs` 中旧 V3 内容只作历史背景。 |
| 预定义 fixture 初版没有足够强调客户端侧非权威随机。 | loadout randomize、随机英雄/皮肤、赞助名单会让“同一 fixture”展示或命令入口漂移。 | 已在 `predefined-match-and-random-system-design.md` 增加“客户端侧随机隔离”。 |

## 设计文档反查

| 文档区域 | 反查结论 |
|---|---|
| `docs/mod-features-overview.md` | 与 reverse docs 一致：mod 云同步面向 `mod-api-v4.bazaarplusplus.com`，AutoBazaar 是本地 47900 loopback，上传/ghost sync 不是 live run 内的官方玩法协议。 |
| `docs/run-upload.md` | 主接口与 reverse docs 一致：`POST /run-bundles`、`GET /ghost-battles`、`POST /ghost-battles/:battleId/replay-link`；`player_account_id` 是核心身份输入；replay 下载 URL 是短 TTL 链接。二轮交叉审计发现该文档仍有 V3 残留措辞、旧源码路径，以及“客户端直接传 `is_final_battle`”的说法；当前 mod DTO/构造逻辑没有上传该字段。 |
| `docs/bazaardb-screenshot-upload.md` | 与 reverse docs 一致：mod 上传 `/bazaardb-screenshots`，BazaarDB 再拉 `/bazaardb/manifest`；manifest 和公开 `image_url` 不是当前游戏进程主动请求。 |
| `docs/reference/auto-bazaar-http-api-v1.md` | 与 reverse docs 一致：`GET /v1/context`、`POST /v1/actions`、ETag/304、503 no snapshot、64KB POST body 上限、loopback。 |
| `docs/reference/ghost-battle-data-flow.md` | 与 reverse docs 一致：ghost battle 查询和 replay link 是 mod 后台同步/历史面板功能，不是官方 run session。 |
| `docs/reference/sqlite-schema-reference.md` | 与 reverse docs 一致：本地 SQLite 是 run/battle/history 的 source of truth，server artifact body 在 R2/预签 URL。 |
| `docs/combat-replay-video-recording.md` | 有额外 installer 下载 FFmpeg 的设计说明。该下载属于安装器/工具链，不是游戏进程或当前 mod runtime 的联网请求；未并入“游戏联网请求”总表。 |
| `docs/superpowers/plans` 和 `docs/superpowers/specs` | 里面有不少历史实施计划，部分内容已过期。审查时只用来理解演进，不作为当前接口真相来源。 |

## 源码反查摘要

| 维度 | 结果 |
|---|---|
| 网络/URL 字符串抽取 | 静态抽取到 158 个包含 `/api/`、`http(s)`、`/sessions`、`/commands`、静态数据路径的字符串。精确未命中的大多是日志字符串、插值模板或已按规范化路径记录的 endpoint。 |
| 官方 REST | `DataProvider` 当前主接口已覆盖；新增确认 app/achievement、single chest open、pending reward claim、title equip 等插值路径已按规范化路径记录。 |
| 旧 REST | `RequestFacade` / `BazaarRequestManager` 的 collection history、loadout randomize、旧 pending reward、旧 daily purchase、旧 chest listing 创建等已补入旧接口段。 |
| Mod API | `RunBundleClient` / `GhostBattleClient` / `BazaarDbScreenshotClient` 都不添加鉴权 header；只有 content-type 和 factory 的 User-Agent。 |
| AutoBazaar | `AutoBazaarRuntime` 默认端口 47900；`AutoBazaarHttpServer` 写 `endpoint.json`，GET/POST 路径与文档一致。 |
| 随机 | 权威玩法随机应来自本地 server deterministic RNG；客户端 `UnityEngine.Random`/`System.Random` 多为 UI/VFX/debug/cosmetic/retry jitter，必须与 fixture state hash 隔离。 |

## 保留风险

| 风险 | 说明 | 建议 |
|---|---|---|
| 客户端只能反映 wire contract，不包含官方后端真实实现。 | 业务规则细节如商店池、掉落、战斗 resolver 的线上真实版本在服务器侧。 | 本地实现先用 fixture/scripted + 旧 `BazaarBattleService` 参考，逐步补齐。 |
| MessagePack nested event 字段没有逐个事件展开到所有字段级别。 | 已列出 union key 和主要 DTO；所有 `GameSimEvent*` / `CombatSimEvent*` 的每个字段还可继续机器化展开。 | 若进入实现阶段，生成一份 `messagepack-event-field-catalog.md`。 |
| Addressables remote catalog 当前安装包未发现显式远端 URL，但代码仍支持 update catalog。 | 未来版本可能配置远端 catalog。 | 本地模式应强制本地 catalog 或短路 update failure。 |
| 旧 UI/test helper 可达性不完全确定。 | `MainMenuUIDataHandler` 有下载函数，但当前主流程可能不调用。 | 离线模式中统一禁止未知 `UnityWebRequest` 外发，或做 loopback/allowlist。 |
| 历史设计文档和当前实现可能继续漂移。 | `docs/superpowers` 是计划/规格存档，不一定随实现同步。 | 后续审查优先看当前源码、顶层 docs、`docs/reference` 和 reverse docs。 |

## 本轮文档变更

- 更新 `network-interface-inventory.md`：补充 mod API 无鉴权/身份输入、manifest 不是当前 mod client 主动请求、旧/测试 UI、telemetry stub。
- 更新 `data-structure-catalog.md`：补充旧/测试 UI DTO、mod API 身份输入和 manifest route 说明。
- 更新 `predefined-match-and-random-system-design.md`：补充客户端侧随机隔离表。
- 更新 `README.md`：加入本文档索引。

## 二轮交叉审计补充

详见 `design-docs-crosscheck-2026-05-27.md`。需要额外收紧的地方：

- `docs/run-upload.md` 开头的 "V3 `run-bundle`" 是历史残留；当前路由和源码在 `ModApi/ModApiRoutes.cs` / `ModApi/Clients/*`。
- 当前 `RunBundleUploadRequest.BattleProjection` 没有 `is_final_battle` 字段，`RunBundleUploadStore.BuildBattleProjection()` 也不填该字段。离线本地 upload stub 若需要 final marker，应在接收端按 bundle 最后一场推导，或等待后续代码显式增加字段。
