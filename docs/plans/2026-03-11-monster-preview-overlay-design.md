# Monster Preview Overlay Design

**Date:** 2026-03-11

**Goal:** 设计一个独立于旧实现的 3D world-space overlay 系统。该系统可以统一控制显示开关，可以配置或跟踪一个世界锚点，在该锚点下创建一个 `board` 作为根对象，并将多张预览卡片作为 `board` 的子对象进行布局和更新。

## 1. 背景与问题

旧实现的问题不在于“生成了几张卡”，而在于对象职责混乱：

- overlay 的显示逻辑、定位逻辑、卡片实例化逻辑、调试逻辑耦合在同一个类里
- `board` 没有成为一个明确的世界对象，而是临时依赖场上已有 socket 推导位置
- 调试路径和正式路径没有隔离，容易把临时逻辑带进生产代码
- 坐标系统没有围绕《The Bazaar》的实际 3D 俯视角规则来设计

根据 `postion.md`，这个系统必须按以下世界坐标语义设计：

- `X`：屏幕左右
- `Z`：屏幕上下
- `Y`：镜头距离 / 物体高度

其中：

- 想解决遮挡、叠放、压桌面的问题，优先调整 `Y`
- 想让 overlay 在视觉上更靠上或更靠下，调整 `Z`
- 该系统处理的是 3D 世界对象，不是 `RectTransform` UI

## 2. 设计目标

本次设计只关注一件事：建立一个可以长期扩展的 overlay 分层模型。

目标如下：

- overlay 可以统一开关
- overlay 可以承载一个独立的 `board`
- `board` 的位置与旋转来自一个“锚点来源”
- 锚点来源可以是固定世界坐标，也可以是“跟踪某个对象”
- 卡片通过统一工厂函数创建，并挂载到 `board` 下
- 布局只在 `board` 局部空间内完成
- 调试能力与正式逻辑隔离，调试代码不污染线上路径

非目标：

- 不兼容旧的 socket 定位逻辑
- 不复用旧 overlay 的内部结构
- 不在本设计里引入复杂的配置树或通用 scene graph

## 3. 推荐方案

推荐采用“分层 Overlay，Board 作为核心对象”的方案。

整体分成四层：

1. `OverlayController`
2. `AnchorSource`
3. `PreviewBoard`
4. `PreviewCardFactory`

这四层里，真正的核心对象是 `PreviewBoard`。

- `OverlayController` 负责生命周期和开关
- `AnchorSource` 负责给出世界锚点
- `PreviewBoard` 负责承载与布局
- `PreviewCardFactory` 负责实例化卡片

这样可以保证：

- 定位来源可替换
- 卡片生成方式可替换
- 调试和正式使用同一套 `board` 布局逻辑
- 代码天然按职责拆开，而不是堆到一个 `MonoBehaviour`

## 4. 架构分层

### 4.1 OverlayController

职责：

- 创建和销毁 overlay 根对象
- 控制显隐
- 持有当前 `AnchorSource`
- 接收外部输入的卡片列表
- 驱动 `PreviewBoard` 同步状态

不负责：

- 直接计算卡片局部布局
- 直接实例化卡片资源
- 持有 debug 面板逻辑

建议接口：

```csharp
public sealed class MonsterPreviewOverlayController
{
    public bool Visible { get; set; }

    public void SetAnchorSource(IOverlayAnchorSource anchorSource);
    public void SetCards(IReadOnlyList<PreviewCardSpec> cards);
    public void ClearCards();
    public void Refresh();
}
```

### 4.2 AnchorSource

职责：

- 回答“board 当前应该位于哪里、朝向哪里”

建议接口：

```csharp
public interface IOverlayAnchorSource
{
    bool TryGetAnchor(out Vector3 position, out Quaternion rotation);
}
```

推荐两个实现：

- `FixedWorldAnchorSource`
  - 用于 debug
  - 直接返回配置里的世界坐标和旋转

- `TrackedObjectAnchorSource`
  - 用于正式逻辑
  - 由外部代码先解析目标 `Transform`
  - 再将目标 `Transform.position / rotation` 作为锚点输出

关键点：

- `board` 不关心锚点来自哪里
- “固定坐标”和“跟踪对象”只是锚点来源不同
- 最终落到的都是世界坐标和朝向

### 4.3 PreviewBoard

职责：

- 作为最大的场景对象存在
- 拥有一个独立根节点
- 根据锚点更新自身世界位置和旋转
- 管理所有卡片子对象
- 在局部空间内完成排布

建议对象树：

```text
OverlayRoot
└── PreviewBoardRoot
    ├── CardSlot_0
    │   └── CardInstance
    ├── CardSlot_1
    │   └── CardInstance
    └── CardSlot_2
        └── CardInstance
```

说明：

- `PreviewBoardRoot` 是世界空间根节点
- `CardSlot_i` 是布局节点
- `CardInstance` 是具体生成出来的卡片对象

推荐这样做的原因：

- board 的整体平移、旋转、缩放只影响根节点
- 单张卡片的位置变化只作用在 slot
- 后续若要加动画、排序、插入、删除，slot 是天然缓冲层

### 4.4 PreviewCardFactory

职责：

- 根据卡片描述创建卡片实例
- 进行初始化
- 支持更新与销毁

建议接口：

```csharp
public interface IPreviewCardFactory
{
    GameObject CreateCard(PreviewCardSpec spec, Transform parent);
    void UpdateCard(GameObject cardObject, PreviewCardSpec spec);
    void DestroyCard(GameObject cardObject);
}
```

关键点：

- `PreviewBoard` 只管“需要几张卡、放到哪里”
- `PreviewCardFactory` 只管“这张卡怎样创建出来”
- 卡片资源实现细节不能反向污染布局层

## 5. 坐标与布局设计

### 5.1 世界空间原则

overlay 本质是世界空间对象，不是屏幕 UI。

因此：

- `board` 必须拥有独立的世界根节点
- 外部只向它提供世界 `position` 和 `rotation`
- 任何“参考已有 socket 推导坐标”的做法都不是系统基础能力

### 5.2 Board 局部布局原则

卡片布局统一在 `board` 的局部空间里完成。

推荐约定：

- `local X`：控制卡片左右排列
- `local Z`：控制整排在屏幕上的上下偏移
- `local Y`：控制整排抬高，用于避免遮挡

这个约定直接继承 `postion.md` 的结论：

- `Y` 是解决遮挡和层叠的主要控制轴
- `Z` 是视觉上的“往屏幕上方/下方移动”
- `X` 是横向排布主轴

### 5.3 Board 布局配置

建议引入专门的布局配置对象：

```csharp
public sealed class PreviewBoardLayout
{
    public Vector3 LocalOffset;
    public Vector3 CardSpacing;
    public Vector3 CardScale;
}
```

建议语义：

- `LocalOffset`
  - 相对锚点的整体偏移
  - 其中 `LocalOffset.y` 主要用于抬高
  - `LocalOffset.z` 用于调上下

- `CardSpacing`
  - 多张卡之间的间距
  - 当前主要使用 `CardSpacing.x`
  - 后续可扩展到扇形、双排、错列

- `CardScale`
  - 控制 board 下卡片统一缩放

### 5.4 为什么必须保留 rotation

虽然最终的核心落点是 `Vector3(x, y, z)`，但 `board` 不能只存位置，不存朝向。

原因：

- 如果只存坐标，卡片仍然可能朝向错误
- 当 `board` 跟踪某个对象时，位置和朝向往往应一起继承
- 局部布局依赖一个稳定的局部坐标系

因此锚点必须包含：

- `position`
- `rotation`

## 6. 卡片数据模型

卡片创建应围绕一个稳定的数据对象，不直接依赖 UI 或调试输入格式。

建议模型：

```csharp
public sealed class PreviewCardSpec
{
    public string TemplateId;
    public int Tier;
    public string Enchant;
    public Dictionary<int, int> Attributes;
}
```

后续如有必要可加：

- `Id`
- `Label`
- `Variant`
- `LocalOffsetOverride`
- `Visible`

但在当前阶段保持最小模型即可。

## 7. Board 对外行为

`PreviewBoard` 需要暴露清晰的卡片管理接口，而不是直接接受一段 JSON 字符串。

建议接口：

```csharp
public interface IPreviewBoard
{
    void SetCards(IReadOnlyList<PreviewCardSpec> cards);
    void AddCard(PreviewCardSpec card);
    void RemoveCard(string cardId);
    void ClearCards();
    void SetLayout(PreviewBoardLayout layout);
    void SetVisible(bool visible);
    void UpdateAnchor(Vector3 position, Quaternion rotation);
}
```

说明：

- 对外统一用结构化数据，不用字符串协议做内部边界
- JSON 只能出现在外部适配层，不能成为 board 的核心接口
- `UpdateAnchor` 只负责更新 board 根节点
- `SetCards` 负责同步子卡片集合

## 8. 调试与正式逻辑的隔离

这是本设计的硬约束。

### 8.1 正式路径

正式路径只允许包含：

- `OverlayController`
- `TrackedObjectAnchorSource`
- 正式卡片数据提供者
- `PreviewBoard`
- `PreviewCardFactory`

这条路径不应该依赖：

- 快捷键
- 手动坐标拖动
- 测试数据注入
- 调试菜单

### 8.2 Debug 路径

建议单独提供：

- `OverlayDebugController`
  或
- `OverlayDebugPanel`

职责：

- 切换 overlay 开关
- 调整固定世界坐标
- 调整 rotation
- 调整 board 的 `LocalOffset`
- 增删测试卡片
- 编辑卡片属性

关键原则：

- debug 是外部工具层
- debug 只能通过正式接口调用正式系统
- 正式系统不能反向依赖 debug 代码

也就是说：

- 正式系统可单独运行
- 去掉 debug 代码后，正式功能不应受影响

## 9. 生命周期设计

建议生命周期如下：

1. `OverlayController` 初始化
2. 创建 `OverlayRoot` 与 `PreviewBoard`
3. 注入一个 `AnchorSource`
4. 接收卡片数据并调用 `SetCards`
5. 每帧或在需要时读取锚点并刷新 `PreviewBoardRoot`
6. 显示关闭时，仅隐藏 board 或停更，不销毁接口层状态
7. 完整销毁时，再销毁 scene object 与卡片实例

设计原则：

- 显隐不等于重新建系统
- `board` 是稳定对象
- 卡片同步是数据变更，不是“重建整个 overlay”

## 10. 推荐实现草图

```csharp
public sealed class MonsterPreviewOverlayController : MonoBehaviour
{
    private IOverlayAnchorSource _anchorSource;
    private MonsterPreviewBoard _board;
    private IReadOnlyList<PreviewCardSpec> _cards;

    public void SetAnchorSource(IOverlayAnchorSource anchorSource) { }
    public void SetCards(IReadOnlyList<PreviewCardSpec> cards) { }
    public void SetVisible(bool visible) { }

    private void LateUpdate()
    {
        if (_anchorSource == null || _board == null)
            return;

        if (_anchorSource.TryGetAnchor(out var position, out var rotation))
            _board.UpdateAnchor(position, rotation);
    }
}
```

```csharp
public sealed class MonsterPreviewBoard
{
    public void UpdateAnchor(Vector3 position, Quaternion rotation) { }
    public void SetLayout(PreviewBoardLayout layout) { }
    public void SetCards(IReadOnlyList<PreviewCardSpec> cards) { }
}
```

## 11. 决策总结

最终确定以下设计决策：

- 不参考旧实现
- 不使用 `playerStorageSockets` 作为系统基础挂点
- `board` 是独立世界对象
- 锚点来源抽象成接口
- 调试时使用固定世界坐标
- 正式时使用“解析目标对象 Transform”的跟踪锚点
- 卡片是 `board` 的子对象
- 布局统一在 `board` 局部空间完成
- 调试入口与正式入口分离

## 12. 后续实现建议

后续实现时建议按以下顺序推进：

1. 先实现 `IOverlayAnchorSource` 与 `FixedWorldAnchorSource`
2. 再实现最小 `PreviewBoard`
3. 再实现单卡 `PreviewCardFactory`
4. 再实现 `SetCards` 的增删同步
5. 最后再补 `TrackedObjectAnchorSource` 和 debug 工具层

这样可以先验证分层是否成立，再逐步把线上路径与调试路径接上。
