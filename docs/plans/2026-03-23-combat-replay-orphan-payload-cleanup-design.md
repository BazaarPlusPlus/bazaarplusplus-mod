# Combat Replay Orphan Payload Cleanup Design

## Goal

在不改变现有 `payload -> manifest` 可见性语义的前提下，消除新的 orphan replay payload，并清理历史已经遗留的 orphan payload 文件。

## Decision

采用“即时回滚 + 启动时惰性历史清理”的最小方案：

- 保持现有写入顺序：先写 payload，再写 manifest
- 如果 payload 已保存成功、但 manifest 保存失败，立即删除刚写出的 payload 文件
- 在 `CombatReplayRuntime.Awake()` 初始化完 payload store 与 battle catalog 后，扫描 payload 目录并删除 catalog 中不存在对应 manifest 的历史 orphan payload

## Why This Design

这个问题本质上是局部持久化一致性，而不是 battle/replay 架构需要重做。

如果直接上“两阶段文件写入”或引入新的事务边界，会把这次 hardening 扩展到 payload store API、queue 协议和 runtime 协调的更大范围。相反，保留现有顺序并补上 rollback + lazy cleanup，有这些好处：

- battle 的“对外可见性”仍然只由 manifest 决定
- 新 orphan payload 会在失败当场被清掉
- 历史 orphan payload 会在 runtime 启动时被惰性清理
- 不需要改 sqlite schema，不需要重写 replay 读取路径

## Scope

### In Scope

- 为 `CombatReplayPayloadStore` 增加删除和枚举 payload battle id 的能力
- 在 `CombatReplayPersistenceQueue` 里对 manifest-save failure 做 payload rollback
- 在 `CombatReplayRuntime` 启动时清理历史 orphan payload
- 为上述行为补充 source-reading assertions

### Out of Scope

- temp-file / rename 型两阶段文件协议
- sqlite schema 变更
- replay 读取与 bootstrap 行为变更
- run logging、tooltip、history panel 逻辑
- 长驻后台清理任务或周期性 sweep

## Current State

当前保存链路是：

1. `CombatReplayRuntime` 捕获一场 battle 的 `payload` 和 `manifest`
2. `CombatReplayPersistenceQueue` 先调用 `CombatReplayPayloadStore.Save(payload)`
3. 再调用 `PvpBattleCatalog.Save(manifest)`
4. 成功后主线程发布 `PvpBattleRecorded`

这个顺序保证“manifest 成功之前 battle 不可见”，但也留下一个已知设计 debt：

- 如果 payload 写入成功、manifest 写入失败，磁盘上会留下一个没有 catalog 入口的 orphan payload

## Proposed Changes

### 1. `CombatReplayPayloadStore`

新增两个局部 API：

- `Delete(string battleId)`
  - 根据 `battleId` 删除 `*.payload.json`
  - 文件不存在时静默返回
- `ListBattleIds()`
  - 扫描 payload 根目录下的 `*.payload.json`
  - 从文件名中恢复 `battleId`
  - 返回 battle id 列表给 runtime 做历史 orphan 清理

这两个 API 只服务于 hardening，不改变现有 `Save / Load / Exists` 读写语义。

### 2. `CombatReplayPersistenceQueue`

构造函数增加一个 payload-delete 依赖，例如 `Action<string> deletePayload`。

worker 保存逻辑改成：

1. `_savePayload(request.Payload)`
2. 标记 `payloadSaved = true`
3. `_saveManifest(request.Manifest)`
4. 如果第 3 步抛错，并且 `payloadSaved == true`
   - 调用 `_deletePayload(request.Payload.BattleId)` 尝试回滚
   - 如果回滚删除失败，只记 warning，不覆盖原始 manifest-save exception
5. 仍然把失败结果入队到 `_completed`

这样做能保证：

- payload-save failure 不会触发误删
- manifest-save failure 不会留下新的 orphan payload
- 主线程仍然能观察到原始失败并继续沿用现有错误处理路径

### 3. `CombatReplayRuntime`

在 `Awake()` 初始化完 `_battleCatalog` 和 `_payloadStore` 后，调用一个新的私有方法，例如 `CleanupOrphanedPayloads()`。

该方法流程：

1. 调用 `_payloadStore.ListBattleIds()`
2. 对每个 `battleId`
   - 若 `_battleCatalog.TryLoad(battleId) != null`，保留
   - 若 `_battleCatalog.TryLoad(battleId) == null`，调用 `_payloadStore.Delete(battleId)` 删除 orphan payload
3. 对删除成功、删除失败、枚举异常分别记录日志

这个 cleanup 是 best-effort：

- 单个 payload 删除失败，不阻断 runtime 启动
- 遇到坏文件名或目录扫描异常时，记录日志并尽量继续

## Error Handling

- manifest-save failure：
  - 仍以 manifest-save exception 作为主失败
  - rollback delete failure 只作为附加 warning 暴露
- 历史 cleanup failure：
  - 不抛出到 `Awake()`
  - 用 `BppLog.Warn(...)` 或 `BppLog.Error(...)` 暴露
- 不增加新的 silent-failure path

## Testing Strategy

继续沿用当前 replay hardening 的 source-reading test 风格，只修改：

- `tests/CombatReplayRecording.Tests/Program.cs`

断言要求：

- `CombatReplayPayloadStore` 暴露 `Delete(...)` 和 `ListBattleIds()`
- `CombatReplayPersistenceQueue` 在 manifest 保存失败后会调用 payload delete 做 rollback
- `CombatReplayRuntime.Awake()` 调用 orphan cleanup
- orphan cleanup 同时使用 `_payloadStore.ListBattleIds()`、`_battleCatalog.TryLoad(...)` 和 `_payloadStore.Delete(...)`

focused verification 只跑：

- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

## Tradeoffs

- 启动时 cleanup 是 O(N) 扫描 payload 目录，但当前 replay 资产规模可接受，且这一步只在 runtime 初始化时发生
- 这个方案不会自动修复“manifest 存在、payload 缺失”的另一类不一致；那是不同方向的 hardening
- 不引入事务或 temp-file 协议，换来更小的改动范围和更低的回归风险
