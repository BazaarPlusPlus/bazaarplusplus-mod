# 幽灵回放视角约定统一设计

## 背景

幽灵回放（ghost replay）播放的是**别人上传的、他打我的那一场战斗**。数据来自对方 run bundle 中的一条 `RunBattleV5`。

当前实现让同一份 `PvpBattleManifest` 同时承担两种相反的视角约定，导致双方 loadout 在注入时被交叉写入：

- **Replay 字节流保持录制者视角**：`SpawnMessageBytes` / `CombatMessageBytes` / `DespawnMessageBytes` 从 bundle 原样 `ToArray()` 透传，无任何 `ECombatantId` 改写（[GhostBattleSyncService.cs:270-277](../../src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs)）。
- **Manifest 被整体翻转成本地视角**：参与者与四组快照左右互换（[GhostBattleSyncService.cs:299-351](../../src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs)）。

而标签与 sim 侧的对应关系是固定的：`player_hand` 采自 `ECombatantId.Player`（[PvpBattleSnapshotCollector.cs:58-61](../../src/BazaarPlusPlus/Game/PvpBattles/PvpBattleSnapshotCollector.cs) → [RunPayloadComposer.cs:210-213](../../src/BazaarPlusPlus/Game/BundlePipeline/RunPayloadComposer.cs)），游戏侧 `ECombatantId.Player → Data.Run.Player`（`decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs:65`）。

于是回放注入时 `Data.Run.Player` 是上传者，`manifest.Participants.Player*` / `manifest.Snapshots.Player*` 却是我，两者被当作同一个人消费。

### 已确认的故障点

| 位置 | 症状 |
|---|---|
| [SnapshotRehydrator.cs:62-63,79-80](../../src/BazaarPlusPlus/Game/CombatReplay/Bootstrap/SnapshotRehydrator.cs) | `ReplaceSkillCollection` 整体替换技能列表 → 双方技能栏完整对调 |
| [SnapshotRehydrator.cs:30,46](../../src/BazaarPlusPlus/Game/CombatReplay/Bootstrap/SnapshotRehydrator.cs) | 卡牌 `Owner` / `Section` / `LeftSocketId` 被写成另一侧的值 |
| [PlayerAttributeRepairer.cs:64-83](../../src/BazaarPlusPlus/Game/CombatReplay/PlaybackUi/PlayerAttributeRepairer.cs) | `Data.Run.Player` 的 Level/Prestige/Income/Gold 取自另一侧 |
| [ReplaySavedStateNormalizer.cs:53,56](../../src/BazaarPlusPlus/Game/CombatReplay/Bootstrap/ReplaySavedStateNormalizer.cs) | 缺省等级补齐取错侧 |
| [CombatReplayRuntime.cs:1597](../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs) | `ApplySelectedHeroOverride` 用错侧英雄 |
| [OpponentPortraitController.cs:177-221](../../src/BazaarPlusPlus/Game/CombatReplay/PlaybackUi/OpponentPortraitController.cs) | `SimPvpOpponent` 身份与 spawn 消息中的 `CurrentState.PvpOpponent` 冲突 |
| [GhostBattleSyncService.cs:158-166](../../src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs)、[HistoryPanelDataService.cs:183-192](../../src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelDataService.cs) | 把本地视角的 counts 喂给期待 raw 约定的 `MarkGhostReplayDownloaded`，读出时再翻一次 → 列表行 chip 数字左右颠倒 |

## 术语

| 术语 | 含义 | bundle 中 | sim 中 | 回放画面 |
|---|---|---|---|---|
| **挑战者** | 上传 bundle 的人 | `Participants.Player` / `player_hand` | `ECombatantId.Player` | 下方棋盘 |
| **我** | 被挑战的本地玩家 | `Participants.Opponent` / `opponent_hand` | `ECombatantId.Opponent` | 上方棋盘 |

**录制者视角**（recorder perspective）= 与 bundle / sim 原生一致，挑战者在 Player 位。
**本地视角**（local perspective）= 我在 Player 位。

## 目标与非目标

**目标**

1. 幽灵回放画面中双方 loadout、技能、属性、英雄各归其位。
2. 列表行的物品/技能计数与实际棋盘一致。
3. 面板文案不再用"对手"称呼挑战者，改为"XXX 挑战了你"语义。

**非目标**

- 不做 sim 消息的视角翻转（不追求"我在下方"）。幽灵回放的定义就是"看别人打我时的第一视角"，挑战者在下方即为正确呈现。
- 不改 bundle 上传协议、不改 DB schema。

## 核心约定

> **从 ghost bundle 解出的 `PvpBattleManifest` 一律保持录制者视角。视角翻转只允许发生在两个明确的展示适配点：`GhostBattleLocalProjector`（列表行/统计）与面板文案层。**

据此，各层职责固定为：

| 层 | 约定 | 改动 |
|---|---|---|
| bundle / sim 字节流 | 录制者视角 | 无 |
| ghost payload manifest | 录制者视角 | **改：去掉翻转** |
| ghost DB 表 `player_*` / `opponent_*` 列 | 录制者视角（`uploader_account_id = record.PlayerAccountId`，[HistoryPanelRepository.cs:446-448](../../src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelRepository.cs)） | 无 |
| 回放注入链路 | 消费 manifest 原生约定 | 无（被动修复） |
| `HistoryBattleRecord`（列表行） | 本地视角，由 `GhostBattleLocalProjector` 在读取时翻转 | 无 |
| 预览 / 文案 | 本地视角措辞 | **改：取数侧 + 文案** |

### 不变量：胜负语义恒为本地视角

筛选项「我赢了 / 我输了」（[HistoryPanelText.Runs.cs:38-40](../../src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.Runs.cs)）、结果药丸、「挑战者出局」标记全部读 `HistoryBattleRecord.Result` / `WinnerCombatantId`（[HistoryPanelGhostBattleFilter.cs:40-62](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelGhostBattleFilter.cs)、[HistoryPanelFormatter.cs:71-107](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelFormatter.cs)），对 ghost 而言这两个值由 `GhostBattleLocalProjector` 投影而来，**必须保持本地视角**。

因此 `GhostBattleLocalProjector.ProjectResultToLocal` / `ProjectCombatantIdToLocal` 及其调用点不得删除。本方案移除的只是 `BuildLocalPerspectiveManifest` **内部**对这两个函数的调用 —— 那条路服务的是回放注入，与列表行同名而不同路。

面板上三处措辞立场一致，均以「我」为基准，挑战者是被命名的对象而非视角基准：结果药丸「胜利/失败」、行文案「XXX 挑战了你」、筛选「我赢了/我输了」。

## 改动清单

### A. 数据约定统一

**A1** — 新增 `Ghost/GhostManifestProjection.cs`（`internal static`），承载两个纯函数：

- `BuildRecorderPerspectiveManifest(GhostBundleReference, string runId, RunBattleV5)` —— 由 `GhostBattleSyncService.BuildLocalPerspectiveManifest` 迁入并去掉互换：
  - `Participants.Player* ← battle.Participants.Player.*`，`Participants.Opponent* ← battle.Participants.Opponent.*`
  - 新增读取 `Participants.PlayerIncome/PlayerGold ← battle.Participants.Player.Income/Gold`（`BattleParticipantV5` Key(8)/(9) 已有采集，旧代码从未读取）
  - `Outcome` 直接使用 `battle.Facts.Result/WinnerCombatantId/LoserCombatantId`，移除 `ProjectResultToLocal` / `ProjectCombatantIdToLocal` 调用，使其与 `CombatSim.Winner` 同侧
  - `Snapshots.PlayerHand ← "player_hand"`，`PlayerSkills ← "player_skills"`，`OpponentHand ← "opponent_hand"`，`OpponentSkills ← "opponent_skills"`
- `SwapPerspective(PvpBattleManifest)` —— 就地互换参与者、四组快照与 Outcome，供 legacy payload 迁移使用。

`GhostBattleLocalProjector.ProjectResultToLocal` / `ProjectCombatantIdToLocal` 保留 —— 列表行仍然需要它们。

**A2** — `GhostBattleSyncService.ExtractPayload` 改调 `GhostManifestProjection.BuildRecorderPerspectiveManifest`，并在写入 payload 时置 `PerspectiveVersion = 1`。

**A3** — `GhostBattleSyncService.cs:158-166` 与 `HistoryPanelDataService.cs:183-192` 的 `CountSnapshots` 调用**参数顺序不变**，自动变正确；补一行注释说明列为录制者视角。

### B. 回放注入层

零改动。`SnapshotRehydrator`、`PlayerAttributeRepairer`、`ReplaySavedStateNormalizer`、`ApplySelectedHeroOverride`、`EnsureOpponentIdentity` 在 manifest 约定修正后全部自动对齐。

### C. 展示取数

`HistoryPanel.cs:329` 的 `HistoryBattlePreviewProjection.BuildOpponent(snapshots, signature)` 改为 `BuildPlayer` —— 渲染结果不变，仍是挑战者的棋盘。

`HistoryPanelUiToolkitView.Rows.cs:363-376` 的两个 chip 取自 `HistoryBattleRecord`（本地视角），语义已对：`PlayerSummaryChip` = 我，`OpponentSummaryChip` = 挑战者。只换文案。

### D. 面板文案

`Text/HistoryPanelText.Battles.cs` 新增：

| 键 | en | zh-Hans | zh-Hant |
|---|---|---|---|
| `GhostChallengerSideShort` | `CHA` | 挑战者 | 挑戰者 |
| `GhostDefenderSideShort` | `YOU` | 你 | 你 |
| `GhostChallengedYou(name)` | `{name} challenged you` | {name} 挑战了你 | {name} 挑戰了你 |
| `GhostSnapshotSummary(...)` | `YOU {a} items · {b} skills \| CHA {c} items · {d} skills` | 你 {a} 件物品 · {b} 个技能 \| 挑战者 {c} 件物品 · {d} 个技能 | （繁体同构） |

`GhostOpponentEliminatedShortText` 由「对手出局」改为「挑战者出局」（en 由 `Knocked Out` 改为 `Challenger Out`），`GhostOpponentEliminatedNoticeText` 同步。

消费点：

- `HistoryPanelUiToolkitView.Rows.cs:371-374,378-381` —— 按 `battle.Source == HistoryBattleSource.Ghost` 选择 side chip 文案
- `HistoryPanel.UiToolkit.cs:131-132` —— ghost 时 `DetailOpponentName` 用 `GhostChallengedYou(OpponentName)`
- `HistoryPanelFormatter.FormatSnapshotSummary` —— 增加 `HistoryBattleSource` 参数，ghost 走 `GhostSnapshotSummary`

### E. 已下载 payload 的兼容迁移

磁盘上已有的 `GhostBattlePayload` 文件里存的是旧约定（本地视角）manifest，改完后会被反向解读。

- `GhostBattlePayload` 新增 `public int PerspectiveVersion { get; set; }`。序列化走 `ContractlessStandardResolverAllowPrivate`（[MessagePackGzipCodec.cs](../../src/BazaarPlusPlus.ModApi/MessagePackGzipCodec.cs)），旧文件缺字段时解出默认值 `0` —— 无需版本门或重新下载。DTO 图保持 `public`。
- 新增 `Ghost/GhostBattlePayloadReader.cs`：`Normalize(GhostBattlePayload)` —— `PerspectiveVersion == 0` 时调 `SwapPerspective` 并置 `PerspectiveVersion = 1`；已是 1 则原样返回（幂等）。
- 全部四个读取点统一经此入口：`HistoryPanelReplayService.cs:243`、`HistoryPanel.cs:324`、`HistoryPanelDataService.cs:183`、`BazaarAgentReplayRecorderWiring.cs:68`。
- `GhostBattlePayloadStore` 的类名/方法名/ctor arity/文件后缀被 exe-runner 测试反射钉住（见 `docs/MEMORY.md`），**不改其签名**，只在调用侧包一层。
- 任何把 normalize 后对象回写磁盘的路径必须携带 `PerspectiveVersion = 1`，否则再次加载会二次翻转。

### F. 外部投递路径

`BazaarAgentReplayRecorderWiring` 接受外部 POST 的 `GhostBattlePayload` 用于视频录制。同样经 `Normalize` 入口，`PerspectiveVersion` 缺省按 legacy 处理。ADR-0007 补记该字段与视角约定。

### G. 测试

`tests/GhostBattleSync.Tests/Program.cs`（exe-runner）新增：

1. `RecorderPerspectiveManifestKeepsSides` —— 构造 `RunBattleV5`（`player_hand` = A、`opponent_hand` = B、uploader = U），断言 `Snapshots.PlayerHand` 项为 A、`Participants.PlayerAccountId == U`、`Outcome.WinnerCombatantId` 与 `Facts` 一致。
2. `LegacyPayloadNormalizesOnceAndIsIdempotent` —— `PerspectiveVersion = 0` 的 payload normalize 后等于录制者视角且 version 变 1；再次 normalize 不变。
3. `SnapshotCountsStayRawAcrossWriteRead` —— counts 写入列再经 `ReadGhostBattle` 读出后，与本地视角期望一致（守住"只翻一次"）。

`tests/Architecture.Tests` 新增边界：`Game/CombatReplay/**` 不得引用 `Game/HistoryPanel/Ghost/**`，防止注入层再次耦合视角投影。

### H. 验收

```bash
./run.sh test
```

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

运行时验证（Steam 启动，App ID 1617400）：

1. 下载并播放一场 ghost 回放，确认下方棋盘的物品/技能/英雄/等级/血量全部属于挑战者，上方属于我，技能栏不再互换。
2. 列表行 chip「你 N 件 / 挑战者 N 件」与回放中实际棋盘数量一致。
3. 详情标题显示「XXX 挑战了你」，行内与摘要中不再出现「对手」。
4. `<GameDir>/BepInEx/LogOutput.log` 中不出现新增的 `PlayerSnapshotUnavailable` / `OpponentSkillsUnavailable` / `PlayerAttributesUnavailable` 降级事件。
5. 用改动前已下载的 ghost payload（legacy，version 0）重复第 1 步，确认迁移路径生效。

## 风险与待确认项

| 项 | 说明 | 处置 |
|---|---|---|
| 二次翻转 | legacy payload 被 normalize 后若无版本更新地回写磁盘，下次加载会翻回去 | `PerspectiveVersion` 守卫 + 幂等测试 |
| `Outcome` 语义变更 | 改后 manifest 的 `Outcome` 变成录制者视角。当前回放链路不消费它（已确认无引用），但外部消费者若有依赖会变 | ADR-0007 记录；`GhostBattleLocalProjector` 仍供列表行使用 |
| 服务端字段约定 | `GhostBattleImportRecord.Player*` 是否恒为上传者 | 已由 `uploader_account_id = record.PlayerAccountId` 佐证；上线前再核对一次服务端契约 |
| 文案分支面 | ghost 与 local 两套 side 文案增加分支，易漏 | 分支集中在 `HistoryPanelFormatter` 与 Rows 绑定两处，不散落 |

## 实施顺序

1. A（`GhostManifestProjection` + `ExtractPayload` 改调）
2. E（`PerspectiveVersion` + `Normalize` 入口 + 四个读取点）
3. C（预览取数）
4. D（文案）
5. G（测试）
6. F（ADR-0007 补记）
7. H（构建 + 全量测试 + 运行时验证）
