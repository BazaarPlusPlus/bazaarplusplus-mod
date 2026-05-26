# 第三轮自动复核记录 2026-05-27

## 本轮目标

用户再次要求自动 review 既有结论并反查所有设计文档。本轮在前两轮文档基础上重新枚举、重新抽取关键词、重新反查源码，目标是确认：

- 是否有设计文档漏查。
- 是否有新的联网接口或外部 URL 没写入 reverse docs。
- 是否有现有结论和当前源码冲突。
- 是否需要修正本地离线/预定义对局/随机系统方案。

本轮仍只写文档，没有编译、没有运行测试、没有修改代码。

## 输入清单

重新枚举结果：

- `docs/**/*.md`：34 份。
- `decompiled/reverse-docs/*.md`：本轮开始时 9 份。
- 当前 mod 源码范围：`Game/`、`ModApi/`、`Core/`、`Patches/`、`Storage/`。
- 反编译范围：`decompiled/Assembly-CSharp`、`decompiled/TheBazaarRuntime`、`decompiled/BazaarGameClient`、`decompiled/BazaarGameShared`、`decompiled/BazaarBattleService`。

重新抽取关键词：

- URL / endpoint：`https?://`、`/api/`、`/sessions`、`/commands`、`/run-bundles`、`/ghost-battles`、`/bazaardb`、`/v1/`。
- 网络调用形态：`UnityWebRequest`、`HttpClient`、`HttpListener`、`WebRequest`。
- 版本与鉴权：`V3`、`V4`、`Authorization`、`Bearer`、`auth`、`unauth`、`未认证`。
- final marker：`is_final_battle`、`is_bundle_final_battle`。
- 随机与 fixture：`Random`、`System.Random`、`UnityEngine.Random`、`seed`、`ISeedManager`、`GetSeedManager`、`fixture`、`offline`。

## 第三轮结论

第三轮没有推翻前两轮结论。当前最重要的事实仍是：

1. 官方游戏启动/账号/经济系统走 `Config.NetURL` 下 JSON REST。
2. 进入 run 后玩法命令走 `Config.SocketURL` 下 MessagePack HTTP：`POST /sessions`、`POST /commands`、`DELETE /sessions`。
3. 静态数据走 `Config.MaintenanceDataURL` / `Config.DataURL` / bundled StreamingAssets / Addressables。
4. BazaarPlusPlus 云端同步和推荐数据不是官方 live run 协议；它们是 mod 附加网络面。
5. AutoBazaar 仍只是本地 loopback 控制层，不是本地 run engine。
6. 本地离线方案仍应保留客户端协议，替换为本地 REST facade + 本地 MessagePack session server + 本地静态资源服务。
7. 预定义对局仍应由本地权威 session server 管理 RNG；客户端 UI/VFX/cosmetic 随机必须隔离在 state hash 之外。

## 未发现的新活跃接口

本轮重新反查后，联网面仍可归为以下集合，没有出现新的需要单独设计的活跃 runtime 接口：

| 集合 | 当前结论 |
|---|---|
| 官方 REST | 已在 `network-interface-inventory.md` 的 `/api/*` 表覆盖。 |
| 官方 run session | 已在 `session-command-protocol.md` 覆盖 `/sessions`、`/commands`、`DELETE /sessions`。 |
| 静态/CDN | 已覆盖 maintenance、GameData、translations、announcement image、Addressables。 |
| 旧 TempoNet / battle service | 已作为旧接口面记录，当前主流程不以它为主。 |
| Mod API V4 | 已覆盖 `/run-bundles`、`/ghost-battles`、`/ghost-battles/{id}/replay-link`、`/bazaardb-screenshots`、`/bazaardb/manifest`。 |
| Mod 推荐数据 | 已覆盖 `bpp-static` supporter catalog 和 `bpp-metrics` final build recommendation。 |
| AutoBazaar | 已覆盖 `GET /v1/context`、`POST /v1/actions`。 |
| telemetry stub | 仍只确认到 `api/telemetry` 模型/base 初始化，未发现实际发送调用。 |
| 旧/测试 UI helper | 仍作为潜在面记录：`MainMenuUIDataHandler`、`example.com`、`picsum.photos`。 |
| 外部法律链接 | `playthebazaar.com` privacy/terms/EULA 由 UI 打开，不是游戏数据协议。 |

## 仍然成立的两处文档问题

### 1. `docs/run-upload.md` 有 V3 残留

第三轮再次确认：

- 当前源码是 `ModApi/ModApiRoutes.cs` 和 `ModApi/Clients/*`。
- 当前默认 base 是 `https://mod-api-v4.bazaarplusplus.com`。
- 当前客户端不设置 bearer/API-key header。
- `docs/run-upload.md` 中的 "V3 `run-bundle`" 和旧 `Game/Online/V3Routes.cs` / `Game/Online/RunBundleClient.cs` 路径仍是历史残留。

判定不变：reverse docs 应按 V4 口径描述；如果后续允许改 `docs/`，优先修正 `docs/run-upload.md`。

### 2. final-battle 来源仍是当前最大冲突点

第三轮再次确认：

- `ModApi/Models/RunBundleUploadRequest.cs` 的 `BattleProjection` 没有 `is_final_battle` 字段。
- `Game/RunLogging/Upload/RunBundleUploadStore.cs` 的 `BuildBattleProjection()` 没有填 final marker。
- `ModApi/Clients/GhostBattleClient.cs` 会从 ghost response 的 `is_final_battle` 读取到本地 `IsBundleFinalBattle`。
- 本地 SQLite 字段 `is_bundle_final_battle` 是 mod 本地 schema 名称，不代表 V4 wire 名称。

判定不变：

- 当前客户端能消费 `is_final_battle`，但当前客户端不上传 `is_final_battle`。
- 本地 upload stub 若需要 final marker，应在接收端按完整 bundle 的最后一场 battle 推导，或等未来代码显式增加字段。
- `docs/reference/sqlite-schema-reference.md` 和 `docs/reference/ghost-battle-data-flow.md` 中暗示 "incoming projection carries final marker / uploader sets it" 的说法不能作为本仓库当前源码事实。

## 随机系统复核

第三轮重新抽取 `Random` / `seed` / `ISeedManager` 后，既有随机结论仍成立：

| 随机来源 | 权威性判断 | 离线 fixture 处理 |
|---|---|---|
| `BazaarGameShared.Domain.Game.ISeedManager` | 领域层核心随机接口。 | 本地 engine 应提供兼容 adapter，并在上层使用命名 stream。 |
| `BazaarGameClient.Domain.Models.Run.GetSeedManager()` | 当前客户端返回 null，不是官方 live run 权威随机。 | 不把客户端 run model 当作 RNG authority。 |
| `BazaarBattleService` 旧 `seedManager` | 旧服务实现中承担选择、奖励、目标、PVP opponent 等随机。 | 可作为本地 engine 参考，但要绑定 fixture/game data version。 |
| `CollectionManager.GetRandomizedLoadout()` | `System.Random`，影响 cosmetic loadout，且在 `/sessions` 前可能调用 equip-loadout REST。 | fixture 模式禁用 randomize 或固定 loadout ids。 |
| `HeroSelectButtonsView.SelectRandomHeroImmediate()` / mod RandomHeroPool | `UnityEngine.Random`，影响 selected hero。 | fixture 入口锁定 hero，随机英雄只作为非 fixture UI 功能。 |
| `RandomHeroSkinPoolRuntime.ApplyToRandomizedLoadout()` | `UnityEngine.Random`，影响展示皮肤/收藏品。 | 不写入玩法 state hash；需要展示一致时固定 skin/loadout。 |
| `CardSetPreviewSponsorCatalog` | tier-weighted `UnityEngine.Random`，只影响 sponsor 展示。 | 从玩法 hash 排除；导出 sidecar 时固定一次。 |
| `AutoBazaarUlid` | cryptographic random，只生成 decision id。 | 不属于玩法随机。 |
| VFX/UI/旧测试 UI `UnityEngine.Random` | 动画、数值展示、测试假数据。 | 不纳入 deterministic run state。 |
| `ServersHealthService` / `DataDownloader` retry jitter | 网络重试抖动。 | 本地服务可以保留或去掉，不影响玩法复现。 |

因此，预定义对局不应只固定一个全局 seed。仍建议固定：

- static data version / hash
- fixture id / fixture hash
- initial run state
- named RNG streams
- command log
- response hash / state hash
- optional replay payload hash

## 设计文档反查状态

第三轮的设计文档分类没有变化：

- 顶层 `docs/*.md` 和 `docs/reference/*.md` 是当前口径的主要来源，但 `run-upload`、`ghost-battle-data-flow`、`sqlite-schema-reference` 在 final marker 上需要按源码收紧。
- `docs/superpowers/plans|specs` 中一部分是历史计划或未来设计，不能直接当作当前接口真相。
- BazaarDB 的 V3 `ModCFServerV3`、`mod-api-v3`、`/bazaardb/image/{id}` 仍只作为历史背景；当前口径是 V4 server + public R2 image URL。
- AutoBazaar agent `/agent/*` API 仍是未来设计，不是当前 runtime 网络面。
- Combat replay video 的 FFmpeg 下载仍是 installer 侧，不是 mod runtime 外发。

## 对本地离线方案的最终影响

第三轮没有要求修改总体方案。当前推荐实现顺序仍是：

1. 静态数据固定：使用 bundled/cache `GameData.db.zip`、translations、Addressables，本地 maintenance JSON。
2. 本地 REST facade：满足启动、auth、bootstrap、profile、collection、loadout、wallet/season/challenge 等最小 JSON 返回。
3. 本地 session server：实现 `/sessions`、`/commands`、`DELETE /sessions` 的 MessagePack 协议和 `sid`/`rid` 语义。
4. fixture runtime：用 fixture + named RNG stream 驱动 run state、选择、奖励、PVP opponent、combat。
5. mod 网络面策略：默认关闭上传/同步；需要演示时提供本地 V4-compatible stub。
6. 外部控制：保留 AutoBazaar 作为动作输入层，但不把它当作权威状态生成器。

如果后续进入实现阶段，最值得提前补的一份文档是：

- `messagepack-event-field-catalog.md`：机器化展开所有 `INetMessage`、`GameSimEvent*`、`CombatSimEvent*` 的字段、union key 和本地最小实现需求。

## 第三轮结论

本轮没有发现新的活跃联网接口，也没有发现会推翻离线/预定义对局方案的新证据。需要继续保留的纠偏点仍是：

1. `docs/run-upload.md` 的 V3 残留。
2. final-battle 字段来源和当前客户端 DTO 不一致。
3. 历史 `docs/superpowers` 不能当作当前接口真相。
4. 服务端 D1/Worker 行为必须以 `bazaarplusplus-server` 仓库最终确认；本仓库只能证明客户端 wire contract 和本地处理逻辑。

