# Collection Panel 商店概率浮层 实施计划（Phase A + B）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**设计依据：** [2026-06-15-collection-panel-shop-probability-design.md](2026-06-15-collection-panel-shop-probability-design.md)（已 red-team 复核 + 用户决策，§9.1）。本计划只覆盖 **首个增量 = Phase A 解释层 + Phase B 估算层**；Phase C（schema v5 catalog hints）与 Phase D（按估算排序）另起计划。

**Goal:** 在 Collection Panel 给每张卡加一个可开关浮层，复用现有 catalog/grid 管线，按 `old-bazaar-card-dealer` 旧模型展示「来源池归属 / native·loose 解释 / 带不可剥离标注的估算区间」，绝不冒充线上权威概率。

**Architecture:** 一个 Unity/BepInEx-free、仅经 `BazaarGameShared` 枚举耦合的纯核心（`Game/CollectionPanel/DealerModel/`，确定性、exe-runner 可单测）产出每卡 `CollectionDealerCardExplain`；在 `CollectionPanel.ApplyFilters()` 既有接缝（`_offerPoolCache.GetOrResolve` 之后）计算并经 `CollectionGridVirtualizer.SetVisible` 的新可选参传到每格 uGUI 徽标 + hover 抽屉；估算层蒙特卡洛按需单卡跑、默认关。

**Tech Stack:** C# 12 / netstandard2.1（mod）、net10.0（exe-runner 测试）、BepInEx 5 `ConfigEntry`、Unity uGUI（徽标）、Harmony 不涉及。

## Global Constraints

> 每个 task 的要求都隐含包含本节。值逐字照抄自设计/仓库约定。

- **分层：** `DealerModel/*.cs` **禁止** `using UnityEngine` / `using BepInEx`；可用 `BazaarGameShared.Domain.Core.Types`（`ETier`/`EHero`/`ECardType`/`ECardSize`/`ECardTag`/`EHiddenTag`）。**必须在 `Game/` 层，不能放 `Core/`**（`CoreLayeringTests` 会拦截）。
- **诚实红线：** UI 不显示冒充权威的精确概率（只给区间/档位）；估算数值在数据层与 `{Model="old-bazaar-card-dealer", Authority=Reference, Source, ReferenceOnly=true}` 绑成 **不可剥离值对象**；`Estimate` 显示默认 **关**。
- **native 概率：** 面板可配置 `ConfigEntry<float>`，**默认 `0.8f`**，范围 [0,1]，非权威假定（UI 标注）。
- **GUID 安全：** 浮层 payload 一律按真实 `vm.Id`（catalog 真卡）键控；dealer-hint 裸 id（`CardIdFilters`/`ItemTierFilters`）只作模拟 **输入**，绝不当渲染键。**伪造/占位 GUID 绝不流到 `CollectionCardFactory.TryBind`。**
- **回收不变式：** 新徽标 `Bind` 在每个实化格子 **无条件调用**，无 entry 时 **显式 `SetActive(false)`**（`CollectionCardPool.Return` 只 `SetActive(false)`、不重置子物体）。
- **测试：** exe-runner（`dotnet run --project ... -c Debug`），断言失败 throw → 进程非零退出；**禁止 coverage-theater**（断言行为/单调性，不断言脆弱浮点、不匹配源码文本）。
- **构建：** `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`（worktree 内须加 `-p:BPPInstallerSourcePath=<abs-installer-resources>`）。完成后 `csharpier format .`，本目录外被 csharpier 改到的文件单独成 commit。
- **本地化：** 所有徽标/抽屉文案走 `CollectionPanelText`，中英双语；标注串（model/source/仅供参考/staleness）为不可省略后缀。
- **提交：** 每 task 末尾 commit，scoped；先 review 自己的 diff 再 commit。Release Notes 一行（`Added`）。

---

## File Structure

**新建（纯核心，`src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/`）：**
- `CollectionDealerProbabilityState.cs` — 状态枚举。
- `EstimateBucket.cs` — 估算不可剥离值对象（数值 + 标注元组）。
- `DealerTierWeightReference.cs` — wiki 0.1.9 参考权重表 + 元数据 + staleness。
- `CollectionDealerSourceContext.cs` / `CollectionDealerCardExplain.cs` — 解释层输入/输出 DTO。
- `CollectionDealerExplainResolver.cs` — 解释层纯函数（Phase A）。
- `IRng.cs` / `SeededRng.cs` — 确定性 PRNG 注入缝。
- `DealerShopDefinition.cs` / `DealerPlayerState.cs` / `DealerCandidate.cs` — 模拟输入 DTO（Phase B）。
- `DealerProbabilityCore.cs` — dealer 状态机 + `SimulateOneDeal` + `EstimateAppearance`（Phase B）。

**新建（UI / 配置，`src/BazaarPlusPlus/Game/CollectionPanel/`）：**
- `Grid/CollectionShopProbabilityBadge.cs` — uGUI 徽标（镜像 `CollectionSourceAttributionBadge`）。
- `Grid/CollectionShopProbabilityDrawer.cs` + `Grid/CollectionShopProbabilityHoverRelay.cs` — hover 抽屉 + 指针 relay。
- `CollectionShopProbabilityCache.cs` — 解释/估算缓存（key 含 day）。
- `CollectionShopProbabilitySettingsDockEntry.cs` — 设置开关入口。

**修改：**
- `Core/Config/IBppConfig.cs` + `Core/Config/BppConfig.cs` — 加两个 `ConfigEntry`。
- `Game/Settings/BppSettingsDockOrder.cs` — 加常量 `CollectionShopProbability = 10`。
- `BppComposition.cs` — 注册 settings dock entry。
- `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs` — `SetVisible` 加 `explainByCardId` 可选参 + 绑定点。
- `Game/CollectionPanel/CollectionPanel.cs` — `ApplyFilters` 计算 explain；加静态 `NotifyShopProbabilityToggled()`。
- `Game/CollectionPanel/Text/CollectionPanelText.cs` — 文案。

**测试（exe-runner）：**
- `tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj` + `Program.cs`。

---

## Task 1: 测试项目骨架 + 状态枚举

**Files:**
- Create: `tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj`
- Create: `tests/CollectionShopProbability.Tests/Program.cs`
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/CollectionDealerProbabilityState.cs`

**Interfaces:**
- Produces: `enum CollectionDealerProbabilityState { NotInPool, Pool, Fixed, Explain, WeightsMissing, Estimate }`；测试 harness `Section/AssertTrue/AssertEqual/Fail` + 非零退出。

- [ ] **Step 1: 建 exe-runner csproj**（镜像 `CollectionSourceFiltering.Tests.csproj` 的 ManagedPath/Reference 模式；先只 pin 本 task 需要的源）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreAdditionalProjectSources>
      https://api.nuget.org/v3/index.json;
      https://nuget.bepinex.dev/v3/index.json;
    </RestoreAdditionalProjectSources>
  </PropertyGroup>

  <PropertyGroup>
    <MacSteamManagedDefault>$(HOME)/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed</MacSteamManagedDefault>
  </PropertyGroup>
  <PropertyGroup Condition="'$(ManagedPath)' == '' and Exists('$(MacSteamManagedDefault)/Assembly-CSharp.dll')">
    <ManagedPath>$(MacSteamManagedDefault)</ManagedPath>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="..\..\src\BazaarPlusPlus\Game\CollectionPanel\DealerModel\CollectionDealerProbabilityState.cs" Link="CollectionDealerProbabilityState.cs" />
    <Reference Include="BazaarGameShared">
      <HintPath>$(ManagedPath)/BazaarGameShared.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

> 后续 task 每新增一个 `DealerModel/*.cs`，都要在此追加一条 `<Compile Include .../>`（memory「Test harness Compile-Include trap」）。`DayTierSchedule.cs` 与 `CollectionCardFacetRanks.cs` 在 Task 4 加入时务必一并 pin。

- [ ] **Step 2: 建 Program.cs harness（断言失败 → 非零退出，修正既有 runner「失败仍退 0」的坑）**

```csharp
using System;

internal static class Check
{
    private static int _failures;

    public static void Section(string name) => Console.WriteLine($"== {name} ==");

    public static void True(bool cond, string msg)
    {
        if (cond) return;
        _failures++;
        Console.WriteLine($"  FAIL: {msg}");
    }

    public static void Equal<T>(T expected, T actual, string msg)
        => True(System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual),
                $"{msg} (expected {expected}, got {actual})");

    public static int Finish()
    {
        if (_failures == 0) { Console.WriteLine("All CollectionShopProbability checks passed."); return 0; }
        Console.WriteLine($"FAILED: {_failures} check(s).");
        return 1;
    }
}

// --- tests ---
Check.Section("smoke");
Check.Equal(0, (int)BazaarPlusPlus.Game.CollectionPanel.DealerModel.CollectionDealerProbabilityState.NotInPool, "NotInPool is 0");

return Check.Finish();
```

- [ ] **Step 3: 建状态枚举**

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal enum CollectionDealerProbabilityState
{
    NotInPool,
    Pool,
    Fixed,
    Explain,
    WeightsMissing,
    Estimate,
}
```

- [ ] **Step 4: 跑测试，确认通过 + 退出码 0**

Run: `dotnet run --project tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj -c Debug`
Expected: 输出 `All CollectionShopProbability checks passed.`，`echo $?` = 0。

- [ ] **Step 5: 提交**

```bash
git add tests/CollectionShopProbability.Tests src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/CollectionDealerProbabilityState.cs
git commit -m "Add CollectionShopProbability test harness and dealer probability state enum"
```

---

## Task 2: EstimateBucket（不可剥离标注）+ wiki 参考权重表

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/EstimateBucket.cs`
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/DealerTierWeightReference.cs`
- Modify: `tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj`（pin 两个新文件）
- Modify: `tests/CollectionShopProbability.Tests/Program.cs`（加断言）

**Interfaces:**
- Produces:
  - `enum EstimateTier { Low, Medium, High }`，`enum EstimateAuthority { Reference }`
  - `sealed class EstimateBucket { EstimateTier Tier; double Low; double High; string Model; EstimateAuthority Authority; string Source; bool ReferenceOnly; }` — 唯一构造 `EstimateBucket.Create(EstimateTier, double low, double high)` 强制写入 `Model="old-bazaar-card-dealer"`、`Authority=Reference`、`Source=DealerTierWeightReference.SourceLabel`、`ReferenceOnly=true`。无任何路径产出缺标注的实例（设计 §0/§4.2 的不可剥离元组 = `{Model, Authority, Source, ReferenceOnly}`，[F-002]）。
  - `static class DealerTierWeightReference { const string SourceLabel = "thebazaar.wiki.gg 0.1.9"; const string RecordedAgainstGameVersion = "0.1.9"; IReadOnlyDictionary<ETier,double> ForDay(int day); }`

- [ ] **Step 1: 写失败测试**（追加到 Program.cs，Task 1 的 `return Check.Finish();` 之前）

```csharp
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarGameShared.Domain.Core.Types;

Check.Section("EstimateBucket metadata inseparable");
var bucket = EstimateBucket.Create(EstimateTier.Medium, 0.05, 0.15);
Check.Equal("old-bazaar-card-dealer", bucket.Model, "bucket carries model");
Check.Equal(EstimateAuthority.Reference, bucket.Authority, "bucket carries Authority=Reference");
Check.Equal("thebazaar.wiki.gg 0.1.9", bucket.Source, "bucket carries source");
Check.True(bucket.ReferenceOnly, "bucket marked reference-only");

Check.Section("wiki reference weights");
var d3 = DealerTierWeightReference.ForDay(3);
Check.True(d3.ContainsKey(ETier.Bronze) && d3.ContainsKey(ETier.Silver), "day3 has Bronze+Silver");
Check.True(!d3.ContainsKey(ETier.Diamond), "day3 has no Diamond");
Check.True(System.Math.Abs(d3.Values.Sum() - 1.0) < 0.05, "day3 weights ~sum 1");
```

- [ ] **Step 2: 跑测试确认编译失败**（类型不存在）

Run: `dotnet run --project tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj -c Debug`
Expected: 编译错误 `EstimateBucket`/`DealerTierWeightReference` 不存在。

- [ ] **Step 3: 实现 EstimateBucket**

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal enum EstimateTier { Low, Medium, High }

internal enum EstimateAuthority { Reference }

// Inseparable value object: a probability bucket can never exist without its
// old-model provenance. There is exactly one factory; no setter, no parameterless ctor.
internal sealed class EstimateBucket
{
    private EstimateBucket(EstimateTier tier, double low, double high)
    {
        Tier = tier;
        Low = low;
        High = high;
        Model = "old-bazaar-card-dealer";
        Authority = EstimateAuthority.Reference;
        Source = DealerTierWeightReference.SourceLabel;
        ReferenceOnly = true;
    }

    public EstimateTier Tier { get; }
    public double Low { get; }
    public double High { get; }
    public string Model { get; }
    public EstimateAuthority Authority { get; }
    public string Source { get; }
    public bool ReferenceOnly { get; }

    public static EstimateBucket Create(EstimateTier tier, double low, double high) =>
        new(tier, low, high);
}
```

- [ ] **Step 4: 实现参考权重表**（数值逐行照抄 [shop-entry-3 §7]）

```csharp
#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

// Network-sourced reference weights (wiki 0.1.9), NOT live authority — it is obfuscation-stripped
// from the client and may predate the running game build. See shop-entry-3 §7 / shop-entry-5 §4.5.
internal static class DealerTierWeightReference
{
    public const string SourceLabel = "thebazaar.wiki.gg 0.1.9";
    public const string RecordedAgainstGameVersion = "0.1.9";

    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<ETier, double>> ByDay =
        new Dictionary<int, IReadOnlyDictionary<ETier, double>>
        {
            [1] = W(1.00, 0, 0, 0),
            [2] = W(0.91, 0.09, 0, 0),
            [3] = W(0.61, 0.39, 0, 0),
            [4] = W(0.36, 0.64, 0, 0),
            [5] = W(0.19, 0.70, 0.12, 0),
            [6] = W(0.09, 0.73, 0.18, 0),
            [7] = W(0, 0.81, 0.19, 0),
            [8] = W(0, 0.60, 0.32, 0.08),
            [9] = W(0, 0.46, 0.39, 0.15),
            [10] = W(0, 0.40, 0.45, 0.15),
        };

    // Day > 10 clamps to the day-10 row; day < 1 clamps to day 1.
    public static IReadOnlyDictionary<ETier, double> ForDay(int day)
    {
        var d = day < 1 ? 1 : day > 10 ? 10 : day;
        return ByDay[d];
    }

    private static IReadOnlyDictionary<ETier, double> W(double b, double s, double g, double d)
    {
        var map = new Dictionary<ETier, double>();
        if (b > 0) map[ETier.Bronze] = b;
        if (s > 0) map[ETier.Silver] = s;
        if (g > 0) map[ETier.Gold] = g;
        if (d > 0) map[ETier.Diamond] = d;
        return map;
    }
}
```

- [ ] **Step 5: pin 新文件到测试 csproj**（追加两条 `<Compile Include>`，路径 `...\DealerModel\EstimateBucket.cs` / `DealerTierWeightReference.cs`，Link 同名）。

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet run --project tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj -c Debug`
Expected: 全 PASS，退出码 0。

- [ ] **Step 7: 提交**

```bash
git add src/BazaarPlusPlus/Game/CollectionPanel/DealerModel tests/CollectionShopProbability.Tests
git commit -m "Add EstimateBucket value object and wiki 0.1.9 reference tier weights"
```

---

## Task 3: 解释层输入/输出 DTO

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/CollectionDealerSourceContext.cs`
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/CollectionDealerCardExplain.cs`
- Modify: 测试 csproj（pin 两文件）

**Interfaces:**
- Produces:
  - `enum CollectionDealerSourceKind { Merchant, Trainer }`
  - `sealed class CollectionDealerSourceContext { string SourceKey; CollectionDealerSourceKind Kind; EHero? Hero; int Day; bool SuppressDayGate; ETier? PinnedTier; bool EstimateEnabled; float NativeAssumption; DealerShopHint? Hint; }` —（`DealerShopHint`/`DealerProbabilityCore` 在后续 task；本 task 先用 `object? Hint = null` 占位字段名 `Hint`，Task 8 收紧类型）
  - `sealed class CollectionDealerCardExplain { CollectionDealerProbabilityState State; bool InPool; bool NativeEligible; bool LooseEligible; bool DayGatePass; bool SkillBoundaryExcluded; bool? FixedDealVerified; EstimateBucket? Estimate; IReadOnlyList<string> Notes; }`

> 注：为避免 Task 3 反向依赖 Task 7/8 的类型，`CollectionDealerSourceContext.Hint` 本 task 定为 `object?`，Task 8 改成强类型 `DealerShopHint?` 并更新引用。

- [ ] **Step 1: 写失败测试**

```csharp
Check.Section("explain DTO shape");
var ctx = new CollectionDealerSourceContext
{
    SourceKey = "Goldie", Kind = CollectionDealerSourceKind.Merchant,
    Hero = EHero.Vanessa, Day = 3, SuppressDayGate = true, PinnedTier = ETier.Gold,
    EstimateEnabled = false, NativeAssumption = 0.8f,
};
var explain = new CollectionDealerCardExplain
{
    State = CollectionDealerProbabilityState.Explain, InPool = true,
    NativeEligible = false, LooseEligible = true, DayGatePass = true,
    Notes = new[] { "tier-specialist" },
};
Check.Equal(CollectionDealerProbabilityState.Explain, explain.State, "state set");
Check.True(explain.LooseEligible && !explain.NativeEligible, "tier specialist loose-only");
Check.Equal(0.8f, ctx.NativeAssumption, "native assumption default carried");
```

- [ ] **Step 2: 跑测试确认编译失败。**

- [ ] **Step 3: 实现 DTO**

```csharp
#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal enum CollectionDealerSourceKind { Merchant, Trainer }

internal sealed class CollectionDealerSourceContext
{
    public string SourceKey { get; init; } = string.Empty;
    public CollectionDealerSourceKind Kind { get; init; }
    public EHero? Hero { get; init; }
    public int Day { get; init; }
    public bool SuppressDayGate { get; init; }
    public ETier? PinnedTier { get; init; }
    public bool EstimateEnabled { get; init; }
    public float NativeAssumption { get; init; } = 0.8f;
    public object? Hint { get; init; } // tightened to DealerShopHint? in Task 8
}
```

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class CollectionDealerCardExplain
{
    public CollectionDealerProbabilityState State { get; init; }
    public bool InPool { get; init; }
    public bool NativeEligible { get; init; }
    public bool LooseEligible { get; init; }
    public bool DayGatePass { get; init; }
    public bool SkillBoundaryExcluded { get; init; }
    public bool? FixedDealVerified { get; init; }
    public EstimateBucket? Estimate { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 4: pin 两文件到测试 csproj。**
- [ ] **Step 5: 跑测试确认通过。**
- [ ] **Step 6: 提交**

```bash
git commit -am "Add dealer explain source-context and per-card explain DTOs"
```

---

## Task 4: 解释层 Resolver（Phase A，确定性，无随机）

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/CollectionDealerExplainResolver.cs`
- Modify: 测试 csproj（pin Resolver + `Data/DayTierSchedule.cs` + `Data/CollectionCardFacetRanks.cs`；`CollectionCardVm.cs` 已被 Task 间接需要，确认在列）
- Modify: Program.cs（A1–A5 用例）

**Interfaces:**
- Consumes: `CollectionCardVm`（`Id/Type/StartingTier/Heroes/...`）、`CollectionDealerSourceContext`、`DayTierSchedule.AllowsStartingTier`/`CeilingTier`、`CollectionCardFacetRanks.TierRank`。
- Produces: `static Dictionary<Guid, CollectionDealerCardExplain> CollectionDealerExplainResolver.Resolve(CollectionDealerSourceContext ctx, IReadOnlyList<CollectionCardVm> offeredCards)` —— 入参 `offeredCards` 已是当前 source 的 offered 池（调用方用现有 `OfferedCardIds` 过滤后传入）。

> 分类规则（确定性）：
> - `ActiveTierKeys(day)` = `{Bronze..CeilingTier(day)}` 去掉 `Legendary`（参考表无）。
> - `DayGatePass` = `SuppressDayGate ? true : DayTierSchedule.AllowsStartingTier(card.StartingTier, day)`。
> - `LooseEligible` = `SuppressDayGate ? TierRank(card.StartingTier) <= TierRank(PinnedTier) : DayGatePass`。
> - `NativeEligible` = `!SuppressDayGate && DayGatePass && card.StartingTier ∈ ActiveTierKeys(day)`（tier 专卖恒 loose → native 永假）。**`ETier` 无 `Invalid` 变体**——设计 §2.3 的 `Invalid` 是 dealer 侧 `EItemTier`；mod 的 `StartingTier` 是 `ETier`（`{Bronze,Silver,Gold,Diamond,Legendary}`），只需排除 `Legendary`，[F-009]。
> - `State`（本 task 只产 Phase A 态）：不在池 → `NotInPool`；否则 → `Explain`（带 native/loose 资格）。`Fixed`/`WeightsMissing`/`Estimate` 与 Merchant 门、Hint 验证由 **Task 8** 决定（Task 8：Trainer 永不 Estimate→`Explain`，[F-004]；`Estimate` 态的 bucket 延后到 hover 现算，[F-005]）。

- [ ] **Step 1: 写失败测试 A1–A5**（用小工厂造 `CollectionCardVm`）

```csharp
using BazaarPlusPlus.Game.CollectionPanel.Data;

static CollectionCardVm Card(Guid id, ETier tier, ECardType type = ECardType.Item) =>
    new() { Id = id, Type = type, StartingTier = tier };

var gBronze = Guid.NewGuid();
var gGold = Guid.NewGuid();

Check.Section("A1 day-gate");
var ctxDay3 = new CollectionDealerSourceContext { SourceKey = "Nufu", Kind = CollectionDealerSourceKind.Merchant, Day = 3 };
var r1 = CollectionDealerExplainResolver.Resolve(ctxDay3, new[] { Card(gBronze, ETier.Bronze), Card(gGold, ETier.Gold) });
Check.True(r1[gBronze].DayGatePass, "Bronze passes day3 gate");        // ceiling day3 = Silver
Check.True(!r1[gGold].DayGatePass, "Gold blocked by day3 ceiling");

Check.Section("A3 native/loose static eligibility");
Check.True(r1[gBronze].NativeEligible && r1[gBronze].LooseEligible, "Bronze native+loose day3");
Check.True(!r1[gGold].LooseEligible, "Gold not loose-eligible day3");

Check.Section("A3b tier specialist (SuppressDayGate) is loose-only");
var ctxGoldie = new CollectionDealerSourceContext { SourceKey = "Goldie", Kind = CollectionDealerSourceKind.Merchant, Day = 2, SuppressDayGate = true, PinnedTier = ETier.Gold };
var r2 = CollectionDealerExplainResolver.Resolve(ctxGoldie, new[] { Card(gGold, ETier.Gold), Card(gBronze, ETier.Bronze) });
Check.True(!r2[gGold].NativeEligible && r2[gGold].LooseEligible, "Goldie: Gold loose-only, native off");
Check.True(r2[gBronze].LooseEligible, "Goldie: Bronze (<=Gold) loose-eligible despite day2");

Check.Section("A4 WeightsMissing when estimate on but no hint");
var ctxEst = new CollectionDealerSourceContext { SourceKey = "Nufu", Kind = CollectionDealerSourceKind.Merchant, Day = 3, EstimateEnabled = true, Hint = null };
var r3 = CollectionDealerExplainResolver.Resolve(ctxEst, new[] { Card(gBronze, ETier.Bronze) });
Check.Equal(CollectionDealerProbabilityState.WeightsMissing, r3[gBronze].State, "no hint -> WeightsMissing");

Check.Section("A5 keys are real vm.Id only");
Check.True(r1.ContainsKey(gBronze) && r1.Count == 2, "explain keyed by real catalog ids only");
```

- [ ] **Step 2: 跑测试确认编译失败。**

- [ ] **Step 3: 实现 Resolver**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

// Phase A: deterministic, no RNG. Input is the source's already-offered pool (real catalog VMs).
internal static class CollectionDealerExplainResolver
{
    public static Dictionary<Guid, CollectionDealerCardExplain> Resolve(
        CollectionDealerSourceContext ctx,
        IReadOnlyList<CollectionCardVm> offeredCards)
    {
        if (offeredCards == null) throw new ArgumentNullException(nameof(offeredCards));
        var result = new Dictionary<Guid, CollectionDealerCardExplain>(offeredCards.Count);
        var ceilingRank = CollectionCardFacetRanks.TierRank(DayTierSchedule.CeilingTier(ctx.Day));

        foreach (var card in offeredCards)
        {
            var cardRank = CollectionCardFacetRanks.TierRank(card.StartingTier);
            var dayGatePass = ctx.SuppressDayGate
                || DayTierSchedule.AllowsStartingTier(card.StartingTier, ctx.Day);

            bool looseEligible;
            if (ctx.SuppressDayGate && ctx.PinnedTier is ETier pinned)
                looseEligible = cardRank <= CollectionCardFacetRanks.TierRank(pinned);
            else
                looseEligible = dayGatePass;

            // native needs StartingTier == a tier the day can roll; tier specialists never go native.
            var nativeEligible = !ctx.SuppressDayGate
                && dayGatePass
                && card.StartingTier != ETier.Legendary
                && cardRank <= ceilingRank;

            CollectionDealerProbabilityState state;
            if (ctx.EstimateEnabled && ctx.Hint == null)
                state = CollectionDealerProbabilityState.WeightsMissing;
            else
                state = CollectionDealerProbabilityState.Explain; // Estimate filled in Task 8

            result[card.Id] = new CollectionDealerCardExplain
            {
                State = state,
                InPool = true,
                NativeEligible = nativeEligible,
                LooseEligible = looseEligible,
                DayGatePass = dayGatePass,
                Notes = ctx.SuppressDayGate
                    ? new[] { "tier-specialist-loose" }
                    : Array.Empty<string>(),
            };
        }
        return result;
    }
}
```

- [ ] **Step 4: pin 到测试 csproj（Compile Include，[F-010]）：** `DealerModel/CollectionDealerExplainResolver.cs`、`Data/DayTierSchedule.cs`、`Data/CollectionCardFacetRanks.cs`、**`Data/CollectionCardVm.cs`（POCO 半边，本 task 测试用 `Card()` 工厂构造它；切勿 pin `CollectionCardVm.From.cs`——它拉入 `TCardBase` 会破坏编译）**。逐一确认每个文件在 `<ItemGroup>` 里有 `<Compile Include .../>`。
- [ ] **Step 5: 跑测试确认通过。**
- [ ] **Step 6: 提交**

```bash
git commit -am "Add Phase A dealer explain resolver (deterministic pool/native/loose/day-gate)"
```

---

## Task 5: 确定性 PRNG 注入缝

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/IRng.cs`
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/SeededRng.cs`
- Modify: 测试 csproj（pin 两文件）+ Program.cs

**Interfaces:**
- Produces: `interface IRng { double NextDouble(); int NextInt(int exclusiveMax); }`；`sealed class SeededRng : IRng`（`SeededRng(int seed)`，确定性）。

- [ ] **Step 1: 写失败测试**

```csharp
Check.Section("SeededRng deterministic");
var a = new SeededRng(42); var b = new SeededRng(42);
Check.Equal(a.NextInt(1000), b.NextInt(1000), "same seed -> same int");
Check.Equal(a.NextDouble(), b.NextDouble(), "same seed -> same double");
```

- [ ] **Step 2: 跑测试确认编译失败。**

- [ ] **Step 3: 实现**

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal interface IRng
{
    double NextDouble();      // [0,1)
    int NextInt(int exclusiveMax);
}
```

```csharp
#nullable enable
using System;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class SeededRng : IRng
{
    private readonly Random _random;
    public SeededRng(int seed) => _random = new Random(seed);
    public double NextDouble() => _random.NextDouble();
    public int NextInt(int exclusiveMax) => _random.Next(exclusiveMax);
}
```

- [ ] **Step 4: pin + 跑测试通过 + 提交**

```bash
git commit -am "Add deterministic IRng/SeededRng seam for dealer simulation"
```

---

## Task 6: 模拟输入 DTO（Phase B）

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/DealerShopDefinition.cs`（含 `DealerShopHint`）
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/DealerPlayerState.cs`
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/DealerCandidate.cs`
- Modify: 测试 csproj

**Interfaces:**
- Produces:
  - `sealed class DealerCandidate { Guid Id; ECardType Type; ECardSize Size; ETier StartingTier; }`
  - `sealed class DealerShopDefinition { string SourceKey; int NumberCardsToSpawn=3; IReadOnlyList<Guid> CardIdFilters; IReadOnlyList<ETier> ItemTierFilters; bool RerollRepeats; float NativeItemTierProbability; IReadOnlyDictionary<ETier,double> TierWeights; string Model="old-bazaar-card-dealer"; }`
  - `sealed class DealerShopHint { int NumberCardsToSpawn; IReadOnlyList<Guid> CardIdFilters; IReadOnlyList<ETier> ItemTierFilters; bool RerollRepeats; bool Verified; }`（schema v5 入口，Phase C 填，本期默认 `Verified=false`）
  - `sealed class DealerPlayerState { int Day; IReadOnlyCollection<Guid> PlayerSkillCardIds; IReadOnlyCollection<Guid> RerollExclusionIds; }`

- [ ] **Step 1: 写失败测试**（仅构造+读字段，断言默认值）

```csharp
Check.Section("Phase B DTO defaults");
var shop = new DealerShopDefinition { SourceKey = "Nufu", TierWeights = DealerTierWeightReference.ForDay(3), NativeItemTierProbability = 0.8f };
Check.Equal(3, shop.NumberCardsToSpawn, "default spawn 3");
Check.Equal("old-bazaar-card-dealer", shop.Model, "default model label");
var st = new DealerPlayerState { Day = 3 };
Check.True(st.PlayerSkillCardIds != null, "skill ids non-null");
```

- [ ] **Step 2: 跑测试确认编译失败。**

- [ ] **Step 3: 实现三个文件**

```csharp
#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class DealerCandidate
{
    public Guid Id { get; init; }
    public ECardType Type { get; init; }
    public ECardSize Size { get; init; }
    public ETier StartingTier { get; init; }
}
```

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class DealerShopDefinition
{
    public string SourceKey { get; init; } = string.Empty;
    public int NumberCardsToSpawn { get; init; } = 3;
    public IReadOnlyList<Guid> CardIdFilters { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<ETier> ItemTierFilters { get; init; } = Array.Empty<ETier>();
    public bool RerollRepeats { get; init; }
    public float NativeItemTierProbability { get; init; } = 0.8f;
    public IReadOnlyDictionary<ETier, double> TierWeights { get; init; } =
        new Dictionary<ETier, double>();
    public string Model { get; init; } = "old-bazaar-card-dealer";
}

// schema v5 carrier (Phase C). Verified=false until populated from verified static data.
internal sealed class DealerShopHint
{
    public int NumberCardsToSpawn { get; init; } = 3;
    public IReadOnlyList<Guid> CardIdFilters { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<ETier> ItemTierFilters { get; init; } = Array.Empty<ETier>();
    public bool RerollRepeats { get; init; }
    public bool Verified { get; init; }
}
```

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class DealerPlayerState
{
    public int Day { get; init; }
    public IReadOnlyCollection<Guid> PlayerSkillCardIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> RerollExclusionIds { get; init; } = Array.Empty<Guid>();
}
```

- [ ] **Step 4: pin 三文件 + 跑测试通过 + 提交**

```bash
git commit -am "Add Phase B dealer simulation DTOs (shop def, hint, player state, candidate)"
```

---

## Task 7: DealerProbabilityCore 状态机（8 用例 + 确定性/单调性）

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/DealerModel/DealerProbabilityCore.cs`
- Modify: 测试 csproj + Program.cs（8 行为用例 + 确定性 + 单调性）

**Interfaces:**
- Consumes: `DealerShopDefinition`, `DealerPlayerState`, `IReadOnlyList<DealerCandidate>`, `IRng`, `CollectionCardFacetRanks.TierRank`。
- Produces:
  - `IReadOnlyList<Guid> DealerProbabilityCore.SimulateOneDeal(DealerShopDefinition shop, DealerPlayerState state, IReadOnlyList<DealerCandidate> pool, IRng rng)` — 复刻 §2.2 闸门顺序（skill 排除 → 固定直发 → reroll 排除 → 纯技能边界）+ §2.3 逐槽 native/loose；返回本次发出的卡 id（未建模 ExperiencePoints/AutoSelect/Combat，见设计 §2.1）。
  - `double DealerProbabilityCore.AppearanceFrequency(Guid target, DealerShopDefinition shop, DealerPlayerState state, IReadOnlyList<DealerCandidate> pool, int trials, int seed)` — 蒙特卡洛频率。

- [ ] **Step 1: 写失败测试（8 用例 + 确定性 + 单调性）**

```csharp
using System.Linq;

static DealerCandidate Cand(Guid id, ETier t, ECardType type = ECardType.Item) =>
    new() { Id = id, Type = type, StartingTier = t };
static DealerShopDefinition Shop(int day, float nativeP, params (ETier,double)[] w) => new()
{
    SourceKey = "T", NativeItemTierProbability = nativeP,
    TierWeights = w.ToDictionary(p => p.Item1, p => p.Item2),
};

var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid(); var t3 = Guid.NewGuid();

Check.Section("1 fixed direct deal -> probability 1/0");
var fixedShop = new DealerShopDefinition { NumberCardsToSpawn = 2, CardIdFilters = new[] { t1, t2 }, TierWeights = DealerTierWeightReference.ForDay(3) };
var fixedDeal = DealerProbabilityCore.SimulateOneDeal(fixedShop, new DealerPlayerState { Day = 3 }, new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Silver), Cand(t3, ETier.Gold) }, new SeededRng(1));
Check.True(fixedDeal.Contains(t1) && fixedDeal.Contains(t2) && !fixedDeal.Contains(t3), "fixed deal exactly CardIdFilters");

Check.Section("3 native only when StartingTier == rolled T");
// nativeP=1, day weights force Bronze only -> only Bronze cards selectable natively
var nativeShop = Shop(3, 1.0f, (ETier.Bronze, 1.0));
var nf = DealerProbabilityCore.AppearanceFrequency(t3, nativeShop, new DealerPlayerState { Day = 3 }, new[] { Cand(t1, ETier.Bronze), Cand(t3, ETier.Gold) }, 2000, 7);
Check.True(nf < 0.001, "Gold never appears under native+Bronze-only roll");

Check.Section("4 loose <= T destructive narrowing affects later slots");
// nativeP=0 (all loose), weights mix; a Silver roll drops Gold from pool for later slots
var looseShop = Shop(5, 0.0f, (ETier.Silver, 1.0)); // always roll Silver
var lf = DealerProbabilityCore.AppearanceFrequency(t3 /*Gold*/, looseShop, new DealerPlayerState { Day = 5 }, new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Silver), Cand(t3, ETier.Gold) }, 2000, 9);
Check.True(lf < 0.001, "Gold (>Silver) never selected when every loose roll is Silver");

Check.Section("6 ItemTierFilters disables native, rolls within filters");
var tierFilterShop = new DealerShopDefinition { ItemTierFilters = new[] { ETier.Silver }, NativeItemTierProbability = 1.0f, TierWeights = DealerTierWeightReference.ForDay(5) };
var tf = DealerProbabilityCore.AppearanceFrequency(t1 /*Bronze*/, tierFilterShop, new DealerPlayerState { Day = 5 }, new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Silver) }, 2000, 11);
Check.True(tf > 0.001, "Bronze still reachable via loose<=Silver despite nativeP=1 (native disabled by ItemTierFilters)");

Check.Section("7 reroll exclusion only when pool stays big enough");
var rerollShop = new DealerShopDefinition { NumberCardsToSpawn = 2, RerollRepeats = false, TierWeights = DealerTierWeightReference.ForDay(3), NativeItemTierProbability = 0f };
// exclude t1,t2 but pool would drop below 2 -> exclusion cleared, t1 still reachable
var rf = DealerProbabilityCore.AppearanceFrequency(t1, rerollShop, new DealerPlayerState { Day = 3, RerollExclusionIds = new[] { t1, t2 } }, new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Bronze) }, 2000, 13);
Check.True(rf > 0.001, "exclusion cleared when it would starve the deal -> excluded card reappears");

Check.Section("skill exclusion -> probability 0");
var sf = DealerProbabilityCore.AppearanceFrequency(t1, Shop(3, 0f, (ETier.Bronze, 1.0)), new DealerPlayerState { Day = 3, PlayerSkillCardIds = new[] { t1 } }, new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Bronze) }, 2000, 15);
Check.True(sf < 0.001, "skill-equipped card never dealt");

Check.Section("2 non-fixed CardIdFilters narrows the pool [F-003]");
// NumberCardsToSpawn=1, CardIdFilters={t1,t2} (count 2 != 1 -> NOT fixed) -> pool narrowed to {t1,t2},
// then random path. Target t3 (outside filters) must never appear.
var cidShop = new DealerShopDefinition { NumberCardsToSpawn = 1, CardIdFilters = new[] { t1, t2 }, NativeItemTierProbability = 0f, TierWeights = DealerTierWeightReference.ForDay(5) };
var cidPool = new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Silver), Cand(t3, ETier.Gold) };
Check.True(DealerProbabilityCore.AppearanceFrequency(t3, cidShop, new DealerPlayerState { Day = 5 }, cidPool, 2000, 17) < 0.001, "card outside CardIdFilters never dealt");
Check.True(DealerProbabilityCore.AppearanceFrequency(t1, cidShop, new DealerPlayerState { Day = 5 }, cidPool, 2000, 17) > 0.001, "card inside CardIdFilters reachable");

Check.Section("5 native miss latches flag2 -> subsequent slots loose [F-007]");
// nativeP=1 but day always rolls Gold; no card is StartingTier==Gold, so native always misses and
// flag2 latches, retrying the slot as loose. Bronze (<=Gold) must still be dealt despite nativeP=1.
var latchShop = new DealerShopDefinition { NumberCardsToSpawn = 2, NativeItemTierProbability = 1f, TierWeights = new Dictionary<ETier, double> { [ETier.Gold] = 1.0 } };
var latchPool = new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Silver) };
Check.True(DealerProbabilityCore.AppearanceFrequency(t1, latchShop, new DealerPlayerState { Day = 6 }, latchPool, 2000, 19) > 0.99, "Bronze still dealt via loose after native-miss latch");

Check.Section("determinism + monotonicity [F-007]");
// NumberCardsToSpawn=1 so not every card is dealt; day3 weights = Bronze 0.61 / Silver 0.39, all loose.
var monoShop = new DealerShopDefinition { NumberCardsToSpawn = 1, NativeItemTierProbability = 0f, TierWeights = DealerTierWeightReference.ForDay(3) };
var monoPool = new[] { Cand(t1, ETier.Bronze), Cand(t2, ETier.Silver) };
var seq1 = string.Join(",", DealerProbabilityCore.SimulateOneDeal(monoShop, new DealerPlayerState { Day = 3 }, monoPool, new SeededRng(42)));
var seq2 = string.Join(",", DealerProbabilityCore.SimulateOneDeal(monoShop, new DealerPlayerState { Day = 3 }, monoPool, new SeededRng(42)));
Check.Equal(seq1, seq2, "same seed -> identical deal sequence");
var fb = DealerProbabilityCore.AppearanceFrequency(t1 /*Bronze*/, monoShop, new DealerPlayerState { Day = 3 }, monoPool, 10000, 42);
var fs = DealerProbabilityCore.AppearanceFrequency(t2 /*Silver*/, monoShop, new DealerPlayerState { Day = 3 }, monoPool, 10000, 42);
// A low Bronze roll yields S_loose(Bronze)={Bronze} (excludes Silver); a Silver roll yields {Bronze,Silver}.
// So the lower-tier card is strictly favored: fb > fs. (Robust direction, no fragile float.)
Check.True(fb > fs, "Bronze (lower tier) strictly more likely than Silver under all-loose day3");
```

> 用例 8（手牌重复升级）覆盖：`TierDuplicateCardHandling` 只改展示品级、不改出现概率（`:4467-4489`），核心 **不引入手牌输入**（设计 §2.5/§7.3-8），故出现概率天然与手牌无关；该断言留待 Phase C / UI 层（届时核心若加 `PlayerHandCards`，再补「同 target 出现频率不随手牌副本变化」用例）。本 task 显式不建模手牌，注释说明 scope。

- [ ] **Step 2: 跑测试确认编译失败。**

- [ ] **Step 3: 实现状态机**（严格按设计 §2.2–§2.3 与 `BazaarCardDealer.cs:3456-3605`）

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

// Offline replay of the old BazaarCardDealer.DealFilteredCards sell-shop path (design §2.2-§2.3).
// Scope: NON-Combat item shops, empty shelf, no ExperiencePoints/AutoSelect (design §2.1).
internal static class DealerProbabilityCore
{
    public static IReadOnlyList<Guid> SimulateOneDeal(
        DealerShopDefinition shop, DealerPlayerState state,
        IReadOnlyList<DealerCandidate> pool, IRng rng)
    {
        // 2-3. pool build: FilterCards applies CardIdFilters whenever non-empty (restricts to those
        //      ids, skipping other filters — StaticDataCardRepository.FilterCards :163-168) [F-003],
        //      THEN skill-equipped exclusion (:3457-3459).
        IEnumerable<DealerCandidate> built = pool;
        if (shop.CardIdFilters.Count > 0)
            built = built.Where(c => shop.CardIdFilters.Contains(c.Id));
        var list = built.Where(c => !state.PlayerSkillCardIds.Contains(c.Id)).ToList();
        if (list.Count == 0) return Array.Empty<Guid>();

        // 6. fixed direct deal BEFORE reroll/skill (:3477-3483)
        if (shop.NumberCardsToSpawn == shop.CardIdFilters.Count && shop.CardIdFilters.Count > 0)
            return shop.CardIdFilters.ToList();

        // 7. reroll exclusion only if !RerollRepeats and filtered stays >= spawn (:3485-3496)
        if (!shop.RerollRepeats && state.RerollExclusionIds.Count > 0)
        {
            var filtered = list.Where(c => !state.RerollExclusionIds.Contains(c.Id)).ToList();
            if (filtered.Count >= shop.NumberCardsToSpawn) list = filtered;
        }

        // 8. pure-skill boundary -> out of sell model (:3497-3519)
        if (list.All(c => c.Type == ECardType.Skill)) return Array.Empty<Guid>();

        // 10. pool too small -> no slot loop (:3533-3537 / else-branch)
        if (list.Count < shop.NumberCardsToSpawn) return Array.Empty<Guid>();

        var dealt = new List<Guid>(shop.NumberCardsToSpawn);
        var flag2 = false;
        for (var slot = 0; slot < shop.NumberCardsToSpawn; slot++)
        {
            var flag3 = shop.ItemTierFilters.Count == 0 && !flag2
                && rng.NextDouble() < shop.NativeItemTierProbability;     // native gate (:3548)
            var tDay = SelectRandomTier(shop.TierWeights, rng);          // every slot (:3552-3558)

            if (flag3)
            {
                var sNative = list.Where(c => c.StartingTier == tDay).ToList();
                if (sNative.Count == 0) { slot--; flag2 = true; continue; }  // miss latch (:3567-3572)
                var pick = sNative[rng.NextInt(sNative.Count)];             // (:3574)
                dealt.Add(pick.Id);
                list.Remove(pick);                                          // remove only (:3577-3580)
            }
            else
            {
                var t = shop.ItemTierFilters.Count > 0
                    ? shop.ItemTierFilters[rng.NextInt(shop.ItemTierFilters.Count)]  // (:3583-3585)
                    : tDay;
                var rankT = CollectionCardFacetRanks.TierRank(t);
                var sLoose = list.Where(c => CollectionCardFacetRanks.TierRank(c.StartingTier) <= rankT).ToList();
                if (sLoose.Count > 0) list = sLoose;                        // list4 = list10 (:3587-3590)
                var pick = list[rng.NextInt(list.Count)];                  // (:3596)
                dealt.Add(pick.Id);
                list.Remove(pick);
            }
        }
        return dealt;
    }

    public static double AppearanceFrequency(
        Guid target, DealerShopDefinition shop, DealerPlayerState state,
        IReadOnlyList<DealerCandidate> pool, int trials, int seed)
    {
        if (trials <= 0) return 0;
        var rng = new SeededRng(seed);
        var hits = 0;
        for (var i = 0; i < trials; i++)
            if (SimulateOneDeal(shop, state, pool, rng).Contains(target)) hits++;
        return (double)hits / trials;
    }

    // ascending-cumulative; return first tier whose cumulative >= roll; Bronze fallback (:4451-4464).
    // NOTE: comparison is roll <= cumulative to match the decompiled `if (num <= num2)` at :4459 [F-008].
    private static ETier SelectRandomTier(IReadOnlyDictionary<ETier, double> weights, IRng rng)
    {
        var roll = rng.NextDouble();
        var cumulative = 0.0;
        foreach (var pair in weights.OrderBy(p => p.Value))
        {
            cumulative += pair.Value;
            if (roll <= cumulative) return pair.Key;
        }
        return ETier.Bronze;
    }
}
```

- [ ] **Step 4: pin Core + 跑测试通过**（若某频率断言因方差偶发失败，提高 trials 或调阈值，不放宽行为断言）。
- [ ] **Step 5: 提交**

```bash
git commit -am "Add DealerProbabilityCore state machine with native/loose path-dependent simulation"
```

---

## Task 8: 估算接入解释层（Estimate 态）+ 收紧 Hint 类型

**Files:**
- Modify: `CollectionDealerSourceContext.cs`（`object? Hint` → `DealerShopHint? Hint`）
- Modify: `CollectionDealerExplainResolver.cs`（Estimate/Fixed 分类 + 调 Core）
- Modify: Program.cs（Estimate/Fixed 用例）

**Interfaces:**
- Consumes: `DealerProbabilityCore.AppearanceFrequency`、`DealerTierWeightReference.ForDay`、`EstimateBucket.Create`、`DealerShopHint`。
- Produces: **`Resolve` 只做 Phase A，绝不跑蒙特卡洛（[F-005]：估算重、且全表数字会显得可排名，设计 §4.4/§5.4 禁止）。** 分类：`EstimateEnabled && Merchant && Hint.Verified && 非 Fixed` → `State=Estimate, Estimate=null`（bucket 延后由 `EstimateForCard` 在 hover 时单卡算）；`…Fixed && Verified` → `State=Fixed, FixedDealVerified=true`；估算开但无验证 hint → `WeightsMissing`；**Trainer 永不 Estimate**（skill 走 `RollSkillTierAndFilter`、不在本模型范围，[F-004]）→ `Explain`。新增 `static EstimateBucket? EstimateForCard(CollectionDealerSourceContext ctx, Guid targetId, IReadOnlyList<CollectionCardVm> offeredCards)`：按需单卡蒙特卡洛，非 Merchant/无验证 hint/Fixed 时返回 null。

- [ ] **Step 1: 写失败测试**

```csharp
Check.Section("Estimate state deferred; bucket computed on-demand with inseparable metadata [F-005]");
var hint = new DealerShopHint { Verified = true, NumberCardsToSpawn = 3 };
var estCtx = new CollectionDealerSourceContext { SourceKey = "Nufu", Kind = CollectionDealerSourceKind.Merchant, Day = 3, EstimateEnabled = true, NativeAssumption = 0.8f, Hint = hint };
var estOffered = new[] { Card(gBronze, ETier.Bronze), Card(gGold, ETier.Silver) };
var er = CollectionDealerExplainResolver.Resolve(estCtx, estOffered);
Check.Equal(CollectionDealerProbabilityState.Estimate, er[gBronze].State, "verified hint + estimate on -> Estimate state");
Check.True(er[gBronze].Estimate == null, "Resolve does NOT compute the bucket (deferred to hover) [F-005]");
var bucket = CollectionDealerExplainResolver.EstimateForCard(estCtx, gBronze, estOffered);
Check.True(bucket != null && bucket!.ReferenceOnly && bucket.Authority == EstimateAuthority.Reference, "on-demand bucket has inseparable metadata");

Check.Section("Trainer never estimates (skill model out of scope) [F-004]");
var trainerCtx = new CollectionDealerSourceContext { SourceKey = "Pip", Kind = CollectionDealerSourceKind.Trainer, Day = 3, EstimateEnabled = true, Hint = hint };
var trCards = new[] { Card(gBronze, ETier.Bronze, ECardType.Skill) };
var tr = CollectionDealerExplainResolver.Resolve(trainerCtx, trCards);
Check.Equal(CollectionDealerProbabilityState.Explain, tr[gBronze].State, "trainer + estimate -> Explain not Estimate");
Check.True(CollectionDealerExplainResolver.EstimateForCard(trainerCtx, gBronze, trCards) == null, "EstimateForCard null for trainer");

Check.Section("Fixed unverified downgrades");
var fixedHint = new DealerShopHint { Verified = false, NumberCardsToSpawn = 2, CardIdFilters = new[] { gBronze, gGold } };
var fctx = new CollectionDealerSourceContext { SourceKey = "X", Kind = CollectionDealerSourceKind.Merchant, Day = 3, EstimateEnabled = true, Hint = fixedHint };
var fr = CollectionDealerExplainResolver.Resolve(fctx, new[] { Card(gBronze, ETier.Bronze) });
Check.Equal(CollectionDealerProbabilityState.WeightsMissing, fr[gBronze].State, "unverified fixed -> WeightsMissing not green Fixed");
```

- [ ] **Step 2: 跑测试确认失败（现 resolver 不产 Estimate）。**

- [ ] **Step 3: 改 Hint 类型 + Resolver**

`CollectionDealerSourceContext.cs`：把 `public object? Hint { get; init; }` 改为 `public DealerShopHint? Hint { get; init; }`。

`CollectionDealerExplainResolver.cs`：把 state 决策段替换为：

```csharp
CollectionDealerProbabilityState state;
bool? fixedVerified = null;

var hint = ctx.Hint;
var isMerchant = ctx.Kind == CollectionDealerSourceKind.Merchant;
var isFixed = hint != null
    && hint.NumberCardsToSpawn == hint.CardIdFilters.Count && hint.CardIdFilters.Count > 0;

if (ctx.EstimateEnabled && isMerchant && hint is { Verified: true })
{
    if (isFixed) { state = CollectionDealerProbabilityState.Fixed; fixedVerified = true; }
    else state = CollectionDealerProbabilityState.Estimate; // bucket DEFERRED to EstimateForCard [F-005]
}
else if (ctx.EstimateEnabled && isMerchant) // estimate on, no verified hint
{
    state = CollectionDealerProbabilityState.WeightsMissing;
}
else
{
    // trainers (skill model out of scope, [F-004]) + estimate-off -> explanation only
    state = CollectionDealerProbabilityState.Explain;
}
```

> **`Resolve` 不再跑蒙特卡洛** —— `State=Estimate` 时 `Estimate` 留 `null`，bucket 由 hover 时的 `EstimateForCard` 单卡计算（[F-005]）。把 `result[card.Id] = new CollectionDealerCardExplain { ... }` 补上 `State = state, Estimate = null, FixedDealVerified = fixedVerified,`（`using System;` + `using System.Linq;` 加到文件顶部）。

加按需估算方法 + 私有助手（同文件底部）：

```csharp
// On-demand single-card Monte Carlo [F-005]: invoked from hover, NEVER from bulk Resolve.
public static EstimateBucket? EstimateForCard(
    CollectionDealerSourceContext ctx, Guid targetId, IReadOnlyList<CollectionCardVm> offeredCards)
{
    if (!ctx.EstimateEnabled
        || ctx.Kind != CollectionDealerSourceKind.Merchant
        || ctx.Hint is not { Verified: true }) return null;
    var hint = ctx.Hint!; // non-null after the guard above
    if (hint.NumberCardsToSpawn == hint.CardIdFilters.Count && hint.CardIdFilters.Count > 0)
        return null; // fixed deal: deterministic, not a probabilistic estimate

    var shop = new DealerShopDefinition
    {
        SourceKey = ctx.SourceKey,
        NumberCardsToSpawn = hint.NumberCardsToSpawn,
        CardIdFilters = hint.CardIdFilters,
        ItemTierFilters = hint.ItemTierFilters,
        RerollRepeats = hint.RerollRepeats,
        NativeItemTierProbability = ctx.NativeAssumption,
        TierWeights = DealerTierWeightReference.ForDay(ctx.Day),
    };
    var simPool = offeredCards.Select(c => new DealerCandidate
    { Id = c.Id, Type = c.Type, Size = c.Size, StartingTier = c.StartingTier }).ToList();
    var freq = DealerProbabilityCore.AppearanceFrequency(
        targetId, shop, new DealerPlayerState { Day = ctx.Day }, simPool, trials: 4000, seed: 20260615);
    return ToBucket(freq);
}

private static EstimateBucket ToBucket(double freq)
{
    var tier = freq >= 0.20 ? EstimateTier.High : freq >= 0.07 ? EstimateTier.Medium : EstimateTier.Low;
    // widen to an interval to avoid implying point precision (design §1.4/§5.4)
    var low = System.Math.Max(0, freq - 0.05);
    var high = System.Math.Min(1, freq + 0.05);
    return EstimateBucket.Create(tier, low, high);
}
```

- [ ] **Step 4: 跑测试通过 + 提交**

```bash
git commit -am "Wire DealerProbabilityCore estimate into explain resolver with inseparable bucket"
```

> **核心层完成。** Task 9–14 是 mod 主程序集 + UI 集成，验证方式为 **构建通过 + 用户在游戏内重载验证**（仓库 rule：不建独立探针，在主路径上加可验证行为，用户构建+重载验证）。

---

## Task 9: 设置开关 + native 概率配置 + 静态推送钩子

**Files:**
- Modify: `Core/Config/IBppConfig.cs` / `Core/Config/BppConfig.cs`
- Modify: `Game/Settings/BppSettingsDockOrder.cs`
- Create: `Game/CollectionPanel/CollectionShopProbabilitySettingsDockEntry.cs`
- Modify: `BppComposition.cs`
- Modify: `Game/CollectionPanel/CollectionPanel.cs`（静态 `NotifyShopProbabilityToggled()`）

**Interfaces:**
- Produces: `IBppConfig.EnableCollectionShopProbabilityConfig : ConfigEntry<bool>?`（默认 false）、`IBppConfig.CollectionShopProbabilityNativeAssumptionConfig : ConfigEntry<float>?`（默认 0.8f）；`CollectionPanel.NotifyShopProbabilityToggled()` 静态。

- [ ] **Step 1: 加配置接口属性**（`IBppConfig.cs`，在 `UseFixedSupporterListConfig` 后）

```csharp
    ConfigEntry<bool>? EnableCollectionShopProbabilityConfig { get; }

    ConfigEntry<bool>? EnableCollectionShopProbabilityEstimateConfig { get; }

    ConfigEntry<float>? CollectionShopProbabilityNativeAssumptionConfig { get; }
```

> `EnableCollectionShopProbabilityEstimateConfig`（默认 **false**）是设计 §5.1 的 **二级开关**：控制 `Estimate` 数字是否显示。**没有它 Phase B 不可达**（[F-001]）。主开关只控制浮层总开关，估算需这第二个开关。

- [ ] **Step 2: 加实现 + Bind**（`BppConfig.cs`：加 **三个** `public ConfigEntry<...>? ... { get; private set; }`——enable、estimate-enable、native-assumption，并在 `Initialize` 末尾）

```csharp
        EnableCollectionShopProbabilityConfig = config.Bind(
            "CollectionPanel",
            "EnableShopProbabilityOverlay",
            false,
            "Whether to show the per-card shop spawn-probability overlay in the Collection Panel. Off by default."
        );
        EnableCollectionShopProbabilityEstimateConfig = config.Bind(
            "CollectionPanel",
            "EnableShopProbabilityEstimate",
            false,
            "Whether the overlay also shows the old-bazaar-card-dealer ESTIMATE bucket (reference-only, NOT live-authoritative). Off by default; requires the overlay to be enabled."
        );
        CollectionShopProbabilityNativeAssumptionConfig = config.Bind(
            "CollectionPanel",
            "ShopProbabilityNativeAssumption",
            0.8f,
            "Assumed native-tier gate probability used ONLY by the old-bazaar-card-dealer estimate (NOT live-authoritative). Range 0..1; default 0.8."
        );
```

- [ ] **Step 3: 加 dock order 常量**（`BppSettingsDockOrder.cs`，在 `FixedSupporterList = 9;` 后）

```csharp
    internal const int CollectionShopProbability = 10;
```

- [ ] **Step 4: 加静态推送钩子**（`CollectionPanel.cs`，紧随 `NotifyLocaleChanged()` 之后，约 :140）

```csharp
    internal static void NotifyShopProbabilityToggled()
    {
        if (_instance == null)
            return;
        if (_instance._isVisible)
            _instance.ApplyFilters();
    }
```

- [ ] **Step 5: 建 settings dock entry**（inline `SettingsMenuToggleBridge`，含 `onChanged`）

```csharp
#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionShopProbabilitySettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.CollectionShopProbability;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "CollectionShopProbability",
            CollectionPanelText.ShopProbabilityToggleLabel,   // added in Task 14
            new SettingsMenuToggleBridge(
                () => config.EnableCollectionShopProbabilityConfig?.Value ?? false,
                value =>
                {
                    if (config.EnableCollectionShopProbabilityConfig != null)
                        config.EnableCollectionShopProbabilityConfig.Value = value;
                },
                _ => CollectionPanel.NotifyShopProbabilityToggled()
            )
        );
}
```

- [ ] **Step 6: 注册**（`BppComposition.cs`，在 settings 注册块 :98-107 内加一行）

```csharp
        _settingsDockRegistry.Register(new CollectionShopProbabilitySettingsDockEntry());
```

- [ ] **Step 7: 构建 + 格式化**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`（worktree 加 `-p:BPPInstallerSourcePath=<abs>`）
Expected: 编译通过（`CollectionPanelText.ShopProbabilityToggleLabel` 在 Task 14 前会缺——本 task 暂用占位字符串 `_ => "Shop odds"`，Task 14 替换为本地化）。然后 `csharpier format .`。

> 实施顺序提示：Task 14 的本地化键先于本 task 落地可避免占位。若按本顺序，Step 5 先写 `_ => "Shop odds"`，Task 14 再替换。

- [ ] **Step 8: 提交**

```bash
git commit -am "Add Collection shop-probability settings toggle and native-assumption config"
```

---

## Task 10: uGUI 概率徽标（镜像 AttributionBadge）

**Files:**
- Create: `Game/CollectionPanel/Grid/CollectionShopProbabilityBadge.cs`

**Interfaces:**
- Consumes: `CollectionDealerCardExplain`、`Infrastructure.UiTokens.Colors`、`Infrastructure.Fonts.BppUiFont`。
- Produces: `static void CollectionShopProbabilityBadge.Bind(GameObject host, CollectionDealerCardExplain? explain)` —— **无条件调用**：`explain == null` 时 `SetActive(false)`（回收不变式）。

- [ ] **Step 1: 实现徽标**（结构镜像 `CollectionSourceAttributionBadge.cs:36-74`，左上角避让右上的来源徽标；颜色按态取 token）

```csharp
#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal static class CollectionShopProbabilityBadge
{
    private const string BadgeName = "BppCollectionShopProbabilityBadge";
    private const string LabelName = "BppCollectionShopProbabilityLabel";

    public static void Bind(GameObject host, CollectionDealerCardExplain? explain)
    {
        var badge = EnsureBadge(host);
        if (explain == null || explain.State == CollectionDealerProbabilityState.NotInPool)
        {
            badge.SetActive(false);
            return;
        }
        badge.SetActive(true);
        var label = badge.GetComponentInChildren<Text>(includeInactive: true);
        if (label == null) return;
        label.text = ShortText(explain);
        var image = badge.GetComponent<Image>();
        if (image != null) image.color = BackgroundFor(explain.State);
    }

    private static string ShortText(CollectionDealerCardExplain e) => e.State switch
    {
        CollectionDealerProbabilityState.Pool => "Pool",
        CollectionDealerProbabilityState.Fixed => "Fixed",
        CollectionDealerProbabilityState.WeightsMissing => "?",
        // Estimate bucket is computed on-demand on hover [F-005], so the grid badge shows a neutral
        // marker — never a numeric/ranked bucket — which also avoids side-by-side ranking [F-006].
        CollectionDealerProbabilityState.Estimate => "Est",
        _ => e.NativeEligible ? "Native" : "Loose", // Explain
    };

    private static Color BackgroundFor(CollectionDealerProbabilityState s) => s switch
    {
        CollectionDealerProbabilityState.Fixed => Colors.StatusCompletedBackground,
        CollectionDealerProbabilityState.WeightsMissing => Colors.StatusAbandonedBackground,
        _ => Colors.StatusDefaultBackground,
    };

    private static GameObject EnsureBadge(GameObject host)
    {
        var existing = host.transform.Find(BadgeName);
        if (existing != null) return existing.gameObject;

        var badge = new GameObject(BadgeName, typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(host.transform, worldPositionStays: false);
        var rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);   // top-LEFT (attribution badge is top-right)
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, -12f);
        rect.sizeDelta = new Vector2(74f, 22f);
        rect.localScale = Vector3.one;

        var image = badge.GetComponent<Image>();
        image.color = Colors.StatusDefaultBackground;
        image.raycastTarget = false;

        var labelObject = new GameObject(LabelName, typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(badge.transform, worldPositionStays: false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);
        labelRect.localScale = Vector3.one;

        var label = labelObject.GetComponent<Text>();
        label.font = BppUiFont.Default;
        label.fontSize = 12;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Colors.StatusDefaultText;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;
        return badge;
    }
}
```

> 确认 `Colors.StatusCompletedBackground/StatusAbandonedBackground/StatusDefaultBackground/StatusDefaultText` 存在（红队已核 `Colors.cs:50-55`）。

- [ ] **Step 2: 构建通过 + csharpier。**
- [ ] **Step 3: 提交**

```bash
git commit -am "Add uGUI shop-probability badge with unconditional bind/hide recycle contract"
```

---

## Task 11: Virtualizer 接线（SetVisible 加参 + 绑定 + 回收不变式）

**Files:**
- Modify: `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs`

**Interfaces:**
- Consumes: `CollectionDealerCardExplain`、`CollectionShopProbabilityBadge.Bind`。
- Produces: `SetVisible(..., IReadOnlyDictionary<Guid, CollectionDealerCardExplain>? explainByCardId = null)` 新可选参；每格实化时无条件 `CollectionShopProbabilityBadge.Bind(card.gameObject, lookup)`。

- [ ] **Step 1: 加字段 + 估算器 setter**（与 `_sourceMatchesByCardId` 并列；`using System;` + `using BazaarPlusPlus.Game.CollectionPanel.DealerModel;`）

```csharp
    private IReadOnlyDictionary<Guid, CollectionDealerCardExplain> _explainByCardId =
        new Dictionary<Guid, CollectionDealerCardExplain>();
    // On-demand single-card estimate [F-005]: panel installs this; the hover drawer invokes it
    // lazily for exactly one card. Never used to bulk-fill the grid.
    private Func<Guid, EstimateBucket?>? _shopProbabilityEstimator;

    public void SetShopProbabilityEstimator(Func<Guid, EstimateBucket?>? estimator) =>
        _shopProbabilityEstimator = estimator;
```

- [ ] **Step 2: `SetVisible` 加可选参**（[CollectionGridVirtualizer.cs:88-101] 签名加第四参，并在体内赋值，与 `_sourceMatchesByCardId` 同样兜底）

```csharp
    public void SetVisible(
        IReadOnlyList<CollectionCardVm> visible,
        ECardType activeType,
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>? sourceMatchesByCardId = null,
        IReadOnlyDictionary<Guid, CollectionDealerCardExplain>? explainByCardId = null)
    {
        // ... existing body ...
        _explainByCardId =
            explainByCardId ?? new Dictionary<Guid, CollectionDealerCardExplain>();
```

- [ ] **Step 3: 绑定点**（virtualizer:362 现有 `CollectionSourceAttributionBadge.Bind(card.gameObject, sourceMatches);` 之后无条件加）

```csharp
            _explainByCardId.TryGetValue(vm.Id, out var explain);
            CollectionShopProbabilityBadge.Bind(card.gameObject, explain);
```

> `TryGetValue` 失败时 `explain == null` → `Bind` 隐藏徽标（回收不变式）。**务必无条件调用**，不要包在 `if (_explainByCardId.Count > 0)` 里，否则关掉浮层后残留徽标不会被清。

- [ ] **Step 4: 构建通过 + csharpier + 提交**

```bash
git commit -am "Thread shop-probability explain dict through grid virtualizer to per-cell badge"
```

---

## Task 12: ApplyFilters 计算 explain + 缓存 + 传入 SetVisible

**Files:**
- Modify: `Game/CollectionPanel/CollectionPanel.cs`（`ApplyFilters`）
- Create: `Game/CollectionPanel/CollectionShopProbabilityCache.cs`

**Interfaces:**
- Consumes: `CollectionDealerExplainResolver.Resolve`、`_offerPoolCache` 结果、`_filter.SelectedHero`、`_currentRunDay`、`sourceEntry.SuppressDayGate`、`config.EnableCollectionShopProbabilityConfig`、`config.CollectionShopProbabilityNativeAssumptionConfig`。
- Produces: `CollectionShopProbabilityCache`（key = `offerPoolCacheKey | day | estimateOn | nativeAssumption`）。

- [ ] **Step 1: 建缓存**（镜像 `CollectionSourceOfferPoolCache` 的 per-panel + Clear 模式）

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionShopProbabilityCache
{
    private readonly Dictionary<string, IReadOnlyDictionary<Guid, CollectionDealerCardExplain>> _cache = new();

    public IReadOnlyDictionary<Guid, CollectionDealerCardExplain> GetOrResolve(
        string key, Func<IReadOnlyDictionary<Guid, CollectionDealerCardExplain>> resolve)
    {
        if (_cache.TryGetValue(key, out var hit)) return hit;
        var value = resolve();
        _cache[key] = value;
        return value;
    }

    public void Clear() => _cache.Clear();
}
```

- [ ] **Step 2: 在 ApplyFilters 计算 explain**（[CollectionPanel.cs:813-846]：`offerPoolResult.Status == Ready` 块内、`SetVisible` 之前）

```csharp
            IReadOnlyDictionary<Guid, CollectionDealerCardExplain>? explainByCardId = null;
            Func<Guid, EstimateBucket?>? estimator = null;
            if (sourceEntry != null
                && offeredCardIds != null
                && _config.EnableCollectionShopProbabilityConfig?.Value == true)
            {
                var estimateOn =
                    _config.EnableCollectionShopProbabilityEstimateConfig?.Value ?? false; // [F-001]
                var nativeAssumption =
                    _config.CollectionShopProbabilityNativeAssumptionConfig?.Value ?? 0.8f;
                var day = _currentRunDay ?? DayTierSchedule.OutOfRunDay;
                // CollectionSourceEntry has no PinnedTier property; derive it from the first
                // segment's StartingTier rule (the same signal SuppressDayGate is built from, :38).
                var pinnedTier = sourceEntry.OfferSegments.Count > 0
                    ? sourceEntry.OfferSegments[0].Rule.StartingTier?.Tier
                    : null;
                var ctx = new CollectionDealerSourceContext
                {
                    SourceKey = sourceEntry.SourceKey,
                    Kind = sourceEntry.Kind == CollectionSourceKind.Trainer
                        ? CollectionDealerSourceKind.Trainer
                        : CollectionDealerSourceKind.Merchant,
                    Hero = _filter.SelectedHero,
                    Day = day,
                    SuppressDayGate = sourceEntry.SuppressDayGate,
                    PinnedTier = pinnedTier,
                    EstimateEnabled = estimateOn,
                    NativeAssumption = nativeAssumption,
                    Hint = null, // schema v5 dealer hint (Phase C)
                };
                var offered = new List<CollectionCardVm>();
                foreach (var c in _catalogCards)
                    if (offeredCardIds.Contains(c.Id)) offered.Add(c);
                var key = string.Join("|", sourceEntry.SourceKey, day, estimateOn, nativeAssumption);
                explainByCardId = _shopProbabilityCache.GetOrResolve(
                    key, () => CollectionDealerExplainResolver.Resolve(ctx, offered));
                // On-demand single-card estimator for the hover drawer (NOT bulk) [F-005].
                if (estimateOn)
                    estimator = id => CollectionDealerExplainResolver.EstimateForCard(ctx, id, offered);
            }
```

并把末尾 `_virtualizer.SetVisible(ordered, _filter.ActiveType, offerMatchesByCardId);` 改为（先装估算器，覆盖到关闭时清空）：

```csharp
            _virtualizer.SetShopProbabilityEstimator(estimator);
            _virtualizer.SetVisible(ordered, _filter.ActiveType, offerMatchesByCardId, explainByCardId);
```

> **已核对（以代码为准）：** `CollectionSourceEntry` 暴露 `SourceKey`/`Kind`/`SuppressDayGate`/`OfferSegments`（[Sources/CollectionSourceEntry.cs:41-59]），**无 `PinnedTier`**——已改为从 `OfferSegments[0].Rule.StartingTier?.Tier` 派生（与 `SuppressDayGate` 同一信号，:38）。`_config` 是 `CollectionPanel` 持有的 `IBppConfig`（:121），`_catalogCards`/`_currentRunDay` 均存在。
> 同时加字段 `private readonly CollectionShopProbabilityCache _shopProbabilityCache = new();`，并在现有清缓存处（catalog 重载，`InvalidateCatalog`/`_offerPoolCache.Clear()` 附近）一并 `_shopProbabilityCache.Clear()`。

- [ ] **Step 3: 构建通过 + csharpier。**
- [ ] **Step 4: 游戏内验证**（用户构建 + 通过 Steam 启动 The Bazaar，App ID 1617400，进一局开商人面板）：开关关 → 无徽标；开关开 → 池内卡左上出现 `Native/Loose/Pool` 徽标；tier 专卖商人（Goldie 等）显 `Loose`；切换开关即时生效。读 `BepInEx/LogOutput.log` 确认无 `[BPP]` error。
- [ ] **Step 5: 提交**

```bash
git commit -am "Compute shop-probability explain in ApplyFilters and render per-card badges"
```

---

## Task 13: hover 详情抽屉 + SetUp 完成门

**Files:**
- Create: `Game/CollectionPanel/Grid/CollectionShopProbabilityDrawer.cs`
- Modify: `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs`（在 `PollHover` 的 `IsCompletedSuccessfully` 门后派发抽屉，按当前 `vm.Id` 取 explain）

**Interfaces:**
- Consumes: `CollectionDealerCardExplain`、`_explainByCardId`、`_shopProbabilityEstimator`（Task 11，按需算 bucket）。
- Produces: `static void CollectionShopProbabilityDrawer.Show(GameObject host, CollectionDealerCardExplain explain, EstimateBucket? bucket)` / `Hide(GameObject host)` —— uGUI 子物体，列出 native/loose 资格、day-gate、（`bucket != null` 时）区间 + `old-bazaar-card-dealer · {bucket.Source}（可能早于当前游戏版本）· 仅供参考 · 非线上权威`。bucket 由 hover 现算（单卡），不在 grid 上批量出现。

- [ ] **Step 1: 实现抽屉**（结构同徽标的 lazy-create 子物体，多行 Text；`raycastTarget=false`；文案走 Task 14 的本地化，先用英文直串）。代码与 Task 10 同构（一个略大的面板 + 多行标签），按 §5.2/§5.4 文案；篇幅同 Task 10，此处省略重复模板，实施时镜像 `CollectionShopProbabilityBadge.EnsureBadge` 放大尺寸并写多行文本。

> （这是计划里唯一允许「镜像上一个组件」的步骤，因为结构 1:1 相同；实现者直接复制 Task 10 的 `EnsureBadge`，改名 `EnsureDrawer`、`sizeDelta = (220, 120)`、`anchoredPosition` 居中、Text 改多行。）

- [ ] **Step 2: 在 PollHover 完成门后派发**（virtualizer：找到 `cell.SetUpTask.IsCompletedSuccessfully` 门 [:313-321]，在其分发 `OnHover` 处加）

```csharp
            // RealizedCell exposes Vm (CollectionCardVm) and Card (Component) -> Card.gameObject.
            if (_explainByCardId.TryGetValue(cell.Vm.Id, out var hoverExplain))
            {
                // Compute the estimate bucket lazily for THIS card only [F-005]; null unless Estimate.
                var bucket = hoverExplain.State == CollectionDealerProbabilityState.Estimate
                    ? _shopProbabilityEstimator?.Invoke(cell.Vm.Id)
                    : null;
                CollectionShopProbabilityDrawer.Show(cell.Card.gameObject, hoverExplain, bucket);
            }
```

并在 `DispatchHoverOut`（virtualizer:324-339）拿到 `cell` 处一并 `CollectionShopProbabilityDrawer.Hide(cell.Card.gameObject)`。

> **时序要求（设计 §5.2 / [R-hover-2]）：** 仅在 `cell.SetUpTask.IsCompletedSuccessfully` 后派发（[:317]）；按 **派发时刻当前格子的 `cell.Vm.Id`** 取 explain（`RealizedCell.Vm` [:605]），不在 bind 时捕获——回收的格子绝不显示上一张卡的解释。

- [ ] **Step 3: 构建通过 + csharpier。**
- [ ] **Step 4: 游戏内验证**：悬停池内卡 → 抽屉出现，定性解释正确；快速滚动 + 悬停不报 NRE（验证 SetUp 门）；抽屉文案含 `old-bazaar-card-dealer · 仅供参考 · 非线上权威`。
- [ ] **Step 5: 提交**

```bash
git commit -am "Add hover explanation drawer gated on cell SetUp completion"
```

---

## Task 14: 本地化文案 + 最终验证

**Files:**
- Modify: `Game/CollectionPanel/Text/CollectionPanelText.cs`
- Modify: `CollectionShopProbabilitySettingsDockEntry.cs`（占位 → 本地化键）、徽标/抽屉文案

**Interfaces:**
- Produces: `CollectionPanelText.ShopProbabilityToggleLabel(string key)` 及各态/标注的中英串。

- [ ] **Step 1: 加本地化串**（`CollectionPanelText`，中英；含态名 `Pool/Fixed/Native/Loose/?权重/估算`、免责后缀 `old-bazaar-card-dealer · {source} · 可能早于当前游戏版本 · 仅供参考 · 非线上权威`、native 假定值说明）。镜像该文件现有键的写法。
- [ ] **Step 2: 替换占位**：settings entry 的 `_ => "Shop odds"` 换成 `CollectionPanelText.ShopProbabilityToggleLabel`；徽标/抽屉英文直串换成 `CollectionPanelText.*`。
- [ ] **Step 3: 构建通过 + csharpier。**
- [ ] **Step 4: 全量游戏内验证（中英各一遍）**：① 开关默认关；② 开后 Explain 徽标正确、tier 专卖恒 Loose；③ hover 抽屉文案 + 免责齐全；④ 切语言文案正确；⑤ 关开关后徽标全部消失（回收不变式）；⑥ `LogOutput.log` 无 `[BPP]` error。
- [ ] **Step 5: 跑核心单测确认未回归**

Run: `dotnet run --project tests/CollectionShopProbability.Tests/CollectionShopProbability.Tests.csproj -c Debug`
Expected: 全 PASS，退出码 0。

- [ ] **Step 6: 提交**

```bash
git commit -am "Localize Collection shop-probability overlay copy (zh/en) with reference disclaimers"
```

---

## Self-Review

**1. Spec coverage（设计 §→task）：**
- §1 三态/诚实：Task 4（Explain/WeightsMissing/native-loose）、Task 8（Estimate 不可剥离 + Fixed 未验证降级）、Task 10/13（视觉区分 + 免责）✅
- §2 概率模型：Task 7（§2.2 顺序 + §2.3 native/loose + 路径依赖 + Bronze fallback）✅；ExperiencePoints/AutoSelect/Combat 显式不建模（Task 7 注释 + 设计 §2.1）✅
- §3 数据缺口：Task 8（Hint 缺省→WeightsMissing）、Task 2（wiki 表 + staleness）、Task 9（native 假定 0.8 可配置）✅
- §4 架构/GUID/回收：Task 4/8（纯核心 vm.Id 键）、Task 10/11（无条件 Bind+hide）✅
- §5 UX：Task 9（toggle + 二级 native 配置）、Task 10/13（徽标/抽屉/SetUp 门/uGUI）✅；§5.5 UITK ScrollView 陷阱本期不加 UITK 横条 → 不触发（如加，引 [Ui/CollectionPanelView.Tree.cs:410-436]）。
- §6 分阶段：本计划 = Phase A+B；C/D 明确排除 ✅
- §7 测试：Task 1–8 exe-runner（Compile-Include 清单在 Task 1/4 注明）✅

**2. Placeholder scan：** 无 TBD/TODO。唯一「镜像上一个组件」在 Task 13 Step 1 —— 已明确给出差异（改名/尺寸/多行），非空泛占位。Task 9 Step 7 的 `_ => "Shop odds"` 是显式临时串，Task 14 替换，已注明。

**3. Type consistency：** `CollectionDealerSourceContext.Hint` 在 Task 3 为 `object?`、Task 8 收紧为 `DealerShopHint?`（已注明迁移）；`SetVisible` 第四参 `explainByCardId`（Task 11）与 Task 12 调用一致；`EstimateBucket.Create`/`EstimateTier`/`DealerTierWeightReference.ForDay/SourceLabel` 跨 Task 2/8 一致；`CollectionShopProbabilityBadge.Bind(GameObject, CollectionDealerCardExplain?)` Task 10 定义、Task 11 调用一致。

**已核对（以代码为准，撰写计划时抽查）：** `CollectionSourceEntry` 有 `SourceKey`/`Kind`/`SuppressDayGate`/`OfferSegments`，**无 `PinnedTier`**（Task 12 改为派生）[Sources/CollectionSourceEntry.cs:41-59]；`CollectionPanel._config : IBppConfig`（:121）、`_catalogCards`/`_currentRunDay` 存在；virtualizer 绑定点用 `vm.Id` + `card.gameObject`（:343-362）；`RealizedCell.Vm`/`.Card`/`.SetUpTask`/`.HoverRelay`（:605-610）+ SetUp 完成门（:317）；`Colors.Status*`(:50-55) 由红队核实。**仍需现场确认：** `CollectionSourceOfferSegment.Rule.StartingTier` 的 `.Tier` 属性名（fingerprint 代码用了 `rule.StartingTier?.Tier`，:100，应为准）；`BppUiFont.Default` 与各 `Colors` token 编译可达；`CollectionPanelText` 既有键写法（Task 14）。

---

## Revision 2 — Codex 独立复核修订（2026-06-18）

> 计划初稿经 **Codex（GPT，跨厂商独立复核）** 对照当前代码 + 反编译源审查，得 **10 条**（2 blocker / 6 major / 2 minor）。逐条对照源码核实后 **全部采纳**；正文以 `[F-00x]` 回链。两条算法/方向性发现亲自复核：F-008（`SelectRandomTier` 用 `<=`，[BazaarCardDealer.cs:4459]）、F-003（`FilterCards` 对非空 `CardIdFilters` 收窄池，[StaticDataCardRepository.cs:163-168]）已确认；F-007 的单调性方向经亲自推导更正为 **`fb > fs`**（全 loose + Bronze-heavy 权重下低 tier 反而更易出，Codex/设计原说的 `fs>fb` 是反的）。

| # | 严重度 | 弱点 | 处置 |
| --- | --- | --- | --- |
| F-001 | blocker | `estimateOn` 硬编码 false → Phase B 不可达 | Task 9 加 `EnableCollectionShopProbabilityEstimateConfig`（默认 false）；Task 12 从它取值 |
| F-002 | blocker | `EstimateBucket` 缺 `Authority=Reference` | Task 2 加 `EstimateAuthority` + 工厂强制写入 + 测试断言 |
| F-003 | major | 非固定 `CardIdFilters` 未收窄模拟池 | Task 7 `SimulateOneDeal` 在 skill 排除前按 `CardIdFilters` 收窄 + Task 7 用例 2 |
| F-004 | major | 纯技能/trainer 返回空，与 dealer 发技能不符 | Task 8 Trainer 永不 Estimate → `Explain`；`EstimateForCard` 对 trainer 返回 null + 测试 |
| F-005 | major | 在 `ApplyFilters` 对全表跑蒙特卡洛（设计要求按需单卡） | `Resolve` 改为 Phase-A-only；新增 `EstimateForCard` 按需单卡；Task 11 估算器委托；Task 13 hover 现算 |
| F-006 | major | 混排 `Estimate` 数字与 `?权重` 的相对误读未防 | F-005 后 grid 徽标永不显数字（Estimate→`"Est"`），区间只在单卡 hover 抽屉出现 |
| F-007 | major | Task 7 漏测用例 2/5、单调性断言无效 | 加用例 2（CardIdFilters 收窄）、5（native miss latch）；单调性改 `fb > fs` |
| F-008 | minor | `SelectRandomTier` 用 `<` 而非 `<=` | Task 7 改 `roll <= cumulative`（对齐 :4459） |
| F-009 | minor | `ETier` 无 `Invalid` 变体（设计误用） | Task 4 注明 `Invalid` 是 dealer 侧 `EItemTier`；mod 侧只排除 `Legendary`；设计 §2.3 加注 |
| F-010 | major | Task 4 未显式 pin `CollectionCardVm.cs` | Task 4 Step 4 显式列出 + 警告勿 pin `CollectionCardVm.From.cs` |

**复核结论：** 纯核心的 per-slot RNG 数学正确；缺陷集中在 ① 估算可达性与标注完整性（F-001/F-002），② dealer 边界忠实度（F-003/F-004/F-008），③ 估算的「按需 vs 批量」与相对误读（F-005/F-006），④ 测试与 Compile-Include 完备性（F-007/F-010）。均已在上文修订。

---

## Execution Handoff

Plan complete and saved to `docs/drafts/2026-06-15-collection-panel-shop-probability-plan.md`. 两种执行方式：

1. **Subagent-Driven（推荐）** — 每个 task 派一个 fresh subagent，task 间 review，快速迭代。
2. **Inline Execution** — 本会话内按 `superpowers:executing-plans` 批量执行，带 checkpoint。

要哪种？（仍是设计/计划阶段；动手前请确认采用哪种执行方式。）
