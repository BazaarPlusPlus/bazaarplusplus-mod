# 审计执行 Prompt：fallback 残留 / 分层 / 性能 / 文档漂移 + 完整优化方案

> **Status: DONE — 历史归档(spent executor prompt)。** 产出审计已写入 [docs/audits/2026-06-05-codebase-health-audit.md](../../../audits/2026-06-05-codebase-health-audit.md)。保留为历史记录,勿据此重新执行。

> 读者是一个**对此前分析一无所知的全新 agent**。本文件自包含，不依赖任何对话上下文。
> 工作目录：`bazaarplusplus-mod/`（一个独立 git 仓库；不要在父目录 `bpp/` 跑仓库级命令）。
> 这是一次**只读审计 + 方案**任务：你输出一篇审计文档和一份可确认的优化方案，**不写任何代码补丁**。

---

## 0. 任务与产出

最近这个仓库经历了多次子系统迁移与一次大的物理重构（6 个程序集收进 `src/<AssemblyName>/`，见 `git log`）。每次迁移都可能留下：旧实现的 fallback 分支、死代码、对不上的分层、退化的性能路径、以及落后于代码的文档。本任务是把这些系统性地挖出来，并给出一份**完整的、按优先级排序的优化方案**。

**唯一产出**：一篇审计文档，写到 `docs/audits/<YYYY-MM-DD>-codebase-health-audit.md`（`<YYYY-MM-DD>` 用当前 UTC 日期；目录已存在，见 `docs/audits/2026-06-04-doc-code-drift-audit.md` 的先例）。

**严格只读**：不修改任何 `.cs` / `.csproj` / `.md` / 配置；不顺手修 bug；不删死代码；不动 `decompiled/`。所有结论以"发现 + 证据 + 建议"的形式写进审计文档，实现留到方案被人类确认之后由另一个 agent 执行。

需要覆盖的 6 个维度：

1. **Fallback / 旧路径残留** —— 子系统迁移后没删干净的旧实现、双实现并存、死开关。
2. **不合理逻辑 / 代码坏味** —— 吞异常、不可达分支、死 DTO/字段、复制粘贴分叉、脆弱反射。
3. **分层违规** —— 违反 `CLAUDE.md` 里写明的 `Core/` / `GameInterop/` / `Game/` / `Patches/` 层边界与"纯程序集零游戏引用"约束。
4. **性能瓶颈** —— Unity 逐帧热路径上的反射 / 分配 / LINQ、事件总线扇出、主线程 IO。
5. **文档漂移** —— `CLAUDE.md` / `CONTEXT.md` / `docs/**` 与活代码对不上。
6. **优化方案** —— 把以上发现综合成一份按优先级排序、可执行、可验证的方案。

---

## 1. Hard Rules

1. **代码是唯一真相源**。每一条发现都必须带 `file:line` 证据。文档 / 注释 / 设计 spec / 本 prompt 里的"线索"都只是参考；与代码冲突时**以代码为准并把漂移记为一条发现**。
2. **只读**。不 `Edit`、不 `Write` 任何源码 / csproj / 现有文档；唯一允许写的文件是你自己的审计文档。绝不"顺手"改。
3. **不要把本 prompt 第 2、3 节的线索当成既成事实**。它们是**待验证的起点（seed leads）**，可能已过时。每条都要用 `rg` / `grep` 在当前代码里重新确认，确认不了就标 `to-verify` 或丢弃。
4. `decompiled/` 是只读参考反编译产物，永不修改、不计入"死代码"。
5. **每条发现都要有：维度 / 严重度（P0/P1/P2）/ `file:line` 证据 / 为什么是问题 / 建议修法 / 工作量与影响面**。不要凭感觉，不要无证据断言。
6. **审计先于方案**：先把发现列全，再综合成方案。方案末尾**停下来等人类确认**，不要自行进入实现。这条遵守仓库规则"收到 review / 修订设计后，先把方案送回确认再实现"。
7. 不确定的就老实写 `to-verify` 并给出验证方法，**不要**为了凑一条结论编造机制（仓库规则：失败重复时先写文档列候选机制，不写投机补丁）。

---

## 2. 代码地形图（让你不必从零摸索）

> 以下结构来自 `CLAUDE.md` 与一次目录扫描，**仍需你用 `rg` 复核**。

**6 个程序集，全部在 `src/<AssemblyName>/` 下**（每项目独立编译锥）：

| 程序集 | 角色 | 约束 |
| --- | --- | --- |
| `src/BazaarPlusPlus/` | 主 BepInEx 插件 | 引用游戏 DLL / Unity / BepInEx |
| `src/BazaarPlusPlus.ModApi/` | 云后端 HTTP 客户端 + DTO | **零**游戏 / Unity / BepInEx 引用 |
| `src/BazaarPlusPlus.Storage/` | SQLite 持久化 | **零**游戏 / Unity / BepInEx 引用 |
| `src/BazaarPlusPlus.Localization/` | 本地化引擎 | **零**游戏 / Unity / BepInEx 引用 |
| `src/BazaarPlusPlus.BazaarAgent/` | BazaarAgent 纯传输 / DTO / 队列 | **零**游戏 / Unity / BepInEx 引用 |
| `src/BazaarPlusPlus.BazaarAgentHost/` | 可选的 BepInEx host 桥 | 装了才起 `127.0.0.1:47900` 监听 |

**主插件内部分层（`src/BazaarPlusPlus/` 下，层边界写在 `CLAUDE.md`）：**

- `Core/` —— 纯抽象（config / event bus / paths / runtime 接口），**零游戏 DLL 引用**。
- `GameInterop/` —— 唯一允许耦合游戏 DLL 的适配层（`ClientCache` 反射、`GameStateProbe`、`RunContextStore`、game-typed 事件、静态卡数据、卡预览 prefab、肖像资源等）。
- `Game/` —— 功能实现，按子目录组织（实际子目录：`BuildRecommendations` `CollectionPanel` `CombatReplay` `CombatStatusBar` `HistoryPanel` `Input` `ItemEnchantPreview` `LegendaryPosition` `LiveBuildPanel` `Lobby` `NameOverride` `OverlayPanels` `PvpBattles` `RunLifecycle` `RunLogging` `Screenshots` `Settings` `Supporters` `Tooltips` `Upload`）。
- `Patches/` —— Harmony 补丁，通过静态 `BppPatchHost` 服务定位器拿 `IBppServices`，**不是构造注入**。
- `Infrastructure/` —— 横切工具（日志 `BppLog`、字体、UI design tokens）。

**装配 / 生命周期**：`Plugin.cs`（BepInEx 入口，精简）→ `BppComposition.cs`（手写组合根，无 DI 容器）。三套注册表：`IBppFeature`（`BppFeatureRegistry`）、`IBppMountable`（`BppMountableRegistry`，多数走 `ComponentMount<T>`）、`ISettingsDockEntry`（`SettingsDockEntryRegistry`）。

**已有的护栏**：`tests/Architecture.Tests/CoreLayeringTests.cs` 已经用文件系统扫描强制了一部分层边界。审计时**先读它**，分清"已被测试守住的"与"靠约定、编译器和测试都管不到的"——后者才是分层维度的高价值发现区。

**文档地图**：`docs/README.md` 是索引；`CONTEXT.md` 是术语表；`docs/adr/` 是架构决策（当前 `0001`–`0006`）；`docs/features/` 每功能一篇权威文档；`docs/reference/` 是低漂移契约（sqlite schema / hotkeys / settings dock / bazaar-agent wire）；`docs/design/` 与 `docs/design/archive/` 是设计史；`docs/audits/` 是历次审计。**先读 `docs/audits/2026-06-04-doc-code-drift-audit.md`**：它是上一次文档漂移审计，你要"在它基础上做增量 + 检查它的结论是否已落地或又退化"，而不是从头重来。

**运行期落盘目录**：`<GameRoot>/BazaarPlusPlusV4/`（本地 SQLite + replay）。后台 uploader 把 bundle 推到 `mod-api-v4.bazaarplusplus.com`（**服务端属于另一个仓库 `bazaarplusplus-server`，不在本次审计范围**）。

---

## 3. 六个维度的审计指南

> 每个维度给了"看什么 / 在哪看 / 怎么搜"。搜索命令是**起点不是终点**——命中后必须打开文件核对上下文，确认是真问题再记。

### D1 — Fallback / 旧路径残留（重点）

仓库规则明确："替换子系统 / 迁移到原型时，**整体删除旧实现，原地只留新版**——不要把旧路径留作 fallback，也不要搭一条同时跑新旧两套的合并构建链。" 所以每一次已发生的迁移都是一个潜在的残留点。

**已知的迁移边界（seed leads，全部 `to-verify`）：**

- **BazaarAgent 两插件化**：`docs/adr/0005` → `0006-bazaaragent-as-its-own-plugin.md`。旧的"主插件内 `#if`-gated AutoBazaar"形态应已不存在。核：主插件里有没有残留的 AutoBazaar 装配、`#if` 分支、或对 BazaarAgent 的隐式回退路径。
- **V3 → V4 服务端**：CLAUDE.md 称 V3 后端源码已从 mod 移除。核：mod 里有没有残留的 V3 endpoint / DTO / 客户端 / `v3` 命名。
- **附魔预览迁移移除**：`git log` 有 `Remove legacy enchant preview migration`（commit `19dabcc`），但 `src/BazaarPlusPlus/Game/ItemEnchantPreview/` 与 `tests/ItemEnchantPreview.Tests/` 仍在。核：有没有迁移 shim / 旧 schema 兼容分支残留。
- **本地化抽取**：`docs/design/2026-06-03-localization-module-extraction-*`。核：主插件里有没有和 `BazaarPlusPlus.Localization` 重复的、留作 fallback 的内联本地化路径。
- **`src/` 物理重构**：`docs/superpowers/plans/archive/refactor-src-layout-prompt.md` + `git log` 的 `Move all six assemblies under src/`。核：有没有残留的根布局构建胶水（`Compile Remove/Include`、根级 `obj`/`bin` 输出块、指向旧根路径的引用）。
- **死线格式 / 字段**：候选——`RunLogEvent` 的 `Options` / `Selected*` 字段、`RunLogOptionSnapshot` 可能既无 producer 也无 consumer（疑似 EncounterTracking 重设计前的脚手架）。**这是一条待验证线索**：用 `rg` 确认是否真的无人写入、无人读取，再决定记不记。

**通用搜索（命中后必须核对，不要直接抄进结论）：**

```bash
rg -n -i "fallback|legacy|deprecated|obsolete|backcompat|back-compat|temporary|workaround|for now|remove this|old path|v3" src tests
rg -n "#if|#else|#endif|\[Obsolete" src
rg -n "TODO|FIXME|HACK|XXX" src
```

**怎么判定是发现**：存在一条"新实现已就位、旧实现仍可达 / 仍被引用 / 仍参与构建"的路径；或一段无 producer/无 consumer 的死结构；或一个永远命中同一分支的开关。对每条给出"旧路径在哪（`file:line`）+ 新路径在哪 + 为什么旧的可以删"。

### D2 — 不合理逻辑 / 代码坏味

- **吞异常**：`catch` 后既不记日志也不重抛、或 `catch {}`。在 Unity/反射密集的代码里尤其危险（游戏更新后静默失效）。`rg -n "catch\s*\(.*\)\s*\{\s*\}" src` 与 `catch` + 空体 / 仅 `return`。
- **死代码**：从不被调用的 `public`/`internal` 方法、从不被读的字段、从不被命中的分支。注意 `<PublicizeAll>` 让游戏 `internal` 成员可见——别把"为反射保留的入口"误判为死代码。
- **复制粘贴分叉**：两处近似逻辑只在一两行上不同，修了一处忘了另一处。
- **脆弱反射无降级**：直接 `GetField`/`GetMethod` 后不判 `null` 就用——游戏一更新就 `NullReferenceException`。
- **MessagePack 陷阱**（已知坑）：被 MessagePack 序列化的 DTO 整张图必须 `public`；`internal` DTO 即使属性 public 也会在 Mono 运行期抛 `MethodAccessException`。核 `ModApi` / `Storage` / `RunLogging` 的序列化 DTO 图，**任何非 public 节点都记为 P0/P1 运行期风险**。
- **配置只读不写或只写不读**：`Core/` config 项定义了却没人用，或读了却无处设置。

### D3 — 分层违规

对照 `CLAUDE.md` 的"Architecture layering rules"逐条查，**先排除已被 `tests/Architecture.Tests/CoreLayeringTests.cs` 守住的**：

- `Core/` 是否真的零游戏 DLL 引用（搜 `Core/` 下对游戏命名空间 / Unity / BepInEx 的 `using`）。
- 4 个纯程序集（`ModApi` / `Storage` / `Localization` / `BazaarAgent`）是否真的零游戏 / Unity / BepInEx 引用——查它们的 `.csproj` 有无游戏 `Reference`，查源码有无相关 `using`。
- `Game/` 某功能是否 import 了**另一个功能**的内部实现，只为复用一段游戏运行时适配（规则要求这种共享适配应抽到 `GameInterop/<Concept>/`）。
- 是否有该放进 `GameInterop/` 的游戏运行时适配，被塞在了某个 `Game/` 功能目录里；或反过来，把纯产品策略 / 过滤分类规则塞进了 `GameInterop/`。
- `Patches/` 是否绕过 `BppPatchHost` 直接 new 服务 / 持有功能内部引用。
- **每条违规要写明**：违反的是哪条规则、`file:line`、为什么编译器和现有架构测试没拦住、是否值得补一条架构测试。

### D4 — 性能瓶颈（Unity 逐帧视角）

这是个**跑在游戏进程内**的 mod，热路径上的浪费会直接掉帧。

- **Harmony 补丁打在高频 / 逐帧方法上**（`Update`、战斗 sim 帧、渲染回调）：补丁体里有没有反射、LINQ、`string` 拼接、闭包分配、装箱。
- **反射未缓存**：`GetField`/`GetMethod`/`GetType` 在热路径里每次调用现查，而非启动时缓存进 `static` / 字段（`GameInterop/` 的 `ClientCache` 反射是重点）。
- **事件总线扇出**：`IBppEventBus` 在 combat frame 事件上的订阅者数量与每次发布的分配；逐帧发布是否必要。
- **主线程 IO**：SQLite 写 / 截图 / 视频编码 / 上传是否在主线程，还是已挪到后台。
- **集合 / 网格虚拟化**：`Game/CollectionPanel` 首屏构建成本——已有设计文档 `docs/design/2026-05-31-collection-panel-first-load-performance.md`，核当时的优化是否还在、有没有退化。
- **录制 / 回放成本**：`docs/design/2026-05-30-combat-replay-audio-and-capture-perf-design.md` 里的结论与当前代码是否一致。
- 对每条性能发现，给"在哪、调用频率量级、浪费的是 CPU 还是 GC 分配、建议改法、预期收益"。**别只说"可能慢"**——给出频率与机制。

### D5 — 文档漂移

逐一把文档断言拿去和活代码对：

- **ADR 索引漏项（seed lead）**：`docs/README.md:38` 似乎只列了 ADR `0001`–`0005`，但 `docs/adr/0006-bazaaragent-as-its-own-plugin.md` 存在。核实并记。
- **"parked" 措辞（seed lead）**：`docs/README.md:23,32,33` 与 `docs/features/bazaar-agent.md` 把 BazaarAgent 描述为"当前 parked"。但 ADR `0006` 把它做成了独立插件。**以代码为准**核实 BazaarAgent 当前到底是 parked 还是已作为独立插件 ship，把不一致记为漂移。
- **版本号一致性**：`git log` 有 `Unify mod version to 4.1.0`。核 `Directory.Build.props` 的 `BppVersion`、各处版本字符串、文档里写的版本是否一致（别假设是某个具体数字，去读）。
- **reference/ 契约**：`docs/reference/sqlite-schema-reference.md` vs `Storage` / `RunLogging` 实际表列；`hotkeys-reference.md` vs 实际默认键；`settings-and-debug-surfaces.md` vs 实际注册的 `ISettingsDockEntry`。
- **`CLAUDE.md` 架构描述** vs `src/` 实际布局与层内容（`src/` 重构后是否已同步）。
- **上一次审计**：`docs/audits/2026-06-04-doc-code-drift-audit.md` 的发现是否已修复 / 又退化。
- 每条漂移记"文档断言（`docs/file:line`）vs 代码事实（`src/file:line`）+ 应改文档还是代码"。

### D6 — 完整优化方案（综合）

把 D1–D5 的发现综合成**一份可执行、按优先级排序的方案**：

- 按 **P0（正确性 / 运行期风险）/ P1（分层 + 死代码清理 + 明确性能问题）/ P2（文档 + 低风险整洁）** 分级。
- 每个方案条目：解决哪几条发现（引发现 ID）、改动范围（哪些文件 / 程序集）、影响面与风险（会不会破坏 wire 契约 / MessagePack 图 / 架构测试 / 功能测试）、是否需要版本号变更（仓库规则：为最干净终态可接受 breaking change 并 bump major，而不是留兼容 shim）、验证方法（具体跑哪个 `./run.sh` / 哪个测试工程）。
- **删除类条目必须遵守"整体删除旧实现、不留 fallback、不搭新旧并跑的构建链"**。
- 给一个建议的执行顺序（哪些可并行、哪些有依赖）。
- **方案到此为止，停下来等人类确认；不要开始实现。**

---

## 4. 方法（怎么干）

1. **建图**：先 `git log --oneline -40`、读 `CLAUDE.md` / `CONTEXT.md` / `docs/README.md` / `docs/audits/2026-06-04-*` / `tests/Architecture.Tests/CoreLayeringTests.cs`，再用 `rg --files src | head` 之类摸清实际布局，复核第 2 节地形图。
2. **逐维度扫**：D1→D5 各跑第 3 节的搜索，命中后**打开文件核对上下文**。
3. **验证每条候选发现**：能 `file:line` 钉死的才算"confirmed"，钉不死的标 `to-verify` 并写验证法。宁可少报、不可错报。
4. **写文档**：按第 5 节模板产出，最后综合出 D6 方案。
5. **自检**：交付前确认每条发现都有 `file:line`，且你**没有改动任何源码 / 现有文档**。

---

## 5. 审计文档模板

写到 `docs/audits/<YYYY-MM-DD>-codebase-health-audit.md`：

```markdown
# Codebase Health Audit — <YYYY-MM-DD>

> 只读审计。覆盖 fallback 残留 / 不合理逻辑 / 分层 / 性能 / 文档漂移，并给出按优先级排序的优化方案。
> 基线 commit：<git rev-parse --short HEAD>

## 摘要
- 总发现数：N（P0 a / P1 b / P2 c）
- 与上一次审计（docs/audits/2026-06-04-...）的关系：<已修复 X / 退化 Y / 新增 Z>

## 发现清单
### D1 Fallback / 旧路径残留
- **[F-001] <一句话标题>** — 严重度 P0/P1/P2
  - 证据：`src/...:行` （旧路径）/ `src/...:行` （新路径）
  - 问题：<为什么不合理>
  - 建议：<怎么改 / 删什么>
  - 影响面 & 工作量：<范围 / 风险 / S·M·L>
  - 状态：confirmed / to-verify（验证法：…）
### D2 不合理逻辑 / 代码坏味
…（同上格式，编号 F-0xx）
### D3 分层违规
### D4 性能瓶颈
### D5 文档漂移

## 优化方案（D6）
### P0 — 正确性 / 运行期风险
- **[P0-1] <条目>** ← 解决 F-00x, F-00y
  - 范围 / 影响面 / 版本影响 / 验证方法
### P1 — 分层 + 死代码 + 性能
### P2 — 文档 + 整洁
### 建议执行顺序与依赖
<哪些可并行，哪些有前后依赖>

## Open questions / to-verify
<钉不死、需人类拍板或需进游戏验证的项>
```

---

## 6. 自检（交付前必过）

- [ ] 每条发现都有至少一个 `file:line` 证据，没有无证据断言。
- [ ] 第 2、3 节的 seed leads 全部经 `rg`/`grep` 在当前代码里复核过，确认不了的已标 `to-verify`。
- [ ] 工作树仍然干净：`git status --short` 只应出现你新建的那一篇审计文档（`git diff` 对 `src/**` 与既有 `docs/**` 应为空）。
- [ ] 方案末尾明确"停下等确认"，没有进入实现。

---

## 7. One-line Handoff

> 按 `docs/codebase-health-audit-prompt.md` 做一次**只读**代码体检：覆盖 fallback 残留 / 不合理逻辑 / 分层违规 / 性能瓶颈 / 文档漂移五个维度，每条发现带 `file:line` 证据并标严重度，最后综合出一份按 P0/P1/P2 排序、可验证的完整优化方案，写到 `docs/audits/<今天 UTC 日期>-codebase-health-audit.md`。代码是唯一真相源；不写任何补丁、不改任何既有文件，方案写完停下等我确认。
