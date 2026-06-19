# GOAL — 自制卡面基座 + 成就 MVP（本地一期，可视化验收）

> 这是给**新 Agent** 的独立执行简报。你没有此前对话的记忆——本文件 + 它指向的 spec 就是你需要的全部。
> 你**可以也应当使用 ComputerUse 截图去"看图"验收**游戏内渲染（详见 §5）。

---

## 1. 任务一句话

在 BazaarPlusPlus mod 里建一个**可复用的"在原生 CollectionPanel 网格渲染一张游戏数据里不存在的 BPP 自有卡"的基座**
（合成 `TCardItem` + 自定义美术 + 原生 tooltip），然后上线第四个"**成就 / Achievements**" tab，渲染**一张**本地化成就卡
（cosmic_ray）：占位美术 + tier 框 + hover 出本地化名字与描述 + 品质筛选可用。**纯本地，不碰 server / analyzers。**

**完成标准（Definition of Done）**：游戏内第四个"成就" tab 可见可切；网格出 cosmic_ray（美术 + tier 框）；hover 出
原生 tooltip 显示"宇宙射线 / Cosmic Ray"+ 描述（无乱码/tofu）；tier 筛选生效；物品/包裹/技能三 tab 行为不变；
日志无 "Template lookup failed" 刷屏；build 绿、单测过；合并到 master。A4（解锁徽章）与 server/analyzers 明确**不做**。

---

## 2. 权威 spec（先读，再动手）

1. **`docs/drafts/2026-06-19-custom-card-foundation-and-achievements-plan.md`** —— 完整方案，是 seam 设计、`file:line`
   证据、PR 切分、验证矩阵（§9）、**陷阱清单（§11，强制逐条核对）** 的唯一事实源。**完整读完再开工。**
2. 仓库规则：根 `CLAUDE.md` 与 `bazaarplusplus-mod/CLAUDE.md`（构建/测试/日志/架构分层/收尾规则）。
3. 引用文档（catalog schema、规则分层 A/B/C）：`docs/plans/achievement-service-design.md`、`achievement-ui-local-mvp.md`。
4. **code is source of truth**：一切结论以现仓库源码 + `decompiled/`（只读参考）为准；spec 里的 `file:line` 若与 HEAD 不符，以代码为准并记录漂移。

---

## 3. 必须知道的关键前提（别重新踩坑）

- C# / .NET / BepInEx5 / Harmony。构建 `./run.sh build`（Debug 自动拷进游戏 `BepInEx/plugins/`）。`decompiled/` **只读，勿改**。
- 难点 = 渲染一张 GUID 不在游戏静态数据里的卡。已验证 A 路可行（合成 `TCardItem` 喂原生 `SetUp`）。但**有两个只能真机定的运行时未知**：
  - **F2 闸**：合成模板在真机 `SetUp` 是否 NRE（反编译证据指向"能"）。
  - **F3 闸**：用 `_cardMaterialShader` 现建的 material 在"只有 frame 的合成卡"上是否**正确合成卡脸**（裸 `RawImage.texture` 会渲成未遮罩/错色方块）。
  - F2 失败 → 转 **B 路**（BPP 自绘 UITK tile）；F1/A2 + A3 的 tab/筛选骨架不变，只重写渲染段。
- **名字/描述复用游戏原生 tooltip**（已验证经内联 `.Text` 回退显示），**不要**做网格常驻名字浮层。
- **tier 筛选复用原生 `CollectionFilterEngine`**（把成就 VM 喂进引擎、hero/day suppress），**不要**硬 bypass。
- spec §11 陷阱清单是强制项：违反任意一条会造成回归或每帧 SQLite/Warn 风暴。

---

## 4. 执行顺序（每个 PR 独立分支；build + 验证通过再进下一个）

```
F1 基座结构（纯新增，单测）
 └ F2 接渲染 + virtualizer 加固   ══ A 路 spike 闸（真机看图：合成卡渲出？）══
     ├ PASS → F3 美术 shader-material   ══ 美术真机闸（看图：卡脸合成对？）══
     │          → A1 tab 重构（破坏式，BppVersion 4.3.0→5.0.0，同 PR 改 3 个测试 csproj）
     │          → A2 成就 catalog + 描述符映射 + 注册（cosmic_ray 一张 + 其 jpg）
     │          → A3 成就 tab + 原生 tooltip + tier 筛选   ← MVP 收口
     └ FAIL → B 路（UITK tile）：只重写 F2 渲染分支 + F3 美术段；F1/A2/A3 骨架不变。
```

每个 PR 的精确改动点、`keyFiles`、验证步在 spec 的 §5（基座 F1-F3）/§6（成就 A1-A3）。A1 可与 F3 并行；A3 依赖二者。

**相对体量估计**（S/M/L 是相对大小，非工时承诺；真机闸的耗时取决于游戏加载与看图迭代）：

| PR | 体量 | 主要工作量来源 |
|---|---|---|
| F1 | **S** | 3 个新文件（描述符 / registry / 工厂）+ BppComposition 接线 + 1 个单测项目；纯结构、零行为变化 |
| F2 | **S–M** | 1 个 `TryBind` 分支 + virtualizer 三态 memoize；**时间主要花在真机 A 路 spike 闸** |
| F3 | **M** | gate 分支 + `ApplyAfterLoad` shader-material 注入 + 1 张 jpg；**shader/material 合成是风险点（真机闸）** |
| A1 | **M–L** | 破坏式 tab 重构：分支点多 + 3 个测试 csproj 改 Compile-Include + 升主版本；机械但面广 |
| A2 | **S–M** | JSON + catalog 加载器 + 描述符映射 + 2 个单测 + 1 张 jpg |
| A3 | **M** | 第四 tab 按钮 + RefreshView/ApplyFilters 分支 + `ForAchievements` + `SetAchievementsTab` 清状态 + CJK 预栅格 + 真机看图验收 |

**验证比例原则**：F1 改的是纯结构 → 单测 + build。A1 → 跑三个 exe-runner 测试项目（`dotnet run --project`，**不是** `dotnet test`），
grep 输出 "Failed test projects:"。F2/F3/A3 → **必须真机看图验收**（§5）。不要把 `dotnet build -t:BuildAll` 当冒烟测试。

---

## 5. 真机可视化验收（ComputerUse —— 本任务的核心验收手段）

F2 / F3 / A3 的判定**必须靠截图看图**，不能只读日志。流程：

1. **构建部署**：`./run.sh build`（Debug 自动拷进游戏 `BepInEx/plugins/`）。注意构建输出里打印的拷贝目标路径——
   `BepInEx/LogOutput.log` 就是那个 `plugins/` 文件夹的同级 sibling，验收时读它。
2. **启动（仅经 Steam）**：macOS `open "steam://run/1617400"`。**绝不**直接启 `TheBazaar.app`、**绝不**用 `run_bepinex.sh`
   （会绕过 Steam 运行时、导致诡异失败；见 mod `CLAUDE.md`）。
3. **拿 ComputerUse 权限**：对所需 app 调 `request_access`（"The Bazaar"、必要时 "Steam"）。游戏是**原生桌面 app → full 层**，
   截图 + 鼠标点击 + 移动都可用（不像浏览器/终端被限权）。
4. **截图确认状态**：游戏到主菜单 / run 后 `screenshot`，确认已就位。
5. **进 BPP 收藏面板**：截图找到 BPP 收藏面板入口（mod 的 collection-panel dock 按钮），点开；点第四个"**成就 / Achievements**" tab。
6. **逐项看图核对**：
   - **F2 闸**：网格里有卡格、带 **tier 边框**（此阶段空脸/无美术正常），无报错浮层、无满屏异常。
   - **F3 闸**：卡脸显示 **cosmic_ray 美术**，是正常合成的卡面，**不是**未遮罩的方块或错色块。
   - **A3 tooltip**：`mouse_move` 把鼠标移到卡上，截图 tooltip——确认显示 **名字（宇宙射线 / Cosmic Ray）+ 描述**，无 tofu/乱码、无异常。
   - **tier 筛选**：点一个 tier 品质 chip，截图确认卡按品质显示/隐藏正确。
   - **回归**：切到 物品 / 包裹 / 技能 tab，各截图，确认与改前一致。
   - **语言**（可选）：切换游戏语言后重开/重滚成就 tab，hover 看 tooltip 文本 zh↔en 翻转。
7. **配合读日志**做非视觉断言：读 `BepInEx/LogOutput.log`，grep `[BPP]`；确认**无** "Template lookup failed" 刷屏；
   一个故意不可解析的 GUID 只产生**一条** Warn（virtualizer 失败 bind 已 memoize）。
8. **自愈（仓库规则）**：游戏崩溃/退出就经 Steam 重启再继续，不要第一次失败就停。

> 截图取证习惯：每个闸通过/失败都留一张截图说明结论；失败时连同 `LogOutput.log` 相关行一起记录，再决定 A/B。

---

## 6. A/B 决策（F2 闸结果）

- **PASS**（合成卡渲出）：继续 F3 → A1 → A2 → A3。
- **FAIL**（`SetUp` 对合成模板 NRE——日志 + 截图取证）：**停 A 路、转 B 路**（成就格用 BPP 自绘 UITK tile）；
  F1 描述符/registry、A2 catalog→描述符映射、A3 的 tab/筛选骨架**不变**，只把 F2 渲染分支 + F3 美术段改写为 UITK tile renderer。
  切换前先把 NRE 证据写进 spec 的"验证矩阵/回退"段。

---

## 7. 护栏（仓库规则，务必遵守）

- A1 是破坏式重构：`Directory.Build.props` `BppVersion 4.3.0 → 5.0.0`；**同一 PR**把新增的 `CollectionTabKind.cs` 加进三个
  被 pin 的测试 csproj 的 `<Compile Include>`（`CollectionFilterEngine.Tests` / `CollectionGridLayout.Tests` / `CollectionSourceFiltering.Tests`），否则三测试项目静默编译失败。
- 不改 `decompiled/`；不动 `BazaarPlusPlus.csproj` 的 build-and-copy 流程；不内联改 `.rules` / `CLAUDE.md`。
- 只动每个 PR 命名的目标，不顺手扩大范围；若 csharpier 重排了改动范围外的文件，单独提交。
- **收尾**（每个 PR 完成且验证通过后，按仓库规则）：自查 diff → commit → 把工作分支合并到 `master` → push → 删除已合并分支。
  **验证之前不要 commit；未被要求不要 commit。**
- 若发现非显然的可复用模式，写进收尾小结的 "Suggested rule additions" 标题下，**不要**在常规工作里内联改规则。

---

## 8. 开工前自检 + 起步动作

**开工前自检清单**（逐条确认 HEAD 上这些锚点仍成立；若与代码不符，**以代码为准**并在 spec 记一笔漂移）：

- [ ] `CollectionCardFactory.cs:45` —— `TryBind` 里 `GetCardTemplate` 调用点（F2 前置分支插入处）
- [ ] `CollectionGridVirtualizer.cs:341 / :561` —— `TryRealize` 失败 bind 早退 / `BumpGeneration`（F2 memoize 落点与清空点）
- [ ] `PackageCardArtPatchGate.cs:43` —— `baseMaterial==null` 早退（F3 必须绕过它的原因）
- [ ] `CardPreviewItemArtReplacePatch.cs:36` —— `ApplyAfterLoad`（F3 shader-material 注入处）
- [ ] `CardPreviewItem.cs:16` —— `_cardMaterialShader`（F3 现建 material 用）
- [ ] `CollectionFilterEngine.cs:54` —— tier 筛选 `!filter.Tiers.Contains(...)` 未被 profile 门控（A3 tier 免费可用的根据）
- [ ] `CollectionFilterContext.cs:11,16` —— `ApplyHeroFilter` / `SuppressDayGate` 两个 init 缝（A3 suppress hero/day）
- [ ] `CollectionPanel.cs:842` —— `RefreshView` 的 `For(_filter.ActiveType)`（A3 BLOCKER：必须按 tab 判别式分支选 `ForAchievements()`）
- [ ] `CollectionTabProfile.cs:28,30,38` —— `ShowHeroFilter/ShowTierFilter/ShowDayFilter` 现为 `=> true`（A1 提升为 ctor 可设）
- [ ] `Directory.Build.props` BppVersion = `4.3.0`（A1 升 `5.0.0`）
- [ ] 三个 exe-runner 测试 csproj 的 `<Compile Include>` 列表（A1 同 PR 加 `CollectionTabKind.cs`）
- [ ] `LocalizedTextSet.cs:11`（4-arg ctor）/ `L.cs:23`（`Resolve`）/ `BppUiFont.cs:21`（预栅格）—— 基座/A2/A3 用到的签名

**起步动作**：

1. 读 spec（§2）+ 仓库规则，跑一遍上面的开工前自检。
2. 从 **F1** 开建（纯结构 + 单测，零行为变化、零风险）：`GameInterop/CustomCards/` 三件套 + BppComposition 接线 + `tests/BppCustomCard.Tests`。
3. 进 **F2**：接 `CollectionCardFactory.TryBind` 前置分支 + virtualizer 失败 bind memoize（**只记 hard-miss**），临时注册一张一次性描述符，
   按 §5 **真机看图**跑 A 路 spike 闸，据结果定 A/B。

GUID / 首卡 artifact（cosmic_ray = `5351d91d-2b5c-5f44-8349-bbf334a9bbc5`，命名空间 `fe4ad371-2efd-5e90-a077-eb8e8e8f51e7`）
与完整 cosmic_ray JSON 见 spec 的 §4 与 §8。
