---
status: abandoned
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# 反编译与数据包记录

## 已覆盖的反编译程序集

仓库 `run.sh` 的 `decompile_all()` 覆盖这些游戏程序集：

| 程序集 | 当前文档中的用途 |
|---|---|
| `Assembly-CSharp.dll` | Unity 场景/入口层，当前联网分析中不是主要网络实现位置。 |
| `TheBazaarRuntime.dll` | 当前客户端主体：启动、REST、会话 HTTP、静态数据、维护公告、登录、缓存、UI 状态机。 |
| `BazaarGameShared.dll` | MessagePack 命令/消息 DTO、TempoNet REST DTO、静态数据领域模型。 |
| `BazaarGameClient.dll` | 客户端领域模型，当前 run 模型里 `GetSeedManager()` 返回 `null`，说明真实随机逻辑在服务端/旧服务实现。 |
| `BazaarBattleService.dll` | 旧版/历史服务逻辑、旧 REST manager、旧 `BazaarCardDealer` 和战斗/随机实现线索。 |
| `FMODUnity.dll` | 音频，不是业务网络接口来源。 |

已安装游戏目录 `C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed` 里的核心游戏 DLL 与上述反编译目录对应。额外托管 DLL 主要是 Unity、MessagePack、Newtonsoft.Json、Steamworks、SQLite、Polly、Addressables、FMOD 等第三方依赖；没有发现名为 `Magic` 的托管代码目录。

## `Magic` 目录和资源搜索结论

在安装目录递归搜索没有发现名为 `Magic` 的目录。`Magic` 命中主要来自 Addressables 资源名和资源包内容，例如：

- `Music_Jules_14_KitchenMagic`
- `EncounterClickPresentation_MagicMirror_P`
- `Event_MagicMirror`
- `Bank_MagicSatchel`
- `Carpet_Magic`
- `MagicDust`
- `Magic_root_1`

这些是美术、音频、prefab、材质、资源 label，不是额外 DLL。因此不需要为 `Magic` 单独跑代码反编译；如果未来要分析具体资源表现，需要走 Unity 资源/AssetBundle 提取链路，而不是 C# 反编译链路。

## 静态数据包

安装目录中存在：

`C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\StreamingAssets\GameData.db.zip`

ZIP 内只有一个文件：

| 文件 | 原始大小 | 压缩大小 | 时间 |
|---|---:|---:|---|
| `GameData.db` | 32,231,424 bytes | 2,659,325 bytes | 2026-05-21 15:41:04 +08:00 |

SQLite 表结构和行数：

| 表 | 行数 | 列 |
|---|---:|---|
| `cards` | 2884 | `Id TEXT`, `Data BLOB` |
| `challenges` | 80 | `Id TEXT`, `Data BLOB` |
| `collectibles` | 8 | `Id TEXT`, `Data BLOB` |
| `game_modes` | 1 | `Id TEXT`, `Data BLOB` |
| `level_ups` | 30 | `Id INTEGER`, `Data BLOB` |
| `monsters` | 166 | `Id TEXT`, `Data BLOB` |
| `seasons` | 15 | `Id INTEGER`, `Data BLOB` |
| `tooltips` | 1 | `Id TEXT`, `Data BLOB` |

`JsonGameDataManager` 的读取方式：

- 首先找缓存目录里的 `GameData.db`。
- 如果缓存不存在，则从 `StreamingAssets/GameData.db.zip` 解压到缓存。
- 每行 `Data` 是 UTF-8 JSON blob，不是 MessagePack。
- `cards` 使用 `BazaarJsonSerializerSettings` 并行反序列化为 `ITCard`。
- `collectibles` 表每行反序列化为 `List<TCollectible>` 后展开。
- `tooltips` 表每行反序列化为 `Dictionary<string, TTooltipSymbol>` 后展开。

因此本地化/离线化不需要联网拿基础玩法数据，只要本地缓存或 bundled `GameData.db.zip` 存在，静态数据可以完整加载。联网下载的作用是覆盖/更新缓存。

## Addressables 记录

安装目录中 Addressables 位于：

`C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\StreamingAssets\aa`

关键文件：

- `settings.json`
- `catalog.bin`
- `catalog.hash`
- `StandaloneWindows64/*.bundle`

`settings.json` 中 catalog hash/catalog 的 `m_InternalId` 使用 `{UnityEngine.AddressableAssets.Addressables.RuntimePath}` 和 `{UnityEngine.Application.persistentDataPath}`，当前未发现显式 `http://` 或 `https://` 远端加载路径。代码仍会调用 `Addressables.CheckForCatalogUpdates()`、`Addressables.UpdateCatalogs()`、`Addressables.DownloadDependenciesAsync()`，并通过 `Addressables.WebRequestOverride` 为 UnityWebRequest 加 `x-secret` header。

离线方案中应允许 Addressables 走本地 catalog，同时禁止或短路 catalog update 失败对启动流程的影响。

## 数据源职责归纳

| 数据源 | 来源 | 当前用途 | 离线替代 |
|---|---|---|---|
| `maintenance.json` | `{MaintenanceDataURL}/maintenance.json` | 服务器开关、版本、公告、超时、语言列表 | 本地固定 JSON，全部系统可用，版本匹配。 |
| `GameData.db.zip` | `{DataURL}/GameData.db.zip` 或 bundled StreamingAssets | 卡牌、怪物、赛季、挑战、模式、tooltip | 直接使用 bundled 或本地固定版本。 |
| `translations/{locale}.bytes` | `{DataURL}/translations/{locale}.bytes` | 本地化文本覆盖 | 使用缓存/bundled，或本地静态目录。 |
| Addressables catalog/bundles | `StreamingAssets/aa` 或 Unity remote catalog | UI、场景、美术、音频资源 | 使用本地 catalog/bundles，跳过远端更新。 |
| REST bootstrap | `Config.NetURL` | 账号、钱包、英雄、赛季、挑战、收藏、市场 | 本地 JSON facade。 |
| Game session | `Config.SocketURL` | run 命令、战斗、状态同步 | 本地 MessagePack session server 或进程内替代。 |
| BazaarPlusPlus mod API | `mod-api-v4.bazaarplusplus.com` 等 | run bundle、幽灵战斗、截图、推荐数据、赞助名单 | 默认关闭上传，推荐数据走内置/缓存。 |
