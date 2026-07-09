---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at 78b5ba25 (2026-07-06, "Add UI font selection setting"); all plan steps shipped verbatim — BppUiFontKind KAI/SANS provider + UiFontSettingsDockEntry.

# UI 字体选择（楷体 / 非衬线）实现计划

日期：2026-07-06　状态：待确认，未实现

## 背景

`ce20923b`（Improve UI typography）起，mod 自绘 UI 统一切到了嵌入的 LXGW WenKai（霞鹜文楷，楷体风格）。此前 UI Toolkit 面板用的是 Unity 内置 `LegacyRuntime.ttf`（Liberation Sans，非衬线；CJK 依赖动态字体的 OS 回退，macOS 上落到苹方、Windows 上落到微软雅黑）——证据：`ce20923b` 中被替换的 `GetUiFont()` 原实现是 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`。

本计划新增一个用户可选项：**楷体（LXGW WenKai，保持默认）** 与 **非衬线（LegacyRuntime.ttf，即 pre-ce20923b 行为）** 二选一。

### 当前字体管线盘点（file:line 证据）

字体消费分三条互不相干的管线，本计划只动第 1 条：

1. **UI Toolkit / UGUI 面板管线（本计划的生效范围）** — 全部通过静态 `BppUiFont.Default`（[BppUiFont.cs:19](../../src/BazaarPlusPlus/Infrastructure/Fonts/BppUiFont.cs)）取字体：
   - HistoryPanel：[HistoryPanelUiToolkitView.Style.cs:180](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Style.cs)（`GetUiFont() => BppUiFont.Default`）、[HistoryPanelUiToolkitView.cs:175](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs)（glyph 预热）
   - CollectionPanel：[CollectionPanelView.cs:208](../../src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs)、[CollectionPanelView.Filters.cs:420](../../src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs) 等
   - 网格徽章（UGUI Text）：[CollectionSourceAttributionBadge.cs:71](../../src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionSourceAttributionBadge.cs)
   - LiveBuildPanel：[LiveBuildPanelView.cs:99](../../src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs) 等
   - Supporters 行：[BPPSupporterAttributionRow.cs:76](../../src/BazaarPlusPlus/Game/Supporters/Ui/BPPSupporterAttributionRow.cs) 等
2. **TMP CJK 路由管线（不动）** — [BppTmpFont.cs:22](../../src/BazaarPlusPlus/Infrastructure/Fonts/BppTmpFont.cs) 检测到 CJK 时把 TMP 文本切到由 LXGW 生成的 `TMP_FontAsset`。消费方：tooltip 附魔预览（[ItemEnchantPreviewFormatting.cs:53](../../src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs)）、设置 dock（[BppSettingsDockController.Presentation.cs:178](../../src/BazaarPlusPlus/Game/Settings/BppSettingsDockController.Presentation.cs)）、大厅（[LobbyPanelLayout.cs:36](../../src/BazaarPlusPlus/Game/Lobby/LobbyPanelLayout.cs)）、版本标签（[MainMenuVersionLabelUpdater.cs:25](../../src/BazaarPlusPlus/Game/Lobby/MainMenuVersionLabelUpdater.cs)）。**这条线的"之前"是 tofu**（`36dd2374` 修的正是它），不存在可切回的非衬线状态，故保持 LXGW 不随选项变化。
3. **VoiceSubtitles 管线（不动）** — 中文走系统中文字体（[VoiceLineDisplay.cs:407](../../src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceLineDisplay.cs)、FontDiagnostics），英文走游戏字体。既定决策是保留 BazaarLine 原行为，不用 LXGW。

## 已确认的设计决定（2026-07-06 与用户对齐）

| 决定点 | 结论 |
| --- | --- |
| 生效范围 | 仅 UI Toolkit/UGUI 面板管线（`BppUiFont` 消费方）；TMP 与 VoiceSubtitles 不动 |
| 非衬线来源 | Unity 内置 `LegacyRuntime.ttf`（严格还原 pre-ce20923b 行为，零体积成本） |
| 设置形态 | 设置 dock 两态循环按钮 + BppConfig 持久化 |
| 生效时机 | 切换后**新构建**的 UI 用新字体；已构建面板重启后生效（见"生效语义"） |
| 配置模型 | 两态 enum 定死，不做自定义字体文件 |

## 非目标

- 不给 TMP 管线（tooltip / dock / 大厅）加字体选择
- 不动 VoiceSubtitles 的任何字体行为
- 不支持用户自定义字体文件路径
- 不做已挂载 UI 的实时全量刷新（见"被否决的替代方案"）

## 实现步骤

### 1. Core/Config：新增 enum + 配置项

- 新文件 `src/BazaarPlusPlus/Core/Config/BppUiFontKind.cs`：

  ```csharp
  internal enum BppUiFontKind
  {
      LxgwWenKai,
      SansSerif,
  }
  ```

  与 [SubtitlePosition.cs](../../src/BazaarPlusPlus/Core/Config/SubtitlePosition.cs) 等现有 config enum 同目录同风格。
- [BppConfig.cs](../../src/BazaarPlusPlus/Core/Config/BppConfig.cs)：新增
  - `internal const BppUiFontKind DefaultUiFontKind = BppUiFontKind.LxgwWenKai;`
  - `public ConfigEntry<BppUiFontKind>? UiFontKindConfig { get; private set; }`
  - `Bind("Appearance", "UiFont", DefaultUiFontKind, "Font for BazaarPlusPlus panels (history, collection, live build, supporters). LxgwWenKai = embedded kai-style font. SansSerif = Unity built-in sans with OS fallback for CJK. Panels already opened this session fully apply after a game restart.")`
  - 同步补 [IBppConfig.cs](../../src/BazaarPlusPlus/Core/Config/IBppConfig.cs) 上的属性。

### 2. Infrastructure/Fonts：BppUiFont 改为双字体 + 选择器

改造 [BppUiFont.cs](../../src/BazaarPlusPlus/Infrastructure/Fonts/BppUiFont.cs)：

- 保留现有 LXGW 加载逻辑，缓存改名为 `_lxgw`，通过新公开属性 `LxgwWenKai` 暴露（TMP 管线专用，见步骤 3）。
- 新增 `_sans`，惰性加载 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` —— 与 pre-ce20923b 的 HistoryPanel 实现逐字一致。
- 新增安装点（仿 `L.Install` 模式，避免 Infrastructure 直接依赖 `IBppConfig` 的层级问题）：

  ```csharp
  public static void Install(Func<BppUiFontKind> kindProvider)
  ```

  在 [Plugin.cs:149](../../src/BazaarPlusPlus/Plugin.cs) 的 `InstallStaticUtilities` 中调用：
  `BppUiFont.Install(() => services.Config.UiFontKindConfig?.Value ?? BppConfig.DefaultUiFontKind);`
- `Default` getter 改为**每次调用读 provider 的当前值**返回对应缓存字体；未安装时回退 LXGW（保持现有行为，兼容任何早于 Install 的调用）。
  - **不订阅 `SettingChanged`**：PublicizeAll 会造成 CS0229 二义性（见 memory `project_publicizer_settingchanged_ambiguity`），且每次读原始值本身就免去了缓存失效问题。
- `RequestCharactersInTexture` 不变（内部走 `Default`，自动指向选中字体；两个字体都是 dynamic font，预热语义一致）。
- 所有 UI Toolkit/UGUI 消费方**零改动**——它们已经调用 `BppUiFont.Default`。

### 3. BppTmpFont：固定引用 LXGW

[BppTmpFont.cs:54](../../src/BazaarPlusPlus/Infrastructure/Fonts/BppTmpFont.cs) 的 `TMP_FontAsset.CreateFontAsset(BppUiFont.Default, ...)` 改为 `BppUiFont.LxgwWenKai`，使 TMP CJK 路由与本选项解耦。这是本计划里唯一的行为固化点：若不改，用户选非衬线后 TMP 会用 LegacyRuntime 建 atlas，而 `TMP_FontAsset` 没有 OS 动态回退，CJK tooltip 会退化成 tofu。

### 4. 设置 dock 两态循环按钮

- 新文件 `src/BazaarPlusPlus/Game/Settings/UiFontSettingsDockEntry.cs`，实现 `ISettingsDockEntry`，仿照 [PreviewVisibilityModeDockEntry.cs](../../src/BazaarPlusPlus/Game/Settings/PreviewVisibilityModeDockEntry.cs) 的 读值/循环/状态标签 结构（两态循环：LxgwWenKai ↔ SansSerif）。
- 标签与状态文案走 [BppSettingsDockCatalog.cs](../../src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs) 既有的 `Func<string, string>` 语言解析模式。建议文案：
  - 按钮名：`界面字体` / `UI Font`
  - 状态：`楷体` / `KAI`（LxgwWenKai）、`黑体` / `SANS`（SansSerif）
  - 具体措辞落地时对齐 catalog 中现有条目的长度和口吻。
- 在 [BppComposition.cs:110-120](../../src/BazaarPlusPlus/BppComposition.cs) 的注册块中 `Register(new UiFontSettingsDockEntry())`，`Order` 放在外观类条目附近（与 ChineseLocaleMode 相邻）。
- `IsOverrideActive`（点亮态）定义为 `!= DefaultUiFontKind`，即选了非默认字体时点亮，与其他条目的"偏离默认即点亮"语义一致。

### 5. 生效语义（重要，如实交付）

三大面板的视图都是 `EnsureView()` **惰性建一次、跨开关复用**：

- [CollectionPanel.cs:522-527](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs)
- [HistoryPanel.UiToolkit.cs:18-22](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs)
- [LiveBuildPanel.cs:135-140](../../src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs)

因此实际语义是：

- 切换后，**本会话尚未构建过**的面板（以及每次开面板时重建的行内元素，如 History 行、Collection 网格项）用新字体；
- **已构建**的面板骨架保留旧字体，游戏重启后全部生效。

v1 接受该语义，在配置项描述里写明（步骤 1 的文案已含）。不给各 view 加字体失效订阅。

## 被否决的替代方案

- **实时全量刷新（事件总线广播字体变更）**：需要给每个消费 view 加订阅与重设/重建逻辑，改动面和回归风险远超收益；两态外观项的切换频率极低。
- **系统黑体（`CreateDynamicFontFromOSFont`）作为非衬线来源**：会引入候选字体名单维护（参照 FontDiagnostics 的三平台名单），且不再是"切回之前的状态"；LegacyRuntime 的 OS 回退已经给出系统黑体观感。
- **再嵌一个黑体 ttf**：DLL +10~20MB、许可证文件管理，收益不匹配。
- **自定义字体文件路径**：加载失败回退、CJK 覆盖检测等边界成本高，无诉求支撑。

## 风险与边界

1. **跨平台观感差异**：SansSerif 的 CJK 具体长相取决于用户系统（macOS 苹方 / Windows 雅黑）。这正是 pre-ce20923b 的原始行为，可接受。
2. **粗体**：LegacyRuntime 是 dynamic font，`FontStyle.Bold` 由运行时合成，pre-ce20923b 一直如此工作。
3. **UGUI 徽章**（CollectionSourceAttributionBadge）与 UI Toolkit 共用同一个 `Font` 对象，两种字体均无特殊处理。
4. **早于 Install 的调用**：`Default` 未安装时回退 LXGW，与现状完全一致，无启动顺序风险。
5. **架构测试**：`Core/Config` 新 enum 无游戏引用；`Infrastructure/Fonts` 新增的是 `Func<>` 安装点，不新增层间依赖。预期 Architecture.Tests 无需改动，若有 fixture 枚举 config 键则同步更新。

## 验证方法

按"验证与改动成比例"原则：

1. `./run.sh build`（Debug，自动拷贝进 BepInEx/plugins）。
2. `./run.sh test`，grep 全量输出确认无 "Failed test projects:"（exe-runner 退出码不可信，见 memory）。
3. 游戏内手动矩阵（通过 Steam 启动，`open "steam://run/1617400"`）：
   - 默认（楷体）：History / Collection / LiveBuild / Supporters 渲染与现状一致；
   - dock 切到黑体 → **重启游戏** → 四处面板中英文均为非衬线、无 tofu；
   - 黑体状态下：tooltip 附魔预览、设置 dock、大厅版本标签的 CJK **仍为 LXGW**（TMP 线不受影响）；
   - 字幕不受影响；
   - 会话中切换：先开 History（楷体）→ 切黑体 → 首次打开 Collection 应为黑体，History 骨架维持楷体（符合既定语义）；
   - `BazaarPlusPlus.cfg` 出现 `[Appearance] UiFont`，手改后重启生效。

## 交付顺序

1. 步骤 1-3（config + BppUiFont + BppTmpFont 固化）—— 一个 commit，可单独构建验证
2. 步骤 4（dock 条目 + 文案）—— 一个 commit
3. 游戏内验证矩阵 → 按 wrap-up 流程 review diff、合并
