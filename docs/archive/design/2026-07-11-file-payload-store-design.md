# FileBackedPayloadStore：两份战斗负载存储合一 设计稿

状态：批量流水线 Phase B 草稿，待红队 + 用户统一确认。来源：架构评审候选 3。

## 事实基础（盘点已核）

- `CombatReplayPayloadStore`（`.payload.mpack.gz`，多 `Exists`/`ListBattleIds`，`Save` 前额外重建目录）与 `GhostBattlePayloadStore`（`.ghost.mpack.gz`，多静态 `ResolveDirectory` 兄弟目录策略）的 `WriteAllBytesAtomically` 互为逐字节复制；与 `Infrastructure/AtomicFileWriter` 的差异仅两处不可达边缘（null 目录：store 抛 vs writer 跳过；temp 路径构造等价）+ writer 每次写入前多一个 `Directory.CreateDirectory`（无害）。
- 两码复用同一 `MessagePackGzipCodec` 泛型；两 store 均按 `battleId` 字符串键、同构 `GetFilePath`、无 id 消毒（现状保持）。
- 线程：CombatReplay 走专用后台队列/Task.Run；Ghost 实际主线程。两 store 皆无锁——安全性来自各自调用纪律，共享实现不得引入锁或改变此现状。
- **反射按名钉死**：测试用 `Type.GetType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore, BazaarPlusPlus")` 等全名 + `"Save"/"Load"/"Exists"` 方法名 + 单参 ctor `Activator.CreateInstance(type, tempRoot)`；CombatReplayRecording.Tests 还钉了**精确落盘文件名与无 temp 残留**。⇒ 两个类名、命名空间、方法名、ctor 签名、FileSuffix 全部不可变。

## 设计（推荐决策，待批）

1. **新模块** `Infrastructure/FileBackedPayloadStore.cs`：`internal sealed class FileBackedPayloadStore<T> where T : class`。
   - ctor：`(string rootPath, string fileSuffix, Func<T, byte[]> serialize, TryDeserialize<T> tryDeserialize, string logTag)`（`TryDeserialize<T>` 委托匹配现有 codec 的 `(byte[]?, out T?, out string?)` 形状）；ctor 校验 + `Directory.CreateDirectory` 照抄。
   - 成员：**`Save(string battleId, T payload)`**（红队 major 修订：泛型无法从 `T : class` 取 `BattleId`——id 由调用方显式传入，具名门面转发 `_store.Save(payload.BattleId, payload)`，门面自身的 `Save(PvpReplayPayload)` / `Save(GhostBattlePayload)` 公开签名不变——反射按方法名钉住）；`Load`/`Delete`/`Exists`/`ListIds` 本就以 `string battleId` 为参——语义逐一照抄现状（Load 的 Warn 日志用 logTag；Delete 无 try/catch 照旧；ListIds 惰性 yield 照旧；Save 的 null/空 BattleId 守卫留在门面，与现状位置一致）。
   - 写入委托 `AtomicFileWriter.Write`（吸收 2026-06-11 在案待办）。
2. **两个既有类保留为具名薄门面**（反射钉死所致，且这是**有意保留的浅包装**——删除测验因外部钉住而不适用）：原名原命名空间原签名，内部各持一个 `FileBackedPayloadStore<T>`；`GhostBattlePayloadStore.ResolveDirectory` 静态原样保留；CombatReplay 独有的 `Exists`/`ListBattleIds` 由门面转发（Ghost 门面不暴露）。
3. **行为保真与已知微偏差**：
   - D1：`GhostBattlePayloadStore.Save` 经 `AtomicFileWriter` 后**获得**写前目录重建（现状没有）——目录被外力删除时从抛异常变为自愈；CombatReplay 的 Save 前显式重建则变为冗余但保留语义（AtomicFileWriter 兜底）。
   - D2：null-目录边缘从抛 `InvalidOperationException` 变为跳过（不可达：rootPath 恒为绝对路径）。
   - D3（红队补）：现状两 store 的 temp 写入在 try/finally **之外**（写入中途失败会泄漏 `.tmp`），`AtomicFileWriter` 把写入放在 try 内、finally 兜底清理——良性强化「无 temp 残留」不变量，happy path 逐字节等价。
   - 落盘文件名、temp 命名、Replace/Move 回退逐字节等价（测试钉住项零变化）。

## 测试计划

- 现有反射测试**原样存活**（类名/方法名/ctor/文件名全部不变）——这是本设计的硬约束而非巧合。
- 新增（补盘点发现的覆盖不对称）：GhostBattleSync.Tests 补「精确落盘文件名 + 无 temp 残留」断言（对齐 CombatReplay 侧已有的）。
- `ListBattleIds`/`CleanupOrphanedPayloads` 零覆盖维持现状（后者需编排环境，不在本轮）。
- 验证：`dotnet run --project tests/CombatReplayRecording.Tests` + `tests/GhostBattleSync.Tests` + `dotnet build`。

## 不做的事

- battleId 消毒（现状无，加了是行为变化）。
- 加锁/线程安全化（两侧调用纪律各自成立）。
- 合并两个具名门面或改名（反射钉死 + 名字承载目录策略差异）。
