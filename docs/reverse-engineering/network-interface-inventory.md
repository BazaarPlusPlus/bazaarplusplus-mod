# 联网接口总表、数据结构和业务逻辑

## 总体网络面

| 网络面 | 实现位置 | 传输 | 基地址/地址 | 主要业务 |
|---|---|---|---|---|
| 官方 REST | `TheBazaarRuntime/Core.Data.Providers/DataProvider.cs` + `HttpDataProvider.cs` | JSON over `UnityWebRequest` | `Config.NetURL`，默认 `https://enter.staging.playthebazaar.com` | 登录、token、bootstrap、账号、钱包、赛季、挑战、收藏、商店、Steam purchase、post-run 同步。 |
| 官方 run session | `TheBazaarRuntime/Networking/HttpGameClient.cs` | MessagePack over `HttpClient` | `Config.SocketURL`，默认 `http://server.staging.playthebazaar.com` | 创建/恢复 run session、发送玩家命令、接收 `GameStateSync`/`GameSim`/`CombatSim`。 |
| 维护/静态数据 CDN | `ServersHealthService.cs`、`DataDownloader.cs`、`LocalizationService.cs` | 文件下载 over `UnityWebRequest` | `Config.MaintenanceDataURL` / `Config.DataURL`，默认 `https://internal.playthebazaar.com/staging/static` | 维护状态、版本、公告、静态 DB、翻译。 |
| Addressables | `AddressablesManager.cs`、`AddressablesLoader.cs` | Unity Addressables download | 本地 `StreamingAssets/aa`，可有 catalog update | 场景、资源包、音频。 |
| 旧版 TempoNet facade | `BazaarGameShared.TempoNet.Requests/RequestFacade.cs` | JSON over `HttpClient` | 构造时传入 | 旧客户端/共享库 REST 封装，覆盖账号、市场、排行榜等。 |
| 旧版 battle service manager | `BazaarBattleService/BazaarRequestManager.cs` | JSON over `HttpClient` | 默认 `https://dev-temponet.azurewebsites.net` | 旧 run/ghost/savegame/marketplace/chest/profile 接口。当前主流程不以它为主。 |
| BazaarPlusPlus mod API | `ModApi/*`、`Game/*Upload*` | JSON/bytes over `HttpClient` | `https://mod-api-v4.bazaarplusplus.com` 等 | run bundle 上传、幽灵战斗同步、截图上传、推荐/赞助数据。 |
| AutoBazaar 本地接口 | `AutoBazaar/*` + `Game/AutoBazaarHost/*` | JSON over loopback `HttpListener` | `http://127.0.0.1:47900` 默认 | 暴露当前决策上下文，接受外部自动化 action；Host 默认不编译，需 `EnableAutoBazaarHost=true` + `[AutoBazaar] Enabled=true`。 |
| 潜在 telemetry stub | `AnalyticsManager.cs` | `HttpClient` | `Config.NetURL` 或 `https://localhost:7291/` | 定义了 `api/telemetry` 和统计模型，但当前反编译文件未发现实际发送调用。 |
| 旧/测试主菜单数据 | `MainMenuUIDataHandler.cs` | JSON / image URL over `UnityWebRequest` 或后续图片加载 | 相对路径、`example.com` 常量、`picsum.photos` 测试图 | 旧市场/收藏 UI 测试数据下载。当前主流程不依赖，但属于潜在联网面。 |
| 外链 | UI settings / terms | `Application.OpenURL` | playthebazaar.com | 隐私、条款、EULA、公告链接。 |

## 通用配置和 header

`TheBazaarRuntime/TheBazaar/Config.cs` 默认值：

| 字段 | 默认值 |
|---|---|
| `NetURL` | `https://enter.staging.playthebazaar.com` |
| `SocketURL` | `http://server.staging.playthebazaar.com` |
| `DataURL` | `https://internal.playthebazaar.com/staging/static` |
| `MaintenanceDataURL` | `https://internal.playthebazaar.com/staging/static` |
| `CdnSecret` | `3M2LiAdRtaP6igDHcekBT9hcbP5BMXQ` |

REST JSON 请求 header：

- `X-Platform: Steam`
- `X-ClientFlavor: Paid`
- `Authorization: Bearer <accessToken>`，token 存在时发送
- body 为 UTF-8 JSON，`UploadHandlerRaw.contentType = application/json`

静态下载 header：

- `x-secret: 3M2LiAdRtaP6igDHcekBT9hcbP5BMXQ`
- `If-None-Match: <etag>`，缓存文件存在且不是断点续传时
- `Range: bytes=<offset>-`，存在 `.tmp` 临时文件时

run session header：

- 初始化后默认 header：`aid`、`uid`、`Authorization: Bearer <token>`
- `/commands` 额外 header：`sid`、`rid`
- body：`application/msgpack`

## 启动链路业务逻辑

`AppLoader` 注册服务后按依赖执行任务：

1. `EnsureInternetAsync`：通过 `UnityConnectivityService` 检查 `Application.internetReachability`，无网络会弹窗，用户退出则 `Application.Quit()`。
2. `InitializeCommandArgs`：解析命令行，当前反编译版本主要处理 `disableCinematics`。
3. `ServersHealthService.ParseAndCheckAll`：下载并解析 `maintenance.json`，控制服务器可用、维护、版本强更、公告和 run session 超时。
4. `AddressablesManager.InitializeAsync`：初始化 Addressables、检查 catalog 更新、预下载 `RequiredOnStartup`。
5. `GameInstance.InitializeStaticData`：下载/加载 `GameData.db.zip`，失败时允许清缓存退出或 `Data.CreateFallbackManager()`。
6. `LoginManager.TryAutoLogin` + `AccessTokenProvider`：自动登录并刷新 access token。
7. `GameInstance.ConfigureServerTime`：GET `/api/time`，用服务器时间校正本地 `Data.CurrentServerTime`。
8. `DataProvider.GetBootstrapData` 与 `GetPlayerProfileCareer` 并发拉取，`BootstrapApplier` 写入 `ClientCache` 和 profile。

离线阻断点主要是第 1、3、6、7、8 步；静态数据本身有 bundled fallback。

## 静态/CDN 接口

| 方法 | URL | 数据结构 | 实现方式 | 业务逻辑 |
|---|---|---|---|---|
| GET | `{MaintenanceDataURL}/maintenance.json?timestamp=<utc>` | `StatusDto`：`systems: Dictionary<string,SystemEntry>`、`versions: Dictionary<string,string>`、`maintenance`、`announcement`、`httpGameClientTimeouts`、`breakingChange`、`locales` | `UnityWebRequest.Get`，60s timeout，5 次 Polly 指数退避+jitter，支持 ETag、Range、304、404 | 控制是否可进游戏；维护中弹窗退出；`breakingChange` 且客户端版本不在 `versions` 列表时要求更新；提供公告和 `HttpGameClient` 超时配置。 |
| GET | `{DataURL}/GameData.db.zip` | ZIP 内 `GameData.db`；表见 `decompile-and-data-notes.md` | `UnityWebRequest` + `DownloadHandlerFile`，30s timeout，4 次重试，最多 4 并发，支持 ETag/Range | 下载后解压到缓存；`JsonGameDataManager` 读取 JSON blob，构造卡牌、怪物、模式、赛季等静态数据。 |
| GET | `{DataURL}/translations/{locale}.bytes` | 翻译二进制/bytes，locale 来自 `maintenance.json.locales` | 同 `DataDownloader.DownloadTranslationsAsync` | 语言文本覆盖。失败时可以继续使用缓存/bundled。 |
| GET | `announcement.image` 解析后的 URL | 图片纹理 | `UnityWebRequestTexture.GetTexture` + `x-secret` | 主菜单公告图片。`announcement.link` 通过 `Application.OpenURL` 打开。 |
| Addressables | catalog hash/catalog/bundle | Unity `ContentCatalogData`、AssetBundle | `Addressables.CheckForCatalogUpdates`、`UpdateCatalogs`、`DownloadDependenciesAsync`，WebRequestOverride 加 `x-secret` | 资源更新和预下载。当前安装目录 catalog 指向本地 runtime path，未发现显式远端 URL。 |

## 当前官方 REST 接口

这些接口都由 `DataProvider` 通过 `HttpDataProvider.SendAsync` 实现：同一 endpoint 1 秒内重复调用会被本地限流，最多 3 次请求尝试，失败重试等待 3s、6s，timeout 30s。若 `ServersHealthService.AllAvailable == false`，直接返回 `503 ServiceUnavailable`。主要请求/响应字段集中整理在 [data-structure-catalog.md](data-structure-catalog.md)。

### Auth / Account

| 方法 | 路径 | 请求结构 | 响应结构 | 业务逻辑 |
|---|---|---|---|---|
| GET | `/api/time` | none | string date/time | 启动时校正服务器时间；失败弹窗并退出。 |
| POST | `/api/auth/login/silent` | `SilentLoginAuth { SteamTicket }` | `LoginResponse` | Steam 静默登录，成功后缓存 access/refresh token。 |
| POST | `/api/auth/external` | `GooglePlayExternalAuth { provider, proofType, proof.serverAuthCode, deviceId }` | `LoginResponse` | Android Google Play 外部登录。 |
| POST | `/api/auth/login/user` | `LoginAuth { Email, Password }` | `LoginResponse` | 邮箱密码登录。 |
| POST | `/api/auth/register` | `RegistrationAuth { Username, Password, Email, FirstName, LastName, DateOfBirth, MarketingOptIn }` | `LoginResponse` | 账号注册。 |
| POST | `/api/auth/refreshtokens` | `RefreshTokenRequest { RefreshToken }` | `RefreshTokenResponse` | access token 过期前 3 分钟刷新；失败后 30 秒内阻塞重复刷新。 |
| POST | `/api/auth/resetpassword` | JSON string email | `EmptyResponse` | 发送重置密码。 |
| POST | `/api/accounts/check-username` | `CheckUsernameAuth { Username }` | `EmptyResponse` | 用户名可用性检查，错误日志关闭。 |
| POST | `/api/accounts/link-account` | `LinkAuth { Email, Password, SteamTicket }` | `EmptyResponse` | Steam 账号绑定邮箱账号。 |
| POST | `/api/Accounts/client/change-email` | `ChangeEmailAuth { Password, EmailAddress }` | `EmptyResponse` | 修改邮箱。 |
| POST | `/api/Accounts/sendconfirmation` | none | `EmptyResponse` | 发送邮箱验证。 |
| GET | `/api/accounts/me/email-verification-status` | none | bool | 查询邮箱验证状态。 |
| GET | `/api/Accounts/feature-access` | none | `FeatureAccessResponse { accountId, chestPurchases, prizePasses, subscriptions }` | 启用/隐藏付费和功能入口。 |
| GET | `/api/Accounts/me/summary` | none | `AccountSummaryResponse { HasViewedDailySpecial, DemoSummary }` | 账号摘要，写入 `ClientCache.AccountSummary`。 |

`LoginResponse` 字段：`AccessToken`、`RefreshToken`、`AlternateRefreshToken?`、`AccessTokenExpiresAt`、`CurrentTime`。  
`RefreshTokenResponse` 字段：`AccessToken`、`RefreshToken`、`AlternateRefreshToken`、`AccessTokenExpiresAt`。

### Bootstrap / Profile / Rank

| 方法 | 路径 | 请求结构 | 响应结构 | 业务逻辑 |
|---|---|---|---|---|
| GET | `/api/Bootstrap/app` | none | `BootstrapResponse` | 启动主聚合接口，包含 account/profile/wallet/rank/season/challenges/heroes/collection/chests/marketplace/leaderboard/pendingRewards/runRewardLevels/errors。 |
| GET | `/api/Bootstrap/post-run` | none | `PostRunBootstrapResponse` | run 结束后刷新 wallet、challenges、profile、featureAccess、seasonTrack、chests。 |
| GET | `/api/PlayerProfiles/me` | none | `PlayerProfileResponse` | 账号 ID、用户名、title、rank、active run，写入 profile cache。 |
| GET | `/api/PlayerProfiles/me/career` | none | `PlayerProfileCareerResponse` | 个人生涯统计，启动时与 bootstrap 并发拉取。 |
| GET | `/api/PlayerProfiles/{accountId}/career` | none | `BazaarPlayerCareer` | 查询其他玩家生涯。 |
| GET | `/api/PlayerProfiles/career?userName=<name>` | none | `BazaarPlayerCareer` | 按用户名查询生涯。 |
| POST | `/api/PlayerProfiles/reset-tutorial` | none | `EmptyResponse` | 重置教程完成状态。 |
| GET | `/api/PlayerRanks/me` | none | `PlayerRankResponse` | 查询当前玩家 rank。 |
| GET | `/api/PlayerRanks/me?seasonId=<id>` | none | `PlayerRankResponse` | 查询指定赛季 rank。 |
| GET | `/api/Leaderboards/position/me?seasonId=<id>` | none | `LeaderboardPositionResponse` | 查询自己排行榜位置。 |
| GET | `/api/Wallets/me` | none | `WalletResponse` | 查询钱包：`Gems`、`RankedVouchers`、`DailyRankedVouchers`。 |
| GET | `/api/RunRewardLevels` | none | `RunRewardLevelResponse[]` | run 胜场奖励配置。 |

`BootstrapResponse` 关键字段：`serverTime`、`account`、`profile`、`wallet`、`playerRank`、`season`、`challenges`、`heroes`、`collection`、`chests`、`marketplace`、`leaderboard`、`pendingRewards`、`runRewardLevels`、`errors`、`hasErrors`。  
离线 facade 最少需要返回 profile、owned heroes、collection/loadouts、season、wallet、runRewardLevels，否则主菜单和开始 run 会缺 profile/cache。

### Collection / Loadouts / Heroes / Titles

| 方法 | 路径 | 请求结构 | 响应结构 | 业务逻辑 |
|---|---|---|---|---|
| GET | `/api/CollectionItems` | none | `Guid[]` | 当前客户端只用 item id 列表作为 owned collection。 |
| GET | `/api/collectionitems/duplicate-rates` | none | `Dictionary<string,int>` | 开箱重复物品换 gem 比例。 |
| GET | `/api/Heroes/listings` | none | `ClientHeroListingsResponse` | 可购买英雄列表、价格、折扣。 |
| GET | `/api/Heroes/owned` | none | `OwnedHero[]` | 已拥有英雄。 |
| POST | `/api/app/heroes/purchase` | `{ heroId }` | `StateDeltaResponse` | gem/商店购买英雄，应用 state delta。 |
| GET | `/api/purchase/listings/Hero` | none | `PurchaseHeroListings` | 新英雄付费 listing。 |
| GET | `/api/Loadouts/me` | none | `HeroLoadoutsResponse` 或旧 `BazaarCollectionLoadout` | 获取所有英雄 loadout；当前 `GetAllHeroLoadouts` 写入 `ClientCache.HeroLoadouts`。 |
| GET | `/api/Loadouts/me/{heroId}` | none | `BazaarHeroLoadout` | 获取单英雄 loadout。 |
| POST | `/api/Loadouts/hero/{heroId}/equip/{collectibleId}` | none | `BazaarHeroLoadout` | 装备某个 cosmetic/collectible 并更新本地 cache。 |
| POST | `/api/Loadouts/hero/{heroId}/unequip/{collectibleId}` | none | `BazaarHeroLoadout` | 卸下某个 cosmetic/collectible。 |
| POST | `/api/Loadouts/hero/{heroId}/equip-loadout` | `EquipLoadoutRequest` | `BazaarHeroLoadout` | 一次性设置 board/carpet/cardBack/toy/stash/bank/album/heroSkin；开始 run 前 randomize loadout 会调用。 |
| GET | `/api/Title{Prefix|Suffix}es/{titleID}` | none | `TitleResponse` | 获取单个 title。 |
| GET | `/api/Title{Prefix|Suffix}es?id=<csv>` | none | `TitleResponse[]` | 获取多个/全部 title。 |
| GET | `/api/Title{Prefix|Suffix}es/owned` | none | `PrefixTitleResponse` | owned titles。 |
| POST | `/api/Title{Prefix|Suffix}es/equip/{id|null}` | none | `EmptyResponse` | 装备或清空 title。 |

`BazaarHeroLoadout` 字段：`accountId`、`heroId`、`boardId?`、`carpetId?`、`cardBackId?`、`toyId?`、`stashId?`、`bankId?`、`albumId?`、`heroSkinId?`、`cardSkinIds?`、`randomizeLoadout`。  
`EquipLoadoutRequest` 字段：同 loadout 的 cosmetic id，但没有 `heroId/accountId/randomizeLoadout`。

### Seasons / Challenges / Rewards

| 方法 | 路径 | 请求结构 | 响应结构 | 业务逻辑 |
|---|---|---|---|---|
| GET | `/api/Seasons` | none | `SeasonResponse[]` | 所有赛季。 |
| GET | `/api/Seasons/current` | none | `SeasonResponse` | 当前赛季。 |
| GET | `/api/Seasons/{seasonID}` | none | `SeasonResponse?` | 指定赛季。 |
| GET | `/api/SeasonTracks/{seasonId}` | none | `SeasonTrackResponse` | battle pass / season track 配置。 |
| GET | `/api/SeasonTrackProgressions/me?seasonId=<id>` | none | `SeasonTrackProgressionResponse` | 当前账号赛季进度。 |
| POST | `/api/SeasonTrackProgressions/reset` | none | `EmptyResponse` | 重置 season track。 |
| POST | `/api/app/seasontrackprogressions/claimall/{seasonId}` | none | `AppClaimAllRewardsResponse` | 领取全部可领取 track 奖励。 |
| POST | `/api/app/seasontrackprogressions/claim` | `{ seasonId, tier, isPaid }` | `StateDeltaResponse` | 领取单个 track 奖励。 |
| GET | `/api/SeasonRankRewards?seasonId=<id>` | none | `SeasonRankRewardResponse[]` | 赛季排名奖励。 |
| GET | `/api/Challenges/me` | none | `PlayerChallengesResponse` | 日/周挑战进度。 |
| POST | `/api/Challenges/daily/reroll` | `RerollDailyChallengeRequest { dailyChallengeId }` | `PlayerChallengesResponse` | 重随每日挑战。 |
| POST | `/api/Challenges/weekly/reroll` | `RerollWeeklyChallengeRequest { weeklyChallengeId }` | `PlayerChallengesResponse` | 重随每周挑战。 |
| POST | `/api/Challenges/acknowledge/{challengeId}` | none | `EmptyResponse` | 标记挑战完成提示已读。 |
| POST | `/api/app/challenges/progress` | none | `StateDeltaResponse` | 应用挑战进度 state delta。 |
| POST | `/api/app/achievements/claim/{achievementId}` | none | `StateDeltaResponse` | 领取成就奖励。 |
| GET | `/api/PendingRewards/me` | none | `PendingRewardResponse[]` | 未领取奖励。 |
| POST | `/api/app/pendingrewards/claim/{pendingRewardId}` | `PendingRewardRequest { pendingRewardId }` | `StateDeltaResponse` | 启动后自动领取 subscription pending reward。 |

`StateDeltaResponse` 字段：`Wallet?`、`SeasonTrackProgression?`、`PlayerRank?`、`Challenges?`、`OwnedHeroesAdded`、`CollectionItemsAdded`、`ChestsAdded`、`OwnedTitlePrefixesAdded`、`OwnedTitleSuffixesAdded`、`CollectionItemsRemoved`、`ChestsRemoved`、`PendingRewardsRemoved`、`DailySpecialPurchased`。  
所有 `app/*` 写接口通常不单独刷新全量 cache，而是通过 `StateDeltaApplier` 对本地 cache 做增量应用。

### Chests / Store / Purchase

| 方法 | 路径 | 请求结构 | 响应结构 | 业务逻辑 |
|---|---|---|---|---|
| GET | `/api/Chests` | none | `ChestResponse[]` | 所有 chest。 |
| GET | `/api/Chests/unopened` | none | `ChestResponse[]` | 未开启 chest，写入 `ClientCache.Chests`。 |
| GET | `/api/Chests/drop-rates/{seasonId}` | none | `ChestDropRatesResponse` | 展示掉落概率。 |
| GET | `/api/Chests/store-prices` | none | `ChestBundleResponse[]` | 宝箱商店价格。 |
| POST | `/api/Chests/grant` | `ChestRequest { AccountId, SeasonId, Count }` | `ChestResponse[]` | grant 后刷新 unopened chests。 |
| POST | `/api/app/chests/store-purchase` | `ChestPurchaseRequest { quantity }` | `AppStorePurchaseChestsResponse` | gem 购买 chest，返回 state + chests。 |
| POST | `/api/app/chests/open` | `ChestOpenRequest { SeasonId, Count, IncludedChestIDs }` | `AppOpenChestsResponse` | 批量开 chest。 |
| POST | `/api/app/chests/{chestId}/open` | none | `AppOpenChestResponse` | 单个 chest open。 |
| GET | `/api/CurrencyListings/{listingType}` | none | `CurrencyListingsResponse` | 货币 listing。 |
| GET | `/api/CurrencyListings/purchase/{transactionID}` | none | `CurrencyListingPurchaseStatus` | 查询货币购买状态。 |
| POST | `/api/purchase` | `PurchaseTokenRequest { ListingType, AcceptTermsOfService, Quantity, ListingID }` | `StripePurchaseResponse` | Stripe checkout token。 |
| GET | `/api/purchase/{transactionID}` | none | `GetPurchaseTransactionResponse` | 查询 purchase transaction。 |
| POST | `/api/purchase/steam` | `StartPurchaseRequest { AcceptTermsOfService, ListingId, Quantity, UserSession }` | `StartPurchaseResponse { TransactionId, OrderId, SteamUrl }` | Steam purchase 开始。 |
| POST | `/api/Purchase/steam/finalize/{orderId}` | none | `EmptyResponse` | Steam purchase finalize。 |
| POST | `/api/Purchase/steam/transaction/{orderId}` | none | `TransactionResponse` | Steam transaction 状态。 |
| POST | `/api/Steam/manage-dlc/me` | none | `EmptyResponse` | 刷新/管理 Steam DLC entitlements。 |
| GET | `/api/MarketplaceListings/specials/daily/me` | none | `ClientDailySpecialListingsResponse` | 每日特殊商店。 |
| POST | `/api/app/marketplacelistings/specials/daily/purchase/gems/{listingId}?collectibleType={type}` | none | `AppPurchaseDailySpecialResponse` | gem 购买 daily special。 |
| POST | `/api/MarketplaceListings/chests/{listingId}/buy` | none | `EmptyResponse`/status | 旧 chest marketplace purchase wrapper。 |

离线模式不应该触发真实支付。推荐对所有 purchase/Steam/Stripe 接口返回本地 fake 状态或禁用 UI；只给 run 所需 entitlement/collection 一个固定本地账户状态。

### Misc / Run Completion

| 方法 | 路径 | 请求结构 | 响应结构 | 业务逻辑 |
|---|---|---|---|---|
| POST | `/api/game/feedback` | raw UTF-8 string | `EmptyResponse` | 反馈/bug report。 |
| POST | `/api/app/runs/complete` | none | `StateDeltaResponse` | run 完成后应用奖励/挑战/排名等状态变化。 |

## Run session 接口

完整结构见 [session-command-protocol.md](session-command-protocol.md)。这里列网络层接口：

| 方法 | 路径 | 请求 | 响应 | 业务逻辑 |
|---|---|---|---|---|
| POST | `/sessions` | MessagePack `InitializeRunCommand { GameModeId?, PlayMode, SelectedHero }` | MessagePack `INetMessage`，header 必须有 `sid`，可有 `rid` | 创建新 run session 或恢复 session；响应通常包含 `NetMessageRunInitialized`、`NetMessageGameStateSync`、`NetMessageGameSim` 的聚合或序列。 |
| POST | `/commands` | MessagePack `INetCommand` union：买/移动/选择/出售/reroll/退出/底座/放弃/cheat | MessagePack `INetMessage`，header 更新 `rid` | 处理 run 内全部玩家动作，返回状态同步、模拟事件或战斗事件。 |
| DELETE | `/sessions` | header `sid` | 无关心 body | 客户端退出/Dispose 时 best-effort 删除 session，失败只日志。 |

## 旧版 RequestFacade / BazaarRequestManager

`RequestFacade` 和 `BazaarRequestManager` 是重要参考，因为它们暴露了历史/共享接口和旧本地战斗服务的业务边界。当前主客户端 flow 主要使用 `DataProvider` + `HttpGameClient`。

### RequestFacade 额外/旧接口

| 路径 | 数据结构 | 业务 |
|---|---|---|
| `/api/Leaderboards?seasonId=&pageNumber=&pageSize=` | `LeaderboardResponse` | 分页排行榜。 |
| `/api/Leaderboards/position/me?seasonId=` | `LeaderboardPositionResponse` | 自己位置。 |
| `/api/Leaderboards/position?seasonId=&accountId=` | `LeaderboardPositionResponse` | 任意账号位置。 |
| `/api/PlayerRanks?userName=<name>&seasonId=<id>` | `PlayerRankResponse` | 按用户名查询 rank。反编译里无 season 时拼接少了 `=`：`?userName` + userName，应按旧实现记录。 |
| `/api/SeasonTrackProgressions/skip` | `SkipTierRequest` | 旧跳级接口。 |
| `/api/SeasonTrackProgressions/claim` | `ClaimTierRequest` | 旧领取 track 奖励。 |
| `/api/SeasonTrackProgressions/claimall/{seasonId}` | bool/status | 旧 claim all route，无 `/app` 前缀。 |
| `/api/CollectionItems/{collectibleId}/history` | `BazaarCollectionTransaction[]` 或 `int` | 旧收藏交易历史/价格查询。 |
| `/api/Loadouts/me?heroId={heroId}` | `BazaarHeroLoadout` | 旧 facade 的单英雄 loadout 查询变体。 |
| `/api/Loadouts/hero/{heroId}/randomize` | `SetRandomizeRequest { randomize }` | 设置英雄 loadout 是否随机化。 |
| `/api/MarketplaceListings/items/search` | `BazaarMarketplaceSearchRequest` | 搜索物品市场。 |
| `/api/MarketplaceListings/items` | `BazaarCreateMarketplaceListing` / `BazaarMarketplaceListing` | 创建物品 listing。 |
| `/api/MarketplaceListings/items/me` | `BazaarMarketplaceListing[]` | 我的物品 listing。 |
| `/api/MarketplaceListings/items/{id}` | DELETE/POST | 删除或购买物品 listing。 |
| `/api/MarketplaceListings/chests/search` | `BazaarChestMarketplaceSearchRequest` | 搜索 chest market。 |
| `/api/MarketplaceListings/chests` | `BazaarCreateChestMarketplaceListing` / `BazaarChestMarketplaceListing` | 创建 chest listing。 |
| `/api/MarketplaceListings/chests/me` | `BazaarChestMarketplaceListing[]` | 我的 chest listing。 |
| `/api/MarketplaceListings/chests/{id}` | DELETE/POST | 删除或购买 chest listing。 |
| `/api/MarketplaceListings/specials/daily/purchase` | `PurchaseDailySpecialListingResponse` | 旧 daily special purchase。 |
| `/api/Chests/open` | `ChestOpenRequest` | 旧开箱。 |
| `/api/Chests/store-purchase` | `ChestPurchaseRequest` | 旧商店买 chest。 |
| `/api/Chests/store-price` | `ChestBundleResponse[]` | 旧 chest 价格。 |
| `/api/Chests/admin/grant` | `ChestRequest` | 管理 grant。 |
| `/api/CurrencyListings` | `CurrencyListingsResponse` | 旧无 listing type 的货币 listing 聚合。 |
| `/api/Heroes/purchase` | `PurchaseHeroRequest` | 旧英雄购买。 |
| `/api/PendingRewards/me` | `PendingRewardResponse[]` | 旧 pending rewards 列表。 |
| `/api/PendingRewards/{id}/claim` 或相近路径 | `PendingRewardRequest` | 旧 pending reward 领取。 |

### BazaarRequestManager 旧接口

默认 base：`https://dev-temponet.azurewebsites.net`，`HttpClient.Timeout = 30s`，证书校验回调直接返回 true。

| 路径 | 数据结构 | 业务 |
|---|---|---|
| `/api/game/getghost?day=` | 旧 ghost DTO | 按 day 拉取 ghost opponent。 |
| `/api/game/getspecificspook?id=` | `BazaarPVPDataObject` 等 | 拉取指定 ghost。 |
| `/api/game/addghost` | ghost/save DTO | 上传 ghost。 |
| `/api/game/savegame` | `SaveGame` | 旧云存档。 |
| `/api/game/loadgame` | `SaveGame` | 旧云读档。 |
| `/api/game/deleteactivesave` | none | 删除 active save。 |
| `/api/game/feedback` | raw string | 旧 bug report/feedback。 |
| `/api/Runs/complete` | `BazaarRunCompleteObject` | 旧 run 完成上报。 |
| `/api/auth/playerprofile` | profile DTO | 旧玩家 profile auth。 |
| `/api/PlayerProfiles` / `?name=` | `BazaarPlayerProfile` | 查询 profile。 |
| `/api/CollectionItems` / `/{id}` | collection DTO | 旧收藏读写。 |
| `/api/CollectionItems/{collectibleId}/history` | `BazaarCollectionTransaction[]` 或 `int` | 旧收藏交易历史/价格查询。 |
| `/api/Loadouts/me`、`/equip/{id}`、`/unequip/{id}` | `BazaarCollectionLoadout` | 旧 loadout。 |
| `/api/MarketplaceListings/*` | marketplace DTO | 旧市场。 |
| `/api/MarketplaceListings/chests` | `BazaarCreateChestMarketplaceListing` / `BazaarChestMarketplaceListing` | 旧 chest listing 创建。 |
| `/api/Chests/*` | chest DTO | 旧 chest。 |

离线本地实现不需要优先兼容这些旧接口，除非某些老 UI 或 mod 功能仍显式调用。建议先记录和 stub；发现实际调用再补行为。

## BazaarPlusPlus mod 网络接口

### Mod API

默认 base：`https://mod-api-v4.bazaarplusplus.com`。`BppHttpClientFactory` 设置 `User-Agent: BazaarPlusPlus/<version>` 和可选 suffix，默认上传 timeout 60s。当前 mod client 没有设置 bearer/API-key auth header；身份主要来自请求体或 query 中的 `player_account_id`。

| 方法 | 路径/URL | 请求结构 | 响应结构 | 业务 |
|---|---|---|---|---|
| POST | `/run-bundles` | `RunBundleUploadRequest`：`SchemaVersion`、`PlayerAccountId`、`SubmittedAtUtc`、`ArtifactCodec`、`ArtifactBytes`、`RunProjection`、`BattleProjections` 等 | success/failure string | 上传 run bundle 和战斗 projection；当前客户端不附加鉴权 header，`PlayerAccountId` 必须由 payload 提供。 |
| GET | `/ghost-battles?player_account_id=<id>&limit=<1..200>` | none | `{ battles: [...] }` -> `GhostBattleImportRecord[]` | 同步“against me”的 ghost battle 列表；`player_account_id` 为空时客户端直接返回 `player_account_id_required`，不会请求网络。 |
| POST | `/ghost-battles/{battleId}/replay-link` | none | `{ download_url }` | 为 ghost battle 获取 replay 下载链接。现有设计文档说明该 URL 是短 TTL 预签下载链接；客户端只要求响应里存在 `download_url`。 |
| GET | `download_url` | none | bytes | 下载 replay payload。 |
| POST | `/bazaardb-screenshots` | `BazaarDbScreenshotUploadRequest`：账号、截图、run、hero、rank、图片 bytes 等 | success/failure | 上传 BazaarDB 截图；当前客户端不附加鉴权 header；4xx 除 408/429 视作永久失败。 |
| GET | `/bazaardb/manifest...` | manifest path | manifest JSON | route 已定义，但当前 mod client 未发现调用；现有设计文档描述为 BazaarDB 服务端拉取 manifest，而不是游戏进程主动拉取。 |

### Mod 额外远端数据

| URL | 数据结构 | 实现 | 业务 |
|---|---|---|---|
| `https://bpp-static.bazaarplusplus.com/supporter-list.json` | `{ name, tier }[]` | 10s timeout，1h temp cache，失败用 fallback | Monster preview 赞助文本随机展示。 |
| `https://bpp-metrics.bazaarplusplus.com/final_builds_for_mod.json` | `heroes -> builds/cardIndex/subsetIndex` | 10s timeout，20h 本地 cache，失败用内置 JSON | Monster preview/card set 推荐最终阵容。 |

离线模式应默认关闭上传类功能；推荐/赞助数据使用本地 cache 或内置 fallback。

## AutoBazaar 本地 HTTP 接口

这是 mod 已有的 loopback 自动化接口，不依赖互联网。

| 方法 | 路径 | 请求结构 | 响应结构 | 业务 |
|---|---|---|---|---|
| GET | `/v1/context` | header 可带 `If-None-Match: "<tickId>"` | `AutoBazaarContext`，304 支持，503 无 snapshot | 暴露当前 run/选择/卡牌/可执行动作。 |
| POST | `/v1/actions` | `AutoBazaarAction`，body 最大 64KB | success envelope 或 error envelope | 校验 action 是否在 `availableActions` 中、tick 是否 stale、冷却是否结束，然后在 Unity 主线程调用 `AppState.CurrentState.*Command()`。 |

`AutoBazaarAction` 字段：`schemaVersion?`、`actionKind`、`cardInstanceId?`、`targetSection?`、`targetSockets?`、`hero?`、`playMode?`、`reason?`、`forTickId?`。  
`actionKind`：`Wait`、`StartOrContinueRun`、`AbandonRun`、`SelectItem`、`SelectSkill`、`SelectEncounter`、`CommitToPedestal`、`MoveItem`、`SellItem`、`Reroll`、`ExitState`。

这个接口适合离线预定义对局的外部控制层，但它不是 run 引擎：真实状态变化仍来自官方 `/sessions`/`/commands` 或未来本地替代。

## 潜在 telemetry stub

`AnalyticsManager` 初始化一个 `HttpClient`：

| 条件 | BaseAddress | endpoint 字段 | 当前状态 |
|---|---|---|---|
| `isLocal = true` | `https://localhost:7291/` | `api/telemetry` | 只看到模型字段累积和 base address 初始化，未看到 POST/GET 调用。 |
| `isLocal = false` | `Config.NetURL` | `api/telemetry` | 同上。 |

离线模式建议直接不挂载该 manager，或保证没有 telemetry flush 调用。若未来补上发送逻辑，应把它归入可禁用/本地-only 日志通道。

## 旧/测试主菜单数据接口

`MainMenuUIDataHandler` 是旧市场/收藏 UI 测试数据 helper。它有 `UnityWebRequest.Get(itemPathURL)` 路径，但当前反编译显示 `GetItemPathURL()` 返回的是相对文件名：

| 方法 | URL/路径 | 数据结构 | 业务 |
|---|---|---|---|
| GET | `SaleItemsLimited.json` | `BazaarSaleItems { SaleItems: BazaarSaleItem[] }` | 下载限时售卖测试数据。 |
| GET | `SaleItemsBuy.json` | `BazaarSaleItems` | 下载可购买测试数据。 |
| GET | `SaleItemsSell.json` | `BazaarSaleItems` | 下载可出售测试数据。 |
| GET | `CollectionItems{CollectionType}.json` | `BazaarSaleItems` | 下载某类收藏测试数据。 |

同文件还保留了 `https://example.com/SaleItemsLimited.json`、`https://example.com/SaleItemsBuy.json`、`https://example.com/SaleItemsSellURL.json`、`https://example.com/CollectionItems...` 常量，以及 `useWebImages=true` 时写入 `https://picsum.photos/512` / `https://picsum.photos/1024` 图片 URL。它们更像测试/占位逻辑，不属于当前官方主流程；离线模式应避免调用这些 helper，或把 sale item JSON 和图片路径固定到本地资源。

## 外链

| 位置 | URL | 业务 |
|---|---|---|
| `OptionsDialogSettingsAsset` | `https://playthebazaar.com/privacy-policy` | 隐私政策。 |
| `OptionsDialogSettingsAsset` | `https://playthebazaar.com/terms-and-conditions` | 条款。 |
| `TOSPurchaseTerms` | `https://www.playthebazaar.com/eula` | EULA。 |
| `TOSPurchaseTerms` | `https://www.playthebazaar.com/terms-and-conditions` | 购买条款。 |
| `AnnouncementController` | maintenance `announcement.link` | 公告链接，支持相对 `MaintenanceDataURL` 解析。 |

## 离线实现时的接口优先级

必须实现：

- `maintenance.json`
- `/api/time`
- `/api/auth/refreshtokens` 或完全替代 token provider
- `/api/auth/login/silent` 或直接注入 cached token
- `/api/Bootstrap/app`
- `/api/PlayerProfiles/me/career`
- `/sessions`
- `/commands`
- `DELETE /sessions`

应该 stub：

- `Bootstrap/post-run`
- challenges、season track、wallet、collection、loadouts、heroes、run rewards
- `app/runs/complete`

可以禁用或 fake：

- purchase、Steam、Stripe、marketplace、leaderboard、feedback、mod uploads、公告图片远端、推荐/赞助远端。
