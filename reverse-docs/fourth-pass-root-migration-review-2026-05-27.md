# 第四轮复核与根目录迁移记录 2026-05-27

## 本轮目标

本轮根据用户要求再次自动复核结论、反查所有设计文档，并把逆向文档从 `decompiled/reverse-docs` 迁移到仓库根目录下的 `reverse-docs/`，使其可以被 Git 跟踪和推送。

约束：

- 只写文档。
- 不编译、不运行测试、不重新 decompile。
- 不改项目代码。
- 文档最终位置不再依赖 `decompiled/`。

## 迁移说明

仓库根目录没有发现名为 `superpower-deplant` 的目录；实际存在的是 `.superpowers/`。因此按“根目录下同级目录”的意图，使用新的根目录目录：

- `reverse-docs/`

该目录与 `.superpowers/`、`docs/`、`decompiled/`、`Game/`、`ModApi/` 等同级。

从旧位置机械复制的文档包括：

- `README.md`
- `network-interface-inventory.md`
- `data-structure-catalog.md`
- `session-command-protocol.md`
- `offline-local-run-design.md`
- `predefined-match-and-random-system-design.md`
- `decompile-and-data-notes.md`
- `review-audit-2026-05-27.md`
- `design-docs-crosscheck-2026-05-27.md`
- `third-pass-review-2026-05-27.md`

本文件是迁移后的第四轮复核记录。

## 第四轮复核输入

重新读取和反查范围：

- `docs/**/*.md` 共 34 份。
- 旧 `decompiled/reverse-docs/*.md` 共 10 份。
- 当前 mod 源码：`Game/`、`ModApi/`、`Core/`、`Patches/`、`Storage/`。
- 反编译源码：`decompiled/Assembly-CSharp`、`decompiled/TheBazaarRuntime`、`decompiled/BazaarGameClient`、`decompiled/BazaarGameShared`、`decompiled/BazaarBattleService`。

重新抽取关键词：

- URL / endpoint：`https?://`、`/api/`、`/sessions`、`/commands`、`/run-bundles`、`/ghost-battles`、`/bazaardb`、`/v1/`。
- 网络调用形态：`UnityWebRequest`、`HttpClient`、`HttpListener`、`WebRequest`。
- 版本与鉴权：`V3`、`V4`、`Authorization`、`Bearer`、`未认证`、`鉴权`。
- final marker：`is_final_battle`、`is_bundle_final_battle`。
- 随机与 fixture：`Random`、`System.Random`、`UnityEngine.Random`、`seed`、`ISeedManager`、`GetSeedManager`、`fixture`、`offline`。

## 第四轮结论

第四轮没有发现新的活跃联网接口，也没有发现会推翻本地离线方案的新证据。

仍然成立的主结论：

1. 官方启动、账号、经济和 bootstrap 走 `Config.NetURL` 下的 JSON REST。
2. 官方 run 内玩法命令走 `Config.SocketURL` 下的 MessagePack HTTP：`POST /sessions`、`POST /commands`、`DELETE /sessions`。
3. 静态数据和维护状态走 `Config.MaintenanceDataURL`、`Config.DataURL`、bundled `StreamingAssets`、Addressables。
4. BazaarPlusPlus mod API 是附加网络面，不是官方 live run 协议。
5. AutoBazaar 是 loopback 控制层，不是权威 run 引擎。
6. 本地离线形态仍应实现本地 REST facade + 本地 MessagePack session server + 本地静态资源服务。
7. 预定义对局仍应由本地权威 session server 管理 RNG；客户端 UI/VFX/cosmetic 随机不得进入权威 state hash。

## 仍需保留的纠偏点

### `docs/run-upload.md` 的 V3 残留

当前源码和路由是：

- `ModApi/ModApiRoutes.cs`
- `ModApi/Clients/RunBundleClient.cs`
- `ModApi/Clients/GhostBattleClient.cs`
- 默认 base `https://mod-api-v4.bazaarplusplus.com`

因此 `docs/run-upload.md` 中的 "V3 `run-bundle`"、`Game/Online/V3Routes.cs`、`Game/Online/RunBundleClient.cs` 仍应视为历史残留。

### final-battle 来源与客户端 DTO 不一致

当前源码再次确认：

- `RunBundleUploadRequest.BattleProjection` 没有 `is_final_battle` 字段。
- `RunBundleUploadStore.BuildBattleProjection()` 没有填 final marker。
- `GhostBattleClient` 能从 ghost response 读取 `is_final_battle`。
- 本地 SQLite 字段 `is_bundle_final_battle` 只是本地 schema 名称。

因此：

- 当前客户端能消费 `is_final_battle`。
- 当前客户端不上传 `is_final_battle`。
- 本地 V4-compatible upload stub 若需要 final marker，应按 bundle 最后一场 battle 推导。

### 历史设计文档不能作为当前接口真相

`docs/superpowers/plans|specs` 中仍有历史 V3 内容，例如 `ModCFServerV3`、`mod-api-v3`、`/bazaardb/image/{id}`。当前实现应以源码、顶层 `docs/`、`docs/reference/` 和本 `reverse-docs/` 为准。

## 最终推荐方案未变

本地运行、不依赖网络的实现顺序仍建议：

1. 固定静态数据和 Addressables。
2. 本地 REST facade 满足启动和账号/经济/bootstrap。
3. 本地 MessagePack session server 实现 `/sessions`、`/commands`、`DELETE /sessions`。
4. 引入 fixture runtime 和命名 RNG stream。
5. 默认关闭 mod 上传/同步，必要时提供本地 V4-compatible stub。
6. 保留 AutoBazaar 作为外部动作输入，不把它当作权威状态生成器。

如果进入实现阶段，下一份最有价值的文档仍是：

- `messagepack-event-field-catalog.md`

该文档应机器化展开所有 `INetMessage`、`GameSimEvent*`、`CombatSimEvent*` 的字段、union key 和本地最小实现需求。

