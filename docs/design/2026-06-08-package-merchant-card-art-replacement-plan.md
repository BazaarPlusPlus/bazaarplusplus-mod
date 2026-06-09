# 包裹卡商人头像 — 直接替换卡图方案（Card-Art Replacement）设计 + Goal

> **Status:** IMPLEMENTED — 文字版管线已验证（in-run 卡 + CollectionPanel 预览）；真实/头像美术后续按同名 `<templateId>.png` 覆盖。
>
> 与姊妹方案 [`2026-06-08-package-merchant-portrait-plan.md`](2026-06-08-package-merchant-portrait-plan.md)（世界空间 overlay 叠头像）**目标相同**——让玩家一眼看出「这张包裹卖给哪个商人」——但**手段不同**：本方案**直接替换包裹卡的卡面插画贴图**（类似 mod 现有 collection panel 的「换 icon / 换材质」做法），用**我们自己创作的图片**替换原始卡图。两方案二选一，对比见 §10。
>
> `file:line` 证据分两类：**（A）committed** = `decompiled/`、`src/` 锚点；**（B）活数据/资产实测** = 对游戏运行期缓存与抽取产物的一次性测量（见 §3、§9）。

**Goal:** 给背包/Stash/手牌里的「包裹卡」（`EHiddenTag.Package`，名为 `"<Merchant>'s Package"`）**直接替换其卡面插画**为一张能体现对应商人的自制图片；替换在原生卡图加载链的单一 chokepoint 完成，池化/换牌/附魔/品质安全。

**Tech Stack:** C# 12 / netstandard2.1、Unity 世界空间 `Renderer.sharedMaterial` + `Material.SetTexture(_BaseMap)`、`Texture2D.LoadImage`(磁盘 PNG)、Harmony postfix、`BepInExPathProvider`、复用 `CollectionCardMaterialCache`/`BppDockButtonSpriteProvider` 的图样模式。

---

## 0 已确认决策

| # | 决策点 | 选定 |
|---|---|---|
| 1 | 替换什么 | 包裹卡的**插画贴图**（材质 `_BaseMap`/`mainTexture`），不动卡框/品质/附魔 |
| 2 | 替换原点 | **`[HarmonyPostfix]` on `ItemVisualsController.SetCardFrameMaterial`**（`ItemVisualsController.cs:189`）——每次卡图加载的唯一落点，池化复用会重触发 |
| 3 | 注入方式 | 在原生新建的 per-card `materialInstance` 上 `SetTexture(CardArtShaderVariables.EncounterBaseMap, tex)` + `mat.mainTexture = tex`（**只改贴图，保留品质/附魔 keyword**） |
| 4 | 身份判定 | 经 `__instance` 反查 `ItemController.CardData`（`HiddenTags` 含 `Package` 且该卡有自制图）；身份缓存见 §5 |
| 5 | 图片来源 | **磁盘**：`<GameRoot>/BazaarPlusPlusV4/CustomCardArt/`（用户可改）；mod 可内嵌一套默认作为 fallback |
| 6 | 映射键 | **`TemplateId` 为主键**（共 **120** 张 = 40 商人 × 3 size）；可按 `InternalName`+`Size` 分组。**运行期不需要能力图 resolver** |
| 7 | 分辨率 | 源贴图 **1024×1024 正方形**；但**每个 size 原生就是一张独立卡图**（见 §3：包裹只有 3 个 ArtKey = 3 个 size 的通用箱子图），故建议**按 size 各做一张**；512² 可接受 |
| 8 | 生命周期 | 同步加载+缓存 → postfix 内立即 `SetTexture`；**无需 child 物体、无需 Cleanup patch、无需 token**（依赖 postfix 每次重绑重触发，§5） |
| 9 | fallback | 非包裹卡 / 无自制图 / 解码失败 → 不替换，保留原生卡图 |
| 10 | 边界 | patch→`Patches/`；原生材质/着色器 poke→`GameInterop/`；catalog+纹理缓存+特性→`Game/`；加架构测试 |

---

## 1 原生卡图加载机制（证据 A）

in-run 卡（`ItemController`）的插画由其子组件 **`ItemVisualsController`** 负责，渲染在单个 `Renderer cardIllustrationRenderer`（`decompiled/.../TheBazaar.Game.CardFrames/ItemVisualsController.cs:30-32`）上，材质是 per-card 实例 `materialInstance`（`:56`）。加载链：

1. `ItemController.Setup(Card)` → `await visualsController.Setup(bazaarCard)`（`ItemController.cs:852-863`）；池化生成走 `BoardManager.SpawnCardInstantly → UpdateData → Setup`（`BoardManager.cs:1315-1318`）。
2. `ItemVisualsController.Setup(Card,…)`（`:255-260`）→ `await GetCardAssetData(tCard)`。
3. `GetCardAssetData(ITCard)`（`:153-182`）读 `cardTemplate.ArtKey`（`:161`）→ `_assetLoader.LoadAssetAsyncByAddress<CardAssetDataSO>(ArtKey)`（`:167`）。
4. `CardAssetDataSO.cardMaterial`（`decompiled/.../CardAssetDataSO.cs:24-26`）——**插画贴图烘焙在这张材质里**，无独立 texture 字段。
5. `Setup(CardAssetDataSO,…)`（`:267-272`）→ `SetCardFrameMaterial(cardAssetData.cardMaterial, isPremium, enchant)`。
6. **`SetCardFrameMaterial`（`:189-217`）**：销毁旧 `materialInstance`（`:193-196`）→ `materialInstance = Object.Instantiate(cardFrameMaterial)`（`:197`）→ `cardIllustrationRenderer.sharedMaterial = materialInstance`（`:198`）→ 应用 premium keyword（`:199-206`）与 enchantment keyword（`:207-210`）。

**贴图着色器槽位**：`_BaseMap`（`CardArtShaderVariables.EncounterBaseMap = Shader.PropertyToID("_BaseMap")`，`CardArtShaderVariables.cs:47`；全局卡/图标/头像走 `_BaseMap`，少数 Standard 走 `_MainTex`/`mainTexture`）。注入自制贴图时**两个都设**最稳：`if (mat.HasProperty(EncounterBaseMap)) mat.SetTexture(EncounterBaseMap, tex); mat.mainTexture = tex;`。

> 关键性质：`materialInstance` 是**每卡 `Instantiate()` 副本**（`:197`），改它**不会污染别的卡**；游戏在 `OnDestroy` 自行销毁它（`:307-318`），故 `SetTexture` 方式我们不持有任何需要释放的对象。

---

## 2 替换原点（证据 A）

**主选：`[HarmonyPostfix] ItemVisualsController.SetCardFrameMaterial`（`:189`）。** 原方法跑完后 `__instance.cardIllustrationRenderer.sharedMaterial` 即新鲜的 per-card `materialInstance`，premium/enchant keyword 已应用；我们只把它的 `_BaseMap` 换成自制贴图。优点：唯一 chokepoint、每次重绑（池化/换品质/换附魔/preview）都重触发、天然每卡隔离、保留品质/附魔效果。代价：方法无 card 入参，需反查身份（§5）。

**备选（不推荐为主）**：
- Postfix `GetCardAssetData`（`:153`，有 `ITCard` 身份、async）替换返回的 `CardAssetDataSO.cardMaterial`——但 SO 是 Addressables 共享对象，**必须 clone 再改**（参 `CollectionCardMaterialCache.cs:51` 的 `new Material(...)`），生命周期更重。
- Setup 后直接 set `VisualsController.cardIllustrationRenderer`——**活不过**游戏自身重绑（`UpdateCard`/`SetCardTierAsset`/池化），除非再挂重绑触发器；淘汰。

---

## 3 范围与分辨率 / 尺寸（证据 B：实测活数据 prod/cache + 1073 张抽取卡图）

**范围（重要修订——不是「40 张」）**：活数据共 **120 张包裹卡 = 40 个商人 × 3 个 size（Small/Medium/Large）**，每个商人每个 size 各一张、各有独立 `TemplateId`。完整 `TemplateId↔商人↔size` 见 §15。

**native 包裹卡图是「通用箱子」，按 size 共 3 张、跨商人共享**：120 张包裹卡只有 **3 个 distinct ArtKey**——
- Small：`b44a99faac3eae3468d3f12dcc284c63`（40 张共用）
- Medium：`b8c002a5eee6a2541925014180a54b63`（40 张共用）
- Large：`47aa57dfa612fa544a087cddf0ebce89`（40 张共用）

即**原生根本不画商人**，只是 3 个 size 的通用箱子——这正是本特性的动机。两个推论：
1. **不能按 ArtKey 做替换键**（只有 3 个，会把所有商人塌成 3 张）→ 必须按 `TemplateId`（或 `InternalName`+`Size`）。
2. 游戏**为每个 size 单独画了一张箱子图**（3 个不同 ArtKey），说明不同 size 的卡框裁切/比例不同到需要分别出图 → **我们的替换也应按 size 各做一张**才像素级正确。

**分辨率**：item 卡插画源贴图普遍 **1024×1024 正方形**（实测 1061/1073 为 1024²；少数离群 1×2048²、几张 512²）。Small/Medium/Large **不**按 socket 数改插画分辨率；可见宽度差异来自 per-size 卡框 mesh 裁切（卡框独立 per-size prefab，`AssetLoader.cs:424-430`）。抽取无缩放（`art.py:188`；`convert_image_to_rgb` 丢 alpha，`art.py:51-54`）。

**创作单位（已定）：为每个商人的每个 size 各画一张** = `(商人 × size)`，与 native「每 size 一张箱子」一致、像素级贴合每个 size 的卡框裁切。
- 规模：**40 商人 × 3 size = 120 张**；**The Tester（§16 第 37 行）是真实商人，也包含在作图范围内**。
- 文件名 `<templateId>.png`（运行期按 `TemplateId` 命中，§4）；全表见 §16。
- **不采用** 的「每商人一张复用到 3 size」精简法：native 本就按 size 分图，单图在三种 size 卡框下裁切不一、观感妥协。
- 每张 1024² 正方形；各 size 的安全区/裁切需 authoring 时对照游戏内实际显示校准。

---

## 4 替换流程

1. **目录与清单**：`<GameRoot>/BazaarPlusPlusV4/CustomCardArt/`（在 `BepInExPathProvider` 加 `CustomCardArtDirectoryPath`，`:18-41` 同款），内含若干 PNG + 可选 `manifest.json`：`[{ "key": "Tinker's Package", "file": "tinkers-package.png" }, …]`；无清单时按文件名约定（`slug(InternalName).png` 或 `<templateId>.png`）。
2. **加载**：磁盘 `File.ReadAllBytes(path)`（prior art `CombatReplayPayloadStore.cs:47`）→ `new Texture2D(2,2,ARGB32,false)` → `tex.LoadImage(bytes,false)` → `tex.Apply(false,false)`（完全照 `BppDockButtonSpriteProvider.cs:57-71`）。
3. **注入**：在 §2 postfix 内，若当前卡是包裹卡且 `key` 命中清单 → 取（缓存的）`Texture2D` → `SetTexture(_BaseMap)` + `mainTexture`。
4. **键映射（运行期，无需能力图 resolver）**：包裹卡 `CardData.TemplateId` → 自制图 file（主键，共 120）。清单可让同一商人的 3 个 size templateId 指向同一文件（→ 做 40 张）或各指一文件（→ 做 120 张，§3 推荐）。`InternalName`+`Size` 可作辅助/人类可读键。**注意不能用 ArtKey 当键**（只有 3 个，§3）。

---

## 5 生命周期 / 池化（比 overlay 方案更简单）

- **池化安全（核心）**：`SetCardFrameMaterial` 每次重绑都新建 `materialInstance` 并重跑我们的 postfix（`:193-198`）。
  - 复用为**非包裹卡** → 身份判定不命中 → 不 `SetTexture` → 新 `materialInstance` 保持原生贴图 → 正确。
  - 复用为**另一张包裹卡** → 命中新 key → 换成新自制图 → 正确，无残留。
  - 故 **无需 child 物体、无需 patch `Cleanup`、无需 token**（与 overlay 方案的 [high] 池化坑根本不同——我们不新增对象，只改游戏每次都会重建的材质）。
- **身份判定**：
  - 主：`__instance` 反查 `ItemController`——优先用一个 **`ConditionalWeakTable<ItemVisualsController, Card>`**，由 `[HarmonyPostfix] ItemVisualsController.Setup(Card,…)`（`:255`，有 `Card`）填充；`SetCardFrameMaterial` postfix 读它。比 `GetComponentInParent<ItemController>()` 更稳（不假设层级），后者作为兜底。
  - 拿到 `Card.Template.InternalName` / `TemplateId` 判 `HiddenTags.Contains(EHiddenTag.Package)` + 清单命中。
- **同步加载消除 async 竞态**：`Texture2D.LoadImage` 与 `File.ReadAllBytes` 都是同步主线程 API；首次遇到某 key 时同步解码并缓存，postfix 内立即 `SetTexture`。首帧可能有一次性微卡——可在 run 开始/进背包时**预热**命中的少数 key 规避。
- **品质/附魔**：postfix 在 keyword 应用之后只改 `_BaseMap`，premium/enchant 视觉保留（§1）。`CardVisualProxy`（板上代理，`CardController.cs:158,440-448`）底层仍是同一 `ItemVisualsController`，需进游戏确认（§13）。

---

## 6 缓存策略

- `Dictionary<string key, Texture2D>` 缓存已解码贴图（key = 命中的 file/InternalName）；最多 ~40 张。
- 显存：1024² ARGB32 ≈ 4MB/张，40 张 ≈ 160MB 上限——**按需加载**（只解码实际出现的 key），必要时 512² 或 LRU 上限（可借 `CollectionCardMaterialLru` 的 LRU 结构思想）。
- 清单/目录扫描结果缓存一次；文件 mtime 变化可选热重载（开发期便利，非必须）。

---

## 7 Fallback

非包裹卡 / 清单无此 key / 文件缺失 / `LoadImage` 失败（销毁半成品 `Texture2D` 如 `BppDockButtonSpriteProvider.cs:66`）→ 不替换，保留原生卡图；失败记 Debug 日志。

---

## 8 边界 / 分层 + 架构测试

- `Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs` — 两个 postfix：`Setup(Card,…)`（填身份表）+ `SetCardFrameMaterial`（注入），经 `BppPatchHost` 取服务。
- `GameInterop/CardArtReplacement/` — 原生着色器/材质 poke 适配（`_BaseMap` 常量、`SetTexture`、反查 `ItemController.CardData`/`ConditionalWeakTable`）。游戏耦合。
- `Game/CardArtReplacement/` — `CustomCardArtCatalog`（清单/键映射）、`CustomCardArtTextureCache`（磁盘→`Texture2D`+缓存）、`CardArtReplacementFeature`（`IBppFeature` + 可选 `ISettingsDockEntry` 开关）。
- `Core/Paths/BepInExPathProvider.cs` — 加 `CustomCardArtDirectoryPath`。
- **架构测试**：`GameInterop.CardArtReplacement` 不引用 `Game.*`（编译器不强制，须显式断言）。

---

## 9 资源创作流水线（我们自己造图）

- **基底**：抽取产物 `bazaarplusplus-extractor/exports/card-assets/prod/cards/<templateId>/RGB_Preview.png`（1024² 原生卡图）+ 同卡 `manifest.json`（带 size/material）。
- **商人头像素材**：商人 encounter 的 `icon.png`/portrait（抽取产物或 `EncounterPortraitSpriteProvider` 同源 ArtKey）。
- **离线合成**（一次性，非运行期）：包裹卡 → 商人映射**已离线算好**（见 §16 全表，源自活数据 `prod/cache/GameData.db` 包裹卡能力图 `TPrerequisiteRun→TRunConditionalCurrentEncounter→TCardConditionalId.Id`，与 overlay 方案同源）。把对应商人头像合成到 **该 size 的** native 箱子图上，按 (商人×size) 导出 **120 张**（40 商人 × 3 size，包含 The Tester）1024² PNG 到 `CustomCardArt/`，文件名 `<templateId>.png`（直接对应 §16）。
  - 可写 `uv`+Pillow 脚本批量合成（repo Python 用 `uv`、不 `from __future__`），输入 = §16 表 + 抽取的 per-size native 箱子图（3 个 ArtKey）+ 商人头像。
- **运行期不依赖** 该映射——只读清单/文件名 `TemplateId→file`。映射只服务**离线作图**。

### 9.1 Phase 0：占位图先行（验证管线，与真实美术解耦）

为了**先验证替换管线的可行性**（不等成品图），用 `bazaarplusplus-extractor/scripts/generate_placeholder_package_art.py`（`cd bazaarplusplus-extractor && uv run python scripts/…`）按 §16 全表自动生成 120 张占位图（40 商人 × 3 size，包含 The Tester），文件名 `<templateId>.png`：
- **`--mode text`**：纯占位——`"<Merchant>'s <Size> Package"` 文字 + 按 size 着色背景。零外部素材，纯验证「templateId 命中 → SetTexture 生效」。
- **`--mode portrait`（推荐起步）**：直接用**真实商人头像**（抽取产物 `exports/card-assets/prod/cards/<merchantEncounterId>/icon.png`，contain-fit 到 1024² + 底部 size 标签）。实测 **120/120 商人头像都能命中**——所以这套占位图**本身就是可用的过渡美术**，先上它即可看到「卖给哪个商人」，真实精修图 ready 后按同名 `<templateId>.png` 覆盖即可。
- 输出落 `<GameRoot>/BazaarPlusPlusV4/CustomCardArt/`（runtime 读取处）。商人头像源自每张包裹卡能力图里嵌入的 merchant encounter GUID（脚本内联 §2 解析逻辑，自动跟版）。

**流程**：实现 runtime 替换（§11）→ 跑脚本生成占位图 → 进游戏验证替换/池化（§12）→ 真实图 ready 后覆盖同名文件。占位与成品**键完全一致**（`<templateId>.png`），切换零代码改动。

---

## 10 方案对比：直接替换卡图 vs 叠加 overlay 头像

| 维度 | 本方案（替换卡图） | overlay 方案（姊妹文档） |
|---|---|---|
| 观感 | 完全原生融入卡面，无叠加痕迹 | 卡角一个头像角标 |
| 美术工作量 | **需自制 ~40 张图** | 0（复用游戏商人头像） |
| 信息保留 | 替换整张插画（除非合成时保留原图元素） | 原卡图保留，仅加角标 |
| 入侵性 | hook 卡图管线 `SetCardFrameMaterial` | hook `ShowCard`/`Cleanup` + 世界空间子物体 |
| 生命周期复杂度 | **低**（不加对象，靠重绑重触发；无 token/Cleanup patch） | 较高（child 物体 + 池化守卫 + token，见姊妹文档 [high] 修订） |
| 显存 | +最多 ~40×(1024²) 贴图 | 复用已缓存头像，几乎为 0 |
| 跟版 | 自制图需随新包裹/新商人补图 | 自动适配活数据 |

**何时选本方案**：要「卡面级」原生观感、愿意维护一套自制图。**何时选 overlay**：要零美术、自动跟版、改动更轻。

---

## 11 实施步骤（按文件）

1. `Core/Paths/BepInExPathProvider.cs` — 加 `CustomCardArtDirectoryPath = GameRootPath/BazaarPlusPlusV4/CustomCardArt`。
2. `Game/CardArtReplacement/CustomCardArtCatalog.cs` — 扫描目录 + 读 `manifest.json` + `InternalName/TemplateId → file` 映射（缺省按文件名约定）。
3. `Game/CardArtReplacement/CustomCardArtTextureCache.cs` — `File.ReadAllBytes → Texture2D.LoadImage → Apply`，按 key 缓存；失败兜底。
4. `GameInterop/CardArtReplacement/CardArtInjector.cs` — `Apply(ItemVisualsController vis, Texture2D tex)`：取 `cardIllustrationRenderer.sharedMaterial`（反射/Publicizer 私有字段），`SetTexture(_BaseMap)`+`mainTexture`；以及 `ConditionalWeakTable` 身份表辅助。
5. `Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs` — postfix `Setup(Card,…)`（存身份）+ `SetCardFrameMaterial`（判包裹+命中→注入）。
6. `Game/CardArtReplacement/CardArtReplacementFeature.cs`（`IBppFeature`）+ `BppComposition.cs` 注册；可选设置开关。
7. `tests/CardArtReplacement.Tests/`（xunit）— catalog 键映射/possessive、cache 命中、身份判定纯逻辑；`Architecture.Tests` 断言 `GameInterop.CardArtReplacement ↛ Game.*`。
8. （离线）`tools/`（或 extractor 侧）合成脚本 + 一套默认 `CustomCardArt/` PNG（可内嵌少量作 fallback）。

---

## 12 验证

- **Build**：`./run.sh build`（worktree 加 `-p:BPPInstallerSourcePath=<abs>`）。
- **Test**：`dotnet test tests/CardArtReplacement.Tests/…csproj` + `Architecture.Tests`（`./run.sh test` 时 grep `Failed test projects:`）。
- **探针先行**：在 `SetCardFrameMaterial` postfix 加临时 Debug 日志，dump 命中包裹卡的 `InternalName`/是否找到自制图/是否成功 `SetTexture`，进游戏验证后移除。
- **进游戏**（`open "steam://run/1617400"`）：
  - 背包/Stash 放一张包裹卡（如 `Tinker's Package`）→ 卡面插画被自制图替换。
  - **池化/复用专项**：卖掉/销毁该卡，其 `ItemController` 复用为**非包裹卡**→ 卡图恢复原生；复用为**另一包裹卡**→ 显示对应自制图，无残留/串卡。
  - 换品质（tier）、附魔、premium → 替换保留且品质/附魔视觉正常；hover/preview/拖拽正常；非包裹卡全程不受影响。
  - `CardVisualProxy` 板上代理路径确认（§13）。
- **日志**：`<GameDir>/BepInEx/LogOutput.log` 过滤 `[BPP][CardArtReplacement]`。

---

## 13 风险 / 开放问题

1. **`SetCardFrameMaterial` 无 card 入参** → 身份靠 `ConditionalWeakTable`(Setup 填) + `GetComponentInParent` 兜底；须进游戏确认两者至少一条稳。
2. **`CardVisualProxy`/`ArtLayerRoot` 代理**（`CardController.cs:158,440-448`）：板上可能走代理渲染，需确认替换在代理路径也生效（agent 称底层同 `ItemVisualsController`，待实测）。
3. **size 安全区**：同一 1024² 贴图在不同 size 卡框下裁切不同，自制图关键内容须落在该卡 size 的可见区——authoring 需对照实际显示校准（§3）。
4. **显存**：最多 ~40×4MB；按需加载 + 可选 512²/LRU（§6）。
5. **首次解码微卡**：同步 `LoadImage` 可能掉一帧 → 进背包/run 开始预热命中 key。
6. **活 DLL 验证**：`ItemVisualsController`/`CardAssetDataSO`/`_BaseMap` 取自 v5.0.0 期 decompiled，卡数据更新（2889>2709）；实现后进游戏确认管线一致（探针）。
7. **跟版维护**：新包裹/新商人需补自制图；运行期无自制图则 fallback 原生（不报错）。
8. **与第三方 mod 共存**：若其它 mod 也 hook `SetCardFrameMaterial`，postfix 顺序无关（我们最后 SetTexture），但若对方换整材质需注意；记录即可。

---

## 14 Goal（可直接粘进 `/goal`）

> 在 `/Users/yxinyu/codes/bpp/bazaarplusplus-mod` 实现「包裹卡商人头像 — 直接替换卡图」特性。**先通读本文档（`docs/design/2026-06-08-package-merchant-card-art-replacement-plan.md`）再动手**，以代码/活数据为准。
>
> **做什么**：对 `HiddenTags` 含 `EHiddenTag.Package` 的卡（名为 `"<Merchant>'s Package"`），把其卡面插画贴图替换为磁盘上对应的自制 PNG（按 `TemplateId` 命中）。
>
> **Phase 0（先做，验证管线）**：先跑 `bazaarplusplus-extractor/scripts/generate_placeholder_package_art.py --mode portrait`（已存在）生成 120 张 `<templateId>.png` 占位图（真实商人头像，可作过渡美术），落 `<GameRoot>/BazaarPlusPlusV4/CustomCardArt/`，用它把 runtime 替换跑通；真实精修图 ready 后按同名覆盖、零代码改动。
>
> **替换原点**：`[HarmonyPostfix] ItemVisualsController.SetCardFrameMaterial`（`ItemVisualsController.cs:189`）——原方法后，对 `__instance.cardIllustrationRenderer.sharedMaterial` 做 `SetTexture(CardArtShaderVariables.EncounterBaseMap, tex)` + `mat.mainTexture = tex`（只改插画，保留品质/附魔 keyword）。身份用 `ConditionalWeakTable<ItemVisualsController,Card>`（由 `Setup(Card,…)` postfix 填）+ `GetComponentInParent<ItemController>` 兜底；判 `CardData.Template` 是包裹卡且清单命中。
>
> **图片**：磁盘 `<GameRoot>/BazaarPlusPlusV4/CustomCardArt/`（`BepInExPathProvider` 加属性），`File.ReadAllBytes → Texture2D.LoadImage → Apply`（照 `BppDockButtonSpriteProvider.cs:57-71`），按 key 缓存。**键 = `TemplateId`**（文件名 `<templateId>.png`）。范围 = **每个商人的每个 size 各一张**，共 40 商人 × 3 size = 120（包含真实商人 The Tester），全表见 §16。**分辨率 1024×1024 正方形**（可 512²）。**不要用 ArtKey 当键**（只有 3 个，会塌掉所有商人）。
>
> **不可破约束**：① 只改 per-card `materialInstance` 的贴图（它是 `Instantiate` 副本，§1），**不要**新增 child 物体、不要 patch `Cleanup`、不要持有需释放的材质——靠 `SetCardFrameMaterial` 每次重绑重触发实现池化安全；② `GameInterop.CardArtReplacement` 零 `Game.*` 依赖 + 架构测试守住；③ 只新增本特性文件，不动无关改动；④ 实现前在 postfix 加临时 Debug 探针 dump 命中情况，进游戏验证后移除。
>
> **交付**：`Core/Paths/BepInExPathProvider`(+属性)、`Game/CardArtReplacement/{CustomCardArtCatalog, CustomCardArtTextureCache, CardArtReplacementFeature}`、`GameInterop/CardArtReplacement/CardArtInjector`、`Patches/CardArtReplacement/ItemVisualsArtReplacePatch`、`BppComposition` 注册、`tests/CardArtReplacement.Tests/` + 扩展 `Architecture.Tests`。
>
> **验证**：`./run.sh build`；`dotnet test`；进游戏（`open "steam://run/1617400"`）重点验**卖掉/销毁→复用**（非包裹卡恢复原生、另一包裹卡换对应图、无串卡）、换品质/附魔/premium 保留、非包裹卡不受影响；日志过滤 `[BPP][CardArtReplacement]`。
>
> **收尾**：自检 diff → commit（含 `Co-Authored-By`）→ 合并到 `master` → push → 删已合并分支（未经要求不提前 commit）。落地后把本文档 Status 改 `IMPLEMENTED`。
>
> **注意数据漂移**：当前卡数据在 `~/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache/GameData.db`（2889 卡，含 Package），非随包 StreamingAssets（陈旧 2709、无 Package）。

---

## 15 附录：关键锚点

- 原生卡图：`decompiled/.../TheBazaar.Game.CardFrames/ItemVisualsController.cs:30-32`(renderer)、`:56,197-198`(materialInstance/Instantiate/sharedMaterial)、`:153-182`(GetCardAssetData/ArtKey)、`:189-217`(SetCardFrameMaterial)、`:255-280`(Setup)、`:307-318`(OnDestroy)。`CardAssetDataSO.cs:24-26`(cardMaterial)。`CardArtShaderVariables.cs:47`(_BaseMap)。`ItemController.cs:159`(VisualsController)、`:852-863`(Setup)。
- 换材质 prior art：`src/.../Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:18,102-104`、`Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs:43-57`（结构可借，目标是 UGUI `RawImage.material`，须改写为世界空间 `Renderer.sharedMaterial`）。
- 磁盘图→纹理：`src/.../Game/Settings/BppDockButtonSpriteProvider.cs:57-71`；磁盘读 `CombatReplayPayloadStore.cs:47`；数据目录 `Core/Paths/BepInExPathProvider.cs:18-41`。
- 分辨率实测：`bazaarplusplus-extractor/exports/card-assets/prod/cards/`（1073 张 RGB_Preview.png，1024² 主导）；`art.py:188,51-54`。
- 包裹→商人映射（仅离线作图用）：见 [`2026-06-08-package-merchant-portrait-plan.md`](2026-06-08-package-merchant-portrait-plan.md) §1–§2。

---

## 16 附录：120 张包裹卡 TemplateId 全表（作图工作清单）

> **决定**：为**每个商人的每个 size 各画一张**（见 §3）。共 **40 商人 × 3 size = 120 张**；**The Tester（#37）是真实商人，包含在作图范围内**。文件名建议 `<templateId>.png`（运行期按 `TemplateId` 命中）。
>
> native 通用箱子 ArtKey（跨商人共享、按 size）：Small `b44a99faac3eae3468d3f12dcc284c63` / Medium `b8c002a5eee6a2541925014180a54b63` / Large `47aa57dfa612fa544a087cddf0ebce89`——证明原生不画商人，须按 (商人×size) 替换。

| # | 商人 | Small templateId | Medium templateId | Large templateId |
|---|---|---|---|---|
| 1 | Aero | 419fe627-2d31-422c-848a-b944cf51e73a | 19f99c8c-0984-4024-a350-43665eff2380 | 1c81f030-2e5a-4d8e-85f8-96e737820d13 |
| 2 | Aila | f1c758e8-2cb2-400b-a6f2-1481e8c4e668 | a67f4c3e-4aa4-42d5-875f-5c91601e7542 | b3af2d13-ca7a-4103-81fc-7924550ecbdb |
| 3 | Aimbot | 9a6b4409-4908-49b9-bf17-8be3367383a5 | 44968cbb-7aae-490e-8448-16f52b82b69b | 1d445827-dc17-4d1f-bfd0-826d272abb3d |
| 4 | Ande | 8ed36031-d55f-4652-a531-cc9d789cecea | bb7b7d25-cd17-4a2c-9edc-805924034a71 | 5292bd55-65b0-4595-9948-0cba7587ca76 |
| 5 | Barkun | fb7fa9a1-4da4-43f7-8039-9f6d54a6d3f8 | fd81f825-b793-4e39-9af5-d924625728db | ead7d3e3-eff3-42a9-ab34-841925e0f5c1 |
| 6 | Chronos | f2dddc77-bff0-4f51-8aec-03d4b8edc8b8 | 81ece1ea-a6e6-4cad-995b-75cfdab68a91 | 534cc610-0b42-4514-a4db-7589838f470f |
| 7 | Cobweb | a89314f0-b7b4-4458-aa46-61dec4f073a6 | e6369268-6e80-45b7-9598-f013dd50488b | e12f8d7a-2493-4017-95e2-f1f5056747c7 |
| 8 | Colt | 69d98ff6-b41a-4b74-8199-22fdee776189 | a40db083-a189-4702-9a2d-bef8bef8a234 | 3e85ab96-e938-4868-94a2-45f6bc39bb5d |
| 9 | Curio | 83b50d6c-f595-4def-b790-eea85bbb5ea2 | 7946d100-8889-4878-8706-bd973fced075 | f38fd872-2ecb-4a20-b752-a534ff40d068 |
| 10 | Eli | 4f74adc0-85d6-47f5-92bc-6a9c609507d3 | f40cb217-3dea-44d1-8572-6fff1113d362 | 42bca630-3463-4638-b613-1e86ea02b1db |
| 11 | Flex | 9b02b586-7f07-4c04-a88d-7e4eabdf89b0 | 79e80407-4cfa-4a6b-a686-e12b82714c02 | ad089351-8d28-49f7-a3b6-f9fd84928459 |
| 12 | Freiya | 019f50b8-2d76-493e-ae62-fae6875efbc7 | eca94fe9-051f-4191-aa2b-9ea63ec83126 | d2a4749c-149c-40c3-b14d-2ed82edb57a1 |
| 13 | Gaseo | b0170fe8-47ba-4722-9eb5-210fb7218b7d | 4ca3b3b6-3769-4ed8-84b3-4a13225e0b5c | 647af800-cece-4177-b506-01ae0ff4ccc3 |
| 14 | Gastro | 485ed276-55e3-47c2-b959-0a8bc64c77ac | 3d615d09-3266-44aa-9e90-2878d6b7fb4a | 4d15e26b-8fc5-4b73-925e-418a86b16a3e |
| 15 | Goldie | 125a0f25-7d8b-4a13-ab1c-b13c53af6108 | 66d8fa96-1e00-4884-b687-cda19e947f87 | 96fe93d4-65a9-421e-a659-f59e8d193f43 |
| 16 | Hef | 611bfd99-70b4-4799-b85b-b26adfcd8cf5 | 97fcc902-4834-4251-a510-9126ef550036 | cce3026f-949f-4500-8044-0888dee71a23 |
| 17 | Herma | 274936a6-0915-4d89-b66a-0817d99993da | 77b766d2-27c8-456f-aa4c-07716adef205 | 314f0b49-4710-46b2-9b1f-7e09c4d67aab |
| 18 | Jay Jay | 4ddb3806-a5ee-4cfd-985f-19f184580ade | e38928f3-33d1-4b92-bdba-2b3c8508bd7c | 7b748cbf-1191-4672-8abc-cc55c7d1721b |
| 19 | Kev's Armory | a65b8ad9-9096-47c8-96cb-b60b0c8bc735 | 19f790c3-f054-4f79-a6ca-63700fff6c57 | 4cdb5613-6011-4cfc-9836-2ed3f15f19cf |
| 20 | Kina | 13dc6c6f-99ca-46fa-9434-36ad9d1dc8e0 | bca5953f-e68e-4663-9b14-88a9804c282b | 55e4cc7e-b985-491c-a476-3e5055adb442 |
| 21 | Knightshade | d0567331-7166-41a1-82d9-d6a6ccd9ed13 | eaafb4c0-10a9-4b9a-ad84-8544f28863b2 | 676cff2f-cbcc-468d-85bd-14d919228bda |
| 22 | Luxe | 26c08f9b-5140-478e-9d4c-44d59edd5070 | 4d423a8a-2cfa-49e8-87b8-efddf3966a8e | 77b7d220-a061-417e-a222-3445f46740e7 |
| 23 | Midsworth | f6006ab7-1adb-4840-8bdb-0301eba72c9b | 04780027-1949-4517-84a2-f833819d5d93 | c6942421-a0ae-4a02-9d6b-01cd4be3c42a |
| 24 | Mittel | 029ed0ac-823c-42f9-ad2c-d851182629af | f238d9da-c5e3-49c3-a184-5c70c11aa09e | 90616780-1c80-4465-afb6-84cc665b367e |
| 25 | Mr. Morland | 263b629e-1852-4ea5-9312-a2defcc5c00d | 8ad5c56c-730f-4215-acd8-ceea24cdec2b | 5e74250a-99dc-4025-a624-3a593528be3a |
| 26 | Nautica | 6ae4a6dc-414e-4324-bba6-a6e4acf7103e | 21aff4c7-a0e9-4d9b-9ce1-0a5b95b2109c | dbbc7e62-ea73-4739-8765-dc73541e99cf |
| 27 | Orion | ef0af597-d8eb-4f06-bd21-8b051d919110 | 4f61332a-a64b-4c0e-b047-543d9db7a2e2 | bbe0376e-40fb-4844-9b69-b7212e0ad834 |
| 28 | Pinfeather | 6e16c671-71bd-428e-ada5-99ed0700a0f3 | 8f9e1b9e-3e19-41fd-a739-59cb736ae22b | 08a851a2-b44c-4aec-8aaa-5c5cd8cd0028 |
| 29 | Pol | 103282bc-65d8-4888-9ccb-d106c1b1d5a7 | ecc98e05-2c05-4fb7-bb4b-0c76c5d2a0d7 | 3ff2aeaf-db8c-4797-8815-6a9f444439df |
| 30 | Prospero | b48f00bd-675e-409d-90f5-556cd745da49 | 68086459-1a17-4753-8722-1ac8b4b33ed4 | ab08b795-7509-4691-a0ba-831d4c8ca3e6 |
| 31 | Quixel | 852aa5af-7bfb-4700-8ad9-307e554b6948 | 7b8c8cd6-4080-44eb-b5e5-3dd697c74a54 | dce7df0b-a653-4894-91ef-9c5af9607364 |
| 32 | Serafina | 1ff92caf-3790-4f8b-9dce-6120b984bd30 | 544643e1-7465-4520-9149-dbd87f13a7c1 | 4c193c8e-e123-48c8-ad1f-2cde7c6277b4 |
| 33 | Shelter Shelby | d222ae62-b81a-4fb0-a423-40c045cbb898 | dbb42f54-4f69-4236-8129-15da7bc693c0 | ff0270a0-52d0-46c1-8029-f799e1bd3d02 |
| 34 | Silvia | 21b570fc-b5c9-494c-8f9f-52aed6bcd11a | 43dd428f-ee17-492a-aff1-0b2d6492cf27 | 2a3cc868-07fb-4d64-b5ae-3d14755b0855 |
| 35 | Tatiana | 8ffb584b-7d91-402b-aeeb-4dbe38d92c78 | 5049bcca-cf8f-4cc5-9e0d-faea06d6fcc6 | 81c9a871-6010-4cc9-994e-ac554e08c2ad |
| 36 | The Antiquarian | f44bdeec-6c20-49fd-ac42-2d54879053ec | 5a45e8c9-b948-490c-a042-31809704a561 | 3d9f6f13-ffef-4b5e-8215-c067298d7f44 |
| 37 | The Tester | aff387d9-a2b8-4210-b1fd-3ff19bc05d10 | 8bde0419-323a-4c43-a634-2feac448c3c3 | 700dd052-eb06-405b-99a5-3dea1a9417a1 |
| 38 | Tinker | 00c67a4d-eea4-4297-b797-3071893c2a1c | 0c2ffb12-9202-4f0e-95db-d5dc7fc93706 | b999d37f-b2a0-4cf9-a6e0-fe02419bee58 |
| 39 | Tok's Clocks | dc22510b-5577-422d-bcde-b23572c16f5a | 5db2e1cc-2d4b-40d7-9d05-bb3ec99ab67a | 14f3b0a2-cfc1-449b-8d29-0d936b3f7b3f |
| 40 | Valpak | 6ac5f8e2-1c03-4dda-becf-4836e56b31ec | 9fd5de2a-7546-4788-a4ba-4b08a0149243 | 53f0819a-82e8-406e-8d04-e1463d5ea380 |

> 复现/重生成（活数据可能跟版变动）：见 §2 复现脚本，把筛选改为 `'Package' in HiddenTags`，按 `merchant_of()` 取商人、`Size` 取尺寸。
