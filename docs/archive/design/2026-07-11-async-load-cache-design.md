# AsyncLoadCache\<TKey,TValue\>：合并两份手写异步头像缓存 设计稿

状态：已红队修订并获实施确认。来源：架构评审候选 2。

## 事实基础（盘点已核）

- `GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs` 与 `GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs` 的缓存骨架逐行同构：静态 `CachedSprites`/`InFlightLoads` 字典（永不清空）、TryGetCached、在飞去重、`finally { 移除在飞; if (shouldCacheResult) 缓存结果 }`。全库仅此两份（`Dictionary<.*Task<` 零其他命中）。
- **负缓存分叉是同一原则的两种实例**，非任意分歧：「前置*服务*未就绪不缓存（重试廉价），服务确认存在后一切下游结果（含异常——两边共有的潜在 bug：瞬态异常被永久负缓存）都缓存」。Hero 1 个门（CollectionManager），Encounter 2 个门（静态数据管理器、AssetLoader）。
- 唯一消费者：`CollectionPanelView.Filters.cs`（5 个调用点），主线程专用，无锁（现状正确性依赖 Unity 主线程上下文）。
- 测试：零覆盖、零 csproj 钉住、零反射按名。先例：`tests/BppLog.Tests` 用 Compile-Include 单文件 + 零 ManagedPath 测纯 Infrastructure 类。

## 设计（已确认）

1. **新模块** `Infrastructure/AsyncLoadCache.cs`：`internal sealed class AsyncLoadCache<TKey, TValue> where TKey : notnull where TValue : class`——**泛型不含 Unity 类型**（Sprite 由调用方作类型实参），才能走 BppLog.Tests 式纯测试。
   - 成员：`bool TryGetCached(TKey key, out TValue? value)`；`Task<TValue?> GetOrLoadAsync(TKey key)`；ctor 收 `Func<TKey, Task<AsyncLoadResult<TValue>>> loader`。
   - `AsyncLoadResult<TValue>`：`TValue? Value; bool ShouldCache;`——**与 `AsyncLoadCache` 同文件声明**（单文件 Compile-Include 测试的硬前提，红队 rev）。门控语义由 loader 表达；骨架执行「finally 移除在飞 + 按 ShouldCache 决定是否缓存（含 null 负缓存）」。
   - **异常路径（红队 major 纠正）**：现状是 loader **内部 catch** 且门后异常**会**负缓存 null、task 从不 faulted（fire-and-forget 调用点因此从不抛）。委托返回值无法表达「抛出后仍缓存」——因此**各 provider 的 loader 必须保留自己的 try/catch + 可变 `shouldCache` 局部**（Hero 在 `:64` 门、Encounter 在 `:57`/`:74` 门翻真），catch 中 `return new AsyncLoadResult<Sprite>(null, shouldCache)`，异常永不逸出 loader。骨架对「loader 真抛出」的契约（清在飞、不缓存、task faulted）只是防御性兜底，非任何现有 provider 的路径。
   - XML-doc 写明契约：**主线程专用、无锁**；负缓存含 null；**骨架内零日志/零 Unity/零 BepInEx 引用**（日志全部留在 loader 内——现状本就如此，两 provider 的 BppLog 调用全在 loader 体内）。
2. **两个 provider 保留原名原签名**（静态门面，消费者零改动）：各持一个 `static AsyncLoadCache<EHero, Sprite>` / `<Guid, Sprite>` 实例 + 私有 loader（保留各自的门与 `IsRenderableHero`/`Guid.Empty` 谓词）。删除各自的字典/去重/finally 骨架（每边约 -30 行）。
3. **行为保真**：门控条件逐门照抄（Hero 1 门、Encounter 2 门）；**异常负缓存的潜在 bug 原样保留**（不趁机修——修复=行为变化，记为后续观察项）；无锁不加锁。

## 测试计划

- 新 xUnit 项目 `tests/AsyncLoadCache.Tests`（BppLog.Tests 模式：Compile-Include `Infrastructure/AsyncLoadCache.cs` 单文件，零 ManagedPath、零 InternalsVisibleTo），TValue 用 object/string：缓存命中、并发去重合流（两次 GetOrLoad 同 key 只跑一次 loader）、null 负缓存、`ShouldCache=false` 后可重试、**骨架兜底契约**：loader 真抛出时在飞清理 + 不缓存（注意：这是骨架自身契约，两个 provider 的「门后异常→catch→负缓存」行为在 loader 内、不被此单文件测试覆盖——记为观察项）、TryGetCached 不触发加载。
- 不改 `CoreLayeringTests`（批量冲突控制：该文件本批只归候选 9 动）。
- 验证：新测试项目 + `dotnet build`。

## 不做的事

- 修异常负缓存潜在 bug（行为变化，另立候选）。
- 缓存清空/驱逐（现状永不清空，保持）。
- 线程安全化（无消费者需要）。
- CoreLayeringTests 集中化 ratchet（归候选 9 的文件独占权）。
