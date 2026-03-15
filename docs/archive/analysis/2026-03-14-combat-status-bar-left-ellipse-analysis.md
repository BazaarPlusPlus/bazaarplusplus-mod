# 2026-03-14 CombatStatusBar 左侧“椭圆”分析

## 问题

需要确认 `CombatStatusBar` 左侧看起来像“多了一个椭圆”的部分，是否真的是一个独立且无用途的 UI 元素。

## 结论

当前实现里，`CombatStatusBar` 左侧没有单独创建一个“椭圆控件”。

从代码结构看，不存在“左边挂了一个没有用途的独立椭圆对象”这一实现。

如果运行时视觉上看起来左侧像多出一个椭圆，更可能是圆角背景、内层 glow，或者多个圆角矩形叠加后形成的视觉错觉。

## 代码依据

### 1. Bar 的根节点只创建了一层背景和一层 glow

`EnsureUi()` 里创建 Bar 根节点后，只给根节点挂了：

- `_barBackground`
- `_barGlow`

对应代码：

- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:82`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:89`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:90`

这说明外层没有额外再创建一个左侧专属装饰物。

### 2. Bar 的内容结构只有 4 个 segment 和 3 个 divider

`HorizontalLayoutGroup` 下依次添加的是：

- `TimeSegment`
- `Divider`
- `FrameSegment`
- `Divider`
- `MultiplierSegment`
- `Divider`
- `PauseSegment`

对应代码：

- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:94`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:103`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:106`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:108`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:111`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:113`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:117`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:119`

左侧并没有单独插入一个额外的 image、badge、cap 或 ellipse 节点。

### 3. 所有 Image 共用的是圆角矩形 sprite，不是单独的椭圆 sprite

所有背景图都通过 `AddImage(...)` 创建，而 `AddImage(...)` 会统一使用 `GetRoundedSprite()`：

- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:405`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:408`

`GetRoundedSprite()` 本身生成的是一个带圆角的矩形贴图，逻辑由 `IsInsideRoundedRect(...)` 决定：

- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:486`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:521`

也就是说，视觉基础形状是“圆角矩形”，不是“单独画出来的椭圆”。

## 更可能的视觉来源

如果游戏里肉眼看见左侧像一个没用的椭圆，更可能来自下面几种情况。

### 1. 根背景左端的圆角

`_barBackground` 本身就是一个带较大圆角的外框。左边缘在某些分辨率、缩放或对比度下，容易看起来像半个椭圆。

### 2. `BarGlow` 叠在背景里面

`_barGlow` 会铺满整个 bar，只是四边缩进 `3f`：

- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:90`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:91`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:92`

这层 glow 在左端会与外层背景的圆角叠加，可能强化“左边鼓出一块”的感觉。

### 3. 第一个 segment 与外层背景的圆角叠加

最左侧的 `TimeSegment` 也有自己的圆角背景，因为 segment shell 同样通过 `AddImage(...)` 创建：

- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:303`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:323`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs:330`

因此最左边其实是：

- 外层 bar 圆角
- 内层 glow 圆角
- `TimeSegment` 圆角

三层叠在一起。视觉上完全可能被误认为是一个多余椭圆。

## 是否可以认定为“没用”

如果“椭圆”指的是一个独立对象，那么当前代码下不能认定它存在，更谈不上“没用”。

如果“椭圆”指的是左侧那块视觉效果，那么它不是一个独立功能组件，而是现有背景系统的副产物。它本身不承载交互或状态信息，因此可以视为纯视觉表现，而不是功能元素。

## 可选解决方案

如果目标是去掉这块“像椭圆”的视觉感受，可以按影响范围从小到大选择下面几种方案。

### 方案 A：去掉 `BarGlow`

做法：

- 删除 `_barGlow = AddChildImage(...)`
- 同时删除 `RefreshUi()` 里对 `_barGlow` 的颜色更新

优点：

- 改动最小
- 最容易验证左侧“鼓包感”是不是 glow 造成的

风险：

- 整个 bar 会少一层内部氛围
- UI 可能显得更平

### 方案 B：减小圆角半径

做法：

- 调整 `GetRoundedSprite()` 中的 `radius`

当前值：

- `const int radius = 12;`

优点：

- 保留整体风格
- 可以同时减弱外层和内层所有“椭圆感”

风险：

- 会影响 bar、segment、button、divider 的全部圆角风格
- 是全局视觉改动，不只影响左侧

### 方案 C：让外层 Bar 用直角，内部 segment 保留圆角

做法：

- 给 `_barBackground` 使用另一种 sprite 或单独样式
- segment 和 button 继续使用现有圆角 sprite

优点：

- 最能消除“左侧像有一个额外椭圆外壳”的感觉
- 内部控件仍保留圆角层次

风险：

- 需要把外层背景和内部组件的视觉体系拆开
- 代码会比方案 A/B 多一点

### 方案 D：保留 glow，但让最左侧 segment 贴边方式不同

做法：

- 调整 bar padding
- 或调整第一个 segment 的宽度/边距
- 或让外层背景与首个 segment 的圆角不要叠得这么明显

优点：

- 可以定向修正左侧视觉

风险：

- 需要实际运行后肉眼调参数
- 代码分析可以提出方向，但不能替代运行时视觉确认

## 推荐处理顺序

建议按下面顺序验证：

1. 先临时去掉 `BarGlow`
2. 如果左侧“椭圆感”明显消失，说明主要问题来自 glow 叠层
3. 如果仍然明显，再尝试减小圆角半径
4. 如果想要最干净的外形，再考虑把外层 bar 改成直角背景

这个顺序的好处是：

- 每一步改动都很小
- 容易回退
- 能快速定位到底是 glow、圆角，还是布局叠加导致的视觉问题

## 目前最稳妥的判断

基于当前源码，最稳妥的判断是：

- 没有独立的“左侧无用椭圆控件”
- 左侧像椭圆的东西大概率是圆角矩形和 glow 的叠加视觉
- 如果要处理，优先从 `BarGlow` 和圆角半径入手
