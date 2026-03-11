# Monster Database Design

**Date:** 2026-03-11

**Goal:** 重构 `MonsterDatabase`，使其直接基于 `Data/monsters_bazaardb.json` 提供按 `encounter_id` 查询的结构化怪物数据，并为 overlay/debug 预览建立一条从 monster board 数据到 `PreviewCardSpec` 的渲染链路。

## 1. 背景

当前的 `MonsterDatabase` 设计过于薄弱：

- 键是 encounter internal name，而不是数据源里的 `encounter_id`
- 只返回 `items` 和 `skills` 两个字符串数组
- 依赖配置目录里的另一个文件，而不是仓库内的 `monsters_bazaardb.json`
- 不能直接支持“按怪物 id 取完整 board / skills / rewards / level”的需求

而 `Data/monsters_bazaardb.json` 的实际结构是：

- 顶层是一个 `Dictionary<string, object>`
- key 是 `encounter_id`
- value 是完整的怪物记录
- 记录中至少包含：
  - `encounter_id`
  - `title`
  - `base_tier`
  - `rewards`
  - `combatant`
  - `monster_metadata.board`
  - `monster_metadata.skills`

因此数据库层应该围绕 `encounter_id` 重新设计。

## 2. 目标与边界

本次改造目标：

- `MonsterDatabase` 启动时从 `Data/monsters_bazaardb.json` 加载
- 统一按 `encounter_id` 查询
- 对外返回结构化 `MonsterInfo`
- 为 overlay/debug 提供 monster board -> preview card 的数据链路

本次明确不做：

- 不修改 `EncounterTracker`
- 不替换现有 `RunInfo.MonsterPreview` 模型
- 不把旧的 encounter-name 查询路径兼容进新接口

## 3. 推荐方案

推荐采用“单类数据库 + 结构化领域返回 + 独立 builder”的方案。

分三层：

1. JSON DTO
2. 领域模型
3. overlay 适配 builder

说明：

- `MonsterDatabase` 负责“加载、索引、查询”
- `MonsterInfo` 负责“表达 monster 领域信息”
- `MonsterPreviewSpecBuilder` 负责“把 board 数据转成 preview cards”

这样可以避免：

- 让 `MonsterDatabase` 直接依赖 overlay
- 让 overlay 直接读原始 JSON DTO

## 4. 数据模型

### 4.1 DTO 层

DTO 需要覆盖当前使用到的 JSON 字段：

- `MonsterRecordDto`
  - `encounter_id`
  - `title`
  - `base_tier`
  - `rewards`
  - `combatant`
  - `monster_metadata`

- `MonsterRewardsDto`
  - `gold`
  - `xp`

- `MonsterCombatantDto`
  - `type`
  - `level`

- `MonsterMetadataDto`
  - `available`
  - `day`
  - `health`
  - `board`
  - `skills`

- `MonsterBoardCardDto`
  - `cardid`
  - `title`
  - `tier`
  - `size`
  - `type`

- `MonsterSkillDto`
  - `skill_id`
  - `title`
  - `tier`
  - `type`

DTO 只负责承接 JSON，不作为业务接口对外暴露。

### 4.2 领域层

`MonsterDatabase` 对外统一返回 `MonsterInfo`：

```csharp
public sealed class MonsterInfo
{
    public Guid EncounterId { get; init; }
    public string Title { get; init; }
    public string BaseTier { get; init; }
    public int? CombatLevel { get; init; }
    public int? Health { get; init; }
    public int? RewardGold { get; init; }
    public int? RewardXp { get; init; }
    public IReadOnlyList<MonsterBoardCardInfo> BoardCards { get; init; }
    public IReadOnlyList<MonsterSkillInfo> Skills { get; init; }
}
```

配套子模型：

- `MonsterBoardCardInfo`
  - `CardId`
  - `Title`
  - `Tier`
  - `Size`
  - `Type`

- `MonsterSkillInfo`
  - `SkillId`
  - `Title`
  - `Tier`
  - `Type`

这些类型应该稳定、可序列化、与 overlay 无耦合。

## 5. 数据库接口

建议 `MonsterDatabase` 暴露的最小接口为：

```csharp
public static void Load();
public static bool TryGetByEncounterId(Guid encounterId, out MonsterInfo monster);
public static IReadOnlyCollection<MonsterInfo> GetAll();
```

原则：

- 查询入口统一使用 `Guid encounterId`
- 不保留旧的 `TryGet(string encounterInternalName)` 作为主路径
- 如果后续必须兼容旧逻辑，也应作为过渡接口，不应再是核心 API

## 6. 文件来源

新数据库应直接从仓库文件加载：

- `Data/monsters_bazaardb.json`

不再把“monster db 的主数据源”放在配置目录下动态生成。

理由：

- 该文件是当前约定的数据事实来源
- 内容完整
- 结构稳定
- 与 `encounter_id` 查询方式天然匹配

如果后续需要支持用户自定义覆盖，应作为独立 overlay / patch 文件，而不是本次基础数据库职责。

## 7. Overlay 数据链路

本次只为 overlay/debug 打通 monster 数据链路。

数据流设计如下：

1. debug 输入确定一个 `encounter_id`
2. `MonsterDatabase.TryGetByEncounterId(encounterId, out var monster)`
3. `MonsterPreviewSpecBuilder.Build(monster)`
4. 得到 `List<PreviewCardSpec>`
5. `MonsterPreviewOverlayController.SetCards(...)`

### 7.1 Builder 设计

建议增加独立转换器：

```csharp
public static class MonsterPreviewSpecBuilder
{
    public static List<PreviewCardSpec> Build(MonsterInfo monster);
}
```

职责：

- 读取 `MonsterInfo.BoardCards`
- 把 `cardid` 转成 `PreviewCardSpec.TemplateId`
- 把字符串 tier 映射到 preview 使用的 tier int
- 当前不处理技能渲染，只处理 board cards

### 7.2 为什么不把转换放进 MonsterDatabase

因为 `MonsterDatabase` 的职责是“查 monster 数据”，不是“构建 overlay 渲染模型”。

如果把 `PreviewCardSpec` 构造塞进去，会导致：

- 数据库层依赖 overlay 层
- 领域边界混乱
- 后续别的消费者难以复用

builder 是更合理的连接点。

## 8. Debug 使用方式

本次只要求先把 monster db 链路跑通，因此 debug 阶段可先使用一个代码中的默认 `encounter_id`。

建议策略：

- `OverlayDebugController` 保留原有“玩家手牌预览”路径
- 新增一个“monster database 预览”路径
- 初期通过代码中的默认 `Guid` 切换

这样便于：

- 先验证数据链路
- 不影响现有手牌预览能力
- 后续再补更好的切换 UI

## 9. 决策总结

最终决策如下：

- `MonsterDatabase` 改为从 `Data/monsters_bazaardb.json` 加载
- 查询键统一为 `encounter_id`
- 对外返回结构化 `MonsterInfo`
- `EncounterTracker` 本轮不改
- overlay/debug 新增一条 monster-db 数据源
- `MonsterInfo -> PreviewCardSpec` 转换由独立 builder 负责

## 10. 后续实现顺序

建议按以下顺序实施：

1. 定义 DTO 与领域模型
2. 重写 `MonsterDatabase.Load`
3. 实现 `TryGetByEncounterId`
4. 新增 `MonsterPreviewSpecBuilder`
5. 在 `OverlayDebugController` 接入默认 `encounter_id`
6. 用固定 monster 验证 board cards 可渲染
