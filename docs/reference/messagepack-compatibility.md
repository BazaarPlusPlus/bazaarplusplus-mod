# MessagePack Compatibility Notes

## Scope

这份说明只覆盖当前仓库里自定义 MessagePack DTO 的兼容性判断，主要是：

- `Game/HistoryPanel/Ghost/GhostBattlePayload.cs`
- `Game/PvpBattles/PvpReplayPayload.cs`
- `Game/Online/Models/RunBundleUploadRequestV3.cs` 里的 `RunArtifactV3` 及其嵌套模型

相关 codec 当前都使用 `ContractlessStandardResolverAllowPrivate`，并通过强类型 `Serialize<T>` / `Deserialize<T>` 读写：

- `Game/HistoryPanel/Ghost/GhostBattlePayloadCodec.cs`
- `Game/PvpBattles/PvpReplayPayloadCodec.cs`
- `Game/Online/V3RunBundleArtifactCodec.cs`

## Current Contract Shape

当前这些 DTO 没有显式 `[MessagePackObject]` / `[Key(...)]` 标注，而是依赖 contractless resolver。

这意味着：

- MessagePack contract 主要由 C# 成员名决定
- `[JsonProperty("...")]` 只影响 JSON，不影响这里的 MessagePack
- 当前用法没有看到 typeless、union、`object` 多态这类把类型全名写入 payload 的路径

## What Is Safe To Change

- 只移动 `.cs` 文件目录：安全，不影响 MessagePack
- 只调整文件物理位置，不改类型名、成员名、可见性：安全
- 只改 `namespace`：按当前用法通常也是安全的，因为 codec 都是强类型反序列化，不依赖 payload 内的类型全名

## What Breaks Compatibility

- 重命名 DTO 成员：会破坏 contractless MessagePack 兼容性
- 修改 DTO 成员类型：通常不兼容
- 改变嵌套 DTO 的成员名或成员类型：同样会破坏兼容
- 把 MessagePack DTO 从 `public` 改成 `internal`：Unity/Mono 下可能触发运行时 `MethodAccessException`

## Compatibility Rules

- 新增字段：通常安全。旧数据里没有该字段时，新代码会读到默认值
- 删除字段：通常可行。新代码会忽略旧数据中多出来的字段，但不要复用旧字段语义
- 重命名字段：不兼容，除非显式固定旧 key
- 改字段类型：通常不兼容，需要迁移逻辑

## Recommended Stable Contract

如果这些 DTO 后续还会继续演进，建议从 contractless 改成显式 MessagePack contract：

1. 给 DTO 加 `[MessagePackObject]`
2. 给每个序列化成员加 `[Key(...)]`
3. 保持 DTO 及其嵌套 DTO 为 `public`

两种 key 策略都可以：

- 字符串 key：例如 `[Key("BattleId")]`。适合在保留旧字段名兼容性的前提下重命名 C# 属性
- 整型 key：例如 `[Key(0)]`、`[Key(1)]`。适合长期稳定演进，但后续只能追加，不能复用旧编号

## Migration Strategy

如果已经存在历史 contractless 数据，又要切到显式 key，推荐迁移方式是：

1. 先尝试按新格式反序列化
2. 失败后按旧 contract 反序列化
3. 将旧对象映射到新对象
4. 等历史数据自然淘汰后，再考虑删除兼容分支

## Practical Guidance For Refactors

对当前仓库，目录重组可以直接做，只要满足下面几点：

- 不改 DTO 成员名
- 不改 DTO 成员类型
- 不把 MessagePack DTO 改成 `internal`

如果重构需要顺手整理 `namespace`，按当前实现通常不会破坏 MessagePack 读写；真正需要谨慎的是 DTO 的成员 contract，而不是源码文件的目录结构。
