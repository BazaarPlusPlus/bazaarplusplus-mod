# 包裹卡卡图替换 — 文字版 Executor Goal（先用文字占位图打通管线）

> 配套设计：[`2026-06-08-package-merchant-card-art-replacement-plan.md`](2026-06-08-package-merchant-card-art-replacement-plan.md)（机制/证据/边界以它为准，本文件不重复证据）。
>
> **本 Goal 的范围 = 文字版**：实现完整的「按 `TemplateId` 替换包裹卡卡图」runtime，并用**文字占位图**（`"<Merchant>'s <Size> Package"`）端到端验证管线跑通。真实/头像美术是**后续步骤**，届时按同名 `<templateId>.png` 覆盖即可，**无需改代码**。

---

## Goal（可直接粘进 `/goal`）

在 `/Users/yxinyu/codes/bpp/bazaarplusplus-mod` 实现「包裹卡卡图替换」特性，并用文字占位图验证打通。**先通读 `docs/design/2026-06-08-package-merchant-card-art-replacement-plan.md` 再动手**；以代码 / 活数据为准，遇不符在文档订正。

### 背景（已验证，勿重新论证；证据见设计文档）

- 「包裹卡」= `ITCard.HiddenTags` 含 `EHiddenTag.Package` 的 Item，名为 `"<Merchant>'s Package"`。共 **120 个 templateId = 40 商人 × 3 size（Small/Medium/Large）**；每个 (商人,size) 一个唯一 templateId（全表见设计文档 §16）。**The Tester 是真实商人，也包含在范围内**。
- 原生卡图是**通用箱子**（只有 3 个 ArtKey，按 size 共享、跨商人相同）→ 原生不显示商人；所以 **不能按 ArtKey 替换，必须按 `TemplateId`**。
- 替换原点：**`[HarmonyPostfix] ItemVisualsController.SetCardFrameMaterial`**（`ItemVisualsController.cs:189-217`）。原方法新建 per-card `materialInstance`（`Object.Instantiate`，`:197`）挂到 `cardIllustrationRenderer.sharedMaterial`（`:198`）。我们在其后对该材质做 `SetTexture(CardArtShaderVariables.EncounterBaseMap /*"_BaseMap"*/, tex)` + `mat.mainTexture = tex`（只改插画，保留 premium/enchant keyword）。
- in-run 卡是**世界空间 `MonoBehaviour`**（`CardController.cs:32`），不是 UGUI/UITK。
- 卡图源贴图 1024×1024 正方形。
- **数据漂移坑**：当前卡数据在运行期缓存 `~/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache/GameData.db`（2889 卡），非随包 StreamingAssets（陈旧 2709、无 Package）。Mod 运行期经 `BppStaticDataAccess` 读活数据。

### 不可破约束（来自 Codex 对抗评审 + 边界规则；违反即返工）

1. **池化安全（最高优先）**：attach 只能发生在 **active、非池化** 的卡上。
   - `SetCardFrameMaterial` postfix 内 attach 守卫：`__instance.gameObject.activeInHierarchy && <当前卡是包裹卡且命中自制图>`。`activeInHierarchy` 守卫专挡 `CardDeathComplete()` 的 `Cleanup()→PoolObject()(SetActive(false))→ShowCard(true)` 序列（`ItemController.cs:808-813`、`Extensions.cs:9-12`）。
   - 身份：`SetCardFrameMaterial` 无 card 入参 → 用 `ConditionalWeakTable<ItemVisualsController, Card>`（由 `[HarmonyPostfix] ItemVisualsController.Setup(Card,…)`（`:255`）填）读 `CardData.TemplateId`；`GetComponentInParent<ItemController>()` 作兜底。
   - 因为只改「游戏每次重绑都会重建的 per-card 材质」（不新增对象），复用为非包裹卡时不命中→保持原生，复用为另一包裹卡→换对应图，**无串卡**；故无需 child 物体、无需额外 Cleanup 清理。可选 token 校验（InstanceId+TemplateId）以防异步加载竞态（若采用同步加载则不需要）。
2. **层边界**：`GameInterop.CardArtReplacement` **禁止**引用任何 `BazaarPlusPlus.Game.*`；catalog/texture-cache 放 `Game/`。新增/扩展 `tests/Architecture.Tests` 断言此边界。
3. **替换而非并存**：只新增本特性文件，不动 BuildRecommendations 等无关改动。
4. **探针先行**：实现前在 `SetCardFrameMaterial` postfix 加临时 Debug 日志（dump 命中包裹卡 `InternalName`/`TemplateId`/是否找到自制图/是否 SetTexture 成功），由用户 build+进游戏确认路径正确，验证后移除。

### 交付物（按文件）

1. `Core/Paths/BepInExPathProvider.cs` — 加 `CustomCardArtDirectoryPath = GameRootPath/BazaarPlusPlusV4/CustomCardArt`（与既有 CombatReplays/Screenshots 同款，`:18-41`）。
2. `Game/CardArtReplacement/CustomCardArtCatalog.cs` — 扫描该目录，建 `TemplateId(Guid) → png 路径` 映射（文件名 `<templateId>.png`）；缺失静默。
3. `Game/CardArtReplacement/CustomCardArtTextureCache.cs` — `File.ReadAllBytes → Texture2D.LoadImage(bytes,false) → Apply(false,false)`（照 `BppDockButtonSpriteProvider.cs:57-71`），按 templateId 同步加载+缓存；解码失败销毁半成品 Texture2D 并兜底。
4. `GameInterop/CardArtReplacement/CardArtInjector.cs` — `Apply(ItemVisualsController vis, Texture2D tex)`：取 `cardIllustrationRenderer.sharedMaterial`（反射/Publicizer 私有字段），`SetTexture("_BaseMap")` + `mainTexture`；含 `ConditionalWeakTable` 身份表。**零 `Game` 依赖**。
5. `Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs` — 两个 postfix：`Setup(Card,…)`（填身份表）+ `SetCardFrameMaterial`（active 守卫 + 命中→注入），经 `BppPatchHost` 取服务。
6. `Game/CardArtReplacement/CardArtReplacementFeature.cs`（`IBppFeature`）+ 在 `BppComposition.cs` 注册；可选 `ISettingsDockEntry` 开关。
7. `tests/CardArtReplacement.Tests/`（xunit）— catalog 键映射/缓存命中纯逻辑单测；扩展 `tests/Architecture.Tests` 断言 `GameInterop.CardArtReplacement ↛ Game.*`。

### 文字占位图（本 Goal 的验证素材）

用已存在的生成器产出**文字版**占位图并放到 runtime 读取目录：

```bash
# 生成器在 extractor 仓库（其 venv 已含 Pillow）；输出到运行期数据目录
# （与 CombatReplays/Screenshots 同级的 BazaarPlusPlusV4/）
cd bazaarplusplus-extractor
uv run python scripts/generate_placeholder_package_art.py \
    --mode text \
    --out "<GameRoot>/BazaarPlusPlusV4/CustomCardArt"
```

产出 120 张 `<templateId>.png`，每张为按 size 着色背景 + 居中文字 `"<Merchant>'s <Size> Package"`（如 `Tinker's Small Package`）。runtime 按 `TemplateId` 命中即把卡面替换为该文字图。

### 验证（完成判据）

- `./run.sh build` 通过（worktree 加 `-p:BPPInstallerSourcePath=<abs>`）。
- `dotnet test tests/CardArtReplacement.Tests/…csproj` 与 `tests/Architecture.Tests` 通过（`./run.sh test` 时 grep `Failed test projects:`，勿只看尾部）。
- 进游戏（经 Steam：`open "steam://run/1617400"`）：
  - 背包/Stash 有包裹卡（如 `Tinker's Package`）→ 卡面显示文字图 `Tinker's Small/Medium/Large Package`（按该卡 size）。
  - **池化/复用专项**：卖掉/销毁包裹卡（走 `CardDeathComplete`）→ 死卡无残留；该 `ItemController` 复用为**非包裹卡**→ 恢复原生卡图；复用为**另一包裹卡**→ 显示对应文字图，无串卡。
  - 换品质/附魔/premium → 替换保留且品质/附魔视觉正常；非包裹卡全程不受影响；`CardVisualProxy` 板上路径确认替换生效（设计文档 §13）。
- 日志：`<GameDir>/BepInEx/LogOutput.log` 过滤 `[BPP][CardArtReplacement]`（Debug 行仅 Debug build）。

### 收尾

- 自检 diff → commit（含 `Co-Authored-By`）→ 合并工作分支到 `master` → push → 删已合并分支（未经要求不提前 commit）。
- 文字版验证通过后：把设计文档对应 Status 标注「文字版管线已验证」；真实/头像美术 ready 后按同名 `<templateId>.png` 覆盖 `CustomCardArt/`（零代码改动，可用 `bazaarplusplus-extractor/scripts/build_package_merchant_artpack.py` 产出的数据包 + 生图模型生成）。
