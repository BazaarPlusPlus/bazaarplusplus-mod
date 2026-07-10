# 工具提示文本：测试性修复 + ScaleInlineSizes 去重 设计稿

状态：批量流水线 Phase B 草稿，待红队 + 用户统一确认。来源：架构评审候选 7，**经盘点定性改道**。

## 定性修正记录（重要：供未来评审引用，勿再重提原命题）

- **「统一两个行归一化器」证伪**：字符级比对确认二者是**不同变换**——`AppendTooltipText`（附魔预览侧）保留行内空白、逐行 `\n` 重组、void 追加进共享 StringBuilder；`NormalizeInline`（任务奖励侧）逐行 Trim、空格塌缩为单行、另有 `" / "` 跨值连接 + Ordinal 去重。共同点仅「按 \r\n 拆行 + 丢空白行」。强行统一需要 trim 开关 + 分隔符开关 = 配置记录，非深模块。**勿再提议统一。**
- 存活的两个真问题：
  1. **反射钥匙孔**：`AppendTooltipText`/`AppendLine` 是补丁类私有成员，测试靠 `GetMethod("AppendTooltipText", NonPublic|Static)` + "should stay testable" 断言维系（`tests/ItemEnchantPreview.Tests/Program.cs:196-207`）。
  2. **新发现的真重复**：`BppQuestRewardPreviewText.ScaleInlineSizes`（`QuestRewardPreviewTooltipPatch.cs:114-130`）与 `ItemEnchantPreviewFormatting.ScaleInlineSizes`（`Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs:59-75`）——同一 `<size=(\d+)%>` 正则、同一 round/clamp-to-1 数学，两份手写。

## 变更清单

1. **消灭反射钥匙孔**：`AppendTooltipText`/`AppendLine` 从 `CardTooltipDataPassivePatch` 私有成员移入 `Game/ItemEnchantPreview/ItemEnchantPreviewFormatting`（放置先例：纯文本工具全部在 `Game/` 下、`Patches/Tooltips` 只住 Harmony 类）。**可见性（红队 rev，消除自相矛盾）**：`AppendTooltipText` 必须 `public static`（exe-runner 测试项目无 InternalsVisibleTo，直调只能走 public；类本就是 `public static class`）；`AppendLine` 保持 `private static`（仅内部调用）。补丁改为薄调用。语义逐字节不变（无分配版手写扫描照搬）。注意 `ItemEnchantPreviewFormatting.cs` 需补 `using System.Text;`（StringBuilder 参数，红队 rev）。
   - 测试改造：`ItemEnchantPreview.Tests/Program.cs:196-207` 反射块改直调；**顺带清理孤儿**（红队 rev）：`RequireType` 局部函数（唯一调用方就是该反射块）与 `using System.Reflection` 一并删除（若无其他使用）。
2. **ScaleInlineSizes 去重**：红队已确认两份实现**字符级等价**（同 regex/选项/Round/clamp/透传，scale 均为参数）。但目标方法现为 `private static`（`ItemEnchantPreviewFormatting.cs:59`）——**升为 `internal static`**（两调用点同程序集，internal 足够；红队 rev 纠正了「public 工具」的错误措辞）。`BppQuestRewardPreviewText.ScaleInlineSizes` 删除改调；**同时删除随之变死的 `SizeTagRegex`**（`QuestRewardPreviewTooltipPatch.cs:77-80`，唯一使用者在被删方法内；`RewardSizePercent`/`RewardInlineSizeScale` 仍活保留——红队 rev）。
3. `QuestRewardPreviewTooltipPatch` 其余（`NormalizeInline`/`BuildRewardText`/`AppendRewardPreview`）**不动**——xUnit 正门测试已覆盖，无钥匙孔问题。

## 风险与保真

- 热文件警告：`QuestRewardPreviewTooltipPatch.cs` 当日刚被 PR#20 改过（同日五连改），改动面压到最小（只删一个私有方法 + 改一处调用）。
- `ItemEnchantPreviewPatch.cs` 是冷文件，移出两个方法风险低。
- 反射测试改直调后删除 "should stay testable" 检查（其存在理由消失）。

## 测试与验证

- `ItemEnchantPreview.Tests`：反射块改直调，断言字符串不变（`"first\r\n\r\n second \rthird\n   \n"` → `"first\n second \nthird\n"`）。
- `CollectionEncounterTooltip.Tests/QuestRewardPreviewTextTests` 6 个 Fact 原样存活（`AppendRewardPreview` 面不变；`120%→66%`、`121%/122%→67%` 去重断言是 ScaleInlineSizes 等价性的现成回归网）。
- 验证：两个测试项目 + `dotnet build`。

## 不做的事

- 统一两个行归一化器（证伪，见上）。
- 动 `NormalizeInline`/`BuildRewardText`/`AppendRewardPreview`。
- CoreLayeringTests（归候选 9 独占）。
