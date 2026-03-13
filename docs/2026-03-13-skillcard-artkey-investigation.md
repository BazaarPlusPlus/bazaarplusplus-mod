# 2026-03-13 Skill Card ArtKey Investigation

## 现象

- Monster preview 启动后，skill card 图片可能错乱，与实际技能不一致。
- 鼠标 hover 到错图 skill 上时，tooltip / 交互表现看起来是正常的。
- 商店中的候选技能图片也会错乱。
- 候选技能被选中并进入玩家自己的 skill 区域后，图片又会恢复正确。

## 当前判断

当前证据更支持“显示对象复用后没有完整 reset”而不是 “TemplateId / ArtKey 取错”。

## 已确认的原生链路

### 1. ArtKey 的来源

原生 skill token 的 icon art 来自静态模板：

- `skillCard.TemplateId`
- `Data.GetStatic().GetCardById(skillCard.TemplateId)`
- `cardById.ArtKey`

对应反编译代码：

- `decompiled/TheBazaarRuntime/AssetLoader.cs`
- `ConstructAndInstantiateSkill(...)`
- `component.Setup(cardById.ArtKey, skillCard, ...)`

### 2. SkillController 如何使用 ArtKey

`SkillController.Setup(iconArtKey, card, ...)` 的流程：

- `SetCardData(card)`
- `InitializeCardTooltipData()`
- `LoadCurrentFrame(card.Tier)`
- `LoadAssetAsyncByAddress<Texture>(iconArtKey)`
- `UpdateSkillIcon(texture)`

这说明真正决定 skill 图标的输入值是 `iconArtKey`，而不是 preview spec 里的 `SourceName` 等字段。

### 3. Hover 正常不代表屏幕上的图正常

tooltip 数据来自 `CardData`，而不是当前已经贴到材质上的 icon texture。

因此出现以下组合是合理的：

- 屏幕上 skill 图标错
- hover 后 tooltip 正常

这意味着：

- 数据层可能是正确的
- 可视层可能残留了上一次池化对象的 frame / icon / material 状态

## 与当前 BazaarPlusPlus preview 代码的对应关系

当前 BPP preview skill factory 已经对 skill 做了额外刷新：

- 实例化后调用 `skillController.Cleanup()`
- 然后重新执行 `skillController.Setup(card.Template?.ArtKey ?? "Invalid", card, false)`

对应文件：

- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs`

这段逻辑本身就是在绕过“池化 skill card 可能保留旧 frame / 旧 icon”的问题。

## 为什么“商店候选技能错，但进入自己区域后正确”很重要

这说明问题不只存在于 BPP preview，自带 UI 的 skill 展示链路里也可能存在类似的对象复用问题。

目前更合理的解释是：

- 商店候选技能和 preview skill 走的是某种会复用 skill 显示对象的链路
- 这条链路没有稳定地做完整 reset
- 玩家自己的 skill 区域使用另一套更完整的初始化逻辑
- 所以上板后图被“纠正”

## 当前结论

优先级最高的假设：

1. `TemplateId -> GetCardById -> ArtKey` 这条映射大概率是正确的。
2. 真正的问题更像是 skill 显示对象池化复用后的可视状态污染。
3. 如果后续日志证明 `TemplateId` 和 `ArtKey` 都正确，但图片仍然错误，就可以基本排除 `ArtKey` 取值逻辑本身。

## 下一步验证

建议补日志记录以下值：

- preview spec / source skill 的 `TemplateId`
- `card.Template?.InternalName`
- `card.Template?.ArtKey`
- 实际传入 `SkillController.Setup(...)` 的 `iconArtKey`
- hover 时 tooltip 对应的 `CurrentCard.TemplateId`

判断标准：

- 如果 `TemplateId` 和 `ArtKey` 都正确，但图仍然错：问题在显示复用。
- 如果 `TemplateId` 正确但 `ArtKey` 错：再回头查模板映射或静态数据读取。

## 待继续追踪的链路

- 商店候选技能到底使用的是 `SkillController` 还是其他 skill 显示组件。
- 候选技能进入玩家 skill 区域之前，是否缺少一次 `Cleanup + Setup`。
- 是否有 frame / material / texture 在池化归还时没有被重置。

## 新增结论：候选技能与玩家技能区不是同一套 renderer

进一步对照反编译代码后，可以确认：

- 普通 card 实例化入口 `AssetLoader.InstantiateCardAsync(...)` 遇到 `ECardType.Skill` 时，会走 `ConstructAndInstantiateSkill(...)`
- 这条链最终产出的是 `SkillController`
- 玩家自己的 skill 区域则由 `SkillPresentationManager` 管理，使用的是 `SkillProxyRenderer.Initialize(card, tier, cardById.ArtKey)`

这意味着：

- 商店 / 候选位 skill 更接近“board card / token”显示链路
- 玩家 skill 区是独立的展示系统

因此下面这个现象就有了更直接的解释：

- 候选技能图错
- 买入后进入玩家 skill 区图正确

原因很可能不是“同一个对象后来修正了自己”，而是：

- 候选位用的是 `SkillController`
- 玩家区重新创建了一个 `SkillProxyRenderer`
- 玩家区这套初始化更完整，因此图被重新按正确的 `ArtKey` 渲染

## 进一步的根因推测

当前最可疑的是 `SkillController` 这条链：

- `ConstructAndInstantiateSkill(...)` 会调用 `component.Setup(cardById.ArtKey, skillCard, ...)`
- 但在这之前没有显式 `Cleanup()`
- `SkillController.Cleanup()` 才会把 `_currentFrame` 退池并清空 `_currentFrame`

这和 BPP preview 里专门补的一段逻辑形成了鲜明对照：

- BPP preview 在实例化后额外执行 `Cleanup + Setup`
- 注释直接说明 “Pooled skill cards can retain the previous frame/icon visuals.”

因此当前最强假设是：

- 原生的某些候选 / 商店 skill 显示链路复用了旧的 `SkillController`
- 但实例化后没有像 BPP preview 那样先做完整 reset
- 导致 frame / icon / material 状态残留

## 一个额外的危险点

`SkillController.UpdateCard(...)` 只在 tier 变化时更换 frame，并且之后优先复用 `_skillIconCurrentTexture`：

- 如果 `_skillIconCurrentTexture` 来自上一次复用对象
- 而当前链路没有重新执行完整 `Setup(...)`

那么就可能出现：

- 当前 `CardData` 已经换了
- 但显示层仍沿用上一张 skill 的 icon texture
