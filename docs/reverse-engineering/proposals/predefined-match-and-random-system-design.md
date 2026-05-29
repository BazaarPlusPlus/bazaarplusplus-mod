# 预定义对局、对战系统和随机系统方案

> **状态：未实现的设计提案（aspirational）。** 与 [offline-local-run-design.md](offline-local-run-design.md) 是同一套离线模式提案的两半，建议一起读。代码中无对应实现（fixtures / 确定性 RNG / 本地对战引擎均未落地）。

## 设计目标

用户体验目标：

- 多个玩家可以选择同一个预定义对局，看到相同初始条件、相同商店/事件/对手池。
- 同一个 fixture + 同一串玩家命令，结果完全一致。
- 可以做教学关、挑战关、每日固定题、展示局、回放局。
- 离线运行，不依赖官方服务器。

工程目标：

- 不改客户端 UI 主链路，仍使用 `INetCommand` 和 `INetMessage`。
- 随机系统可审计、可复现、可版本化。
- fixture 与 `GameData.db` 版本绑定，避免卡牌数据变化导致结果漂移。

## 两类预定义对局

| 类型 | 做法 | 适合场景 | 交互性 |
|---|---|---|---|
| 回放驱动 | fixture 内直接存 `GameSim`/`CombatSim`/快照序列 | 展示、教程录像、bug replay | 低，玩家主要观看。 |
| 引擎驱动 | fixture 固定初始状态和随机流，玩家命令实时进入本地引擎 | 共享挑战、教学关、每日题、比赛 | 高，推荐。 |

推荐主路线是“引擎驱动 fixture”，同时支持把任意局导出成“回放驱动 fixture”用于验证和展示。

## Fixture manifest

建议 JSON schema：

```json
{
  "schemaVersion": 1,
  "fixtureId": "tutorial-pyg-001",
  "displayName": "Pygmalien Opening Drill",
  "description": "Fixed opening shop and first combat.",
  "gameDataVersion": "2026.05.21",
  "clientVersion": "optional",
  "hero": "Pygmalien",
  "playMode": "Unranked",
  "runSeed": "0x9f0f2b10",
  "rngAlgorithm": "pcg32-v1",
  "initialState": {
    "day": 1,
    "hour": 1,
    "wins": 0,
    "losses": 0,
    "gold": 0,
    "income": 0,
    "health": 100,
    "maxHealth": 100,
    "level": 1,
    "prestige": 0,
    "unlockedSlots": 3,
    "board": [],
    "stash": [],
    "skills": []
  },
  "schedule": {
    "shops": [],
    "encounters": [],
    "levelUps": [],
    "loot": [],
    "pedestals": [],
    "pvpOpponents": [],
    "combats": []
  },
  "rules": {
    "allowReroll": true,
    "allowAbandon": true,
    "strictCommandReplay": false
  },
  "expected": {
    "fixtureHash": "sha256:..."
  }
}
```

## 初始状态结构

`initialState` 应直接映射到客户端 snapshot：

| Fixture 字段 | 客户端 DTO |
|---|---|
| `day/hour/wins/losses` | `RunSnapshotDTO.Day/Hour/Victories/Defeats` |
| `hero` | `PlayerSnapshotDTO.Hero` |
| `gold/income/health/maxHealth/level/prestige` | `PlayerSnapshotDTO.Attributes` |
| `unlockedSlots` | `PlayerSnapshotDTO.UnlockedSlots` |
| `board/stash/skills` | `CardSnapshotDTO[]`，`Owner=Player`，`Section`/`Socket` 设置位置 |
| `currentState` | `RunStateSnapshotDTO.StateName` |
| `selection` | `RunStateSnapshotDTO.SelectionSet` |

卡牌实例建议结构：

```json
{
  "instanceId": "item-0001",
  "templateId": "guid",
  "tier": "Bronze",
  "enchantment": null,
  "owner": "Player",
  "section": "Hand",
  "socket": "Socket_0",
  "attributes": {},
  "tagsOverride": null
}
```

`instanceId` 必须在 fixture 内稳定，不能用每次启动随机 Nanoid，否则命令 replay 无法复现。

## Schedule 结构

### Shop / selection

```json
{
  "at": { "day": 1, "hour": 1, "state": "Choice", "roll": 0 },
  "selectionType": "Shop",
  "cards": [
    { "instanceId": "offer-001", "templateId": "...", "tier": "Bronze", "price": 4 },
    { "instanceId": "offer-002", "templateId": "...", "tier": "Silver", "price": 8 }
  ],
  "rerollCost": 1,
  "rerollsRemaining": 2
}
```

`roll` 用于区分初始选择和第 N 次 reroll。若 fixture 没有显式列出某次 reroll，才落到 deterministic RNG generator。

### Encounter route

```json
{
  "at": { "day": 1, "hour": 2 },
  "choices": [
    { "instanceId": "enc-001", "templateId": "monster-or-event-guid" },
    { "instanceId": "enc-002", "templateId": "vendor-guid" }
  ]
}
```

### Combat

两种模式：

```json
{
  "encounterInstanceId": "enc-001",
  "mode": "scriptedResult",
  "winner": "Player",
  "playerHealthAfter": 72,
  "rewards": { "gold": 2, "xp": 1 }
}
```

或：

```json
{
  "encounterInstanceId": "enc-001",
  "mode": "engine",
  "combatSeed": "stream:combat/day1/hour2",
  "opponent": {
    "hero": "Common",
    "board": []
  }
}
```

早期实现可以用 `scriptedResult` 生成最小 `CombatSim`；后期用 `engine` 跑完整本地战斗。

## 随机系统

不要依赖 `UnityEngine.Random` 或全局 `System.Random` 作为玩法随机。它们适合 UI/VFX，不适合作为可复现核心逻辑。

推荐接口：

```csharp
public interface IDeterministicRng
{
    uint NextUInt();
    int NextInt(int exclusiveMax);
    int NextInt(int inclusiveMin, int exclusiveMax);
    float NextFloat();
    double NextDouble();
    T PickWeighted<T>(IReadOnlyList<T> items, Func<T, double> weight);
    IRngStream Fork(string streamName);
}
```

推荐算法：

- `pcg32` 或 `xoshiro256**`，明确版本号，例如 `pcg32-v1`。
- 所有 stream 由 `Hash(runSeed, fixtureId, gameDataVersion, streamName)` 派生。
- 每次取随机都递增 counter，并可记录到 debug trace。

## 随机流划分

| Stream | 用途 |
|---|---|
| `run` | run 级别分支、初始 id 派生。 |
| `shop/day/hour/reroll` | 商店/选择生成。 |
| `encounter/day/hour` | 路线、事件、怪物选择。 |
| `tier/day/hour` | 卡牌 tier table roll。 |
| `enchant/day/hour` | 随机附魔。 |
| `loot/day/hour` | 战斗后奖励、loot 选择。 |
| `levelup/day/hour` | level up 选择。 |
| `pedestal/day/hour` | pedestal 目标/效果。 |
| `pvp/day/hour` | ghost/opponent 选择。 |
| `combat/combatId` | 战斗中随机目标、暴击、随机效果。 |

为什么要分流：如果商店多 roll 一次，不应该改变未来 PVP 对手或战斗随机。分流可以让 fixture 局部修改不造成全局连锁漂移。

## 与现有随机线索的关系

反编译显示：

- `BazaarGameShared.Domain.Game.ISeedManager` 定义了核心随机接口：`GetDouble`、`GetNumber`、`GetSeedUsingMaster` 等。
- `TierTable.Roll(ISeedManager)` 用 seed manager 做 tier weighted roll。
- `TTargetCardRandom`、`TRangeValue` 等领域逻辑会从 `IRun.GetSeedManager()` 获取随机。
- 当前 `BazaarGameClient.Domain.Models.Run.GetSeedManager()` 返回 `null`，说明当前客户端不负责官方核心玩法随机。
- 旧 `BazaarBattleService.BazaarCardDealer` 内有 `seedManager`，大量选择、目标、奖励、PVP opponent 使用它；这是本地完整引擎的重要参考。
- `ServersHealthService` / `DataDownloader` 里的 `System.Random` 只用于 retry jitter，不是玩法随机。
- `UnityEngine.Random` 在当前客户端大量用于动画、VFX、debug UI、旧测试 UI、随机英雄/随机皮肤展示、赞助名单抽取等；这些不应进入权威玩法随机。
- `StartRunAppState` 在 loadout randomize 开启时会通过 `CollectionManager.GetRandomizedLoadout()` 使用 `System.Random` 生成 cosmetic loadout，并在 `/sessions` 前调用 `/api/Loadouts/hero/{hero}/equip-loadout`。预定义 fixture 必须禁用该开关，或把生成后的 loadout 固定写入 fixture。

因此本地新引擎应实现一个兼容 `ISeedManager` 语义的 adapter，同时在更高层提供命名 stream，避免旧式单 stream 造成难以复现的耦合。

## 客户端侧随机隔离

预定义对局要保证“同一 fixture + 同一命令序列”可复现，必须把非权威客户端随机隔离出去：

| 随机来源 | 风险 | 处理方式 |
|---|---|---|
| loadout randomize | 会在创建 `/sessions` 前改写 cosmetic loadout，影响 replay/展示一致性。 | fixture 模式禁用 randomize，或在 fixture manifest 中固定 loadout ids。 |
| hero select random / RandomHeroPool | 会改变 `InitializeRunCommand.SelectedHero`。 | fixture 选择入口锁定 hero；随机英雄只作为非 fixture UI 功能。 |
| RandomHeroSkinPool | 会改变皮肤展示，不应影响玩法。 | 不写入权威状态；需要展示一致时固定 heroSkinId。 |
| sponsor/supporter random | 只影响展示/sidecar，不应参与玩法 hash。 | 从 `stateHash` 排除，或在导出 replay sidecar 时固定一次。 |
| VFX/UI `UnityEngine.Random` | 只影响动画和视觉。 | 不纳入 `responseHash` / `stateHash`；本地引擎不得读取它。 |

权威随机只能来自本地 session server 的 deterministic RNG stream。客户端 UI 可以继续使用展示随机，但它不能改变 `INetCommand`、`GameStateSnapshotDTO` 或 `GameSim` / `CombatSim` 的玩法字段。

## 对战系统设计

### 对战来源

| 来源 | 用途 |
|---|---|
| fixture 内置 opponent | 教学/挑战局，完全固定。 |
| 本地 ghost store | 玩家历史 run 导出的 board 和属性。 |
| 远端导入缓存 | 如果用户曾经下载过 ghost battle/replay，可以离线使用缓存。 |
| procedural opponent | 没有 fixture 时，按 seed 和静态数据生成。 |

### `IPvpOpponentProvider`

```csharp
public interface IPvpOpponentProvider
{
    PvpOpponentPlan SelectOpponent(LocalRunState state, IRngStream rng, FixtureContext fixture);
}
```

`PvpOpponentPlan`：

- account id/name/title/rank/rating/hero
- board/stash/skills
- level/health/prestige
- loadout cosmetics
- source：`fixture` / `localGhost` / `generated`

生成 `SimPvpOpponent` 时要填客户端 UI 用字段：`Name`、`TitlePrefix`、`TitleSuffix`、`Rank`、`Rating`、`Division`、`Victories`、`Prestige`、`Level`、`Hero`、`PlayerLoadout`、`PlayerCollection`。

### 战斗解析层

```csharp
public interface ICombatResolver
{
    CombatResolution Resolve(LocalRunState state, CombatPlan plan, IRngStream rng);
}
```

`CombatResolution` 输出：

- `NetMessageGameSim`：进入 `Combat` 或 `PVPCombat` 状态。
- `NetMessageCombatSim`：战斗帧、winner/loser、card stats、gold/xp thresholds。
- `NetMessageGameSim`：战斗后进入下一状态，更新 wins/losses/health/rewards。

为了兼容客户端历史面板，应该保持 `GameSim -> CombatSim -> GameSim` 三段消息顺序。

## Command replay 和确定性校验

每个本地 session 保存 JSONL：

```json
{
  "index": 12,
  "rid": 13,
  "commandType": "SelectItemCommand",
  "commandHash": "sha256:...",
  "rngBefore": { "shop/day1/hour1": 4 },
  "rngAfter": { "shop/day1/hour1": 5 },
  "responseHash": "sha256:...",
  "stateHash": "sha256:..."
}
```

校验规则：

1. 同 fixture、同 GameData version、同命令序列，所有 `responseHash` 和 `stateHash` 必须一致。
2. `strictCommandReplay=true` 时，命令必须与 fixture 内预期命令完全一致，否则返回 409/422。
3. `strictCommandReplay=false` 时，允许玩家自由操作，但所有随机仍由 fixture seed 决定。

## 预定义对局接口

在本地 REST facade 增加本地-only 接口，供 UI/mod/外部工具使用：

| 方法 | 路径 | 请求 | 响应 | 业务 |
|---|---|---|---|---|
| GET | `/local/fixtures` | none | fixture summary list | 列出本地预定义对局。 |
| GET | `/local/fixtures/{id}` | none | fixture manifest | 查看完整 fixture。 |
| POST | `/local/fixtures/{id}/select` | `{ hero?, playMode? }` | selected fixture state | 设置下一次 `/sessions` 使用的 fixture。 |
| POST | `/local/runs/{runId}/export-fixture` | options | fixture manifest + replay | 将当前 run 导出为 fixture。 |
| GET | `/local/runs/{runId}/replay-log` | none | command/message log | 调试和校验。 |

这些接口不要伪装成官方 `/api/*`，避免和客户端原协议混淆。

## 与 AutoBazaar 的组合

AutoBazaar 已经能暴露当前 `availableActions` 并接收 `SelectItem`、`Reroll`、`ExitState` 等动作。预定义对局可以这样使用：

1. 本地 fixture server 提供确定性 run。
2. AutoBazaar 外部 bot 读取 `/v1/context`。
3. bot 根据 fixture 目标选择动作。
4. 客户端通过原 `AppState` 发送 `INetCommand` 给本地 session server。
5. 本地 session server 产生确定性消息。

这样可以做“所有人同一局面，比较策略”的挑战模式。

## 版本化和兼容

fixture 必须绑定：

- `schemaVersion`
- `rngAlgorithm`
- `gameDataVersion`
- `clientProtocolVersion` 或至少记录 DLL/build id
- `fixtureHash`

加载时检查：

| 检查 | 不通过时 |
|---|---|
| schema major 不兼容 | 拒绝加载。 |
| GameData version 不一致 | 警告或拒绝，取决于 strict。 |
| templateId 缺失 | 拒绝加载并指出缺失卡。 |
| enum 值未知 | 拒绝或跳过该局。 |
| fixtureHash 不匹配 | 拒绝，防止分享文件损坏。 |

## 实施阶段

### Phase 1：本地可启动 + scripted combat

- 本地 REST facade。
- 本地 `/sessions`/`/commands`。
- fixture 固定初始状态和选择。
- `SelectItem`、`MoveItem`、`SellCard`、`Reroll`、`ExitState`、`SelectEncounter`。
- 战斗用 scripted result 生成最小 `CombatSim`。

### Phase 2：确定性 generator

- 命名 RNG stream。
- 商店/路线/loot/levelup/pedestal generator。
- 本地 ghost/opponent provider。
- command replay hash。

### Phase 3：完整战斗

- 移植或重建 effect/target/combat resolver。
- 支持随机目标、随机附魔、sandstorm、quest、transform、aura。
- 输出完整 `CombatSimFrame` 和 stats。

### Phase 4：分享和比赛

- fixture browser。
- 导入/导出 fixture。
- 每日固定挑战。
- 结果校验和排行榜可本地保存；如要联网，应另开明确 opt-in 服务，不混用官方接口。

## 最小成功标准

一个 fixture 在无互联网环境中：

1. 客户端能启动到主菜单。
2. 选择 hero 后能创建 `/sessions`。
3. 收到 `RunInitialized + GameStateSync + GameSim` 并进入 board。
4. AutoBazaar 或玩家点击能发送 `/commands`。
5. 同一命令序列两次运行的 `stateHash` 完全相同。
6. 不发生任何官方外网请求；mod 上传默认关闭。
