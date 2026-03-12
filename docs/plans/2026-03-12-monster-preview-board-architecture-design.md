# Monster Preview Board Architecture Design

**Date:** 2026-03-12

**Goal:** 从零梳理 monster preview board 的职责边界，把 `board UI`、`anchor`、`data`、`debug` 拆成独立层，形成一套统一的 preview session 架构，支撑正式展示和 debug 展示两条路径。

## 1. 设计原则

本次重构不以“整理现有类”为目标，而以“重新定义系统边界”为目标。

核心原则：

- `board UI` 只负责视觉结构、slot 布局和 debug 可视化
- `anchor` 只负责产出 board 在世界中的 pose
- `data` 只负责把任意来源整理成统一 board model
- `debug` 作为 presentation 能力存在，不再散落进业务逻辑
- 正式路径和 debug 路径走同一套 session，只在输入上不同

非目标：

- 不保留当前 controller/bridge/debug controller 的职责划分
- 不要求兼容当前 fixed anchor 和 layout 的内部实现方式
- 不在这次设计里处理所有视觉样式优化

## 2. 推荐架构

推荐采用四层结构：

1. `PreviewBoardSession`
2. `PreviewDataController`
3. `AnchorController`
4. `BoardView`

额外包含一个独立的 `BoardDebugOverlay` 作为 `BoardView` 的子模块。

### 2.1 PreviewBoardSession

这是唯一的对外入口，负责协调整条 preview 生命周期。

职责：

- 接收外部请求
- 驱动 data 刷新
- 驱动 anchor 刷新
- 组织 view 渲染
- 管理 show/hide 和同步节流

不负责：

- 直接构造卡片数据
- 直接计算锚点
- 直接处理具体 slot 布局

建议接口：

```csharp
internal sealed class PreviewBoardSession
{
    public void Show(PreviewBoardRequest request);
    public void Hide();
    public void Tick();
}
```

### 2.2 PreviewDataController

它的输入是任意 preview 数据源，输出是统一 model。

职责：

- 从 monster db、encounter cache、hand、debug mock 等来源读取数据
- 生成统一的 `PreviewBoardModel`
- 维护 signature/version，用于避免无意义重建

建议接口：

```csharp
internal interface IPreviewDataSource
{
    bool TryBuild(out PreviewBoardModel model);
}
```

```csharp
internal sealed class PreviewBoardModel
{
    public IReadOnlyList<PreviewCardSpec> ItemCards;
    public IReadOnlyList<PreviewCardSpec> SkillCards;
    public string Title;
    public string Signature;
    public IReadOnlyDictionary<string, string> Metadata;
}
```

说明：

- session 只依赖 `PreviewBoardModel`
- data source 是否来自正式逻辑或 debug 逻辑，对 session 不可见
- 旧的 signature 去重逻辑收敛到 data controller，不再分散在 debug controller 中

### 2.3 AnchorController

这层只负责得到最终 `BoardPose`。

推荐把 anchor 拆成三段：

1. `AnchorTarget`
2. `AnchorResolver`
3. `AnchorAdjuster`

最后由 `AnchorController` 聚合为 `BoardPose`。

```csharp
internal sealed class BoardPose
{
    public Vector3 Position;
    public Quaternion Rotation;
}
```

#### AnchorTarget

定义“参考对象是谁”，例如：

- 固定世界点
- 某个 `Transform`
- 某个 scene path
- 某个逻辑对象推导出的参考点

#### AnchorResolver

把 target 解析为基础 pose。

例如：

- `FixedAnchorResolver`
- `TransformAnchorResolver`
- `PathAnchorResolver`

#### AnchorAdjuster

在基础 pose 上统一应用修正：

- world offset
- local offset
- yaw/pitch/roll 修正
- debug 临时偏移

这样 anchor 的正式修正和 debug 修正共用同一条管线。

建议接口：

```csharp
internal interface IBoardAnchorStrategy
{
    bool TryResolve(out BoardPose pose);
}
```

内部实现不直接暴露 resolver/adjuster 给外部，外部只关心 strategy。

推荐策略：

- `FixedAnchorStrategy`
- `TrackedTransformAnchorStrategy`
- `DerivedAnchorStrategy`
- `DebugAnchorStrategy`

### 2.4 BoardView

这层是纯 UI / 视觉层。

职责：

- 管理 board 根节点
- 管理 item/skill slot
- 创建/销毁 card view
- 应用 layout
- 根据 `BoardPose` 更新世界 transform
- 根据 debug options 控制辅助点可见性

不负责：

- 关心数据来源
- 关心 anchor 的解析方式
- 决定何时 show/hide

建议接口：

```csharp
internal sealed class BoardView : IDisposable
{
    public void SetVisible(bool visible);
    public void ApplyPose(BoardPose pose);
    public void Render(BoardRenderModel renderModel);
}
```

其中 `BoardRenderModel` 是 view 最终消费的数据：

```csharp
internal sealed class BoardRenderModel
{
    public PreviewBoardModel Data;
    public PreviewBoardPresentation Presentation;
    public PreviewBoardDebugOptions Debug;
}
```

## 3. Board UI 分层

建议对象树：

```text
BoardRoot
├── BoardChromeRoot
├── ItemRegionRoot
│   ├── ItemSlot_0
│   ├── ItemSlot_1
│   └── ...
├── SkillRegionRoot
│   ├── SkillSlot_0
│   ├── SkillSlot_1
│   └── ...
└── DebugOverlayRoot
```

职责划分：

- `BoardChromeRoot`：底板、边框、背景等
- `ItemRegionRoot`：item card 区域
- `SkillRegionRoot`：skill card 区域
- `DebugOverlayRoot`：所有 debug markers / labels

这样可以把视觉和内容容器彻底分开。

## 4. Debug 设计

debug 不是“另一个 controller”，而是 session 输入中的一组 options。

```csharp
internal sealed class PreviewBoardDebugOptions
{
    public bool Enabled;
    public bool ShowAnchorPoint;
    public bool ShowItemSlots;
    public bool ShowSkillSlots;
    public bool ShowCardBounds;
    public bool ShowLabels;
}
```

`BoardDebugOverlay` 由 `BoardView` 持有，但由 `PreviewBoardDebugOptions` 驱动。

建议分组：

- `AnchorPoint`
- `ItemSlotPoints`
- `SkillSlotPoints`
- `CardBounds`
- `DebugLabels`

规则：

- 正式路径默认全关
- debug 路径按组开关
- debug 节点随着 render model 一起刷新
- debug 可见性属于 presentation state，不属于 data 逻辑

## 5. 数据流

统一数据流如下：

```text
PreviewBoardRequest
├── IPreviewDataSource
├── IBoardAnchorStrategy
├── PreviewBoardPresentation
└── PreviewBoardDebugOptions

PreviewBoardSession.Tick()
  -> data source produces PreviewBoardModel
  -> anchor strategy produces BoardPose
  -> session builds BoardRenderModel
  -> BoardView.Render(BoardRenderModel)
```

这意味着：

- 正式路径和 debug 路径都只是在构造不同 request
- view 只消费统一 render model
- data / anchor / debug 各自单向输入 session

## 6. 为什么这是更清晰的结构

当前问题的根源不是代码量，而是边界错误：

- board 自己持有太多布局和调试细节
- controller 同时处理 visible、anchor、data cloning、async rebuild
- debug controller 同时承担数据源、输入处理、anchor 调节、layout 调节
- runtime bridge 复制固定 layout 和 fixed anchor 逻辑

新的结构把这些问题分解为：

- `session` 处理生命周期
- `data` 处理模型
- `anchor` 处理 pose
- `view` 处理呈现
- `debug` 处理辅助可视化

每一层都能单独测试，也能单独替换。

## 7. 迁移建议

按风险最低的顺序迁移：

1. 先定义新的 request/model/presentation/debug/pose 类型
2. 新建 `PreviewBoardSession`
3. 把现有 board 渲染能力搬进 `BoardView`
4. 把现有 fixed / tracked anchor 逻辑改造成 strategy
5. 把 monster db、encounter cache、hand preview 分别做成 data source
6. 最后把 debug 输入和 debug markers 接到新的 session 架构

这样可以先收敛边界，再逐步迁出旧类。
