# 执行 Prompt：抽离 `BazaarPlusPlus.Localization` 本地化模块

> 这是一份**自包含**的重构执行说明，交给一个对此前讨论一无所知的新 agent。完整设计见同目录 [`2026-06-03-localization-module-extraction-design.md`](2026-06-03-localization-module-extraction-design.md)（v2），本文是其可执行版。**逐包执行，每包结束后停下等人工 review 再继续。**

---

## 0. 你在哪、怎么构建

- 仓库：`bazaarplusplus-mod`（C# / `netstandard2.1` / C# 12 / BepInEx 5 / Harmony）。它是多 repo workspace 的子项目，**所有命令在该子目录内执行**。
- 构建/测试/格式：
  - `./run.sh build` — Debug 构建（自动 copy 到游戏 `BepInEx/plugins`）
  - `./run.sh all` — Debug + Release（`BuildAll`）
  - `./run.sh test` — 跑 `tests/` 下全部测试工程
  - `./run.sh format` — csharpier 格式化
- 测试工程有两种形态：**xunit**（csproj 含 `Microsoft.NET.Test.Sdk`，用 `dotnet test`）与 **exe-runner**（`<OutputType>Exe</OutputType>` + `Program.cs`，用 `dotnet run --project ...`）。动手前先看 csproj 判断。
- `decompiled/` 只读，**绝不编辑**。
- 若从 git worktree 构建，需 `-p:BPPInstallerSourcePath=<installer resources 绝对路径>`，否则 MSB3030 失败。**不要为此改 `BazaarPlusPlus.csproj`**。

## 1. 任务与终态

把当前散落在 `Game/Settings/` 与各功能 `*Text.cs` 的**本地化引擎**抽离为一个**零 game/Unity/BepInEx 依赖的独立程序集 `BazaarPlusPlus.Localization`**，并：

- 依赖反转：语言来源、地区模式来源经接口注入，引擎不再依赖 `PlayerPreferences` 或 BepInEx `IBppConfig`。
- 译文内容集中到模块内**嵌套静态类目录 `Loc`**，功能侧改为"引用变量 + Resolve"。
- `BppChineseLocalization` 改名 `ChineseScriptConverter`；`BppChineseLocaleMode` 迁入模块。
- 删除测试 shim。

**终态铁律：运行时用户可见文本在 8 语言 × 3 中文 mode 下逐串不变。** 这是重构不是改文案。

## 2. 核心原则

1. **代码是唯一事实**：以实际 `import`/调用/序列化为准，不信注释与命名。本文给的 `file:line` 是动手前的锚点，但行号可能已漂移——动手前用 `rg` 重新定位。
2. **保持外部行为不变**：任何文本输出变化都视为回归，由 P0 快照把关。
3. **只动本地化相关文件**，不顺手改无关配置；`decompiled/` 只读。
4. **每包结束停下等 review**；每包跑最小相关测试 + `./run.sh format`（若 csharpier 顺带格式化了你改动外的文件，单独成 commit）。
5. 破坏性改名取最干净终态，**不留 back-compat shim**。

## 3. 现状清单（动手前用 `rg` 复核行号）

> **⚠️ 本节描述的是抽离前（pre-extraction）的状态，不是当前起点。** P1 + P2 已落地：独立程序集 `BazaarPlusPlus.Localization` 已抽出，引擎 6 类已迁入并改名 `ChineseScriptConverter`，`Resolve(languageCode, mode)` 纯函数核心 + `ILanguageProvider`/`ILocaleModeProvider` + `L` 薄层已就位，`Plugin.cs:108` 已 `L.Install(new GameLanguageProvider(), new ChineseLocaleModeProvider(services.Config))`。**但"语言来源收敛"尚未完成**：`GetLanguageCode()` 仍存在于 `Game/Supporters/Ui/BPPSupporterAttributionRow.cs:193-203`，且多处仍直接 inline 读 `PlayerPreferences.Data.LanguageCode`（如 `Game/Settings/BppSettingsDockController.cs:328`、`Game/Input/BppHotkeyService.cs:189`）——这些是 P2 收敛的遗留尾巴，连同 P0 与 P3–P5 仍待完成。下方清单按原始（pre-extraction）形态保留，动手前一律用 `rg` 复核类型现属哪个命名空间/程序集。

**引擎（将迁入模块）：**
- `Game/Settings/LocalizedTextSet.cs` — `internal readonly struct`；`Resolve(string languageCode)` 在 :76，对中文转调 `BppChineseLocalization.ResolveChineseText`。
- `Game/Settings/LanguageCodeMatcher.cs` — `internal static`，`IsChinese/IsSimplifiedChinese/IsGerman/IsPortuguese/IsKorean/IsItalian`，纯字符串匹配。
- `Game/Settings/BppChineseLocalization.cs` — `internal static`；`Install(IBppConfig)` :12；461 字 `TraditionalCharacterMap` :21；`GetCurrentMode()` :463 读 `Config.ChineseLocaleModeConfig?.Value`；`GetNextMode` :468；`ResolveChineseText(...)` :478/:483；`ResolveModeStatus` :515；`ApplyTaiwanTerms`/`ApplyHongKongTerms`。
- `Core/Config/BppChineseLocaleMode.cs` — `internal enum { Mainland=0, Taiwan=1, HongKong=2 }`。
- `Infrastructure/Fonts/BppTmpFontPolicy.cs` — `ShouldUseEmbeddedCjkFont` + `IsCjk`（纯 Unicode 区间）。
- `Game/Settings/SimplifiedChineseLanguage.cs` — 薄封装。

**耦合点（依赖反转目标）：**
- `IBppConfig.ChineseLocaleModeConfig` 是 `ConfigEntry<BppChineseLocaleMode>`（BepInEx 类型）：`Core/Config/IBppConfig.cs:20`、`Core/Config/BppConfig.cs:20/69`。
- `GetLanguageCode()` 读 `PlayerPreferences.Data.LanguageCode`（`using TheBazaar`），**重复定义**于 `Game/HistoryPanel/HistoryPanelText.cs:944`、`Game/CollectionPanel/CollectionPanelText.cs`、`Game/Supporters/Ui/BPPSupporterAttributionRow.cs`；另有 ~18 文件 inline 读 `LanguageCode`（`rg -l "LanguageCode" Game Patches`）。
- `Plugin.cs` 调 `BppChineseLocalization.Install(config)`。

**内容（留功能侧，仅抽字符串进 `Loc`）：**
- `Game/HistoryPanel/HistoryPanelText.cs`（955 行）：静态 `LocalizedTextSet` 字段 + 动态 builder（`RankLabel` :418 引用 mod 枚举 `RunOutcomeTier`（定义于 `Game/HistoryPanel/HistoryPanelFormatter.cs:8`）、`RunRecord` :405、`BoardSummary` :392、大量 `FormatSimple(...)` inline 字面量 :347+）。**`FontAtlasSample()` :831 用反射枚举自身 `LocalizedTextSet` 字段预热字体图集；`FontAtlasSampleCache` :13 仅以 `languageCode` 为键。**
- `Game/CollectionPanel/CollectionPanelText.cs` — `using BazaarGameShared.Domain.Core.Types`（游戏类型），不可整体搬。
- `Game/CardSetPreview/CardSetPreviewRuntime.cs` — `UnityEngine`/`BazaarGameClient`，不可搬。
- `Game/Supporters/BPPSupporterAttributionText.cs:8-31` — **仅按 `IsChinese` 输出简体、从不过简繁转换**（mode 不敏感）。
- `Game/CardSetPreview/CardSetPreviewModeStatusText.cs:13-15` — 同上，mode 不敏感。
- `Game/CollectionPanel/Data/CollectionLocalizationResolver.cs:14` — `ResolveTitle(TCardBase template)`，按游戏模板解析卡名（游戏类型，留原处）。
- 各 `*.SettingsMenuLabel.cs` / `*SettingsDockEntry.cs`（`rg -l "LocalizedTextSet" Game`）。
- `CombatStatusBar.Canvas.cs` — 纯硬编码英文标签。

**事件 / 刷新 / 字体应用（行为不动）：**
- `Core/Events/ChineseLocaleModeChanged.cs`；发布于 `Game/Settings/ChineseLocaleModeSettingsDockEntry.cs:55`；订阅于 `Game/CollectionPanel/CollectionPanelMount.cs:25`、`Game/HistoryPanel/HistoryPanelMount.cs:57`。
- `Patches/Settings/OptionsDialogLanguageRefreshPatch.cs`；`HistoryPanel.RefreshLocalization()` `Game/HistoryPanel/HistoryPanel.cs:247/:283`。
- `Infrastructure/Fonts/BppTmpFont.cs` / `BppUiFont.cs` / `EmbeddedFontFile.cs`（Unity TMP 字体**应用**，**留 Infrastructure，不迁**）。

**构建先例（照搬）：**
- `BazaarPlusPlus.ModApi.csproj`：`Compile Remove="**/*.cs"` + `Compile Include="ModApi/**/*.cs"`。
- `BazaarPlusPlus.csproj`：`Compile Remove="ModApi/**"`/`Storage/**`（~:49-54）、`ProjectReference`（~:141-142）、copy 到 `BepInEx/plugins`（~:223-238）。
- `Directory.Build.props:11-24` 仅 ModApi/Storage/AutoBazaar 有独立 `obj/bin`。
- 测试 shim：`tests/CombatStatusBarState.Tests/TestChineseLocalizationShim.cs`。

## 4. 目标接口契约

```csharp
namespace BazaarPlusPlus.Localization;   // 全部 internal，靠 InternalsVisibleTo 暴露给主程序集 + 测试工程

enum BppChineseLocaleMode { Mainland = 0, Taiwan = 1, HongKong = 2 }

interface ILanguageProvider   { string CurrentLanguageCode { get; } }     // 实现在 GameInterop/，读 PlayerPreferences
interface ILocaleModeProvider { BppChineseLocaleMode CurrentMode { get; } } // 实现在主程序集，读 IBppConfig

readonly struct LocalizedTextSet
{
    // 现有字段不变。纯函数核心，输出只由入参决定（零全局、可注入、可并行测试）：
    string Resolve(string languageCode, BppChineseLocaleMode mode);
    // 不要保留无 mode 的 Resolve(languageCode)——它会静默丢失简繁/地区语义。
}

static class ChineseScriptConverter { string Convert(string mainland, string? tw, string? hk, BppChineseLocaleMode mode); }
static class LanguageCodeMatcher    { /* 现状不变 */ }
static class CjkDetection           { bool ContainsCjk(string? text); }   // 原 BppTmpFontPolicy 纯判定

static class L   // 薄便利层：仅生产调用点用；测试一律走纯函数核心，不用 L
{
    static void Install(ILanguageProvider language, ILocaleModeProvider mode);
    static string Resolve(LocalizedTextSet set);   // = set.Resolve(lang.CurrentLanguageCode, mode.CurrentMode)
}

static partial class Loc   // 集中文本目录，按功能分区（仅命名分区，不依赖功能代码）
{
    internal static class History { internal static readonly LocalizedTextSet Title = new("Game History", "对局历史", "對局歷史", "對局歷史"); /* … */ }
    // Collection / Supporters / …
}
```

含插值的存"模式串"（如 `"{0}W - {1}L"` / `"{0}胜 - {1}负"`），功能侧用 `string.Format(L.Resolve(Loc.X.Y), ...)`。

**mode 敏感性分类（强制）：** 迁移每条串前归类——
- **mode 敏感（默认）**：只给简体 mainland 值，转换器派生 TW/HK。
- **mode 不敏感**（如 `BPPSupporterAttributionText`、`CardSetPreviewModeStatusText`，当前对所有中文输出同一份文本）：**必须**显式给与原输出一致的 TW/HK 值（通常 = 简体原文），或保留为功能侧格式化器，**不得**默认走转换器。

## 5. 工作包（按序执行，每包后停下 review）

### P0 — 清单 + 快照基线（门禁，先做）
- **改动**：`rg` 穷尽所有本地化站点（补齐 `CollectionLocalizationResolver`、CollectionPanel catalog cache、所有 inline `FormatSimple`、各 `*SettingsMenuLabel`）。在一个**临时测试工程**里对每个文本入口建立 **8 语言码 × 3 `BppChineseLocaleMode`** 的返回值快照基线。
- **完成标准**：快照可重复运行、稳定；清单写入 PR 描述。
- **验证**：基线测试连续两次运行输出一致。
- **rollback**：删临时测试工程即可（无生产改动）。
- **依赖**：无。

### P1 — 模块骨架 + 引擎迁移 + 改名 + 构建隔离
- **改动**：
  1. 新建 `Localization/` 源树 + `BazaarPlusPlus.Localization.csproj`（`netstandard2.1`，`Compile Remove="**/*.cs"` + `Compile Include="Localization/**/*.cs"`，照 `BazaarPlusPlus.ModApi.csproj`）。
  2. 迁入并改 `namespace BazaarPlusPlus.Localization`：`LocalizedTextSet`、`LanguageCodeMatcher`、`BppChineseLocalization`→**改名 `ChineseScriptConverter`**、`BppChineseLocaleMode`（从 `Core/Config/` 迁来）、`SimplifiedChineseLanguage`、`BppTmpFontPolicy`→`CjkDetection`。**此步暂不改依赖反转，仅搬+改名**（`ChineseScriptConverter` 临时保留 `Install(IBppConfig)` 也可，下一包再拆）。
  3. **`Directory.Build.props` 增 `BazaarPlusPlus.Localization` 的 `BaseIntermediateOutputPath`/`BaseOutputPath` 隔离条目**（仿 :11-24）。
  4. 配 `InternalsVisibleTo`（主程序集 + 相关测试工程）。
  5. `BazaarPlusPlus.csproj` 加 `Compile Remove="Localization/**"`、`ProjectReference`、copy 到 `BepInEx/plugins`（仿 ModApi）。
  6. 修所有消费方 `using`（`rg -l "Game.Settings" ...` 中涉及这些类型处；`rg -l "BppChineseLocalization"` 全改名）。
- **完成标准**：编译通过；`BazaarPlusPlus.Localization.dll` 产出并复制到 plugins。
- **验证**：`./run.sh build` 通过；**背靠背**构建 localization 项目、主项目、`./run.sh all` 均通过（验证 obj/bin 隔离）。
- **rollback**：git revert 该包 commit。
- **依赖**：P0。

### P2 — 纯函数核心 + provider 依赖反转
- **改动**：
  1. `LocalizedTextSet.Resolve` 改签名为 `Resolve(string languageCode, BppChineseLocaleMode mode)`；`ChineseScriptConverter` 去掉 `Install(IBppConfig)`/读 config，mode 一律入参。
  2. 新增 `ILanguageProvider`/`ILocaleModeProvider`（模块内）+ `L` 薄层。
  3. `GameLanguageProvider : ILanguageProvider` 落 **`GameInterop/`**（读 `PlayerPreferences.Data.LanguageCode`，含 try/catch 回退 `string.Empty`）；`ChineseLocaleModeProvider : ILocaleModeProvider` 落主程序集（读 `IBppConfig.ChineseLocaleModeConfig`）。
  4. `Plugin.cs` 的 `BppChineseLocalization.Install(config)` 改为 `L.Install(new GameLanguageProvider(...), new ChineseLocaleModeProvider(config))`。
  5. **收敛所有 `GetLanguageCode()` 与 inline `LanguageCode` 读取**改走 `L.Resolve(set)`（生产）或显式 `set.Resolve(lang, mode)`。
- **完成标准**：再无重复 `GetLanguageCode()`；引擎无 game/BepInEx 依赖。
- **验证**：P0 全量快照不变；`./run.sh build` 通过。
- **依赖**：P1。

### P3 — `Loc` 目录 + 字体图集改造
- **改动**：
  1. 建 `Localization/Catalog/Loc.*.cs`，把各功能**已声明为静态 `LocalizedTextSet` 字段**的内容迁入对应 `Loc.X`；功能侧改引用 `Loc.X.Y`。
  2. **改造 `FontAtlasSample()`**：原反射 `typeof(HistoryPanelText).GetFields(...)` 失效（字段已搬走），改为枚举 `Loc` 对应分区收集字符串。
  3. **图集缓存键改为 `(languageCode, mode)`**，并在 `ChineseLocaleModeChanged` 订阅处失效缓存。
- **完成标准**：静态字段全部入目录；图集预热不丢中文字形。
- **验证**：P0 全量快照不变；**新增测试**断言"图集样本含预期 CJK 字形"且 mode 切换后样本随之变化。
- **依赖**：P2。

### P4a — 真硬编码块升格
- **改动**：`CombatStatusBar.Canvas.cs`、`CardSetPreviewModeStatusText.cs` 等纯硬编码/bespoke → `Loc` 条目；逐条做 **mode 敏感性分类**（`CardSetPreviewModeStatusText`、`BPPSupporterAttributionText` 属 mode 不敏感，须显式 TW/HK = 简体原文）。
- **验证**：P0 全量快照不变（**含 3 mode**）。
- **依赖**：P3。

### P4b — inline 字面量清扫（churn 最大，最后做）
- **改动**：`HistoryPanelText` 等里所有 inline `FormatSimple("...","...")` / `$"..."` 插值串 → `Loc` 条目 / 模式串；每条标注 mode 敏感性。
- **验证**：P0 全量快照不变（含 3 mode）。**任何不一致立即停，按 mode 敏感性分类修正，不得放过。**
- **依赖**：P4a。

### P5 — 删 shim + 全量测试
- **改动**：测试工程直 `ProjectReference` `BazaarPlusPlus.Localization`；**删除 `tests/CombatStatusBarState.Tests/TestChineseLocalizationShim.cs`**；删除 P0 临时快照工程（或转正为精简回归测试，遵循"无 coverage-theater"）。
- **验证**：`./run.sh test` 全绿；`./run.sh all` 通过。
- **依赖**：P2（删 shim 须在 P0 基线之后；建议放最后）。

## 6. 执行护栏

- 每包**独立 commit**，结束后**停下等人工 review** 再做下一包。
- 只改本地化相关文件；不动 `BazaarPlusPlus.csproj` 既有 build-and-copy 流程（仅做本设计所述的**新增**模块 wiring）；不动 `OptionsDialogLanguageRefreshPatch` 与字体**应用**链行为；`decompiled/` 只读。
- 行号会漂移——每次动手前用 `rg` 重新定位类型/方法。
- 每包跑最小相关验证（P0 快照 + `./run.sh build`；P5 才 `./run.sh test`）。提交前 `./run.sh format`；若 csharpier 顺带格式化了你改动外的文件，单独成 commit。
- 不写诊断脚手架；临时快照工程在 P5 删除或转为精简回归测试。
- 收尾遵循约定：自查 diff → commit（未要求不提前 commit）。

## 7. 一句话交接命令

> impl this design `docs/design/2026-06-03-localization-module-extraction-refactor-prompt.md` — 按 P0→P1→P2→P3→P4a→P4b→P5 顺序执行；只动本地化相关文件、不重构无关代码、保持用户可见文本在 8 语言×3 中文 mode 下逐串不变（以 P0 快照为准）、以实际代码为唯一事实（行号动手前用 rg 复核）；每包跑最小相关验证并 `./run.sh format`，然后停下等我 review 再做下一包。
