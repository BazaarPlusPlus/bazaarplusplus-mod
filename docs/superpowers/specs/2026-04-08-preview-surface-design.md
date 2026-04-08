# Preview Surface Design

## Goal

抽离一套可复用的预览展示能力，输入一份 preview model 后即可展示 item/skill 卡片、地毯和基础展示布局，并保留单卡 hover 时复用现有 tooltip 链路的能力。

第一阶段的目标不是替换官方原生 tooltip 体系，也不是重写 monster 数据来源，而是把当前 Bazaar++ 自定义 `MonsterPreview` 的“展示层”从 monster 语义中解耦出来，方便后续直接移除自定义 monster preview runtime，只保留数据投影或切回原生实现。

## Scope

本设计覆盖：

- 单卡预览对象的创建、更新、销毁
- item/skill 预览卡的统一展示接口
- 预览棋盘的布局、地毯、显示隐藏、anchor 与 presentation
- 现有 `MonsterPreview` 到新展示层的适配迁移

本设计不覆盖：

- 官方原生 `CardTooltipController` / `MonsterBoardTooltip` 的 patch 或替换
- `MonsterPreview` 数据投影逻辑的大规模重写
- 新增一套与现有 tooltip 平行的 tooltip 系统
- 为非 monster 场景立即接入新展示层

## Current State

当前 Bazaar++ 的自定义 monster preview 已经具备一个接近通用展示层的雏形，但命名和职责仍然绑定在 `MonsterPreview` 语境中：

- `PreviewCardSpec` 已经承担预览卡的输入模型
- `MonsterPreviewItemCardFactory` / `MonsterPreviewSkillCardFactory` 已经承担预览卡对象构建
- `MonsterPreviewBoard` 已经承担棋盘布局、技能区、地毯与可见性
- `MonsterPreviewBoardRenderTarget` / `PreviewBoardSession` 已经承担整板 render 触发

主要问题不是“能不能用”，而是边界不清：

- card preview 能力和 monster preview 宿主能力混在一起
- board 渲染能力被 monster 命名绑定
- 后续如果删除自定义 `MonsterPreview`，现有展示层会一起被删除，无法平移复用

## Design Principles

### 1. 卡片预览核心和怪物宿主语义分离

`spec -> card preview object -> hover tooltip` 应当是独立能力；它不应该知道 monster、encounter 或 run context。

### 2. 地毯属于 board 展示层，不属于单卡预览核心

地毯是背景和布局语义的一部分，和 card object 生命周期不同。它应属于 board surface，而不是单卡 surface。

### 3. 对外接口保持简单，内部允许第一版整板重建

第一版优先暴露稳定的 `Render(model)` 风格接口。内部先允许全量重建，后续再优化成增量 diff，不改变调用侧。

### 4. 复用现有 tooltip 链路

预览卡 hover 后仍然应走现有 tooltip 体系，不新增另一条 tooltip 展示链路。

### 5. 迁移优先于重写

第一阶段优先把能力边界抽出来，并通过适配器把现有 `MonsterPreview` 接到新接口上；不追求一口气重写完所有类。

### 6. 第一版优先最小对象模型，不提前引入 handle 抽象

第一版的 card surface 接口允许直接暴露 `GameObject`，这是有意 trade-off，不是遗漏。当前实现天然围绕 Unity object lifecycle、`ItemController` 和 `SkillController` 工作，立刻引入 handle abstraction 会把第一阶段从“抽能力边界”升级成“重写对象生命周期模型”。

后续如果 board surface 之外也出现新的 card preview consumer，再考虑收敛为 handle/wrapper。

## Proposed Architecture

### Layer 1: Preview Card Surface

职责：

- 输入 `PreviewCardSpec`
- 创建 item 或 skill 的预览 card object
- 销毁或刷新预览 card object
- 保留 hover tooltip 行为

这一层的核心边界是“单卡对象生命周期”，不处理棋盘槽位、地毯、monster metadata 或整体显示逻辑。

建议接口：

```csharp
internal interface IPreviewCardSurface
{
    Task<GameObject> CreateAsync(PreviewCardSpec spec, Transform parent);
    Task UpdateAsync(GameObject cardObject, PreviewCardSpec spec);
    void Destroy(GameObject cardObject);
}
```

实现策略：

- item 和 skill 仍然分成两种实现
- 第一版直接迁移和重命名现有 `MonsterPreviewItemCardFactory` / `MonsterPreviewSkillCardFactory`
- 保留当前通过 `ItemController` / `SkillController` 生成卡片对象的方式
- 保留当前 hover 时复用现有 tooltip 的行为
- 第一版继续让调用方持有 `GameObject` 引用；不额外引入 `IPreviewCardHandle`

### Layer 2: Preview Board Surface

职责：

- 输入完整 preview board model
- 渲染 item 区、skill 区、地毯和 metadata 展示
- 负责整板可见性和 anchor 更新
- 负责 presentation/debug options 等展示参数

建议接口：

```csharp
internal interface IPreviewBoardSurface : IDisposable
{
    Transform RootTransform { get; }
    bool IsAlive { get; }

    void SetPresentation(PreviewBoardPresentation presentation);
    void SetDebugOptions(PreviewBoardDebugOptions debugOptions);
    void SetVisible(bool visible);
    void UpdateAnchor(Vector3 position, Quaternion rotation);
    Task RenderAsync(PreviewBoardModel model, CancellationToken cancellationToken = default);
    void Clear();
}
```

组合关系：

- `PreviewBoardSurface` 通过构造函数接收 item/skill 两个 `IPreviewCardSurface`
- board surface 只依赖抽象接口，不直接 `new` 具体 card surface 实现
- render target 或 composition root 负责装配具体实现

这一层负责：

- 10 格 item 槽位布局
- skill 区布局
- 地毯贴图
- 怪物信息条等附加展示

这一层不负责：

- 从 monster/encounter 生成 `PreviewBoardModel`
- 决定什么时候展示

### Layer 3: Preview Board Host / Render Target

职责：

- 把 session/render coordinator 与 `IPreviewBoardSurface` 接起来
- 管理 render request、可见性与重建节流

这一层本质是现在的：

- `MonsterPreviewBoardRenderTarget`
- `PreviewBoardSession`
- `MonsterPreviewOverlayCoordinator`

但它们应该依赖通用的 `PreviewBoardSurface`，而不是直接依赖 `MonsterPreviewBoard`。

其中 `MonsterPreviewOverlayCoordinator` 在迁移期属于 Layer 3 和 Layer 4 之间的过渡协调器。第一阶段允许它暂时留在此层描述中，但长期目标是让它只承担 session/request 协调，不承载 monster-specific 展示语义。

### Layer 4: Monster Preview Host

职责：

- 读取 monster 或 encounter 数据
- 投影成 `PreviewBoardModel`
- 触发展示层 render
- 决定何时显示、隐藏，以及和现有 runtime 的交互

这一层保留 monster 语义，继续存在于 `Game/MonsterPreview` 下。后续如果删除自定义 monster preview runtime，只需要删除这一层，而 card/board preview 能力仍可保留。

## Data Model

### PreviewCardSpec

继续作为单卡输入模型使用，字段保持当前语义：

- `TemplateId`
- `Tier`
- `Size`
- `Enchant`
- `Attributes`
- `SourceName`

第一版不主动扩字段，避免把“未来可能用到的数据”提前塞进通用模型。

### PreviewBoardModel

继续作为整板输入模型，建议明确划分为三类数据：

- `ItemCards`
- `SkillCards`
- `Metadata`

其中：

- `ItemCards` / `SkillCards` 供 board surface 用于渲染 card objects
- `Metadata` 供宿主展示条或 board surface 的附加文本展示使用

第一版中 `Metadata` 明确为 `IReadOnlyDictionary<string, string>`。当前替换目标仍然是 `MonsterPreview`，metadata 的主要用途也是文本展示，因此没有必要在第一版扩成 `object` 型容器。等 future consumer 真正出现后再决定是否进一步类型化。

## File Layout

建议新增目录并逐步迁移：

- `Game/PreviewSurface/Models/PreviewBoardModel.cs`
- `Game/PreviewSurface/Models/PreviewCardSpec.cs`
- `Game/PreviewSurface/Models/PreviewBoardPresentation.cs`
- `Game/PreviewSurface/Cards/IPreviewCardSurface.cs`
- `Game/PreviewSurface/Cards/PreviewItemCardSurface.cs`
- `Game/PreviewSurface/Cards/PreviewSkillCardSurface.cs`
- `Game/PreviewSurface/Board/IPreviewBoardSurface.cs`
- `Game/PreviewSurface/Board/PreviewBoardSurface.cs`
- `Game/PreviewSurface/Board/PreviewBoardRenderTarget.cs`

第一阶段可以通过 type move 或 rename 的方式迁移现有类，而不是重写实现。

`Game/MonsterPreview` 在迁移后主要保留：

- projector
- data source
- runtime trigger
- native/custom mode switch

## Migration Plan

### Phase 1: Introduce Neutral Naming

- 新增 `Game/PreviewSurface` 目录
- 将 `PreviewCardSpec`、`PreviewBoardModel` 等通用模型迁到新目录
- 将 `MonsterPreviewItemCardFactory` / `MonsterPreviewSkillCardFactory` 迁移为 card surface 实现
- 将 `MonsterPreviewBoard` 迁移为 `PreviewBoardSurface`

这一步允许通过 wrapper 或 adapter 保持原入口可用。

完成判定：

- `PreviewSurface` 目录与 neutral 命名类型已经存在
- 旧 `MonsterPreview` 入口仍可通过 adapter 或 wrapper 正常调用
- `Game/MonsterPreview` 之外已经可以引用 preview card/board 抽象类型
- 相关项目可完成最小构建验证

### Phase 2: Retarget MonsterPreview Runtime

- 让 `MonsterPreviewBoardRenderTarget` 改为依赖 `IPreviewBoardSurface`
- 让 `MonsterPreviewOverlayCoordinator` 和 `PreviewBoardSession` 不再依赖 monster 命名类
- 保持 `MonsterLockShowcaseRuntime` 与 `MonsterPreviewController` 的行为不变

这一步的目标是“monster preview 继续工作，但展示层已经独立”。

完成判定：

- `MonsterPreviewBoardRenderTarget` 不再直接依赖旧 `MonsterPreviewBoard`
- `MonsterPreview` 相关 runtime 不再直接知道 card object 创建细节
- 当前自定义 `MonsterPreview` 的展示行为与迁移前一致
- show/hide、地毯和 hover tooltip 行为通过最小验证

### Phase 3: Shrink MonsterPreview Responsibility

- `MonsterPreview` 目录仅保留数据投影和触发逻辑
- 展示相关实现全部归到 `PreviewSurface`

完成后，删除自定义 `MonsterPreview` runtime 时只会影响宿主层，不会影响 card/board preview 通用能力。

完成判定：

- `Game/MonsterPreview` 目录不再持有 card/board 展示实现
- `PreviewSurface` 可在不引用 monster-specific runtime 的前提下独立工作
- 删除自定义 `MonsterPreview` 展示宿主不会破坏 preview surface 核心能力

## Behavior Requirements

### Rendering

- 给定 item/skill specs 后，展示层必须能渲染完整预览板
- item 与 skill 卡必须继续使用现有控制器和资源加载链路
- 卡牌 tier、attribute、enchant 显示行为必须保持与当前 preview 一致

### Render Concurrency

- `PreviewBoardSurface.RenderAsync` 的第一版语义是 `cancel-and-replace`
- 任意时刻只允许一个 active render 提交结果
- 当新的 render request 到来时，旧 request 必须被取消，且其后续结果不得再写回 surface
- 调用方不需要自行排队；session/host 层负责只保留 latest request

### Hover Tooltip

- 鼠标悬停单卡时，继续复用当前 tooltip 系统
- 若当前已有 locked tooltip，则单卡 hover 应继续走 secondary tooltip 逻辑
- 不新增 preview 专属 tooltip controller

### Carpet

- 地毯是 board surface 的一部分
- 宿主层可通过 model 或 presentation 提供地毯资源
- 没有地毯时，board surface 必须允许安全降级显示

### Visibility and Anchor

- board surface 必须支持显式 `SetVisible(bool)`
- board surface 必须支持在外部更新 anchor pose
- hide/show 不应破坏内部 card object 生命周期的一致性

## Non-Goals

- 第一版不要求 diff-based incremental update
- 第一版不要求对外支持多种布局模板
- 第一版不要求把 metadata 强类型化
- 第一版不要求所有现有 monster preview 文件都立即重命名完成

## Risks

### 1. Monster-specific naming leaks into the new abstraction

如果第一阶段只是简单复制旧实现而不重新定义接口，新的 `PreviewSurface` 仍会被 monster 语义污染。

缓解方式：

- 先定 neutral interface，再迁移旧实现
- 避免在 card/board surface 接口中出现 monster/encounter 命名

### 2. Tooltip behavior regresses during migration

预览卡的价值之一就是 hover 后能直接走现有 tooltip。如果迁移时改动 card object 的创建方式，tooltip 有概率回归。

缓解方式：

- 第一版继续复用当前 `ItemController` / `SkillController` 的生成方式
- 不在迁移阶段重写 hover 事件链路

### 3. Over-abstracting too early

如果第一版为了“未来通用”过度抽象，容易引入大量暂时没有使用方的接口。

缓解方式：

- 只抽当前 MonsterPreview 替换明确需要的两层能力：card surface 和 board surface
- 保持外部接口少而稳定

## Verification

本次设计对应的第一轮实现至少应验证：

- 当前 `MonsterPreview` 仍能渲染 item/skill cards
- 地毯仍能显示
- 单卡 hover 仍能弹出现有 tooltip
- show/hide 和 anchor 更新不回归
- 现有 model/projector 调用方不需要理解 card object 创建细节
- `Game/MonsterPreview` 不再直接引用 card object 创建细节，依赖方向符合设计边界

由于这一轮是架构抽离，不要求新增覆盖率导向测试；优先做最小可用的 targeted build 或相关测试项目验证。

## Recommendation

采用分阶段替换方案：

- 对外先提供稳定的 `PreviewBoardSurface` / `PreviewCardSurface` 抽象
- 内部第一版允许复用现有 `MonsterPreviewBoard` 与 card factory 的实现思路
- 先让 `MonsterPreview` 成为“数据宿主”，再逐步删除自定义 monster preview 展示逻辑

这样可以在不破坏现有行为的前提下，把真正可复用的预览展示能力沉淀下来，并为后续移除自定义 `MonsterPreview` 提前清理边界。
