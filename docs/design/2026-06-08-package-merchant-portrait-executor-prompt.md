# 包裹卡商人头像 — Executor Goal（实施指令）

> 配套设计：[`2026-06-08-package-merchant-portrait-plan.md`](2026-06-08-package-merchant-portrait-plan.md)（v2，已过 Codex 对抗评审）。本文件是给**实施 agent** 的自包含 `/goal` 指令；设计细节与 `file:line` 证据以设计文档为准，本文件不重复证据，只给“做什么 + 不可破的约束 + 完成判据”。

---

## Goal（可直接粘进 `/goal`）

在仓库 `/Users/yxinyu/codes/bpp/bazaarplusplus-mod` 中实现一个新特性：**给玩家背包/Stash/手牌里的「包裹卡」叠加其唯一对应商人的头像**。完整设计与全部 `file:line` 证据见 `docs/design/2026-06-08-package-merchant-portrait-plan.md`，**先通读它再动手**；遇到与活游戏不符处以代码/活数据为准并在文档里订正。

### 背景（已验证，勿重新论证）

- “包裹卡” = `ITCard.HiddenTags` 含 `EHiddenTag.Package` 的 Item，名为 `"<Merchant>'s Package"`。
- card→merchant 是**严格 1:1**：包裹卡的卖出奖励能力带 `TPrerequisiteRun → TRunConditionalCurrentEncounter → TCardConditionalId { Id=商人encounter templateId, IsNot=false }`。活数据 120/120 张可解析、0 张多商人。
- 该商人 GUID 直接喂 `EncounterPortraitSpriteProvider.LoadPortraitAsync(Guid)` 即得 `Sprite`（嵌入变体 ArtKey 已验证 100% 有效）。
- in-run 卡是**世界空间 `MonoBehaviour`**（无 Canvas）；头像必须是世界空间 `SpriteRenderer`，参 `decompiled/bazaar-qinglong/.../TierBadgeRenderer.cs` 的挂载/置顶/先删后建手法。**不要**用 UGUI/UITK。
- **数据漂移坑**：当前卡数据在游戏运行期缓存（`~/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache/GameData.db`，2889 卡），而非随包的 `StreamingAssets/GameData.db`（陈旧 2709 卡、无 Package）。Mod 运行期经 `BppStaticDataAccess` 读的是活数据。离线核对卡数据务必用运行期缓存。

### 不可破的约束（来自 Codex 对抗评审，违反即返工）

1. **池化安全（最高优先）**：attach 头像**只能**发生在 active、非池化、`IsPlayerBoard` 的 live 卡上。
   - `ItemController.ShowCard(bool)` postfix 中，attach 守卫为 `show && __instance.gameObject.activeInHierarchy && __instance.IsPlayerBoard && __instance.CardData != null`。
   - **必须**额外 patch `ItemController.Cleanup()` postfix 无条件 `Remove`（因为 `CardDeathComplete()` 走 `Cleanup()→PoolObject()(SetActive(false))→ShowCard(true)`，且 `CardController.Cleanup()` 不清 `CardData`/`boardSection`）。
   - attach 携带 token `(InstanceId, TemplateId, boardSection)`；异步出图回调贴图前必须重校验“卡仍 active + CardData 的 InstanceId/TemplateId 一致 + boardSection 一致”，否则丢弃。
2. **层边界**：`GameInterop/PackageMerchant/PackageMerchantResolver` **禁止**引用任何 `BazaarPlusPlus.Game.*`（特别是 `CollectionSourceCatalog`）。名字兜底所需 merchant 索引由 resolver 自己用 `BppStaticDataAccess.LoadCardMap()` 枚举 `ECardTag.Merchant` 构建。新增/扩展 `tests/Architecture.Tests` 断言此边界。
3. **替换而非并存**：本特性只新增，不动 BuildRecommendations 等无关改动；只碰本特性命名的文件。
4. **探针先行**：实现前先在 `ItemController.ShowCard` 主路径加**临时** Debug 日志，dump 一张真实包裹卡的能力图与解析出的商人 GUID，由用户 build+进游戏确认 §1.2 路径与活数据一致，验证后**移除**该日志。不要另建独立诊断脚手架。

### 交付物（按文件）

1. `src/BazaarPlusPlus/GameInterop/PackageMerchant/PackageMerchantResolver.cs` — `ITCard → Guid?` 商人 templateId：Package 判定 → 能力图（含 enchantment）取嵌入 GUID → 校验目标为 `ECardTag.Merchant` encounter；名字兜底（自建索引）；`ConcurrentDictionary` 缓存。零 `Game` 依赖。
2. `src/BazaarPlusPlus/Game/PackageMerchantPortrait/PackageMerchantPortraitRenderer.cs`（+ `PortraitToken`）— 世界空间 `SpriteRenderer` 子物体 `Attach/Remove`，定位/置顶仿 `TierBadgeRenderer`，子物体存 token。
3. `src/BazaarPlusPlus/Patches/PackageMerchantPortrait/BackpackMerchantPortraitPatch.cs` — 两个 postfix：`ItemController.ShowCard`（守卫+token+先删后建）与 `ItemController.Cleanup`（无条件 Remove），经 `BppPatchHost` 取服务。
4. `src/BazaarPlusPlus/Game/PackageMerchantPortrait/PackageMerchantPortraitFeature.cs`（`IBppFeature`）+ 在 `BppComposition.cs` 注册；如需开关加 `ISettingsDockEntry`。
5. `tests/PackageMerchantPortrait.Tests/`（xunit）— resolver 纯逻辑 + token 比较单测。
6. `tests/Architecture.Tests/`（扩展）— `GameInterop.PackageMerchant` 不引用 `Game.*`。

### 验证（完成判据）

- `./run.sh build` 通过（worktree 下加 `-p:BPPInstallerSourcePath=<abs>`）。
- `dotnet test tests/PackageMerchantPortrait.Tests/…csproj` 与 `tests/Architecture.Tests` 通过（`./run.sh test` 时 grep `Failed test projects:`，勿只看尾部）。
- 进游戏（`open "steam://run/1617400"`）验证：包裹卡显示正确商人头像；**卖掉/销毁后死卡不残留头像**；该 `ItemController` 复用为另一张卡后无陈旧/串卡；`show=false`/对手卡不显示；动画头像商人静默兜底。日志看 `<GameDir>/BepInEx/LogOutput.log` 过滤 `[BPP][PackageMerchantPortrait]`。

### 收尾

- 自检 diff → commit（信息含 `Co-Authored-By`）→ 合并工作分支到 `master` → push → 删已合并分支（遵循 repo wrap-up；未经要求不要提前 commit）。
- 若 csharpier 顺带 reformat 了无关文件，单独提交。
- 落地后把设计文档 Status 改为 `IMPLEMENTED`，并按需把“是否保留名字兜底/UpdateData 删除”等运行期观察结论回填文档。
