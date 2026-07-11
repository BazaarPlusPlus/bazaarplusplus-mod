# Facet 可用性单一实现（纯删除 + 测试重定向） 设计稿

状态：批量流水线 Phase B 草稿，待红队 + 用户统一确认。来源：架构评审候选 5，**经盘点缩水**。

## 定性修正记录（供未来评审引用）

- 原候选的子项「快照上移 `CollectionCatalogBuildResult`」**放弃**：盘点证实 `SnapshotFor` 在 `SetCatalogCards` 的重算是 2026-06-11 架构审计（bf91a60a）**有意落位的热路径优化**，带注释「纯投影，仅在目录变化点重算，绝不 per-RefreshView」；三条调用路径（缓存命中/新建/清空）都正确经过它。上移收益仅为缓存命中时省一次纯扫描，代价是 DTO 加字段 + 两个构造点 + 空目录特例——不值。
- 候选核心保持成立：静态 `TagsFor`/`KeywordsFor`/`IsFacetSource` **零生产调用方**（唯一生产面是 `SnapshotFor`），测试却专门验证这条死路径。谓词语义已逐例比对：对全部被测组合（Item tags / Item keywords / Skill keywords），静态路径与 `SnapshotFor` 成员**逐集合相等**（共享同一 whitelist 排序管道）；唯一理论分歧（Skill tags 组合）无人调用、快照类型上根本不存在该属性。

## 变更清单（纯删除 + 测试重定向，零行为变化）

1. `Data/CollectionFacetAvailability.cs`：删除静态 `TagsFor`（`:76-94`）、`KeywordsFor`（`:96-114`）、`IsFacetSource`（`:134-135`）。`SnapshotFor` 与 `CollectionFacetAvailabilitySnapshot` 不动。**文件不移动不改名**（`CollectionFilterEngine.Tests.csproj:131-134` 有 Compile-Include 路径钉住——原地删减无需动 csproj）。
2. `tests/CollectionFilterEngine.Tests/Program.cs` 四处重定向（盘点已逐一验证断言值零变化）：
   - `:685-692` `KeywordsFor({vm}, Item)` → `SnapshotFor(new[]{vm}).ItemKeywords`
   - `:964-994` `TagsFor(cards, Item)` → `SnapshotFor(cards).ItemTags`
   - `:995-1006` `KeywordsFor(cards, Item)` → 同一快照 `.ItemKeywords`
   - `:1008-1015` `KeywordsFor(cards, Skill)` → 同一快照 `.SkillKeywords`
   - 后三处共用一个 `SnapshotFor(availableFacetCards)` 调用（现状是 3 次全量扫描，重定向后 1 次——测试代码内部效率顺带改善，断言不变）。
   - `:1034-1038` 直接用快照构造器的 fixture **不受影响**（不经静态方法）。

## 测试与验证

- 无新增测试（删除的是死代码；重定向后测试首次对准生产路径——这本身就是收益）。
- 验证：`dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj` + `dotnet build`。

## 不做的事

- 快照上移 BuildResult（见定性修正，勿再提议）。
- `CollectionPanel.cs`/`CollectionQuery.cs` 任何改动（活跃文件，本候选零触碰）。
