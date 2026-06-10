---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# CollectionPanel 筛选系统 · Package-Solid + Item/Skill 顶层重构 设计

> **Status:** Draft v2 — 已过一轮 Codex 对抗式评审（2026-06-09，见 §0.1），据此修订。待人工确认后实施。
>
> 本文所有 `file:line` 引用经两轮多 agent「映射 → 对抗式复核」+ Codex 评审 + 人工直读对 HEAD（master `7a105ed`）验证。结论以本文为准。

**Goal:** 两件事。(1) 把 **Package 筛选做 Solid**：统一检测口径到游戏权威标签、把开关从「叠加」改为「排他（仅包裹视图）」、正确 tab 门控。(2) 对 **Item/Skill 筛选做顶层重构**：删除死掉的 Merchant-kind facet、统一 source 选择状态、把散落的 `ActiveType` 分支收敛成**声明式 per-tab facet 模型**，并据此让 Skill 页使用自己的筛选维度（用 `EHiddenTag` keyword facet 取代对 skill 无意义的 item 标签）。

**Tech Stack:** C# 12 / netstandard2.1、Unity UI Toolkit（UITK chips）、`BazaarGameShared`（`ECardTag`/`EHiddenTag`/`ETier`/`EHero`/`ECardType`）、`TheBazaar.UI.Tooltips.TooltipTypography`（经 `GameInterop/TagTypography` 适配器）、BepInEx `BppLog`。纯过滤逻辑由 exe-runner 单测覆盖（注意 Compile-Include pin，见 §6）。

---

## 0 已确认决策

| # | 决策点 | 选定 |
|---|---|---|
| 1 | Package「权威定义」来源 | **`EHiddenTag.Package`**（弃用 InternalName 子串）；抽单一 `IsPackage` 助手供 CollectionPanel 与 CardArtInjector 共用。**行为变更**。 |
| 2 | 排他「仅包裹」模式组合语义 | **纯「所有包裹」视图**：`PackagesOnly=true` 时只按 `IsPackage` 过滤，**忽略** hero/tier/size/tag/day/**source** 等其他维度。 |
| 3 | 顶层重构范围 | **清理 + 新增 keyword facet**：R1（删死 Merchant-kind facet）+ R2（统一 source 状态）+ R3（声明式 per-tab facet）+ R5（`EHiddenTag` keyword facet）。 |
| 4 | Skill 页筛选 | **Skill 用自己的筛选逻辑**：Skill 页不再显示 item `ECardTag` 标签 chips；其内容维度改用 keyword facet（`EHiddenTag`）。Item 页保留 `ECardTag` 标签。 |
| 5 | `StartingTierMode.Exact` 清理 | **不做**（Codex #3）：数据死但清理价值极低，且 JSON 解析对未知 mode 抛错；保持现状，移出本次范围。 |

## 0.1 红队评审处置（Codex adversarial review, 2026-06-09）

| # | Codex 发现 | 核实 | 处置 |
|---|---|---|---|
| 1 [high] | 删 `CollectionMerchantKind` 不止动一个 csproj | **属实且更广**：`CollectionMerchantKind.cs` 被 **3 个** exe-runner csproj 显式 Compile-Include（`CollectionFilterEngine.Tests`、`CollectionGridLayout.Tests`、`CollectionSourceFiltering.Tests`），因三者都 include `CollectionCardVm.cs`，而 VM 经 `Merchants` 属性引用该枚举。 | **采纳**：R1 范围扩到 3 个 csproj + 删除前先跑仓库级 include 扫描门禁（§3 R1）。 |
| 2 [high] | 仅在引擎加 PackagesOnly 分支，仍会被选中 source 的 offer pool 限制 | **方向属实**：`ApplyFilters`（`CollectionPanel.cs:742-753`）总是先算 `offeredCardIds` 并传入 context。本文引擎伪码虽把 PackagesOnly 分支放在 offer-pool gate 之前（引擎本身正确），但依赖谓词顺序脆弱、且白算 offer pool、source chip 仍显示选中（UX 困惑）。 | **采纳**：在调用点（`ApplyFilters`）强制 source 旁路——`PackagesOnly` 时不解析 source/不算 offer pool，传干净 context；并加 PackagesOnly × {有 source / 无 source / 有 hero} 回归测试（§2.2）。 |
| 3 [medium] | 删 `StartingTierMode.Exact` 对未来 schema 漂移脆弱 | **部分属实**：JSON 为仓库内嵌资源（非外部数据包），漂移风险低；但删除价值同样极低，解析器对未知 mode 抛错。 | **部分采纳**：移出本次范围，保持现状（决策 5）。 |

---

## 1 现状全景（精简，详证见 §7 引用）

### 1.1 Facet × Tab 矩阵（已验证）

| Facet | Item | Skill | 关键 file:line |
|---|---|---|---|
| Hero chips | `AnyHeroMatch`（OR） | `MatchesSkillHeroScope`（单 hero，独占 ∪ general-shared） | `CollectionHeroScope.cs:14-20` |
| Hero（选中来源时） | 抑制 | 强制 | `CollectionPanel.cs:764` |
| Tier | ✓ | ✓ | `CollectionFilterEngine.cs:48` |
| Size | ✓ | 隐藏 + 引擎跳过 | `engine:33`；view 384-387 |
| Tag (ECardTag) | ✓ | ✓（同 24，但 skill 无 item 标签 → 选中即清空网格） | `engine:52`；无 gate |
| Day | ✓ | ✓ | `engine:50` |
| Source | Merchant 名册 | Trainer 名册 | `CollectionPanel.cs:821-823` |
| Package toggle | ✓ | 显示但恒空 | `CollectionPanel.cs:796` 硬编码 true |
| Sort | ✓ | ✓ | `engine:61-76` |
| ~~Merchant-kind~~ | DEAD | DEAD | `filter.Merchants` 从不被填充 |

### 1.2 ECardTag vs EHiddenTag
- **ECardTag**（30 值）= 物品品类 → 面板 tag chips（`CollectionTagWhitelist` 24 个 = 8 主 + 16 扩展，剔除 6 机制标签）。标签/色来自 `NativeTagTypography.Resolve(ECardTag)`。
- **EHiddenTag**（97 值）= 机制/关键字 + Reference 协同 + 15 `XxxMerchant` + `Package`。**不是** chip facet；只喂养(a)死 Merchant 派生、(b)活 offerRule `hiddenTagsAny`（34 个不同 tag，22/69 条 source）。`NativeTagTypography` 暂无 `Resolve(EHiddenTag)` overload。

### 1.3 已证实死代码
1. **Merchant-kind facet 全链**（生产死、**但被测试覆盖**）：`CollectionMerchantKind` 枚举、`filter.Merchants`+`Clear`（`FilterState.cs:26,60`）、`AnyMerchantMatch`+`merchantFilterCount`（`engine:31,56,109-120`）、`MerchantHiddenTags`+`ResolveMerchants`+`ManualMerchant*Rules`（`Classifier.cs:17-42,97-139`）、`classification.Merchants`/`VM.Merchants`、`CollectionPanelText.Merchant`（160-179）+ `MerchantHeader()`（124，零调用；保留 `MerchantHeaderText`）。测试占用见 R1。
2. `ManualMerchantIdRules`/`ManualMerchantInternalNameRules`（`Classifier.cs:17-23`）：空字典。
3. `CollectionSourceStartingTierMode.Exact`（`Enums.cs:22`，resolver 92-93）：数据死（JSON 8/8 `AtMost`），**但本次不清理**（决策 5）。
4. **Package 检测分裂**：`CollectionCardClassifier` 子串 vs `CardArtInjector` `EHiddenTag.Package`（`CardArtInjector.cs:59-63`，被 `ItemVisualsArtReplacePatch.cs:48` / `CardPreviewItemArtReplacePatch.cs:39` 调用）。

---

## 2 Part A · Package 做 Solid

### 2.1 统一检测口径（决策 1）
- 新增 `GameInterop/Cards/PackageIdentity.cs`（或并入既有 seam）：`static bool IsPackage(TCardBase t) => t.HiddenTags?.Contains(EHiddenTag.Package) ?? false;`
- `CollectionCardClassifier.IsPackage`/`IsPackageName` 改为委托该助手；**删除** `PackageNameMarkers`（`Classifier.cs:14`）及子串逻辑。
- `CardArtInjector.IsPackageCard/IsPackageTemplate` 改为委托同一助手（去重）。
- `CollectionCardVm.IsPackage` 来源不变（仍由 classification 提供），语义改为 hidden-tag。
- **行为变更**：包裹集合 = 带 `EHiddenTag.Package` 的卡（你们的包裹卡面替换已用此 tag，证明它已被正确填充）。

### 2.2 排他「仅包裹」视图（决策 2 + Codex #2）
- `CollectionFilterState.IncludePackages` → **`PackagesOnly`**（默认 `false`，仍 sticky 跨开）。
- **调用点 source 旁路（Codex #2 关键）**：`ApplyFilters`（`CollectionPanel.cs:732-774`）在 `_filter.PackagesOnly` 为真时，**跳过** `ResolveSelectedSourceEntry`/offer-pool 计算，传入干净 context（`OfferedCardIds=null, ApplyHeroFilter=false, SuppressDayGate=false`）。把「忽略 source」落到调用点，不依赖引擎谓词顺序，也不白算 offer pool。
- 引擎重构（`CollectionFilterEngine.Apply`），PackagesOnly 分支**置于所有 facet 谓词之前**（双保险）：
```csharp
foreach (var card in all)
{
    if (card.Type != filter.ActiveType) continue;
    if (filter.PackagesOnly)            // 排他视图：只看 IsPackage，忽略其他所有 facet
    {
        if (!card.IsPackage) continue;
        result.Add(card);
        continue;
    }
    if (card.IsPackage) continue;       // 默认浏览：隐藏包裹
    if (offerPoolSet != null && !offerPoolSet.Contains(card.Id)) continue;
    // …hero / tier / day / tag / (size) 等正常 facet…
    result.Add(card);
}
```
  - 关键：**不能**用单行 `if (PackagesOnly && !IsPackage)`——那样关闭时变空操作、默认不再隐藏包裹（复核已证伪）。
- 同步改名 `CollectionPanel.cs:541,795`、`CollectionPanelViewModel.IncludePackages`（`CollectionPanelView.cs:33`）。

### 2.3 Tab 门控
- `ShowPackageToggle = (_filter.ActiveType == ECardType.Item)`（替换 `CollectionPanel.cs:796` 硬编码 true，复用现成 `RefreshPackageToggle` 显隐通路）。
- 切到 Skill 页时强制 `PackagesOnly=false`（避免残留态产生空网格）。

### 2.4 文案/注释
- `PackagesToggle()` → 「仅包裹 / Packages only」（`CollectionPanelText.cs:62`）；补 tooltip。
- `Tree.cs:268` 注释「gold = packages shown」→「gold = packages only」。

### 2.5 测试
- `Program.cs:201-219`：开关开 → 期望 `[package]`（单张），新增「关 → 仅 normal」。
- 分类测试（`Program.cs:617-626`）：由子串改 `EHiddenTag.Package`。
- **新增回归（Codex #2）**：`PackagesOnly=true` × {选了 source / 没选 source / 选了非 package 命中的 hero/tier} → 结果恒为「该 type 的全部 package」，证明 source/facet 无关。

---

## 3 Part B · 顶层 Item/Skill 重构（R1–R5）

### R1 · 删除死的 Merchant-kind facet（范围按 Codex #1 扩正）
- 删除 §1.3① 整链 + `MerchantHeader()`。
- **删除前置门禁（Codex #1）**：跑仓库级扫描 `rg -l "CollectionMerchantKind" --glob '*.csproj' --glob '*.cs'`，列出所有引用点。
- **必须同步更新的 exe-runner csproj（已核实 = 3 个）**：从 `CollectionFilterEngine.Tests`、`CollectionGridLayout.Tests`、`CollectionSourceFiltering.Tests` 三者的 `<Compile Include>` 列表移除 `CollectionMerchantKind.cs`（三者均经 `CollectionCardVm.cs` 传递引用该枚举）；并核对 `CollectionCardClassification.cs`（被 2 个 csproj include）移除 `Merchants` 后是否还需保留某些 include。
- 删/改 `CollectionFilterEngine.Tests/Program.cs` 的 merchant 用例（72,256-266,328-338,~620）。
- 验证：`./run.sh test` 后 grep 全文「Failed test projects:」逐 runner 核对（[[project_runsh_test_exit_code]]）。
- 收益：消灭「两个 merchant 概念」的认知负担；唯一商人筛选 = source 选择器。

### R2 · 统一 source 选择状态
- `SelectedMerchantSourceKey` + `SelectedTrainerSourceKey` → 合并为 `SelectedSourceKey` + 由 `ActiveType` 派生的 `SourceKind`。
- 简化 `ToggleSource`/`GetSelectedSourceKey`/`ClearSelectedSource`/`PruneSelectedSources` 的双路分派（`FilterState.cs:49-163`）。

### R3 · 声明式 per-tab facet 模型（真正的"顶层"）
- 引入 `CollectionTabProfile`（每个 `ActiveType` 一份），声明：该 tab **显示哪些 facet section**、**引擎启用哪些谓词**、**hero-scope 用哪种**、**source kind**。
- 引擎与 View **读同一份 profile**，替换散落分支：size gate（`engine:33`）、`ApplyHeroFilter` 非对称（`CollectionPanel.cs:764`）、tag 行未门控、package tab-gate、source kind 派发。
- 草案：

  | Facet | Item profile | Skill profile |
  |---|---|---|
  | Hero | ✓ `AnyHeroMatch` | ✓ `SkillHeroScope` |
  | Tier | ✓ | ✓ |
  | Size | ✓ | ✗ |
  | Tags (ECardTag) | ✓ | ✗ |
  | Keywords (EHiddenTag) | （可选，R5） | ✓（Skill 主内容维度） |
  | Day | ✓ | ✓ |
  | Source | Merchant | Trainer |
  | Package | ✓ | ✗ |

- 保持务实：一个小 profile struct + 查表，**不**引入通用框架（遵循 repo「复用既有 prior-art、不手搓新链路」）。

### R4（并入 R3）· Skill 页用自己的筛选逻辑（决策 4）
- Skill profile 不含 `Tags(ECardTag)` → Skill 页隐藏 item 标签行（修掉「Skill 选 tag 清空网格」的隐性 bug）。

### R5 · 新增 `EHiddenTag` keyword facet（决策 3）
- 新增 `CollectionKeywordWhitelist`（curated `EHiddenTag` 关键字子集，主/扩展分组；参考 [[project_collection_filter_v2_backlog]] 的 PlayerFacingKeywordTags）。
- `NativeTagTypography` 加 1 行 `Resolve(EHiddenTag tag) => Resolve(tag.ToString())`（适配器已 string-keyed）。
- 复用 tag-chip 机制（`EnsureTagChips` 同构）渲染 keyword chips；引擎加 `AnyKeywordMatch(card.HiddenTags, filter.Keywords)`。
- 它**取代**死 Merchant-kind facet 的语义位置，并成为 Skill 页内容维度；Item 页是否也显示由 profile 决定（注意 rail 垂直预算 ~1042pt 已紧，放 More/折叠区或第二排）。
- **to-verify（不搭探针、记为验证项）**：skill 卡实际携带哪些有用 `EHiddenTag` 关键字、白名单取哪些、哪些有原生 typography 配色——需一次 live 数据核对（运行时缓存 `GameData.db`，见 [[reference_live_card_data_runtime_cache]]）后定白名单。

---

## 4 数据流（重构后）

```
模板 TCardBase ──► CollectionCardClassifier(IsPackage=EHiddenTag.Package, 去 Merchant 派生)
   └► CollectionCardVm { Id,Type,Size,Tier,Heroes,Tags(ECardTag),HiddenTags(EHiddenTag),IsPackage }
                                         │
用户操作 ──► CollectionFilterState { ActiveType, Heroes,Tiers,Sizes,Tags,Keywords,
                                     SelectedSourceKey+SourceKind, PackagesOnly, Day, Sort }
                                         │
   ApplyFilters ──► PackagesOnly? ─是─► 跳过 source/offer-pool，传干净 context
                       │             └► Engine: 仅 IsPackage（忽略其他）
                       └─否─► 解析 source → offer pool → Engine(按 profile 启用的谓词逐条 AND)
                                         ▼
                       ordered List<CollectionCardVm> ──► Virtualizer（1 VM = 1 cell，不变）
```

---

## 5 分阶段实施（各阶段独立 PR，改前红队）

1. **Phase 1 — Package Solid**（Part A，隔离）：检测统一 + 排他模式（含 ApplyFilters source 旁路）+ tab 门控 + 文案 + 测试（含 §2.5 回归）。
2. **Phase 2 — 死代码清理**（R1）：删 Merchant-kind facet + 同步 3 个测试 csproj + 用例（**前置 include 扫描门禁**）。
3. **Phase 3 — 结构统一**（R2 + R3 + R4）：合并 source 状态 + per-tab profile + Skill 去 item 标签。
4. **Phase 4 — keyword facet**（R5，含 live 数据核对定白名单）：新增 `EHiddenTag` chips，作 Skill 主内容维度。

---

## 6 风险 / 陷阱

- **行为变更（Phase 1）**：包裹集合由「名字含 Package」变「带 Package 标签」；需 in-game 手测确认 ~120 张包裹集合无回归。
- **测试 Compile-Include pin（Phase 2，Codex #1）**：删源文件必须同步改 **3 个** `Collection*.Tests` 的 csproj include 列表 + 用例，否则编译失败（[[project_test_harness_compile_include]]）；删除前先跑 include 扫描门禁。
- **PackagesOnly source 旁路（Phase 1，Codex #2）**：必须在 `ApplyFilters` 调用点旁路 source，不能只靠引擎谓词顺序；加 source/no-source/hero 三组回归。
- **rail 垂直预算（Phase 4）**：keyword 行若常驻会撑爆 rail；放 More/折叠区或与 tag 行二选一。
- **keyword 白名单未定（Phase 4）**：依赖 live 数据核对；先不落白名单内容，记为验证项。
- **`./run.sh test` 退出码不足信**：grep 全文「Failed test projects:」逐 runner 核对（[[project_runsh_test_exit_code]]）。

---

## 7 验证来源
两轮 Workflow（map → 对抗式 verify）+ Codex adversarial review（§0.1）+ 人工直读核心文件与 3 个测试 csproj。关键反例已剔除：包裹携带真实 facet（`CollectionCardVm.From.cs:25-38`）；`CollectionPanelText.Tag` 已于 `e4b76b7` 删除；Merchant facet 被测试覆盖（非纯 uncalled）；`MerchantHeader()` 亦死；`EHiddenTag`=97 值 / `ECardTag`=30 值；`CollectionMerchantKind.cs` 被 3 个 exe-runner csproj 显式 include。
