> **Status: IMPLEMENTED (historical).** Both halves shipped: fonts (Infrastructure/Fonts/, SourceHanSansCN-Regular.otf) and design tokens (Infrastructure/UiTokens/).

# Mod UI Typography & Design Token Foundation

> 本文档是 **背景与 Goal 文档**（"phase 0"），用来锁定问题边界、可验收标准与
> 范围红线。具体实现架构（文件清单、API 形状、迁移步骤、测试设计）由后续
> implementation spec 承接。

## Scope

把 mod 自绘 UI 的两条腐烂主线一次抽掉：

1. **字体**：所有 mod-authored UI 文本目前都走 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
   —— Unity 内置的 Arial 替代品，零 CJK 字模。HistoryPanel 在中文 locale 下当前
   **全部豆腐**。改为打包一份 SIL OFL 许可的 CJK 字体，做成全 mod 共享的字体
   服务，HistoryPanel + CombatStatusBar 同时切换。
2. **样式**：[HistoryPanelUiToolkitView.Style.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs)
   579 行纯内联 `.style.*` —— 没有 token 系统，没有类名约定，颜色、间距、圆角、
   边框值直接散在工厂方法里。改为引入 **design tokens 模式**（不是真 USS，见
   §Constraints 解释），把语义化的色板/间距/尺寸表抽出，工厂方法只引用 token。

两件事合并在一个文档里，是因为它们共享"先建一个 mod-wide UI 基础设施层、
HistoryPanel 是首个受益者"这条主轴。

## Background

### 当前状态：字体

**所有 mod 自绘 UI 文本字体回到 Unity built-in `LegacyRuntime.ttf`**。引用点：

| 文件 | 行 | 上下文 |
|---|---|---|
| [HistoryPanelUiToolkitView.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.cs) | 107 | UI Toolkit 根 panel 的 `_root.style.unityFont` |
| [HistoryPanelUiToolkitView.Style.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs) | 90, 106, 133, 156 | Label / Button / TextElement |
| [CombatStatusBar.Canvas.cs](../../../Game/CombatStatusBar/CombatStatusBar.Canvas.cs) | 633 | uGUI Canvas 战斗状态条 |

`LegacyRuntime.ttf` 是 Unity 引擎自带的 ASCII fallback（实际等同于 Liberation
Sans / Arial 复刻），**完全没有 CJK 字模**。

HistoryPanel 已经是一个深度本地化的面板。看 [HistoryPanelText.cs](../../../Game/HistoryPanel/HistoryPanelText.cs)
的 `LocalizedTextSet`：

```csharp
private static readonly LocalizedTextSet TitleText = new(
    "Game History",
    "对局历史",     // zh-CN
    "對局歷史",     // zh-TW
    "對局歷史"      // zh-HK
);
```

这种条目在 [HistoryPanelText.cs](../../../Game/HistoryPanel/HistoryPanelText.cs) 里有
~60 处。zh-CN / zh-TW / zh-HK locale 下，**这些文本现在全部渲染为 □□□ 豆腐**
——不是"未来才会"，是当前生产构建的实际行为。

**已有的死代码线索**：[HistoryPanelText.cs:747-788](../../../Game/HistoryPanel/HistoryPanelText.cs#L747-L788)
有一个 `FontAtlasSample()` 辅助方法，遍历所有 `LocalizedTextSet` 静态字段、提取
当前 locale 下的去重字符集，用于预热字体 atlas。该方法**没有任何 caller**
（`grep` 后全 mod 零引用），是前一次尝试解决 CJK 问题但未完成的残留物。说明：
（a）该问题被识别过；（b）一直没收尾。

### 当前状态：样式

[HistoryPanelUiToolkitView.Style.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs)
579 行可以拆为三类：

| 类别 | 代表方法 | 行数占比 | USS 化潜力 |
|---|---|---|---|
| 工厂样式（创建 + 静态样式） | `CreateSectionPanel`, `CreateListFrame`, `CreateSectionTitle`, `CreateChip`, `CreateLabel`, `CreateButton`, `CreateRowShell`, `CreateAccentBar`, `CreateRowContent`, `CreateInfoChip`, `CreateInlinePill`, `CreateDayBubble` 等 ~14 个 | ~55% | 高 —— 全部可走 token + helper |
| 状态绑定（运行时改样式） | `RefreshDeleteButton`, `BindHeroPill`, `BindBattleRankPill`, `ConfigureStatusPill`, `BindRunOutcomeBubble`, `RefreshTabButton`, `RefreshGhostFilterButton`, `StyleButton` 等 ~8 个 | ~30% | 中 —— 留在 C#，但内部颜色文字色 tuple 走 token |
| 查表辅助 | `GetRankBadgePalette`, `GetHeroBadgeStyle`, `BuildHeroBadgeStyle`, `ColorFromRgb` ~4 个 | ~15% | 低 —— 纯数据，无样式 |

可量化的重复：

- `new Color(...)` 字面量出现 ~50 次；其中 `0.98f` 透明度反复出现 ~15 次，
  `new Color(0.24f, 0.29f, 0.38f, 0.55f)`（list-frame 边框色）出现 4 次
- `borderTop/Right/Bottom/LeftRadius` 这种四向圆角块 ~7 处，每块 4 行重复
- `borderTop/Right/Bottom/LeftWidth = 1f` ~8 处
- pixel literal: `10f` 圆角 / `14f` 内边距 / `32f` chip 高度 / `86f` chip 最小宽
  ——多处复制粘贴

同样的内联样式模式还泄漏到 [.Tree.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs)
和 [.Rows.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Rows.cs)；
HistoryPanel 整个 UI Toolkit 层都是这个调调。

**全 mod 范围**：

- `grep -rn "StyleSheet\|.uss\|FromUss\|AddToClassList"` → **零命中**
- mod 自绘 UI Toolkit 体系内**从未引入过 stylesheet / class-list 任何痕迹**
- HistoryPanel 是 mod 唯一深度使用 UI Toolkit 的面板；CombatStatusBar 走 uGUI Canvas mode

### 为什么是现在

- **HistoryPanel native rendering migration 刚落地**（spec 已清理，决策见 [ADR-0003](../../adr/0003-history-panel-preview-overlay.md)），
  之前的卡牌渲染分歧解决了，下一层视觉债务自然轮到字体与样式
- **宽屏问题 [history-panel-known-issues.md #1](../../features/history-panel.md) 的第 4 项**
  （行内组件像素硬编码，[Style.cs:74](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs#L74)）
  本质上是 token 表覆盖范围 —— 不在 token 化基础上谈宽屏适配，宽屏 spec 会被迫先做一次 token 抽离
- **CJK 豆腐是当前生产 bug**，不是预防性修复
- 未来 mod 新增任何 UI 面板，都会继续抄 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
  + 内联 `.style.*` 的反模式；不立基础设施就一直交利息

## Goals

### G1 — Bundle 一份 CJK 字体，全 mod 替代 `LegacyRuntime.ttf`

**可验收标准**：

1. mod 主程序集（不含 decompiled、tests）`grep` `LegacyRuntime.ttf` 返回 **零命中**
2. 字体加载收口到单一 accessor（命名暂定 `BppUiFont.Default`，具体路径
   implementation spec 定）；任何 UI 拿字体都经此入口
3. zh-CN / zh-TW locale 下打开 HistoryPanel，title、subtitle、所有
   `LocalizedTextSet` 文案、replay/delete/close 按钮、英雄 chip、battles 段标题
   **全部可见、零豆腐**
4. CombatStatusBar 切到同一 accessor；战斗内自绘状态条文本同样无豆腐
   （即使 CombatStatusBar 目前不显示中文，验证 accessor 在 uGUI 路径下也可
   用 —— mod-wide 基础设施的核心约束）
5. mod DLL 大小增长有明确预算（implementation spec 标注，量级 ~3–10 MB
   depending on 字体子集化策略）
6. 字体许可声明加在 mod 公开发布物（sibling repo `BazaarPlusPlus/`）的
   README / installer 关于页

**非要求**：

- 不要求与游戏自身 TMP_FontAsset 视觉一致 —— mod 拥有自己的视觉身份
- 不要求多字重；Bold 接受 Unity legacy Font 合成（见 §Risks）
- 不要求覆盖 UI Toolkit + uGUI 之外的渲染路径（例如游戏 TMP 文本不动）

### G2 — Design tokens 体系，取代 579 行内联样式

**可验收标准**：

1. [HistoryPanelUiToolkitView.Style.cs](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs)
   行数压缩到 **≤ 200 行**（只剩状态绑定 + 数据查表，工厂样式落到 token + helper）
2. 全 HistoryPanel UI Toolkit 层（Style.cs / Tree.cs / Rows.cs / root view 共
   4 个 partial）的 `new Color(...)` 字面量、像素 literal、圆角块 **零硬编码**，
   全部经 token 解析
3. Token 表按维度分文件组织：色板、间距、尺寸、圆角、边框 —— 命名按**语义**
   而非外观（`PanelBackground` 而非 `DarkBlue`）
4. Token 表与 uGUI 路径兼容：CombatStatusBar 至少有一处颜色 / 间距迁到同一
   token 表，证明这个层不是 UI-Toolkit-only 的（这是 mod-wide 基础设施的
   核心约束）
5. 新增一个 UI 面板（在 sample / 测试范围内验证即可，不必新增正式面板）
   只引用 token + helper、不写裸 `.style.<prop> = <literal>`

**非要求**：

- 不引入真 USS / `StyleSheet` ScriptableObject / AssetBundle 资源管线
  （见 §Constraints 解释为什么）
- 不引入选择器级联、伪类、媒体查询（C# token 系统结构上不支持）
- 不引入运行时 hot reload
- 不实现 theme switching（light / dark / 高对比）—— token 表为未来留口，本期
  只 ship 单一暗主题

### G3（次级）— 消除 known-issues #1 第 4 项的硬编码像素

**可验收标准**：

1. Token 表显式提供尺寸语义：`Sizes.ChipMinWidth`, `Sizes.ChipHeight`,
   `Sizes.ButtonStandardHeight`, `Sizes.DayBubbleSize` 等
2. HistoryPanel 全部 chip / pill / button / bubble 的 `minWidth` / `maxWidth` /
   `width` / `height` 由 token 解析
3. [history-panel-known-issues.md](../../features/history-panel.md)
   §1 的第 4 项（"行内组件像素硬编码"）可以在本工作落地后从 known-issues
   降级或勾除（**注意**：known-issues #1 的 1/2/3 项 —— 根容器写死像素、
   `screenMatchMode` 未设置、preview RT 比例 —— 不在本 spec 解决，是宽屏
   spec 的工作）

**非要求**：

- 不引入百分比布局 / responsive layout（这是宽屏 spec 的范围）
- 不动 [HistoryPanelUiToolkitView.Tree.cs:23-24](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs#L23-L24)
  的 `panel.style.width = 1280f; panel.style.height = 1020f;`（同上）

## Non-goals

明确划出去、避免 scope creep：

- 不重新设计 HistoryPanel / CombatStatusBar 的视觉外观；现有深蓝 + 浅蓝高亮的
  色调原样保留，token 化只是给它们起名字
- 不引入 theme / skin 切换（无 light mode、无 user-customizable color）
- 不引入真正的 USS / stylesheet 资源管线
- 不引入运行时 hot reload
- 不解决 known-issues #1 的根容器宽屏问题（1280×1020 写死、`screenMatchMode`
  缺省、preview RT 4.2:1 写死等）
- 不替换游戏自身用 TMP_FontAsset 渲染的部分
- 不打包多个字重 —— 只 ship Regular，Bold 走合成
- 不实现 fallback 链 / 多 locale 字体切换 —— 单一 CJK 字体覆盖中英日韩
  即可（Noto Sans CJK / Source Han Sans 一份就够，欧文字符与 CJK 同字体）

## Constraints

### 技术硬约束

#### 1. Unity 运行时没有公开 USS 解析 API

`UnityEngine.UIElements.StyleSheet` 是 `ScriptableObject`，必须从 asset 加载，
不能从字符串构造。`StyleSheetImporter` / `StyleSheetParser` 这些反序列化
入口位于 `UnityEditor.UIElements` 命名空间，**runtime / player build 不存在**。

可选实现路径只有：

| 路径 | 评估 |
|---|---|
| AssetBundle 烘焙 USS asset，运行时 `AssetBundle.LoadFromMemory` + `LoadAsset<StyleSheet>` | ✅ 可行，但**需要新建 Unity Editor 项目作 build tool**，给 mod 仓库引入一条新的构建依赖链 |
| 反射构造 `StyleSheet` ScriptableObject + 填充 internal 字段 | ❌ 跨 Unity 小版本极易坏；游戏后续升级 Unity 时随时炸 |
| **Design tokens 模式：纯 C# 提供语义常量 + 工厂 helper + class-name-like 标志位** | ✅ **选定路径**。零新增基础设施、零 Unity 反射、无版本兼容风险 |

**结论**：本 spec **不引入真 USS**。"design tokens" 提供 USS 系统 80% 的维护收益
（集中化 + 语义命名 + 复用 + 易改）而不付出 5% 的基础设施成本，代价是失去
选择器级联与 hot reload —— 这两者本就是 non-goal。

#### 2. `IStyle.unityFont` 接受 `UnityEngine.Font`，不接受 `TMP_FontAsset`

HistoryPanel UI Toolkit 走 legacy uGUI `Font`，CombatStatusBar 走 uGUI `Text`
组件也是 legacy Font。两者**字体类型一致** —— 这是把它们装进同一个 accessor
的前提。游戏自身用 TMP，我们不互通。

#### 3. `EmbeddedResource` 模式已落地

[BazaarPlusPlus.csproj:21](../../../BazaarPlusPlus.csproj#L21) 现有 `<EmbeddedResource Include="Data/BuildRecommendations/final-builds-top50.json" />`，
~1.1 MB 嵌入主 DLL 已经被验证 work。字体走同样路径，无需新增 csproj 玄学。

#### 4. Bold 字重通过 Unity legacy Font 合成

`IStyle.unityFontStyleAndWeight = FontStyle.Bold` 在 legacy Font 路径下由
Unity 引擎做位图描边模拟。中文字符合成的效果一般（笔画偏粗、不锐利），但
HistoryPanel `Bold` 用得不多（标题 + chip 数字 + 部分按钮文字）。本 spec
**接受合成 Bold**，若视觉评审不通过，再单独 ship Bold 字重（列为 risk，
不阻塞 Goal）。

### 许可与合规

- 选定字体必须是 **SIL Open Font License 1.1** 或同等许可（候选：Noto Sans
  CJK SC / Source Han Sans Subset / LXGW WenKai）
- README + installer about 页加字体许可声明（具体放置位置由 implementation
  spec 定）

### Build / packaging

- 字体作为 `<EmbeddedResource>` 嵌入 `BazaarPlusPlus.csproj`
- `bazaarplusplus-mod/run.sh build` 流程不变
- installer 的 `bazaarplusplus-installer/src-tauri/resources/` 不需要单独处理
  —— 字体已经在 mod DLL 内

## High-level approach

> 仅 sketch；详细文件清单与 API 形状交 implementation spec。

### G1 字体

1. 选定字体（候选见 §Open questions）放到 `Resources/Fonts/<font>.ttf` 或
   等价路径，加 `<EmbeddedResource>` 条目
2. 新建一个全 mod 共享的字体服务（位置 / 命名 implementation spec 定，
   预期落在 `Infrastructure/Fonts/` 或 `GameInterop/` 下），暴露
   `BppUiFont.Default` 之类的 lazy `Font` accessor
3. 服务内部：从 `Assembly.GetManifestResourceStream(...)` 读出字节 →
   `Font.CreateDynamicFontFromOSFont` 或 `new Font` from bytes（实现细节
   决定走哪条 —— Unity 文档有坑，需 spike 验证）
4. HistoryPanel 5 处 + CombatStatusBar 1 处 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
   替换为 `BppUiFont.Default`
5. HistoryPanel 内部 `GetUiFont()` 私有方法删除（去重）
6. **决定 [HistoryPanelText.FontAtlasSample()](../../../Game/HistoryPanel/HistoryPanelText.cs#L747)
   的命运**：
   - 如果新字体是 dynamic Font 且需要 atlas 预热 → 在面板挂载时调用一次预热
   - 如果新字体已包含足够字模、无需预热 → 删除该 dead helper

### G2 Tokens

1. 新建 token 命名空间（暂定 `BazaarPlusPlus.Infrastructure.UiTokens` 或
   `BazaarPlusPlus.Game.Common.UiTokens`，路径由 implementation spec 决定）
2. 按维度拆文件：
   - `Colors.cs` —— 语义化色板（`PanelBackground`, `ListFrameBackground`,
     `ChipBackground`, `BorderSoft`, `AccentTitle`, `TextPrimary`,
     `TextSecondary`, `RankBronze/Silver/Gold/Diamond` 等）
   - `Spacing.cs` —— 间距阶梯（`Xs = 4f`, `Sm = 6f`, `Md = 10f`,
     `Lg = 14f` 等）
   - `Sizes.cs` —— 组件尺寸（`ChipMinWidth = 86f`, `ChipHeight = 32f`,
     `ButtonHeight = 32f`, `DayBubbleSize = 36f` 等）
   - `Radii.cs` —— 圆角阶梯（`Sm = 6f`, `Md = 10f`）
   - `Borders.cs` —— 边框宽度 + 色对（`SoftDivider`, `SectionEdge` 等）
3. 提供 helper API 减少模板代码：
   - `Borders.ApplyUniform(IStyle s, float width, Color color)`
   - `Radii.ApplyUniform(IStyle s, float radius)`
   - `Padding.ApplyUniform(IStyle s, float padding)`
   - 类似的 一行 helper 把 4 行重复打包成 1 行
4. HistoryPanel `Style.cs` 重写：工厂方法变薄 —— 调 helper + 引 token，
   状态绑定方法保留逻辑但 hardcoded color tuple 走 token
5. `.Tree.cs` / `.Rows.cs` / root view 同样 sweep
6. CombatStatusBar 取一两个颜色 / 一个间距迁到 token，证明 uGUI 路径兼容
   （uGUI `Image.color` / `RectTransform` 间距字段都接受 `Color` / `float`，
   token 表只暴露这两个类型；天然兼容）

### G3 像素硬编码

包含在 G2 的扫描里 —— token 表覆盖尺寸维度即自然消除硬编码。落地 check：
[Style.cs:74](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs#L74)
的 `chip.style.minWidth = 86f`、[Style.cs:97-106](../../../Game/HistoryPanel/HistoryPanelUiToolkitView.Style.cs#L97-L106)
的 button 固定宽高等条目应被 token 引用替换。

## Risks

| 风险 | 等级 | 缓解 |
|---|---|---|
| 字体合成 Bold 视觉差 | 中 | 接受、记录；不通过则单独 ship Bold variant，约 +3–10 MB |
| `Font.CreateDynamicFontFromOSFont` vs `new Font(bytes)` 路径在 UI Toolkit `IStyle.unityFont` 下行为差异 | 中 | implementation spec 必须先做 spike：建一个 throw-away 测试面板，挂上 embedded 字体、显示中英混排，跑 Debug build 确认 |
| Token 表过早抽象 / over-engineering | 中 | 严格"出现 ≥3 次才抽 token"原则；单次出现的 magic number 留原地，由 review gatekeep |
| CombatStatusBar uGUI 路径与 UI Toolkit token 表兼容性 | 低 | Token 表只暴露 `Color` / `float`，两者均原生接受 |
| 字体首帧 fall back 显示豆腐（懒加载未完成） | 低 | `BppUiFont.Default` 在 plugin 启动时 warm 一次；HistoryPanel 本身不在启动期开 |
| Embedded 字体 ~3–10 MB 影响 mod 启动时间 / 内存 | 低 | 已有 1.1 MB JSON embedded 无问题；字体只在首次 accessor 调用时 inflate |
| 选定字体 OFL 合规性误判 | 低 | implementation spec 选定字体后在 PR 描述附许可全文链接由 review 确认 |
| `FontAtlasSample()` dead code 误判 | 低 | implementation spec 决定保留 / 删除前先确认整个仓库（含 worktree、tests）都不引用 |

## Open questions for implementation spec

以下问题不阻塞本 background doc 落地，但 implementation spec 必须给答案：

1. **选哪一款字体**：Noto Sans CJK SC Regular（~10 MB） vs Source Han Sans
   Subset（~5 MB） vs LXGW WenKai（手写体，~7 MB） vs 子集化后的 Noto
   （~2 MB，仅 GB2312 常用字 + Latin）。视觉感、合规性、体积三向 trade-off。
2. **字体加载技术路径**：`new Font(byte[])` 是 legacy uGUI API，但在 UI
   Toolkit `IStyle.unityFont` 下是否完整 work？需要 spike 验证。
3. **token 命名空间位置**：`Infrastructure/UiTokens/` vs `GameInterop/UiTokens/`
   vs `Game/Common/UiTokens/`。
4. **字体服务挂载方式**：纯静态懒加载（如 `BppUiFont.Default` getter） vs
   `IBppMountable` 启动期 warm。CLAUDE.md 提的 IBppMountable 适合带 game-DLL
   依赖的子系统；字体本身不依赖 game DLL，倾向静态。
5. **`FontAtlasSample()` 处理**（[HistoryPanelText.cs:747](../../../Game/HistoryPanel/HistoryPanelText.cs#L747)）：
   embedded 字体是否需要它预热？还是删除？
6. **Style.cs 拆分**：状态绑定方法（`BindHeroPill`, `ConfigureStatusPill` 等）
   留在原文件，还是拆到 `HistoryPanelUiToolkitView.Bindings.cs`？
7. **CombatStatusBar 迁移粒度**：本 PR 一并切（字体 + 一两个 token），还是
   字体一刀切、token 等下一个 PR？

## 工作量估算

> 粗略下限；implementation spec 应基于 §Open questions 的答案给出精算。

| 子项 | 估算 |
|---|---|
| 字体 spike（验证加载路径） | 0.5 天 |
| 字体集成 + accessor + 全 mod 替换（G1） | 0.5 天 |
| Token 表设计与命名（G2） | 1 天 |
| Style.cs / Tree.cs / Rows.cs / root view sweep（G2） | 1 天 |
| 硬编码像素扫除（G3，融入 G2） | 0 天（包含在 G2） |
| CombatStatusBar token 兼容性验证 | 0.5 天 |
| 中文 locale 视觉验收 + 截图证据 | 0.5 天 |
| **合计** | **~4 天** |

## 关键不变量

1. **mod 主代码**全部 `LegacyRuntime.ttf` 引用都死掉；任何新加 UI 默认拿到
   带 CJK 的字体。
2. **token 表对 uGUI 与 UI Toolkit 双兼容** —— 它只暴露 `Color` / `float`，
   不依赖 UI Toolkit 类型。
3. **HistoryPanel 视觉外观不变** —— token 化只是给现有数值起名字，不是
   重设计。
4. **不引入新的运行时依赖** —— 没有 AssetBundle、没有 Unity Editor build
   pipeline、没有 USS 资源、没有反射 hack 到 Unity internal。
5. **CJK 验收必须有真实证据** —— 不允许"看起来应该 work"通过；implementation
   spec 必须包含一张 zh-CN HistoryPanel 截图。

## 后续 spec 列表（不在本 doc 范围）

- 字体 + Token 的 implementation spec（基于本 doc 的 Goal + open questions）
- 宽屏 / 超宽屏适配 spec（[history-panel-known-issues.md](../../features/history-panel.md)
  §1 的 1/2/3 项 + preview RT 比例自适应）
- 英雄 badge 改用游戏立绘 sprite（前一份 native rendering migration spec
  列出的 future work）
- Theme switching（如果将来真的需要 light mode）
