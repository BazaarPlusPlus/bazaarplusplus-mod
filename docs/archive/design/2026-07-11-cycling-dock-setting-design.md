# 循环设置项深模块（CyclingSettingsDockEntry\<T\>）设计稿 · rev2

状态：红队评审已回收并修订（3 视角均 sound-with-revisions），待用户确认后实现。
来源：2026-07-11 架构评审候选 1 + grilling 第一轮决策 + 红队修订。

## 背景与问题

「按序循环一个配置值、越省缺即高亮、渲染本地化状态」这一个概念，目前在 settings dock 里有 3 个互不相认的半成品抽象和 5 份手写副本：

- `PreviewVisibilityModeDockEntry`（抽象基类，仅 1 个子类 `ItemEnchantPreviewSettingsDockEntry`）
- `VoiceSubtitlesFontScaleSettingsDockEntry`（第二个结构相同的抽象基类，2 个子类）
- `SettingsMenuToggleBridge` + `BppSettingsDockDefinition` 桥接构造重载（bool 特例，附带 2 个纯转发桥接子类）
- 手写循环器 ×5：`UiFontSettingsDockEntry`、`ChineseLocaleModeSettingsDockEntry`、`LegendaryPositionSettingsDockEntry`、`VoiceSubtitlesSettingsDockEntry`（复合）、`VoiceSubtitlesPositionSettingsDockEntry`

每个都重复实现：读配置带默认值防护、取下一个值、写回带空值防护、越省缺高亮判定。每加一个设置行就再抄一遍。

## 已确认的决策（grilling 第一轮，2026-07-11）

1. **吞并范围 = 循环器 + bool（13/16 条目）**。动作按钮（HistoryPanel、HotkeyTutorial）与锁定开关（EndOfRunScreenshot，其「被强制锁开」是 `EndOfRunScreenshotSettingsPolicy` 跨条目策略）不进深模块，保持手写全参定义。
2. **文案先保真，后统一**。本次重构逐条目原样保留现状状态文案（含四不像：简体-only switch / 完整简繁 / 英文 "ON"/"OFF" 字面量 / 无本地化数字）。现有测试钉住的字符串一个不变。文案统一是独立后续 PR。
3. **形态 = 泛型条目类 + 特征工厂**。每个功能保留一个文件，内容退化为静态工厂方法；数据的局部性留在功能目录，组合根不膨胀。
4. **刺头处理**：中文模式 → `onChanged` 槽位 + 工厂收 `IBppEventBus`；字幕复合 → 读写委托而非 `ConfigEntry<T>`；局末截图 → 深模块外；float 阶梯 → ~~epsilon 比较器~~ 红队修订为 `nextOverride`（见下）。

## 红队修订记录（rev2 变更，2026-07-11）

| # | 红队发现 | 采纳的修订 |
|---|---|---|
| R1 (fidelity/major) | 附魔预览未知枚举现状 `未知→Auto`（`NextPreviewVisibilityMode` default 臂），`NextOf 未知→ladder[0]=Off` 是第二个未声明偏差 | 引擎增加可选 `Func<T,T>? nextOverride`；附魔预览传 `BppSettingsDockCatalog.NextPreviewVisibilityMode`（该静态**从删除清单移除**），零偏差 |
| R2 (api/major) | epsilon `IEqualityComparer<float>` 违反哈希契约，且对全部精确可表示的阶梯值是行为空操作；index+1 还丢掉 float「首个严格更大」语义 | **删除 comparer 参数**；两个字号条目传 `nextOverride = NextScale`（现语义原样），float 边缘偏差（1.3→1.0）**消除** |
| R3 (api/major) | bool 条目走全参构造比被删桥接更啰嗦（5 个调用点变冗长） | 增加静态便捷工厂 `Toggle(order, key, resolveLabel, read, write, onChanged = null)`，内部供给 ladder{false,true} + `highlightWhen: v=>v` + `"ON"/"OFF"` 文案 |
| R4 (api/major, fidelity/minor) | onChanged 无条件触发 ≠ 传奇位置现状（刷新在 null-config 早退之后） | 保持 onChanged「activate 后无条件」这一简单契约；传奇位置 null-config 时多一次无害 UI 刷新，列为**唯一已知偏差**（生产不可达：该 ConfigEntry 始终绑定） |
| R5 (api/minor) | `write` 与 `onChanged` 两个副作用槽无边界规则 | 在 XML-doc 写明契约：`write` = 持久化 + 耦合持久化（如 ForceEnabled 连带写另一配置）；`onChanged` = 对变化做反应（事件发布 / UI 刷新 / 泵触发） |
| R6 (api/minor) | 命名 `CyclingDockSettingEntry` 破坏邻居的 `SettingsDock` 词序 | 更名 **`CyclingSettingsDockEntry<T>`**（对齐 `ISettingsDockEntry` / `SettingsDockEntryRegistry` / `*SettingsDockEntry`） |
| R7 (landing/blocker ×3, fidelity/minor ×2) | 测试迁移清单严重不全 | 测试计划全面重写（见下），含 csproj Compile-Include 修改步骤、`new`→`Create()` 机械改写清单、EventPreview `?? true` 陷阱 |
| R8 (landing/minor) | `VoiceSubtitlesSettingsDockEntry.RegisterAll(SettingsDockEntryRegistry)` 有 2 个存活测试依赖其静态签名 | 迁移后的静态类保留同名同签名 `RegisterAll` |

## 新模块（rev2）

位置：`src/BazaarPlusPlus/Game/Settings/CyclingSettingsDockEntry.cs`

```csharp
internal sealed class CyclingSettingsDockEntry<T> : ISettingsDockEntry
{
    internal CyclingSettingsDockEntry(
        int order,
        string key,
        Func<string, string> resolveLabel,          // 保持现签名，功能侧 label 类不动
        IReadOnlyList<T> ladder,                    // 循环顺序即列表顺序
        Func<IBppConfig, T> read,                   // 默认值防护/未知值归一化/复合读取都在这里
        Action<IBppConfig, T> write,                // 空值防护/复合写入/耦合持久化（ForceEnabled）在这里
        Func<T, bool> highlightWhen,                // 高亮判定，独立于配置默认值
        Func<T, string, string> resolveStatus,      // (value, languageCode) → 状态文案，保真期各留各的实现
        Func<T, T>? nextOverride = null,            // 非阶梯循环语义（float 首个更大值 / 附魔预览未知→Auto）
        Action<T>? onChanged = null)                // activate 后无条件同步回调，传新值：事件/UI 刷新/泵

    public int Order { get; }
    public BppSettingsDockDefinition Build(IBppConfig config);

    // bool 便捷工厂：ladder{false,true} + highlightWhen v=>v + "ON"/"OFF" 字面量（保真）
    internal static CyclingSettingsDockEntry<bool> Toggle(
        int order, string key, Func<string, string> resolveLabel,
        Func<IBppConfig, bool> read, Action<IBppConfig, bool> write,
        Action<bool>? onChanged = null);
}
```

`Build` 语义（全部闭包捕获本次调用的 `config`，条目无实例状态，可安全多次 `Build`）：

- `resolveStatus: lang => resolveStatus(read(config), lang)`
- `isActive: () => highlightWhen(read(config))`
- `activate: () => { var next = Next(read(config)); write(config, next); onChanged?.Invoke(next); }`
- `collapseAfterActivate` 恒为 `false`。

**循环规则** `Next(current)`：`nextOverride` 非空则直接调用；否则在 `ladder` 中用 `EqualityComparer<T>.Default` 定位 `current`，返回下一项，末项回绕首项，**找不到回退 `ladder[0]`**（13 个条目中仅 bool 与规整 enum 走此路径，未知值均已在 read 或 nextOverride 中处理）。

## 13 个条目的迁移表（rev2）

| 条目 | 形态 | ladder / nextOverride | read 空值回退 | highlightWhen | status（保真） | onChanged |
|---|---|---|---|---|---|---|
| EnchantPreview | Cycle | ladder Off→Auto→Always；**nextOverride = `BppSettingsDockCatalog.NextPreviewVisibilityMode`**（未知→Auto 原样） | `?? BppConfig.DefaultEnchantPreviewMode` | `!= Off` | `ResolvePreviewVisibilityModeStatus`（静态保留） | — |
| LegendaryPosition | Cycle | Default→Blank→999999→P\|R | `?? Default` | `!= Default` | 现状 IsChinese switch 原样入 lambda | `LegendaryPositionUiRefresh.TryRefreshVisibleDisplays`（见偏差 D1） |
| ChineseLocaleMode | Cycle | Mainland→Taiwan | read 内 `ChineseScriptConverter.NormalizeMode`（钉住「(mode)2 视为 Taiwan」） | `!= Mainland` | `ResolveModeStatus`（"CN"/"TW"） | `eventBus.Publish(new ChineseLocaleModeChanged())`；工厂 `Create(IBppEventBus)` |
| UiFont | Cycle | LxgwWenKai→SansSerif | read 内未知归一默认（钉住 `(kind)99`） | `!= DefaultUiFontKind` | 现状 LocalizedTextSet | — |
| VoiceSubtitlesPosition | Cycle | TopLeft→TopRight→TopCenter | `?? TopCenter` | `!= TopCenter`（默认 ≠ 阶梯首项，现状如此） | 现状 LocalizedTextSet | — |
| VoiceSubtitles（复合） | Cycle | Off→Both→Chinese→English | read/write = 现 `ReadMode`/`WriteMode` 原样变委托（两配置 lockstep） | `!= Off` | 现状 LocalizedTextSet | — |
| 英/中字号 ×2 | Cycle | ladder {1…2.5}；**nextOverride = 现 `NextScale`（首个严格更大 + 容差，原样）** | 原样 | `\|v−默认\|>0.0001f`（epsilon 留在 lambda） | `"0.##"+"x"` 不本地化 | — |
| NameOverride | **Toggle** | — | `?? false` | （工厂内置） | （工厂内置 "ON"/"OFF"） | `NameOverrideUiRefresh.TryRefreshVisibleHeroBanners` |
| EventPreview | **Toggle** | — | **`?? true`（唯一非 false 回退，勿套模板）** | 同上 | 同上 | — |
| CombatStatusBar | **Toggle** | — | 经 `CombatStatusBar.Get/SetEnabledSettingValue` 静态（保留间接层），`?? false` | 同上 | 同上 | — |
| FixedSupporterList | **Toggle** | — | `?? false`；write = 现 `WriteEnabled`（含 ForceEnabled 耦合持久化） | 同上 | 同上 | — |
| BazaarDbUpload | **Toggle** | — | `?? false`；write 含 ForceEnabled | 同上 | 同上 | 现 `OnEnabledChanged`（开启时 ArmImmediate） |

约束：迁移后的 `VoiceSubtitlesSettingsDockEntry` 静态类保留 `internal static void RegisterAll(SettingsDockEntryRegistry)` 原签名（R8）。

## 删除清单（rev2）

- `PreviewVisibilityModeDockEntry`（抽象基类）
- `VoiceSubtitlesFontScaleSettingsDockEntry`（抽象基类）
- `SettingsMenuToggleBridge` + `CombatStatusBarSettingsMenuBridge` + `NameOverrideSettingsMenuBridge`
- `BppSettingsDockDefinition` 的桥接构造重载（`:8-20`）——只保留全参构造

**保留**（rev2 变更）：`BppSettingsDockCatalog.NextPreviewVisibilityMode`（作为附魔预览 nextOverride，R1）与 `ResolvePreviewVisibilityModeStatus` 及其 Theory；`EndOfRunScreenshot`/`HistoryPanel`/`HotkeyTutorial` 手写定义；`ISettingsDockEntry`/`SettingsDockEntryRegistry`/`BppSettingsDockCatalog.Install` 不动。

## 行为保真承诺与已知偏差（rev2）

- 全部 16 条目的 key、Order、label、状态文案、循环顺序（含未知值循环目标）、高亮判定、副作用时序与现状一致；现有测试钉住的字符串不变。float「首个更大值」与附魔预览「未知→Auto」经 `nextOverride` 原样保留——rev1 的两个偏差已消除。
- **D1（唯一已知偏差，生产不可达）**：`LegendaryPositionDisplayModeConfig == null` 时，现状跳过 UI 刷新（刷新在 null 早退之后），新引擎 onChanged 无条件触发 → 多一次无害刷新。该 ConfigEntry 在 `BppConfig.Initialize` 恒绑定（`BppConfig.cs:178`），接受此偏差以换取「onChanged = activate 后无条件」的简单契约。

## 测试计划（rev2，按红队 blocker 全面重写）

**新增**（进 `SettingsDockRegistry.Tests`）：
- 引擎单测：回绕、未知值回退 ladder[0]、nextOverride 优先、onChanged 写后带新值触发、highlight/status 走 read、Toggle 工厂三件套。
- 逐条目契约 Theory（数据驱动，一行一条目）：钉 **(key, Order, label zh/en, 完整循环状态序列, 高亮序列)**——Order 并入此 Theory 是关键：`SettingsDockEntries_use_named_order_constants` 是 LegendaryPosition/NameOverride/ItemEnchantPreview/CombatStatusBar 四个类型的**唯一现有覆盖**，替换时不得丢 Order 断言。

**机械改写（`new Foo()` → `Foo.Create()` / `Toggle` 工厂）**——rev1 误标为「原样存活」：
- `SettingsDockEntries_use_named_order_constants`（:689-743，构造 12 个迁移类型）→ 并入逐条目 Theory
- `SettingsDockCatalog_sorts_…`（:745-783，构造 4 个）→ 改工厂调用
- `EnablingUploadOrStreamMode_forcesEndOfRunScreenshotOn` Theory（:327-370，构造 2 个）→ 改工厂调用，断言不变

**原样存活（7）**：注册表 3、`BppConfig` 迁移、`EndOfRunScreenshotDockEntry_doesNotTurnOffWhileUploadOrStreamModeIsOn`（构造的是不迁移的截图条目）、HotkeyTutorial ×2、`ResolvePreviewVisibilityModeStatus` Theory、`RegisterAll` 顺序测试（依赖 R8 签名保留）。

**`CombatStatusBarState.Tests` 全量迁移步骤（landing blocker）**：
1. 6 个受影响测试方法：`:223-234`（构造 `CombatStatusBarSettingsMenuBridge`）、`:236-253`（共享 onChanged 断言）、`:255-271`（`:260` 用被删的 2 参定义构造）、`:318-336`（`NameOverrideSettingsMenuBridge` 每次变更刷新断言）等——逐一判定：行为已被新引擎单测覆盖的**删除**；`NameOverride 刷新每次触发`与`onChanged 共享`两条断言在 SettingsDockRegistry.Tests 的引擎/条目测试中**重建等价覆盖**后删除原测试。
2. csproj `Compile-Include` 修改：删 `SettingsMenuToggleBridge.cs`（:36）、`CombatStatusBar.SettingsMenuBridge.cs`（:44）、`NameOverride.SettingsMenuBridge.cs`（:64）三条；`BppSettingsDockDefinition.cs`（:40）保留（文件重塑不删除）。该项目无主程序集 ProjectReference，靠源文件拷贝编译——**先改 csproj 再删文件**，否则 MSBuild 文件解析直接红。

**验证命令**：`dotnet test tests/SettingsDockRegistry.Tests/...` + `dotnet test tests/CombatStatusBarState.Tests/...`（先确认其测试形态）+ 全量 `./run.sh test` + `./run.sh build`。注意 run.sh test 的退出码陷阱：grep 输出中的 "Failed test projects:"。

## CONTEXT.md 新词条（实现时一并提交）

> **Cycling Settings Dock Entry（循环设置项）**：settings dock 中「点击在有序值阶梯上循环、越省缺即高亮、渲染本地化状态」的统一概念，由 `CyclingSettingsDockEntry<T>` 承载；功能侧只贡献数据（阶梯 + 读写 + 文案 + 可选 nextOverride/onChanged）。bool 开关是 `Toggle` 工厂承载的二值特例。动作按钮与锁定开关（局末截图的强制锁开策略）不属于此概念。

## 不做的事

- 文案统一（繁体补齐 / ON/OFF 本地化）→ 后续独立 PR。
- 动作按钮建模、锁定语义（lockedWhen）建模 → 明确不做（策略不进深模块）。
- `SettingsMenuToggleInstaller.cs` 文件名过期（内容是 `SettingsMenuLayoutUtility`）→ 与本次无关，不顺手改。
