# 事件预览（Event Preview）设置开关 — 实施方案

状态：待实施。本方案由 review 会话产出，实施后由原会话 review。

## 背景

PR #5（merge `a5b9accc`）为原生 tooltip 新增了两个增强 section：

- 事件卡的选项/结果明细（`Patches/Tooltips/EncounterEventTooltipPatch.cs`，postfix `CardTooltipController.RenderPassiveEffectTextBlock`）
- 英雄升级奖励说明（`Patches/Tooltips/HeroLevelRewardsTooltipPatch.cs`，postfix `HeroLevelTooltipTypeHandler.HandleTooltip`）

这两个 patch 目前**没有任何用户开关**，由 `Plugin.ApplyHarmonyPatches` 扫描程序集后无条件生效。本方案在 BPP 设置 dock 中新增一个布尔开关"事件预览"来 gate 它们。

## 设计决策（已定，不要改）

1. **一个开关同时 gate 两个 section**。两者是同一功能（"原生 tooltip 增强：事件选项明细 + 升级奖励说明"）一起 ship 的，拆两个开关没有用户价值。
2. **布尔开关，不用 `PreviewVisibilityMode`**。`PreviewVisibilityMode`（Off / AutoOnPedestalChoice / Always，`Core/Config/PreviewVisibilityMode.cs`）的三档语义是附魔/升级预览在底座选择场景特有的，对事件 tooltip 无意义。用 `SettingsMenuToggleBridge` 三参构造器（ON/OFF 状态文本自动生成），照抄 `VoiceSubtitlesSettingsDockEntry`。
3. **默认值 `true`**。功能已随 4.4.x 默认启用发布，默认关会构成行为回退。
4. **开关 OFF = 表现如同 patch 不存在**。特别地，`HeroLevelRewardsTooltipPatch.Postfix` 里对原生 quest 行泄漏的清理（`controller._questDisplayService?.BuildDisplay(null, null)`，第 44 行）也一并跳过 —— OFF 应回到原版行为，即使原版有这个泄漏。
5. **每次渲染现读 `.Value`，绝不订阅 `ConfigEntry.SettingChanged`**（PublicizeAll 导致 CS0229 二义性，仓库已知坑）。这样开关即时生效、无需重启。

## Part A — 开关本体（6 个改动点）

新文件统一放在新目录 `src/BazaarPlusPlus/Game/EventPreview/`（对齐 `Game/ItemEnchantPreview/` 的组织方式）。

### A1. 配置项

`src/BazaarPlusPlus/Core/Config/IBppConfig.cs` — 在现有属性列表中追加：

```csharp
ConfigEntry<bool>? EnableEventPreviewConfig { get; }
```

`src/BazaarPlusPlus/Core/Config/BppConfig.cs` — 加对应 auto-property，并在 `Initialize(ConfigFile)` 中 Bind（放在 `EnchantPreviewModeConfig` 的 Bind 之后，与其相邻；section 沿用"每功能一个 section"的惯例）：

```csharp
EnableEventPreviewConfig = config.Bind(
    "EventPreview",
    "Enabled",
    true,
    "Whether to append the event-choice breakdown and hero level-up reward sections to native tooltips."
);
```

### A2. 标签

新文件 `src/BazaarPlusPlus/Game/EventPreview/EventPreviewSettingsMenuLabel.cs`，照抄 `Game/Screenshots/EndOfRunScreenshotSettingsMenuLabel.cs` 的形状。`LocalizedTextSet` 用 7 参完整构造器（顺序：english, chineseMainland, chineseTraditional, german, portuguese, korean, italian —— 见 `src/BazaarPlusPlus.Localization/LocalizedTextSet.cs:24`）：

```csharp
private static readonly LocalizedTextSet Labels = new(
    "Event Preview",
    "事件预览",
    "事件預覽",
    "Ereignisvorschau",
    "Prévia de Eventos",
    "이벤트 미리보기",
    "Anteprima Eventi"
);

internal static string Resolve(string languageCode)
{
    return Labels.Resolve(languageCode, L.CurrentMode);
}
```

### A3. 排序常量

`src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs` — 在 `EnchantPreview = 3` 之后插入 `EventPreview = 4`，其后的常量（`CombatStatusBar` 到 `BazaarDbUpload`，当前 4..15）整体 +1。所有消费方都引用命名常量，只需改这一个文件。

### A4. Dock entry

新文件 `src/BazaarPlusPlus/Game/EventPreview/EventPreviewSettingsDockEntry.cs`，照抄 `Game/VoiceSubtitles/VoiceSubtitlesSettingsDockEntry.cs:10-45` 的 bridge 模式：

```csharp
internal sealed class EventPreviewSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EventPreview;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "EventPreview",
            EventPreviewSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => config.EnableEventPreviewConfig?.Value ?? true,
                enabled =>
                {
                    var entry = config.EnableEventPreviewConfig;
                    if (entry != null)
                        entry.Value = enabled;
                }
            )
        );
}
```

注意读取侧的 fallback 是 `?? true`（与默认值一致），不是 VoiceSubtitles 的 `?? false`。

### A5. 注册

`src/BazaarPlusPlus/BppComposition.cs` 的注册块（当前约 110-121 行）加一行，放在 `ItemEnchantPreviewSettingsDockEntry` 注册的相邻位置：

```csharp
_settingsDockRegistry.Register(new EventPreviewSettingsDockEntry());
```

### A6. Gate 与两个 patch 的接入

新文件 `src/BazaarPlusPlus/Game/EventPreview/EventPreviewGate.cs`，照抄 `Game/VoiceSubtitles/VoiceSubtitlesGate.cs`：

```csharp
internal static class EventPreviewGate
{
    internal static bool IsEnabled()
    {
        return BppPatchHost.Services.Config.EnableEventPreviewConfig?.Value == true;
    }
}
```

**`EncounterEventTooltipPatch.Postfix`**（`Patches/Tooltips/EncounterEventTooltipPatch.cs:44`）：把 gate 并入现有的 content 计算，让 OFF 走既有的 `HideAll` 分支 —— 这样运行中关闭开关后，残留 section 会在下一次 hover 收敛消失：

```csharp
var content =
    string.IsNullOrEmpty(text) || !EventPreviewGate.IsEnabled()
        ? null
        : BuildContent(__instance);
```

不要在 try 块之外 early return，也不要绕过 `HideAll`。

**`HeroLevelRewardsTooltipPatch.Postfix`**（`Patches/Tooltips/HeroLevelRewardsTooltipPatch.cs:32`）：在 try 块内最顶部（`tooltipData` 类型判断之前）early return：

```csharp
if (!EventPreviewGate.IsEnabled())
    return;
```

按设计决策 4，这会连带跳过第 44 行的 quest 行清理，符合"OFF = 原版行为"。该 section 的清理由原生 `ResetValues` → `RenderPassiveEffectTextBlock(empty)` → `HideAll` 路径覆盖（见该文件 15-18 行的类注释），无需额外 teardown。

## Part B — 同文件顺手修复（review 已确认的两个问题）

这两项与 Part A 同属 `EncounterEventTooltipPatch.cs` 附近，一并做掉，但**分开提交**（见"提交划分"）。

### B1. 删除 Release 出货的布局 dump 诊断

`EncounterEventTooltipPatch.cs:98-174` 附近的 `DumpedLayouts` / `DumpLayoutOnce` / `DumpAtEndOfFrame`（含 Postfix 第 59 行的 `DumpLayoutOnce(__instance, text);` 调用点）整体删除。注释自述"remove once the tooltip layout settles"，且它经 `BppLog.Info` 在 Release 版持续向 `LogOutput.log` 输出 tooltip 原文和递归布局树。直接删，不要降级为 Debug（仓库规则：临时探针不留在主路径）。删除后清理不再使用的 using（如 `System.Collections.Generic` 若仅 `HashSet<Guid>` 在用）。

### B2. 把 `AttributeUnitLocalizer` 装配移出 patch 静态构造器

现状：`EncounterEventTooltipPatch` 的 cctor（`EncounterEventTooltipPatch.cs:30-35`）执行

```csharp
CollectionLocalizationResolver.AttributeUnitLocalizer = BppTooltipText.TryLocalizeKeyword;
```

问题：该 hook 还被 Collection 面板的卡牌描述路径消费（`CollectionLocalizationResolver.cs:81`），而类型初始化器要等第一次 postfix 触发才运行 —— 冷启动后先开 Collection 面板会在中文客户端漏出英文单位词。

改法：删除整个 cctor，把这一行移到 `Plugin.InstallStaticUtilities`（`src/BazaarPlusPlus/Plugin.cs:151-164`，静态 seam 安装的既有位置），与 `L.Install(...)` 等并列。`BppTooltipText` 是 `EncounterEventTooltipPatch.cs` 内的第二个类（第 253 行起），保持原位不动，`Plugin.cs` 直接引用即可（需要加 `using BazaarPlusPlus.Patches.Tooltips;`）。`TryLocalizeKeyword` 内部自带 try/catch 且按调用惰性读游戏关键词表，在启动期赋值 delegate 是安全的。

## 明确不在本次范围内（不要顺手做）

- hover 路径的解析缓存 / memoization（review 发现的性能问题，单独立项）
- 复合日期条件截断、tag operator 压平、未知 ability accessor fallback 等解析正确性问题
- `BppTooltipText` 从 patch 文件挪到独立文件 / GameInterop 适配器化
- `BppSettingsDockOrder` 之外的任何重构

## 验证

1. `cd bazaarplusplus-mod && ./run.sh build` — Debug 构建通过（会自动拷贝到 BepInEx/plugins）。
2. `dotnet test tests/CollectionEncounterTooltip.Tests/CollectionEncounterTooltip.Tests.csproj` — 51 个既有测试保持全绿（本方案不改解析层，任何失败都说明改错了地方）。
3. `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj` — 架构测试通过。
4. `./run.sh format` — csharpier；若 format 波及本次未改动的文件，不要把它们带进提交。
5. 游戏内验证由用户执行（Steam 启动，App ID 1617400），要点：dock 中出现"事件预览"（简中）/ "Event Preview"（英文）条目，默认 ON；关闭后 hover 事件卡无 BPP section、英雄等级 tooltip 无奖励明细；不重启游戏重新打开开关，下一次 hover 即恢复；冷启动直接开 Collection 面板，中文客户端卡面描述单位词为中文（验证 B2）。

## 提交划分

在工作分支上按此拆分（不要合成一个提交）：

1. Part A：`Add Event Preview settings dock toggle gating the tooltip sections`
2. Part B1：`Remove shipped tooltip layout dump diagnostics`
3. Part B2：`Install AttributeUnitLocalizer at plugin startup instead of patch cctor`

注意：工作区可能存在未提交的 `Directory.Build.props` 版本号改动（4.4.2 → 4.4.3），那是用户的改动，**不要**把它带进任何提交，也不要还原它。

完成后不要自行 merge/push —— 停在工作分支上等 review。

## Review 验收清单（供 review 会话使用）

- [ ] 两个 patch 的 OFF 路径：encounter 走 `HideAll` 分支、level-rewards 早退且跳过 quest 清理
- [ ] 没有任何 `SettingChanged` 订阅；没有对 `.Value` 的跨帧缓存
- [ ] `?? true` fallback 与 Bind 默认值一致
- [ ] `BppSettingsDockOrder` 重编号后所有常量连续且无重复
- [ ] dump 诊断（字段、两个方法、调用点、无用 using）删净
- [ ] cctor 已删，`InstallStaticUtilities` 中的赋值在 `L.Install` 之后
- [ ] 提交不含 `Directory.Build.props`、不含 format 波及的无关文件
