# Monster Database Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 重构 `MonsterDatabase` 为基于 `encounter_id` 的结构化查询层，并打通从 `monsters_bazaardb.json` 到 overlay/debug preview 的 monster board 渲染链路。

**Architecture:** `MonsterDatabase` 负责加载 `Data/monsters_bazaardb.json` 并索引 `MonsterInfo`。`MonsterPreviewSpecBuilder` 负责把 `MonsterInfo.BoardCards` 转成 `PreviewCardSpec`。`OverlayDebugController` 仅选择数据源并调用 overlay controller，不修改 `EncounterTracker`。

**Tech Stack:** C#, Unity, Newtonsoft.Json, existing Bazaar runtime APIs

---

### Task 1: 定义 Monster DTO 与领域模型

**Files:**
- Create: `Data/MonsterInfo.cs`
- Modify: `Data/MonsterDatabase.cs`

**Step 1: Baseline verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

**Step 2: Write minimal implementation**

新增：

- `MonsterInfo`
- `MonsterBoardCardInfo`
- `MonsterSkillInfo`

在 `MonsterDatabase.cs` 中定义最小 DTO：

- `MonsterRecordDto`
- `MonsterRewardsDto`
- `MonsterCombatantDto`
- `MonsterMetadataDto`
- `MonsterBoardCardDto`
- `MonsterSkillDto`

**Step 3: Run verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

### Task 2: 重写 MonsterDatabase 的加载与查询

**Files:**
- Modify: `Data/MonsterDatabase.cs`

**Step 1: Write minimal implementation**

完成：

- 从 `Data/monsters_bazaardb.json` 加载
- 解析为 `Dictionary<string, MonsterRecordDto>`
- 映射成 `Dictionary<Guid, MonsterInfo>`
- 暴露：
  - `Load()`
  - `TryGetByEncounterId(Guid encounterId, out MonsterInfo monster)`
  - `GetAll()`

删除或废弃旧的 `TryGet(string encounterInternalName)` 主路径。

**Step 2: Run verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

### Task 3: 增加 MonsterPreviewSpecBuilder

**Files:**
- Create: `Game/Overlay/MonsterPreviewSpecBuilder.cs`

**Step 1: Write minimal implementation**

新增：

```csharp
internal static class MonsterPreviewSpecBuilder
{
    public static List<PreviewCardSpec> Build(MonsterInfo monster) { }
}
```

行为：

- 读取 `monster.BoardCards`
- 将 `CardId` 映射到 `PreviewCardSpec.TemplateId`
- 将字符串 tier 映射到 preview 的 tier int
- 不处理技能渲染

**Step 2: Run verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

### Task 4: 在 debug overlay 接 monster db 数据源

**Files:**
- Modify: `Game/Overlay/Debug/OverlayDebugController.cs`
- Modify: `Game/DebugOverlay.cs`

**Step 1: Write minimal implementation**

在 `OverlayDebugController` 中：

- 增加一个默认的 debug `encounter_id`
- 增加“当前数据源模式”字段
- 支持从 `MonsterDatabase.TryGetByEncounterId(...)` 获取 `MonsterInfo`
- 用 `MonsterPreviewSpecBuilder.Build(...)` 生成 `PreviewCardSpec`

`F2` 面板中展示：

- 当前 preview 数据源模式
- 当前 debug `encounter_id`
- 当前 monster title（如果命中）

**Step 2: Run verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

### Task 5: 以默认 monster id 完成预览链路验证

**Files:**
- Modify: `Game/Overlay/Debug/OverlayDebugController.cs`

**Step 1: Write minimal implementation**

选用一个默认存在于 `monsters_bazaardb.json` 的 `encounter_id` 作为 debug 初始值。

要求：

- 当 monster 命中时，优先显示 monster board 数据
- 当 monster 未命中时，安全回退为无卡或玩家手牌，避免崩溃

**Step 2: Run verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

**Step 3: Run targeted checks**

Run:

```bash
rg -n "TryGetByEncounterId|MonsterPreviewSpecBuilder|monsters_bazaardb.json" Data Game -S
```

Expected:

- 新数据库查询链路存在
- overlay 已经接入 monster builder

### Task 6: 最终回归验证

**Files:**
- Modify: relevant files only if implementation diverged

**Step 1: Build verification**

Run: `dotnet msbuild /t:Compile /nologo /v:minimal`

Expected: PASS

**Step 2: Regression grep**

Run:

```bash
rg -n "TryGet\\(string encounterInternalName\\)|BazaarPlusPlus_monsters.json" Data Game -S
```

Expected:

- 不再依赖旧的 monster config 主路径

**Step 3: Manual validation checklist**

手工验证：

- 指定默认 `encounter_id` 后可以在 overlay 中看到 monster board
- `F2` 面板能看到当前 monster 数据源和 id
- 调整 board debug 参数后 monster 预览仍正常
- 未命中 id 时不会崩溃
