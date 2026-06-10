---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# 本地化引擎抽离为独立模块 `BazaarPlusPlus.Localization`

Status: In progress (v2) — 已与维护者确认方案要点，并经 Codex 对抗评审 + 红队评审修订。**P1+P2+P3 缓存键修复已落地**：独立程序集已抽出、L.Install 已接线、FontAtlasSampleCache 键已改为 (languageCode, mode)(HistoryPanelText.cs:887-892)。**P0（快照基线）、P3 其余（Loc 目录 + FontAtlasSample 反射改造）、P4a、P4b、P5 未做。** 全部落地后移入 `archive/` 并加 `Status:` banner；其中"集中机制、依赖反转、`Resolve(languageCode, mode)` 纯函数核心"等决策应在归档前提升为 ADR。

> **v2 修订摘要（对抗评审并入）：** ① `Resolve` 核心改为显式 `Resolve(languageCode, mode)` 纯函数，删除会丢失简繁/地区语义的无 mode 重载；② 新增"mode 敏感性分类"规则，防止 P4 把仅按语言的串错误接入简繁转换；③ 新增 **P0**（穷尽清单 + 快照基线）作为后续步骤门禁；④ `FontAtlasSample` 反射迁移与图集缓存键 `(languageCode, mode)` 列为高风险载荷项；⑤ P1 补 `Directory.Build.props` 隔离；⑥ 决策 `InternalsVisibleTo` 保持 `internal`；⑦ P4 拆为 P4a/P4b。

本文记录把当前散落在 `Game/Settings/` 与各功能 `*Text.cs` 中的本地化逻辑，抽离为一个**零游戏/Unity/BepInEx 依赖的独立程序集 `BazaarPlusPlus.Localization`** 的目标架构与迁移方案。

## 背景与动机

当前本地化由三类代码构成（按代码实证，而非目录名）：

- **引擎（mechanism）**：`LocalizedTextSet`、`LanguageCodeMatcher`、`BppChineseLocalization`、`BppChineseLocaleMode`、`BppTmpFontPolicy` —— 已经基本是纯 C#。
- **内容（content）**：各功能的实际译文，散在 `HistoryPanelText`（955 行）、`CollectionPanelText`、`CardSetPreviewRuntime`、各 `*.SettingsMenuLabel.cs` 中。
- **运行时适配（runtime）**：语言来源 `PlayerPreferences.Data.LanguageCode`、字体应用 `BppTmpFont`/`BppUiFont`、切语言刷新 `OptionsDialogLanguageRefreshPatch`。

### 现状的结构性问题（证据）

1. **横切关注点错放在功能文件夹下。** `LocalizedTextSet`/`LanguageCodeMatcher`/`BppChineseLocalization` 都在 `BazaarPlusPlus.Game.Settings` 命名空间，却被 HistoryPanel、CollectionPanel、CardSetPreview、Input、Patches 等 14+ 处消费（`LocalizedTextSet` 跨 14 文件、`BppChineseLocalization` 跨 7 文件）。
2. **逻辑与数据脑裂。** 枚举 `BppChineseLocaleMode` 在 `Core/Config/BppChineseLocaleMode.cs`，操作它的全部逻辑 `BppChineseLocalization` 在 `Game/Settings/`。
3. **语言来源被复制。** `GetLanguageCode()` 的同一段 try/catch 至少在 `Game/HistoryPanel/HistoryPanelText.cs:944`、`CollectionPanelText`、`Supporters/Ui/BPPSupporterAttributionRow` 各写一遍，另有 18 个文件直接 inline 读 `LanguageCode`，缺一个"语言来源" seam。
4. **测试 shim = 耦合在喊疼。** `tests/CombatStatusBarState.Tests/TestChineseLocalizationShim.cs` 重新定义了一个假的 `BppChineseLocalization`，因为真实那个被困在 game-coupled 的主程序集里无法被测试引用。
5. **文案存储形态不统一。** 同时存在 8/4/2 语言的 `LocalizedTextSet` 重载、纯硬编码（`CombatStatusBar.Canvas`、`CardSetPreviewModeStatusText`）、自成一套的方法式 API（`BPPSupporterAttributionText`）。

### 抽离的真正前提：依赖反转

`BppChineseLocalization` 经 `IBppConfig.ChineseLocaleModeConfig` 间接依赖 BepInEx `ConfigEntry`（`Core/Config/IBppConfig.cs:20`）。这条传递依赖正是测试 shim 存在的根因。抽离的关键不是物理搬文件，而是**把"语言来源"和"地区模式来源"两个外部依赖从引擎里反转出去**。

## 已确认的决策

| # | 决策 |
|---|---|
| 1 | 模块名 `BazaarPlusPlus.Localization`，做成**独立 csproj/DLL**（仿 `ModApi`/`Storage` 先例）。 |
| 2 | `BppChineseLocalization` → 改名 `ChineseScriptConverter`（破坏性改名，取最干净终态，不留 back-compat shim）。 |
| 3 | `BppChineseLocaleMode` 从 `Core/Config` **迁入本模块**作为领域类型。 |
| 4 | 语言来源 `GetLanguageCode()` → 落到 `GameInterop/`，实现 `ILanguageProvider`。 |
| 5 | 译文内容**全量升格**为集中目录条目（含所有 inline 双语字面量），功能侧仅"引用变量 + Resolve"。 |
| 6 | 集中目录用**嵌套静态类**形态（`Loc.History.Title`）。 |
| 7 | 执行 P4：统一文案存储形态，消除硬编码 / bespoke 分支。 |

## 内容能否搬动的硬约束（实测）

"把 `*Text.cs` 整体搬进模块"行不通——一半文件直接引用**游戏程序集**：

| 文件 | 关键 import | 可否整体搬入零依赖模块 |
|---|---|---|
| `Game/Supporters/BPPSupporterAttributionText.cs` | 仅 `Game.Settings` | ✅ 可搬 |
| `Game/HistoryPanel/HistoryPanelText.cs` | `using TheBazaar`（仅 `GetLanguageCode`）+ mod 枚举 `RunOutcomeTier`（`HistoryPanelFormatter.cs:8`）+ 反射自身字段 | ⚠️ 反转 provider 后无游戏依赖，但引用功能领域枚举 |
| `Game/CollectionPanel/CollectionPanelText.cs` | `using BazaarGameShared.Domain.Core.Types`（**游戏类型**）| ❌ 不可整体搬 |
| `Game/CardSetPreview/CardSetPreviewRuntime.cs` | `BazaarGameClient`/`Shared` + `UnityEngine` | ❌ Unity 运行时，文案是附带（已被 LiveBuildPanel 替换，commit 50e64b1）|

**结论：分离"可翻译的字符串数据"（进模块目录）与"带游戏/领域逻辑的格式化器"（留功能侧）。**

## 内容模型：两层

### 第 1 层 · 集中文本目录（进模块，纯字符串，嵌套静态类）

所有可翻译文本集中到模块 `Catalog/` 下，按功能分区（仅命名分区，不依赖功能代码）；含插值的存"模式串"，占位符留给功能侧 `string.Format`：

```csharp
// BazaarPlusPlus.Localization/Catalog/Loc.History.cs （模块内，零游戏依赖；internal + InternalsVisibleTo）
static partial class Loc
{
    internal static class History
    {
        internal static readonly LocalizedTextSet Title     = new("Game History", "对局历史", "對局歷史", "對局歷史");
        internal static readonly LocalizedTextSet Delete    = new("Delete", "删除");
        internal static readonly LocalizedTextSet RunRecord = new("{0}W - {1}L", "{0}胜 - {1}负", "{0}勝 - {1}負", "{0}勝 - {1}負");
        internal static readonly LocalizedTextSet StatHealthShort = new("HP", "生命");
        // …原 FormatSimple(...) 中的 inline 字面量全部升格为此处条目
    }
    internal static class Collection { /* … */ }
    internal static class Supporters { /* … */ }
}
```

### 第 2 层 · 功能侧格式化器（留功能目录，消费目录）

带游戏枚举 / 插值 / 复数逻辑的，留功能侧，但文本来自目录：

```csharp
// Game/HistoryPanel/HistoryPanelText.cs （留功能侧）
internal static string RunRecord(int wins, int losses)
    => string.Format(L.Resolve(Loc.History.RunRecord), wins, losses);

internal static string RunOutcomeBubbleLabel(RunOutcomeTier tier) => tier switch  // 游戏/领域枚举留功能侧
{
    RunOutcomeTier.Gold => L.Resolve(Loc.History.OutcomeGold),
    // …
};
```

纯静态调用退化为目标形态：

```csharp
label.text = L.Resolve(Loc.History.Title);   // “引用变量 + Resolve”
```

### mode 敏感性分类（v2 新增，强制规则）

部分中文串当前**只过 `LanguageCodeMatcher.IsChinese` 判定、从不过简繁/地区转换**，TW/HK 用户看到的就是简体——这是刻意的。证据：[BPPSupporterAttributionText.cs:8-31](../../../src/BazaarPlusPlus/Game/Supporters/BPPSupporterAttributionText.cs)（CardSetPreviewModeStatusText.cs 已随 CardSetPreview 删除，此子项取消）。

把这类串升格为普通 `LocalizedTextSet` 条目会让它们被 `ChineseScriptConverter` 自动转繁——**这是行为变化，不是重构**。因此每条迁移串必须先归类：

- **mode 敏感（默认）**：只给简体 mainland 值，由转换器派生 TW/HK——适用于绝大多数 UI 文案。
- **mode 不敏感（须显式声明）**：当前对所有中文都输出同一份文本的串。迁移时**必须**显式提供与原输出一致的 TW/HK 值（通常 = 简体原文），或保留为功能侧格式化器，**不得**让它默认走转换器。

> 此分类是 P4 的前置条件；快照测试（见 P0）必须覆盖这些 mode 不敏感站点，断言三种 `BppChineseLocaleMode` 下输出不变。

### 字体图集：两处必须随迁改造的高风险载荷项（v2 新增）

1. **`FontAtlasSample()` 反射会静默塌缩。** [HistoryPanelText.cs:831-872](../../../src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.Shared.cs) 用 `typeof(HistoryPanelText).GetFields(...)` 反射**自身** `LocalizedTextSet` 字段预热 CJK 图集。字段迁入 `Loc.History` 后，此反射将找不到任何字段 → 样本塌缩成只剩 ASCII + `FontProbeSample`，**所有中文字形预热丢失**（首次渲染掉字/卡顿）。改造为显式枚举 `Loc` 对应分区，并加测试覆盖"样本含预期 CJK 字形"。
2. **图集缓存键已修复（`(languageCode, mode)` 复合键）。** [HistoryPanelText.cs:887-892](../../../src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.Shared.cs) 的 `CreateFontAtlasSampleCacheKey` 已改为 `(languageCode, mode)` 复合键（`$"{languageCode}{(int)mode}"`）。P3 字段迁入 `Loc.History` 后还需改造 `FontAtlasSample()` 反射为显式枚举，`ChineseLocaleModeChanged`（[Core/Events/ChineseLocaleModeChanged.cs](../../../src/BazaarPlusPlus/Core/Events/ChineseLocaleModeChanged.cs)）时失效缓存的改造也仍待做。

## 模块边界与归属

| 去向 | 内容 |
|---|---|
| **`BazaarPlusPlus.Localization`（新 DLL，零 game/Unity/BepInEx）** | `LocalizedTextSet`、`LanguageCodeMatcher`、`ChineseScriptConverter`（原 `BppChineseLocalization` 的 461 字简繁表 + 台/港术语替换）、`BppChineseLocaleMode` 枚举、`CjkDetection`（原 `BppTmpFontPolicy` 纯判定）、`ILanguageProvider`/`ILocaleModeProvider` 接口、`L` 门面、`Loc` 文本目录 |
| **`GameInterop/`** | `GameLanguageProvider : ILanguageProvider`（读 `PlayerPreferences.Data.LanguageCode`，含 try/catch 回退） |
| **主程序集 / Core** | `ChineseLocaleModeProvider : ILocaleModeProvider`（读 `IBppConfig.ChineseLocaleModeConfig`） |
| **`Infrastructure/Fonts`（不动）** | `BppTmpFont`/`BppUiFont`/`EmbeddedFontFile`（Unity TMP 字体**应用**） |
| **`Patches/`（不动行为）** | `OptionsDialogLanguageRefreshPatch`（切语言刷新） |
| **各功能目录** | 带领域逻辑的格式化器，消费 `Loc` 目录；含游戏类型的解析器如 [CollectionLocalizationResolver](../../../src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionLocalizationResolver.cs)（`ResolveTitle(TCardBase)`，按游戏模板解析卡名）留原处，仅其文案常量进目录 |

> **可见性决策（v2）：** 引擎类型当前为 `internal`。拆成独立 DLL 后，**优先用 `InternalsVisibleTo`**（主程序集 + 测试工程）保持 `internal`，而非全部 `public`——避免把本地化内部结构变成 mod 对外契约。仅 provider 接口与 `Resolve`/`L` 等真正的消费入口按需公开。

## 公共接口契约

```csharp
namespace BazaarPlusPlus.Localization;

enum BppChineseLocaleMode { Mainland = 0, Taiwan = 1, HongKong = 2 }   // internal + InternalsVisibleTo

interface ILanguageProvider   { string CurrentLanguageCode { get; } }    // GameInterop 实现
interface ILocaleModeProvider { BppChineseLocaleMode CurrentMode { get; } } // 主程序集实现

readonly struct LocalizedTextSet
{
    /* 现有字段不变 */
    // —— 纯函数核心：输出完全由入参决定，零全局、可注入、可并行测试 ——
    string Resolve(string languageCode, BppChineseLocaleMode mode);
    // 注意：v1 的无 mode 重载 Resolve(languageCode) 已删除——它会静默丢失简繁/地区语义。
}

static class ChineseScriptConverter { string Convert(string mainland, string? tw, string? hk, BppChineseLocaleMode mode); }
static class LanguageCodeMatcher    { /* IsChinese / IsGerman / … 现状不变 */ }
static class CjkDetection           { bool ContainsCjk(string? text); }

// —— 薄便利层：仅供生产调用点用，把"读两个 provider"的样板收一处。测试不用它。——
static class L
{
    static void Install(ILanguageProvider language, ILocaleModeProvider mode);
    static string Resolve(LocalizedTextSet set) =>          // = set.Resolve(lang.CurrentLanguageCode, mode.CurrentMode)
        set.Resolve(_language.CurrentLanguageCode, _mode.CurrentMode);
    // 转发当前语言 / 地区模式，供需要显式入参（如字体图集预热）的调用点用：
    static string CurrentLanguageCode { get; }             // = _language.CurrentLanguageCode
    static BppChineseLocaleMode CurrentMode { get; }       // = _mode.CurrentMode
}
```

**v2 关键修订（对抗评审 Codex#1 + 红队#8）：**

- **纯函数核心 `Resolve(languageCode, mode)`**：输出只由入参决定。这同时 (a) 修掉 v1 把简繁/地区语义藏进隐式全局的契约缺口，(b) 让引擎可在测试里直接以三种 mode 注入断言、**无需任何全局 `Install`**，从根上解决当年逼出 `TestChineseLocalizationShim` 的"static + Install"全局可变状态问题。
- **`L` 降为生产薄层**：`L.Install` 仍是全局静态，但它**只服务生产调用点**（省去到处读两个 provider 的样板）；测试一律走纯函数核心，互不污染。`L` 不再是唯一入口，因此不构成新的测试障碍。

**依赖反转要点：** `ChineseScriptConverter` 不再 `Install(IBppConfig)`、不再读 `Config.ChineseLocaleModeConfig`；地区模式一律经入参（`Convert(..., mode)` / `Resolve(..., mode)`）传入。这斩断对 BepInEx `ConfigEntry` 的传递依赖。

## csproj / 构建改造（照搬 `ModApi` 模式）

1. 新建 `BazaarPlusPlus.Localization.csproj`：`netstandard2.1` + `Compile Remove="**/*.cs"` / `Compile Include="Localization/**/*.cs"`。
2. **（v2 必补，Codex#3）** [Directory.Build.props](../../../Directory.Build.props) 增加 `BazaarPlusPlus.Localization` 的 `BaseIntermediateOutputPath`/`BaseOutputPath` 隔离条目——Directory.Build.props 当前仅含 BppVersion 与 EnforceCodeStyleInBuild，无路径隔离条目（原说法"只有 ModApi/Storage/AutoBazaar 有（:11-24）"已过时，该隔离尚未添加）。少了它，第 4 个根 csproj 用默认 `obj/bin` 会与主工程相互覆盖 `project.assets.json` / NuGet lock，导致构建顺序相关失败。
3. `BazaarPlusPlus.csproj` 增 `Compile Remove="Localization/**"`、`ProjectReference`、`BepInEx/plugins` 复制项（仿现有 ModApi/Storage 第 49–54、141–142、223–238 行附近写法）。
4. 用 `InternalsVisibleTo` 暴露 `internal` 类型给主程序集与测试工程（见可见性决策）。
5. 测试工程改为直接 `ProjectReference` 新模块，**删除 `TestChineseLocalizationShim.cs`**（仅在 P0 快照基线就绪后）。

## 迁移工作包（可独立合并 + 验证）

| 包 | 内容 | 验证 | 依赖 |
|---|---|---|---|
| **P0**（v2 新增，门禁） | **穷尽本地化清单**（补 `CollectionLocalizationResolver`、CollectionPanel catalog cache、所有 inline `FormatSimple` 站点等）+ 为每个文本入口建立 **8 语言 × 3 中文 mode 快照基线**（临时快照测试，迁完即删）。 | 快照基线可重跑、稳定 | — |
| **P1** | 建模块骨架 + 迁移引擎 6 类 + 改名 `ChineseScriptConverter` + 迁入 `BppChineseLocaleMode` + 修 `using`。新 csproj + **`Directory.Build.props` 隔离** + `InternalsVisibleTo` + 构建复制 plumbing。 | `./run.sh build` 通过；localization 项目、主项目、`BuildAll` **背靠背构建**均通过；产出并复制 `BazaarPlusPlus.Localization.dll` | P0 |
| **P2** | 引擎核心改 `Resolve(languageCode, mode)` 纯函数；引入 `ILanguageProvider`/`ILocaleModeProvider` + `L` 薄层；`GameLanguageProvider` 落 GameInterop；收敛所有 `GetLanguageCode()` / inline `LanguageCode` 读取。 | 全量快照不变 | P1 |
| **P3** | 建 `Loc` 目录；各功能**静态** `LocalizedTextSet` 字段迁入目录，功能改引用 `Loc.X.Y`；**改造 `FontAtlasSample` 枚举 `Loc` 分区** + 图集缓存键改 `(languageCode, mode)` 并在 `ChineseLocaleModeChanged` 失效。 | 全量快照不变；新增"图集样本含预期 CJK 字形"测试 | P2 |
| **P4a** | 真硬编码块升格：`CombatStatusBar.Canvas` → 目录条目（CardSetPreviewModeStatusText 已随 CardSetPreview 删除，实际目标仅余 CombatStatusBar.Canvas.cs）；逐条做 **mode 敏感性分类**。 | 全量快照不变（含 3 mode） | P3 |
| **P4b** | inline 双语字面量 / 插值串清扫（churn 最大、风险最高）→ 目录条目 / 模式串；每条标注 mode 敏感性。 | 全量快照不变（含 3 mode），**门禁于 P0 基线** | P4a |
| **P5** | 测试直引模块、删 shim、跑全部测试。 | `./run.sh test` 全绿 | P2（删 shim 须在 P0 基线之后） |

## 风险与红线

- **简繁/地区输出静默回归（最高优先）**：① 删除无 mode 的 `Resolve(languageCode)` 后，任何遗留的无 mode 调用都必须改造，否则丢失 TW/HK；② P4 把"仅按语言"的串误接入转换器（见 mode 敏感性分类）。红线：**运行时行为零变化**，由 P0 全量快照（含 3 mode）把关，任何步骤前后比对。
- **字符串搬迁笔误回归**（每个串都是潜在 typo），尤其 P4b churn 最大——门禁于 P0 快照基线。
- **中文字形预热回归**：`FontAtlasSample` 反射塌缩 + 图集缓存 mode 陈旧（见高风险载荷项），需专门测试覆盖。
- 不触碰字体**应用**链、不改 `OptionsDialogLanguageRefreshPatch` 行为。
- 严格只动本地化相关文件，不顺手改无关配置。
- 从游戏 worktree 构建时注意 `-p:BPPInstallerSourcePath=...`（见根 CLAUDE.md 陷阱）。
- **删 shim（P5）须在 P0 快照基线就绪之后**，否则回归无人发现。

## 后续 / 待定

- `Loc` 目录是否细分文件（`Loc.History.cs`/`Loc.Collection.cs` partial），还是单文件多嵌套类——P3 落地时按可读性定。
- 归档前把"集中机制、联邦化内容、依赖反转 provider"提升为 ADR。
