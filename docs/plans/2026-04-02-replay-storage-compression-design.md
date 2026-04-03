# Replay Storage Compression Design

**Date:** 2026-04-02

## Goal

为 `ModCFServer` 增加一套仅作用于服务端存储层的 replay 压缩方案，满足以下目标：

- 对客户端上传协议完全无感
- 对客户端下载返回格式完全无感
- 仅覆盖新上传的 replay 对象
- 允许历史未压缩对象与新压缩对象长期共存
- 以较小实现复杂度换取显著的存储与带宽节省

## Background

当前 replay 上传链路已经分成两层：

- 客户端上传 battle artifact JSON
- Worker 验签后，从请求中抽出 `replay_payload` 并把 replay JSON 写入 `R2`
- 下载 replay 时，Worker 从 `R2` 读取对象并按 JSON 原样返回客户端

对应代码位置：

- `Game/ModApi/ModApiRequestSigner.cs`
- `ModCFServer/src/features/uploadBattleArtifact.ts`
- `ModCFServer/src/features/downloadReplay.ts`

这条链路的关键约束是：

- 客户端对原始请求体做 `SHA-256` 和 RSA 签名
- Worker 验签依赖收到的原始 body

因此，只要要求客户端无感，服务端就不能修改入站请求体语义，也不能要求客户端改变 replay schema。

## Problem Statement

当前 replay payload 的主要体积来自：

- `spawn_message_base64`
- `combat_message_base64`
- `despawn_message_base64`

其中 `combat_message_base64` 是绝对大头。对样本文件 `1fJaG8XUN1pXL9VBEDqO+mnlbo=.json` 的测量结果如下：

- 整个 replay JSON 约 `78 KB`
- JSON 本身已经是紧凑格式，去空白没有收益
- 整体 `gzip/deflate` 后约 `35 KB`
- 整体 `brotli` 后约 `33 KB`

这说明问题不在 JSON 外壳，而在服务端把高度可压缩的大对象按原样存进了 `R2`。

## Constraints

本设计需要遵守以下边界：

- 不能修改客户端上传 JSON 结构
- 不能修改客户端签名逻辑
- 不能让客户端下载时感知压缩格式
- 不能要求先迁移历史 replay 才能上线
- 不能把“是否压缩”的判断建立在文件后缀或内容猜测上

## Current Design

当前 `ModCFServer` 的 replay 流程是：

1. `/battles` 接收客户端上传的原始 JSON
2. `requireVerifiedClient()` 校验：
   - `X-BPP-Content-SHA256`
   - `X-BPP-Signature`
   - 时间戳
   - 绑定的 `client_id` / `install_id`
3. `parseBattleUploadBody()` 解析 battle manifest 和 replay payload
4. Worker 重新构造只包含 replay 内容的 JSON
5. 原始 replay JSON bytes 写入：
   - `battle-replays/<client_id>/<battle_id>/<payload_hash>.json`
6. `/replays/:token` 读取该对象并直接把 bytes 返回给客户端

这意味着 replay 对象在服务端已经有一个明确、稳定、独立的存储边界，非常适合在该边界做内部压缩。

## Options Considered

### Option 1: Compress the whole replay object in R2 and decompress on download

做法：

- 上传验签后，对 replay JSON 整体压缩
- 压缩后的 bytes 写入 `R2`
- 下载时根据对象 metadata 决定是否解压

优点：

- 客户端完全无感
- 改动点集中
- 压缩收益高
- 不需要理解 netmsg 内部结构

缺点：

- `R2` 中对象不再是可直接阅读的 JSON

### Option 2: Compress only the three base64 message fields inside replay JSON

做法：

- 服务端把 `*_message_base64` 解码、压缩、再编码成内部 JSON
- 下载时再重建回旧格式 JSON

优点：

- `R2` 中仍然保存 JSON

缺点：

- 编解码链路更复杂
- 更容易引入兼容性问题
- 首次落地不值得承担额外复杂度

### Option 3: Store both raw JSON and compressed copies

做法：

- 同时保存原始 replay JSON 和压缩对象

优点：

- 回滚最直接

缺点：

- 节省空间有限
- 不能真正解决当前的存储成本问题

## Recommendation

推荐采用 **Option 1**：

- 新上传 replay 对象整体压缩后写入 `R2`
- 下载时透明解压回原始 JSON
- 历史对象不迁移
- 通过 metadata 明确区分新旧对象

这是当前收益最高、协议风险最低、实现最小的方案。

## Proposed Design

### Upload Path

客户端上传行为保持不变：

- 请求体仍然是原始 battle artifact JSON
- 签名和 body hash 仍然基于原始 JSON

Worker 在验签成功后：

1. 解析 replay JSON
2. 记录 `originalSizeBytes = replayJsonBytes.byteLength`
3. 对 replay JSON bytes 执行 `gzip`
4. 记录 `compressedSizeBytes = compressedReplayBytes.byteLength`
5. 将压缩后的 bytes 写入 `R2`
6. 为对象写入明确的 codec metadata

`battle-replays/...` 的对象 key 可以保持不变，不需要为了这次改造调整目录结构。

`payload_hash` 应继续基于原始 replay JSON 计算，而不是基于压缩后 bytes 计算。

原因：

- hash 表示 replay 业务内容，而不是存储编码
- 同一份 replay 不应因为压缩实现或压缩级别变化而生成不同 object key
- 压缩只改变 `R2` 中的对象表示，不改变 replay 内容标识和去重语义

### Download Path

客户端下载行为保持不变：

- 客户端仍然请求原来的 `GET /replays/:token`
- 返回值仍然是 `application/json; charset=utf-8`
- 返回体仍然是旧格式 replay JSON

Worker 读取对象时：

1. 先读取对象 metadata
2. 如果对象声明了不支持的 codec，则直接失败，不按老对象回退
3. 读取对象 body bytes
4. 如果对象声明 `gzip` codec，则先解压
5. 如果没有 codec metadata，则按老对象原样返回
6. 如果对象 body 读取失败或 codec 解压失败，则返回稳定错误码，并在 Worker 日志中记录 `battle_id` 与 `replay_object_key`

构造客户端响应时：

- 不应透传 `R2` 对象的 `contentEncoding`
- 应由 Worker 显式设置 `Content-Type: application/json; charset=utf-8`

建议错误处理约定：

- 对象 body 读取失败：`502` + `{ "error": "replay_read_failed" }`
- codec 不支持或解压失败：`502` + `{ "error": "replay_decode_failed" }`
- Worker 日志：记录对象 key、battle id、codec 和异常信息

不建议在 codec 不支持或解压失败时 fallback 到原样返回，因为这会把损坏或不可解释的 bytes 当作 JSON 下发给客户端。

### Object Metadata

是否压缩必须通过 metadata 明确表达，而不是靠推测。

建议为新对象写入：

- `httpMetadata.contentType = application/json; charset=utf-8`
- `httpMetadata.contentEncoding = gzip`
- `customMetadata["bpp-storage-codec"] = "gzip"`
- `customMetadata["bpp-compressed-size-bytes"] = <压缩后对象大小>`

可选扩展：

- `customMetadata["bpp-original-size-bytes"] = <原始 replay JSON 大小>`

说明：

- `contentType` 继续表达对象解压后的媒体类型
- `contentEncoding` 表达对象实际存储时采用的编码
- `bpp-storage-codec` 作为服务端内部显式判断依据，避免读取逻辑依赖文件后缀或内容猜测

读取时以 `bpp-storage-codec` 为主判断依据，`contentEncoding` 作为补充和运维可见信息。

### Compatibility Model

上线后会出现两类 replay 对象：

- 老对象：
  - 内容是原始 JSON
  - 没有 `bpp-storage-codec`
- 新对象：
  - 内容是压缩 bytes
  - `bpp-storage-codec = gzip`

下载逻辑必须同时支持两类对象，这样就不需要先迁移历史对象。

## Why Gzip

本次建议首选 `gzip`，不建议首发采用更激进的编码方案。

理由：

- 压缩率已经足够高，样本中可从 `78 KB` 降到约 `35 KB`
- 压缩和解压能力成熟，通用性好
- 解压成本非常低
- 相比“按字段压缩”或高质量 `brotli`，实现复杂度更低

## Performance Considerations

对样本 replay JSON 的本地基准结果：

- 输入大小：`78,242 bytes`
- `gzip level 6`
  - 压缩后：`35,259 bytes`
  - 平均压缩耗时：约 `1.53 ms`
  - 平均解压耗时：约 `0.08 ms`
- `deflate level 6`
  - 压缩后：`35,247 bytes`
  - 平均压缩耗时：约 `1.52 ms`
  - 平均解压耗时：约 `0.08 ms`
- `brotli quality 5`
  - 压缩后：`33,130 bytes`
  - 平均压缩耗时：约 `1.19 ms`
  - 平均解压耗时：约 `0.15 ms`

这些数据来自本地 Node 基准，不直接等价于 Worker 运行时。最终线上表现取决于实际选用的 Worker 压缩 API 与运行时实现，因此这些数字只应作为量级参考，不应被当作部署后的精确预期。

这些数据仍然足以说明：

- 上传侧新增的 CPU 成本在毫秒级
- 下载侧解压成本远低于上传侧压缩
- 相比减少约一半的对象体积，这个成本是可接受的

## Data Model Impact

本设计不要求修改客户端 schema，也不要求修改 battle 查询模型。

对现有持久化字段的建议：

- `battles.replay_object_key`
  - 语义不变，仍然指向 replay 对象
- `battles.replay_size_bytes`
  - 建议继续表示原始 replay JSON 的大小，而不是压缩后的对象大小

原因：

- 该字段更接近业务上“下载给客户端的 replay 体积”
- 避免已有监控或展示含义发生漂移

压缩后对象大小本次通过 `R2 customMetadata["bpp-compressed-size-bytes"]` 暴露，不在当前设计里新增 `D1` 字段。

## Risks

### 1. Runtime support risk

Worker 运行环境必须能稳定提供压缩和解压能力。

缓解方式：

- 实现前先确认运行时 API 选择
- 在测试里覆盖压缩写入和解压读取

### 2. Metadata mismatch risk

如果对象写入时漏写 codec metadata，下载逻辑可能错误地把压缩内容当作原始 JSON 返回。

缓解方式：

- 把 `bpp-storage-codec` 作为写入必填 metadata
- 同时写入 `contentEncoding = gzip`
- 下载测试明确校验“压缩对象必须先解压”
- 如有必要，可增加 gzip magic number 的兜底判断，但不作为主逻辑

### 3. Read/decode failure risk

如果对象 body 读取失败、codec 不受支持，或对象内容损坏/被误写，下载路径会失败。

缓解方式：

- 下载路径把对象读取错误与 codec/解压错误分开处理
- 对对象读取失败返回 `replay_read_failed`
- 对 codec 不支持或解压失败返回 `replay_decode_failed`
- 在 Worker 端记录 battle id、object key、codec 和异常信息

### 4. Operational visibility risk

对象 key 仍然使用 `.json` 后缀，但内部内容已变为压缩 bytes，排查时可能造成误解。

缓解方式：

- 通过 metadata 明确标识
- 在运维文档中说明 replay 对象已改为压缩存储

### 5. Partial rollout risk

上线后一段时间内，新旧对象会混存。

缓解方式：

- 单次部署同时上线下载兼容与上传压缩
- 在实现和测试阶段先保证下载逻辑可兼容新旧对象
- 不要求一次性迁移历史对象

## Migration Difficulty

本次上线只覆盖新对象，不迁移历史对象。

如果以后需要迁移历史 replay，对象级迁移难度评估为 **中等**。

### Why it is manageable

- 下载链路已经支持新旧对象共存
- 因此迁移可以离线、分批、可暂停

### What makes it non-trivial

- 需要扫描现有 `battle-replays/...` 对象
- 需要识别哪些对象尚未压缩
- 需要安全地重写对象内容和 metadata
- 需要处理失败重试、幂等与中断恢复

### Preferred migration strategy if needed later

推荐未来如需迁移时采用：

1. 先保持下载路径兼容新旧对象
2. 离线扫描历史对象
3. 逐个重写为压缩对象并补 metadata
4. 保持 object key 不变

这样可以避免批量回写 `D1` 中的 `replay_object_key`，把迁移复杂度控制在对象内容重写层。

## Rollout Plan

通过 `wrangler deploy` 单次部署，上传压缩和下载兼容逻辑同时上线。

1. 实现下载路径对新旧对象的兼容读取
2. 实现上传路径对新对象的压缩写入
3. 补齐上传与下载测试
4. 单次部署
5. 观察对象大小、错误率和 replay 下载成功率

## Non-Goals

本设计不包括：

- 修改客户端上传协议
- 修改 replay JSON schema
- 修改 netmsg 编码
- 历史对象批量迁移
- 为 replay 对象引入新的对象 key 格式
- 优化 `battle_manifest` 的字段裁剪

## Summary

本设计的核心思想是：

- 不动客户端协议
- 不动签名逻辑
- 只改变服务端 replay 对象在 `R2` 中的内部表示
- 通过 metadata 区分新旧对象
- 下载时透明解压，保证客户端完全无感

在当前代码结构下，这是一条低风险、高收益、可渐进落地的优化路径。
