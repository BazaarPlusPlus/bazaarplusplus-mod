---
status: abandoned
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# 接口数据结构目录

## 读取口径

本文只记录反编译和当前源码中能看到的 wire DTO / MessagePack DTO / mod DTO。字段名按 C# 类型名或 JSON 属性名记录；大小写不统一是原代码现状，离线 facade 要按客户端反序列化器期望返回。

普通官方 REST 由 `DataProvider` / `HttpDataProvider` 发送 JSON。run session 由 `HttpGameClient` 发送 MessagePack，必须使用 `MessagePackConfig.Options` 和对应 union key。mod API 使用 Newtonsoft.Json / HttpClient。

## 通用传输结构

| 类型 | 字段 | 用途 |
|---|---|---|
| `EmptyResponse` | none | REST 写接口成功但无业务 body。 |
| `Result<T>` / provider result | `StatusCode`、`Data`、`ErrorCode` 等本地封装 | 客户端内部封装，不是服务端 JSON body。 |
| REST error body | `code`，可能有 message/details | `HttpDataProvider` 在 protocol error 时解析 `code`，再映射为客户端错误。 |
| MessagePack body | `INetCommand` / `INetMessage` union | run 内命令和响应，详见 [session-command-protocol.md](session-command-protocol.md)。 |

## Auth / Account

| DTO | 字段 |
|---|---|
| `SilentLoginAuth` | `SteamTicket` |
| `GooglePlayExternalAuth` | `provider`、`proofType`、`proof.serverAuthCode`、`deviceId` |
| `LoginAuth` | `Email`、`Password` |
| `RegistrationAuth` | `Username`、`Password`、`Email`、`FirstName`、`LastName`、`DateOfBirth`、`MarketingOptIn` |
| `RefreshTokenRequest` | `RefreshToken` |
| `CheckUsernameAuth` | `Username` |
| `LinkAuth` | `Email`、`Password`、`SteamTicket` |
| `ChangeEmailAuth` | `Password`、`EmailAddress` |
| `LoginResponse` | `AccessToken`、`RefreshToken`、`AlternateRefreshToken?`、`AccessTokenExpiresAt`、`CurrentTime` |
| `RefreshTokenResponse` | `AccessToken`、`RefreshToken`、`AlternateRefreshToken`、`AccessTokenExpiresAt` |
| `FeatureAccessResponse` | `accountId`、`chestPurchases`、`prizePasses`、`subscriptions` |
| `AccountSummaryResponse` | `HasViewedDailySpecial`、`DemoSummary` |
| `DemoSummary` | `RemainingDemoRuns`、`CanStartDemoRun` |

业务点：

- `AccessTokenProvider` 在 access token 过期前 3 分钟调用 `/api/auth/refreshtokens`。
- 刷新失败后 30 秒内阻止重复刷新，避免请求风暴。
- 本地模式可以返回 fake token；客户端只把 token 当字符串放进 `Authorization`，但过期时间必须在未来。

## Bootstrap 聚合

| DTO | 字段 |
|---|---|
| `BootstrapResponse` | `serverTime`、`account`、`profile`、`wallet`、`playerRank`、`season`、`challenges`、`heroes`、`collection`、`chests`、`marketplace`、`leaderboard`、`pendingRewards`、`runRewardLevels`、`errors`、`hasErrors` |
| `PostRunBootstrapResponse` | `wallet`、`challenges`、`profile`、`featureAccess`、`accountSummary`、`seasonTrack`、`seasonTrackProgression`、`chests`、`errors`、`hasErrors` |
| `StateDeltaResponse` | `Wallet?`、`SeasonTrackProgression?`、`PlayerRank?`、`Challenges?`、`OwnedHeroesAdded`、`CollectionItemsAdded`、`ChestsAdded`、`OwnedTitlePrefixesAdded`、`OwnedTitleSuffixesAdded`、`CollectionItemsRemoved`、`ChestsRemoved`、`PendingRewardsRemoved`、`DailySpecialPurchased` |
| `AppOpenChestResponse` | `State` |
| `AppOpenChestsResponse` | `State`、opened chest payload in wrapper |
| `AppStorePurchaseChestsResponse` | `State`、purchased chest payload in wrapper |
| `AppClaimAllRewardsResponse` | `State` |
| `AppPurchaseDailySpecialResponse` | `State` |

业务点：

- `BootstrapResponse` 是启动主聚合。离线 facade 必须返回结构完整的对象，即使列表为空。
- `PostRunBootstrapResponse` 在 run 结束后刷新账号和奖励 UI。
- `StateDeltaResponse` 是很多 `/api/app/*` 写接口的增量更新格式，客户端通过 `StateDeltaApplier` 更新本地 cache。

## Profile / Wallet / Rank

| DTO | 字段 |
|---|---|
| `PlayerProfileResponse` | `AccountId`、`Username`、`TitlePrefix`、`TitleSuffix`、`SeasonRank`、`ActiveRun` |
| `ActiveRun` | `AccountId`、`Type`、`HeroID`、`PlayerVictories`，旧 response 里还出现 `RunStart`、`SerializedObject` |
| `PlayerProfileCareerResponse` / `CareerResponse` | `AccountId`、`HasCompletedTutorial`、`SeasonRanks`、`Heroes` |
| `CareerResponse.SeasonRank` | `Rank`、`Rating`、`Division`、`LeaderboardPosition` |
| `CareerResponse.HeroStats` | `HeroId`、`GamesPlayed`、`RankedGamesPlayed`、`BronzeTrophies`、`SilverTrophies`、`GoldTrophies`、`DiamondTrophies`、`ChestsEarned`、`PlayerVictories`、`MonsterVictories`、`MonsterDefeats`、`DaysPlayed`、`EventsCompleted`、`MerchantsVisited`、`CoinsSpent`、`LivesLost`、`SecondsSpentPlaying`、`ItemsSold`、`DamageDealt`、`LegendaryItemsAcquired`、`GhostBattles`、`GhostVictories`、`GhostRunsEnded`、`Trophies` |
| `RankResponse` | `Rank`、`Division`、`Rating` |
| `PlayerRankResponse` | `AccountId`、`SeasonID`、`Rank`、`Division`、`RankPoints`、`Rating` |
| `LeaderboardPositionResponse` | `accountId`、`seasonId`、`position` |
| `WalletResponse` | `AccountId`、`Gems`、`RankedVouchers`、`DailyRankedVouchers` |
| `RunRewardLevelResponse` | `WinThreshold`、`UnrankedChestCount`、`RankedChestCount`、`SubscriptionMultiplier`、`UnrankedCurrencyRewards`、`RankedCurrencyRewards` |

业务点：

- `GameInstance.FetchData()` 启动时并发拉取 bootstrap 和 career；其中任一失败都会进入重试/退出流程。
- `ActiveRun` 决定主菜单是否显示 continue/run resume。
- 本地离线模式初始 career 可以全 0，但 `AccountId`、`Username`、`HasCompletedTutorial`、owned hero 信息要稳定。

## Collection / Loadout / Hero / Title

| DTO | 字段 |
|---|---|
| `ClientHeroListingsResponse` | `accountId`、`purchaseCount`、`discountRates`、`heroes` |
| `ClientHeroListing` | `heroId`、`releaseDate`、`price`、`isPurchasable` |
| `OwnedHero` | `heroId`、`releaseDate` |
| `PurchaseHeroListings` | `Listings` |
| `PurchaseHeroListing` | `Id`、`Name`、`ListingType`、`Price`、`Enabled` |
| `PurchaseHeroRequest` | `HeroId` |
| `BazaarHeroLoadout` | `accountId`、`heroId`、`boardId?`、`carpetId?`、`cardBackId?`、`toyId?`、`stashId?`、`bankId?`、`albumId?`、`heroSkinId?`、`cardSkinIds?`、`randomizeLoadout` |
| `BootstrapHeroLoadoutResponse` | `AccountId`、`HeroId`、`AlbumId`、`BankId`、`BoardId`、`CarpetId`、`CardBackId`、`StashId`、`ToyId`、`HeroSkinId`、`CardSkinIds` |
| `BootstrapHeroLoadoutsResponse` / `HeroLoadoutsResponse` | `Loadouts` |
| `EquipLoadoutRequest` | `boardId?`、`carpetId?`、`cardBackId?`、`toyId?`、`stashId?`、`bankId?`、`albumId?`、`heroSkinId?` |
| `CollectionItemResponse` | `itemId`、`mintNumber` |
| `CollectionItemInstanceResponse` | `Id`、`ItemId`、`MintNumber` |
| `TitleResponse` | `ID`、`Name`、`Description` |
| `PrefixTitleResponse` / suffix equivalent | owned title list / equipped title data |

业务点：

- `GET /api/CollectionItems` 在当前客户端里主要作为 owned item id 列表使用。
- `StartRunAppState` 在 loadout randomize 开启时会先调用 `/api/Loadouts/hero/{heroId}/equip-loadout`，再创建 `/sessions`。
- 离线 fixture 若要保证外观一致，应固定 loadout，而不是让本地 `System.Random` 再随机一次。

## Season / Challenge / Reward

| DTO | 字段 |
|---|---|
| `SeasonResponse` | `id`、`name`、`title`、`start`、`allowBundleChestAfterSeasonId` |
| `SeasonTrackResponse` | `seasonId`、`tiers`、`infiniteTier`、`subscriptionXpMultiplier`、`expansionPrice`、`tierSkipPrice` |
| `SeasonTierResponse` | `rarity`、`requiredXp`、`freeRewards`、`paidRewards` |
| `RewardResponse` | `collectionItemIds`、`currencies`、`titlePrefixIds`、`titleSuffixIds`、`chests` |
| `SeasonTrackProgressionResponse` | `accountId`、`seasonId`、`isPaid`、`experience`、`claimedFreeRewardTiers`、`claimedPaidRewardTiers`、`claimedFreeInfiniteTierCount`、`claimedPaidInfiniteTierCount` |
| `ClaimTierRequest` | `seasonId`、`type`、`tierNumber` |
| `SkipTierRequest` | `targetTierNumber` |
| `PlayerChallengesResponse` | `dailyChallenges`、`weeklyChallenges`、`dailyResets`、`weeklyResets`、`dailyChallengesExpireAt`、`weeklyChallengesExpireAt` |
| `ChallengeProgress` | `Id`、`Progress`、`Acknowledged` |
| `RerollDailyChallengeRequest` | `dailyChallengeId` |
| `RerollWeeklyChallengeRequest` | `weeklyChallengeId` |
| `RefreshChallengeRequest` | `dailyChallengeIds`、`weeklyChallengeIds` |
| `ReplaceChallengeRequest` | `replacedChallengeId`、`newChallengeId` |
| `PendingRewardResponse` | `pendingRewardId`、`source`、`collectionItems`、`chest`、`currencyRewards` |
| `PendingRewardRequest` | `pendingRewardId` |
| `SeasonRankRewardResponse` | `Id`、`SeasonId`、`Rank`、`Position`、`ChestCount`、`CollectionItemIds`、`CurrencyRewards`、`TitlePrefixIds`、`TitleSuffixIds` |

业务点：

- Season track 和 challenge 不是开始 run 的最小必要条件，但 bootstrap UI 会读取；本地模式应返回空或固定可解析值。
- `/api/app/seasontrackprogressions/*`、`/api/app/challenges/progress`、`/api/app/achievements/claim/*` 都倾向返回 `StateDeltaResponse`，而不是要求客户端重新 bootstrap。

## Chest / Store / Purchase

| DTO | 字段 |
|---|---|
| `ChestResponse` | `id`、`ownerId`、`seasonId`、`currencies`、`item`、`openedAt`、`parentId`、`children`、`gemsAwarded`、`isDuplicate` |
| `Chest` legacy response | `Id`、`OwnerId`、`SeasonId`、`Currencies`、`Item`、`ParentId`、`Children` |
| `ChestRequest` | `AccountId`、`SeasonId`、`Count` |
| `ChestOpenRequest` | `SeasonId`、`Count`、`IncludedChestIDs` |
| `ChestPurchaseRequest` | `quantity` |
| `ChestBundleResponse` | `quantity`、`gemCost`、`discount` |
| `ChestDropRatesResponse` | `seasonId`、`itemRarities`、`currencies`、`bonuses`、`lootTables` |
| `ItemDropRate` / `CurrencyDropRate` / `BonusDropRate` | reward/currency/rarity + `rate` |
| `CurrencyValue` / `CurrencyResponse` | currency identifier + amount/cost fields |
| `CurrencyListingsResponse` | `currencyListings`、`miscListings` |
| `CurrencyListingResponse` | `id`、`name`、`listingType`、`baseQuantity`、`quantity`、`bonusQuantity`、`price`、`enabled`、`firstPurchaseBonusMultiplier`、`hasFirstTimePurchaseBonus`、`isInSync` |
| `CurrencyListingPurchaseStatus` | `ID`、`ListingID`、`GemsGranted`、`BonusGems`、`Price`、`Currency`、`TransactionID`、`OrderID`、`CreatedAt`、`Status` |
| `PurchaseTokenRequest` | `ListingType`、`AcceptTermsOfService`、`Quantity`、`ListingID` |
| `StripePurchaseResponse` | `TransactionID`、`ClientToken`、`CheckoutURL` |
| `StartPurchaseRequest` | `AcceptTermsOfService`、`ListingId`、`Quantity`、`UserSession` |
| `StartPurchaseResponse` | `TransactionId`、`OrderId`、`SteamUrl` |
| `TransactionResponse` | `Id`、`ListingId`、`Status`、`Transaction` |
| `SteamTransaction` | `Status` |
| `GetPurchaseTransactionResponse` | `ID`、`AccountID`、`ListingID`、`PurchaseType`、`Price`、`Currency`、`TransactionId`、`OrderId`、`CreatedAt`、`Status` |
| `PurchaseTransactionResponse` legacy | `ID`、`ListingID`、`AccountID`、`Price`、`Currency`、`TransactionId`、`OrderID`、`CreatedAt`、`PurchaseStaus`、`Instances` |
| `ClientDailySpecialListingsResponse` | `Listings`、`ExpiresAt`、`ExpiresAtUTC` |

业务点：

- 离线模式不应调用真实 Steam/Stripe。推荐隐藏购买入口或返回本地模式不可购买。
- chest/open/purchase 如果要用于本地奖励，应只写本地 profile store，不要复用真实交易流程。

## Run Session MessagePack DTO

完整 key 编号和状态机见 [session-command-protocol.md](session-command-protocol.md)。这里按接口列请求/响应结构。

| 接口 | 请求结构 | 响应结构 | 关键字段 |
|---|---|---|---|
| `POST /sessions` | `InitializeRunCommand` | `NetMessageRunInitialized`、`NetMessageGameStateSync`、`NetMessageGameSim`，通常可包进 `NetMessageAggregate` | 请求：`GameModeId?`、`PlayMode`、`SelectedHero`；响应 header 必须有 `sid`，建议有 `rid`。 |
| `POST /commands` | `INetCommand` union | `INetMessage` union | 请求 header 必须有 `sid`、`rid`；响应 header 更新 `rid`。 |
| `DELETE /sessions` | header `sid` | no required body | best-effort session cleanup。 |

命令 DTO：

| DTO | 字段 |
|---|---|
| `SelectItemCommand` | `InstanceId`、`TargetSockets`、`Section?` |
| `MoveItemCommand` | `InstanceId`、`TargetSockets`、`Section` |
| `SelectSkillCommand` | `InstanceId` |
| `SelectEncounterCommand` | `InstanceId` |
| `RerollCommand` | none |
| `ExitCurrentStateCommand` | none |
| `SellCardCommand` | `InstanceId` |
| `CommitToPedestalCommand` | `InstanceId` |
| `InitializeRunCommand` | `GameModeId?`、`PlayMode`、`SelectedHero` |
| `CheatCommand` | `Args` |
| `AbandonRunCommand` | none |

响应 DTO：

| DTO | 字段 |
|---|---|
| `NetMessageError` | `MessageId`、`ErrorType`、`Message?` |
| `NetMessageCombatSim` | `Data`、`MessageId` |
| `NetMessageGameSim` | `Data`、`MessageId` |
| `NetMessageGameStateSync` | `Data`、`MessageId` |
| `NetMessageRunInitialized` | `RunId`、`BuildId`、`Environment`、`PlayMode`、`MessageId` |
| `NetMessageAggregate` | `Messages`、`MessageId` |

Snapshot / sim DTO：

| DTO | 字段 |
|---|---|
| `GameStateSnapshotDTO` | `Run`、`CurrentState`、`Player`、`Cards` |
| `RunSnapshotDTO` | `GameModeId`、`Day`、`Hour`、`Victories`、`Defeats`、`HasVisitedFates`、`DataVersion` |
| `RunStateSnapshotDTO` | `StateName`、`CurrentEncounterId`、`Board`、`RerollCost`、`RerollsRemaining`、`SelectionSet`、`SelectionContextRules` |
| `PlayerSnapshotDTO` | `Hero`、`Attributes`、`UnlockedSlots` |
| `CardSnapshotDTO` | `InstanceId`、`TemplateId`、`Attributes`、`Enchantment`、`Heroes`、`HiddenTags`、`Tags`、`Tier`、`Type`、`Size`、`Owner`、`Socket`、`Section` |
| `TSelectionContextRules` | `CanSelectMultiple`、`SelectionIsFree`、`CanExit`、`RerollRules`、`WillAutoSellOnExit`、`NextEncounterOnExit` |
| `RerollRules` | `TotalAllowedRerolls`、`CostIncrease`、`StartingCost`、`CostMax` |
| `GameSim` | `Events`、`Player`、`Opponent`、`Cards`、`Run`、`CurrentState`、`VfxKeys` |
| `CombatSim` | `Frames`、`Winner`、`Loser`、`OpponentHealthThresholdsForGold`、`OpponentHealthThresholdsForXp`、`CardStats`、`VfxKeys`、`PortraitKeys` |
| `CombatSimFrame` | `Events`、`PlayerUpdates`、`OpponentUpdates`、`CardUpdates` |
| `SimPvpOpponent` | `Name`、`TitlePrefix`、`TitleSuffix`、`Rank`、`Rating`、`Division`、`Victories`、`Prestige`、`Level`、`Hero`、`PlayerLoadout`、`PlayerCollection` |

业务点：

- 客户端 history/replay 依赖 `GameSim(Combat/PVPCombat) -> CombatSim -> GameSim` 三段顺序。
- 本地 session server 要把所有点击结果表示为这些 DTO，不需要客户端理解新协议。

## 静态/CDN 数据结构

| 资源 | 结构 |
|---|---|
| `maintenance.json` / `StatusDto` | `systems`、`versions`、`maintenance`、`announcement`、`httpGameClientTimeouts`、`breakingChange`、`locales` |
| `StatusDto.systems` | system name -> availability / message fields |
| `StatusDto.maintenance` | `startDateTime`、`endDateTime`、`message` |
| `StatusDto.announcement` | `id`、`image`、`title`、`description`、`link`、start/end time |
| `httpGameClientTimeouts` | `defaultRequestSeconds`、`inRunCommandSeconds`、`deleteSessionSeconds` |
| `GameData.db.zip` | ZIP containing SQLite `GameData.db` |
| `GameData.db` tables | `cards`、`challenges`、`collectibles`、`game_modes`、`level_ups`、`monsters`、`seasons`、`tooltips` |
| `GameData.db` row | `Id TEXT`、`Data BLOB`，`Data` 是 UTF-8 JSON blob |
| `translations/{locale}.bytes` | locale bytes，具体文本格式由 localization service 消费 |

业务点：

- `ServersHealthService` 允许 `304` 和 `404` 作为部分成功路径，但维护不可用/强更会阻断启动。
- `JsonGameDataManager` 能从 bundled zip 解压静态数据，所以离线玩法不需要联网拿基础 DB。

## 旧/测试主菜单 UI 数据 DTO

这些结构来自 `MainMenuUIDataHandler`，用于旧市场/收藏 UI 的测试 JSON 或本地 StreamingAssets JSON。当前主启动/run 流程不依赖它，但它包含 `UnityWebRequest.Get` 路径，所以离线排查时要记录。

| DTO | 字段 |
|---|---|
| `BazaarSaleItems` | `SaleItems: BazaarSaleItem[]` |
| `BazaarHeroSkins` | `SaleItems: BazaarHeroSkinItem[]` |
| `BazaarSaleItem` | `CollectionType`、`hasAsset`、`AssetData`、`CollectionItemID`、`IsDefault`、`Size`、`Id`、`SeasonNumber`、`Name`、`Description`、`Cost`、`SellPrice`、`listingId`、`chestListedIds`、`MedianPrice`、`Count`、`Rarity`、`ExpireDateTime`、`BannerText`、`ImagePath`、`LargeImagePath`、`ItemActivity` |
| `ItemRarity` | `Name`、`AssetRarity`、`RarityColor`、`RaritySprite` |
| `ItemTransactionActivity` | `Event`、`User`、`Price`、`Date` |

业务点：

- `DownloadItems` / `DownloadAndSaveItemsToPersistentDataPath` 读取 `SaleItemsLimited.json`、`SaleItemsBuy.json`、`SaleItemsSell.json`、`CollectionItems{type}.json`。
- 文件中 `example.com` 常量和 `picsum.photos` 图片路径是测试/占位联网面；离线模式应禁用或替换成本地资源。

## BazaarPlusPlus Mod API

| 接口 | DTO | 字段 |
|---|---|---|
| `POST /run-bundles` | multipart `metadata` (`RunBundleUploadRequest`) + `artifact` (`RunArtifact`) | metadata: `SchemaVersion`、`PlayerAccountId`、`SubmittedAtUtc`、`ArtifactCodec`、`RunProjection`、`BattleProjections`；artifact: gzip-compressed MessagePack bytes |
| `RunBundleUploadRequest.RunProjection` | `RunProjection` | `RunId`、`Status`、`HeroId`、`HeroName`、`PlayerRank`、`PlayerRating`、`PlayerPosition`、`StartedAtUtc`、`EndedAtUtc`、`FinalDay`、`FinalWins`、`FinalLosses`、`FinalPlayerRank`、`FinalPlayerRating`、`FinalPlayerPosition`、`Battles` |
| `RunBundleUploadRequest.BattleProjection` | `BattleProjection` | `BattleId`、`RecordedAtUtc`、`RunId`、`Day`、`PlayerName`、`PlayerAccountId`、`PlayerHero`、`PlayerRank`、`PlayerRating`、`PlayerLevel`、`PlayerPrestige`(`int?`)、`PlayerVictories`(`int?`)、`OpponentName`、`OpponentAccountId`、`OpponentHero`、`OpponentRank`、`OpponentRating`、`OpponentLevel`、`OpponentPrestige`(`int?`)、`OpponentVictories`(`int?`)、`Result`、`WinnerCombatantId`(`string?`)、`LoserCombatantId`(`string?`)、`IsFinalBattle`(`bool`,wire 名 `is_final_battle`) |
| `RunArtifact` | run artifact | `RunId`、`Battles` |
| `RunArtifactBattle` | battle artifact wrapper | `BattleId`、`Manifest`、`Participants`、`Snapshots`、`ReplayPayload` |
| `BattleManifestArtifact` | manifest | `BattleId`、`RecordedAtUtc`、`Day`、`Hour`、`EncounterId`、`CombatKind`、`Result`、`WinnerCombatantId`、`LoserCombatantId` |
| `BattleParticipantsArtifact` | participants | `PlayerName`、`PlayerAccountId`、`PlayerHero`、`PlayerRank`、`PlayerRating`(`int?`)、`PlayerLevel`(`int?`)、`PlayerPrestige`(`int?`)、`PlayerVictories`(`int?`)、`OpponentName`、`OpponentAccountId`、`OpponentHero`、`OpponentRank`、`OpponentRating`(`int?`)、`OpponentLevel`(`int?`)、`OpponentPrestige`(`int?`)、`OpponentVictories`(`int?`) |
| `BattleSnapshotsArtifact` | snapshots | `CardSets` |
| `CardSetCaptureArtifact` | card set | `Label`、`Status`、`Source`、`Items` |
| `CardSetItemArtifact` | card item | `InstanceId`、`TemplateId`、`Type`、`Size`、`Section`、`Socket`、`Name`、`Tier`、`Enchant`、`Tags`、`Attributes` |
| `ReplayPayloadArtifact` | replay bytes | `BattleId`、`Version`、`SpawnMessageBytes`、`CombatMessageBytes`、`DespawnMessageBytes` |
| `GET /ghost-battles` | `GhostBattleImportRecord` | `BattleId`、`RecordedAtUtc`、`Day`、player/opponent fields、`Result`、`WinnerCombatantId`、`LoserCombatantId`、`IsFinalBattle`(`bool`)、`ReplayAvailable`、`ReplayDownloaded`、`LastSyncedAtUtc`；V4 wire 已包含 `is_final_battle`；客户端解析为 `IsFinalBattle`(默认 false，GhostBattleClient.cs:209) |
| `POST /ghost-battles/{battleId}/replay-link` | response JSON | `download_url` |
| `GET download_url` | bytes | replay payload bytes |
| `POST /bazaardb/snapshots/<snapshot_id>` | `BazaarDbSnapshotUploadRequest` | `SchemaVersion`、`Snapshot`、`Player`、`Run`、`Image`、`Client` |

业务点：

- 上传类接口不是游戏启动/玩法必要条件；离线模式应默认禁用，或只写本地队列。
- `ghost-battles` 的 replay payload 可以作为本地 PVP/opponent cache 的来源，但应明确是用户曾同步过的数据。
- 当前 mod client 不附加鉴权 header；`RunBundleUploadRequest.PlayerAccountId`、`GhostBattleClient.QueryAgainstMeAsync(playerAccountId)`、`BazaarDbSnapshotUploadRequest.Player.AccountId` 是主要身份输入。
- BazaarDB 拉取不在游戏进程内执行；服务端通过 `POST /bazaardb/peek` 返回私有 R2 的短期预签 URL，外部 BazaarDB puller 落地后调用 `POST /bazaardb/confirm`。

## BazaarAgent 本地 HTTP DTO

| DTO | 字段 |
|---|---|
| `BazaarAgentCardSnapshot` | `InstanceId`、`Kind`、`Type`、`TemplateId`、`DisplayName`、`Tier`、`Size`、`Enchantment`、`SocketId`、`Location`、`Order`、`Tags`、`HiddenTags`、`Attributes`、`ActiveAbilities`、`BuyPrice`、`SellPrice`、`CanAfford`、`CanFit`、`CanSelect`、`IsFree`、`TargetSection`、`TargetSockets`、`UnavailableReason`、`CanSell` |
| `BazaarAgentCardAbilitySnapshot` | `Id`、`InternalName`、`InternalDescription`、`Trigger`、`Action`、`ActiveIn`、`WorksIn`、`Priority` |
| `BazaarAgentDecisionOption` | `ActionKind`、`Group`、`DisplayKey`、`CardInstanceId`、`TargetSection`、`TargetSockets`、`Card` |
| `BazaarAgentContext` | `SchemaVersion`、`TickId`、`ServerTimeUtc`、`IsInRun`、`HasActiveRun`、`CanStartOrContinueRun`、`IsClientBusy`、`RunId`、`StateName`、`PlayerHero`、`Day`、`Hour`、`Wins`、`Losses`、`PlayerGold`、`PlayerIncome`、`PlayerHealth`、`PlayerMaxHealth`、`PlayerPrestige`、`PlayerLevel`、`SelectionIsFree`、`CanExit`、`CanReroll`、`RerollCost`、`RerollsRemaining`、`CurrentEncounterId`、`CurrentEncounterType`、`ActionCooldownRemainingSeconds`、`InteractableTemplateIds`、`BoardItems`、`ChestItems`、`PlayerSkills`、`SellableItems`、`SelectionOptions`、`AvailableActions` |
| `BazaarAgentAction` | `SchemaVersion?`、`ActionKind`、`CardInstanceId?`、`TargetSection?`、`TargetSockets?`、`Hero?`、`PlayMode?`、`Reason?`、`ForTickId?` |

业务点：

- `GET /v1/context` 可使用 ETag/304；无 snapshot 时返回 503。
- `POST /v1/actions` 只接受当前 `AvailableActions` 中存在的动作，并在 Unity 主线程调用原 `AppState`/`Cmd` 路径。
- BazaarAgent 是控制层，不是权威 run 引擎；离线玩法仍要由本地 session server 产出 `INetMessage`。

## 本地离线新增 DTO 建议

这些不是现有官方 wire DTO，而是实现本地预定义对局时建议新增的本地-only 结构。不要放进 `/api/*` 官方命名空间。

| DTO | 字段 | 用途 |
|---|---|---|
| `FixtureSummary` | `fixtureId`、`displayName`、`hero`、`gameDataVersion`、`tags`、`difficulty`、`fixtureHash` | `/local/fixtures` 列表。 |
| `FixtureManifest` | `schemaVersion`、`fixtureId`、`displayName`、`description`、`gameDataVersion`、`clientVersion`、`hero`、`playMode`、`runSeed`、`rngAlgorithm`、`initialState`、`schedule`、`rules`、`expected` | 完整预定义对局。 |
| `FixtureInitialState` | `day`、`hour`、`wins`、`losses`、`gold`、`income`、`health`、`maxHealth`、`level`、`prestige`、`unlockedSlots`、`board`、`stash`、`skills`、`selection` | 初始化 `GameStateSnapshotDTO`。 |
| `FixtureCard` | `instanceId`、`templateId`、`tier`、`enchantment`、`owner`、`section`、`socket`、`attributes`、`tagsOverride` | 稳定卡牌实例。 |
| `FixtureSchedule` | `shops`、`encounters`、`levelUps`、`loot`、`pedestals`、`pvpOpponents`、`combats` | 固定或半固定流程。 |
| `FixtureRules` | `allowReroll`、`allowAbandon`、`strictCommandReplay` | 控制可交互程度。 |
| `CommandReplayEntry` | `index`、`rid`、`commandType`、`commandHash`、`rngBefore`、`rngAfter`、`responseHash`、`stateHash` | 确定性校验和 debug。 |

业务点：

- fixture id、card instance id、RNG stream name 都必须稳定。
- 本地-only 接口建议使用 `/local/fixtures/*` 和 `/local/runs/*`，避免和官方 `/api/*` 产生混淆。
