# 聚合物品类型机制盘点与验证

## 背景

Issue #48 / PR #62 希望为“自身获得其他物品类型，并按自身类型数量获得收益”的物品显示尚缺类型。最初示例是 Hunter's Pack 与 Cargo Shorts。

## 当前问题

前两轮盘点都错误地把产品语义等同于具体序列化类型：

1. 第一轮只搜索 `TAuraActionCardAddTagsBySource`，得到 5 件物品。
2. 第二轮补上 `TActionCardAddTagsBySource`，得到 7 件物品。

用户确认仍有遗漏。当前 PR 的模板识别也只覆盖上述两个类，因此功能覆盖范围不能视为完整。

## 已确认的根因

- 搜索入口错误：从已知实现类名向外枚举，而不是从“最终会改变 card types / tags，并按 distinct tag count 结算”的语义反向枚举。
- 只检查了顶层 `Abilities[*].Action` / `Auras[*].Action`，尚未完整覆盖嵌套 action、enchantment、transform、quest、trigger/prerequisite、attribute reference 等路径。
- “聚合类型物品”的边界未先定义：可能包括持续复制、事件永久累积、固定获得额外类型、战斗内临时获得、按外部目标类型计数但不修改自身等不同机制。

## 候选机制假设

### H1：还有其他 AddTags 动作类型

搜索所有序列化 `$type` 中包含 `Tag` / `Tags` / `Type` 的 action/aura，而不限定 `AddTagsBySource`。

### H2：AddTagsBySource 被嵌套在组合动作中

递归扫描整张 card JSON，包括 abilities、auras、enchantments、transform 与 quests；记录完整 JSON path，而不是只看顶层 action。

### H3：部分物品不修改 Tags，而是直接聚合 tag count

搜索所有 `TReferenceValue*Tag*`、`Distinct`、`TagCount`、tooltip 文案中的 “for each type / gains types / types of items”，交叉比对是否属于用户所指聚合类型。

### H4：本地 cards.json 不是用户当前看到的完整/同版本数据

核对 prod/PTR cache、GameData.db/manifest、当前运行日志 build channel 与文件版本；必要时从实际静态数据数据库枚举，而不是只信单一 `prod/cache/cards.json`。

## 验证方法

- 从当前 prod 与可用 PTR 数据源分别生成三份全集：
  1. 文案候选：tooltip/localization 命中 type/types 相关语义；
  2. 结构候选：任意嵌套 `$type` 命中 Tag/Tags/Type/TagCount；
  3. 结算候选：存在 distinct tag count 或按 card tags 修改属性的引用。
- 对三份集合做并集，逐卡输出 hero、tier、稳定 template ID、命中文案、命中 JSON path 和机制分类。
- 对每个候选人工判定：是否改变“自身当前类型集合”，是否应显示“尚缺类型”，缺失全集是否仍是 20 个 item types。
- 最终验收不是“命中某两个类”，而是：所有文案明确表示自身获得/拥有其他物品类型的卡都被识别；所有仅按目标类型计数但自身不获得类型的卡不误报。
- 修正 PR 后，用数据驱动测试固定当前版本的 template ID 覆盖矩阵，并重新构建部署。

## 当前状态

已完成 prod 5.0.0 的三路交叉盘点：

- `AddTagsBySource`：7 件（持续复制 5 件、事件永久累积 2 件）。
- `TReferenceValueCardTagCount { Distinct: true }`：10 件；除上述 7 件外，还发现 3 件直接统计玩家物品类型、但不把类型复制到自身的物品。
- 英文 tooltip 中 `type/types` 语义搜索：另命中 Dooltron Mainframe 与 Mysterious Crystal；前者只固定赋予 Dooltron `Core`，后者把随机类型赋给另一件物品，均不是“该物品聚合当前类型并从缺失提示获益”的目标。

最终目标矩阵共 10 件：

| 物品 | 英雄 | 等级 | 聚合来源 |
| --- | --- | --- | --- |
| Cargo Shorts | Pygmalien | Silver | 当前手牌类型复制到自身 |
| Beast of Burden | Pygmalien | Gold | 当前仓库类型复制到自身 |
| Cauldron | Mak | Silver | 当前手牌类型复制到自身 |
| Hunter's Pack | Karnok | Silver | 当前仓库类型复制到自身 |
| Hunter's Sled | Karnok | Silver | 当前手牌类型复制到自身 |
| Hydraulic Press | Dooley | Silver | 永久累积被摧毁物品的类型 |
| Vat of Acid | Mak | Gold | 永久累积被出售物品的类型 |
| Forklift | Dooley | Gold | 直接统计其他手牌的 distinct types |
| Laurel's Fortress | Jules | Silver | 直接统计其他手牌的 distinct types |
| Rowboat | Vanessa | Gold | 直接统计手牌（包含自身）的 distinct types |

PR #62 已修正为同时识别三条顶层结构路径，并按 live card 的当前附魔额外检查对应 enchantment abilities/auras；resolver 测试固定了持续 aura、永久 action、外部 distinct-count 与“只读取当前附魔”四类语义。Mysterious Crystal 不在目标矩阵：其 tooltip 应提示“可能授予哪些随机类型”而不是“自身还缺哪些类型”，属于不同产品语义。
