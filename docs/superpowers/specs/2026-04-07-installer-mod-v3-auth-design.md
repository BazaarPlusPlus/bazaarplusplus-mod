# Installer-Mod V3 身份与上传设计

## 目标

替换当前围绕以下概念构建的上传身份模型：

- `client_id`
- `install_id`
- 延后的 `bind-player`
- 可选的上传者玩家关联

替换为新的 V3 模型，其特点是：

- installer 是身份与激活的入口
- mod 仍然是运行时在线客户端，并直接上传
- 业务用户身份以 `player_account_id` 为主键
- 用安装级请求签名替代旧的客户端注册流程

这个设计明确将 V3 视为一次 clean-break。旧服务可以继续服务老客户端，但 V3 不应继承旧认证语义。

## 问题总结

当前模型存在三个结构性问题：

1. 上传认证与玩家身份是分离的，而且耦合松散
2. 玩家绑定通常通过后续流程机会式完成，例如 ghost sync
3. 由于上传本身不要求已确认的玩家身份，很多上传记录最终没有 uploader/player 关联

结果是，请求真实性与业务归属由不同路径分别处理，导致整个模型难以推理，也难以继续演进。

## 设计原则

- installer 负责身份初始化
- mod 负责运行时服务通信
- `player_account_id` 是业务用户键
- `player_username` 是登录名和显示名
- 绑定必须在 installer 中显式确认
- 即使 installer 不运行，mod 也应能继续工作
- 本地共享文件不应使用 JSON
- 请求签名才是真正的信任边界；本地文件完整性校验只是辅助措施

## V3 显式约束

以下约束是 V3 首版有意设定的产品规则：

- `player_username` 被视为稳定的登录名和显示名
- V3 不支持 `player_username` 的 rename 处理
- V3 不支持 `player_username` 的冲突恢复或 alias 迁移
- V3 不支持混服归属恢复、申诉、转移或接管流程
- 如果某个账号被错误认领，或历史归属存在歧义，这类情况不属于 V3 自助流程处理范围

这些不是无意遗漏，而是首个 clean-break 模型中刻意设定的范围边界。

## 身份模型

### 用户身份

V3 用户以 `player_account_id` 为主键。

`player_username` 用于登录和显示，但不替代主键。

这意味着：

- 一个 `player_account_id` 精确映射一个用户
- 所有业务归属、上传、安装最终都归结到 `player_account_id`
- V3 不需要单独的 `user_id -> player_account_id` 绑定表

### 安装身份

每个已激活的 mod 安装实例都会获得一个独立的 `installation_id`。

属性：

- 一个用户可以有多个安装实例
- 每个安装实例都有自己的密钥对
- 请求由安装实例私钥签名
- 同一台机器切换账号时会创建新的 `installation_id`

### 观察到的玩家身份

mod 会从游戏运行时读取：

- `player_account_id`
- `player_username`

然后将该观察结果写入共享身份目录。

但仅有这个观察结果还不足以完成绑定。installer 必须展示该信息，并要求用户显式确认后，激活流程才算完成。

## 数据模型

### `users`

建议字段：

- `player_account_id` `TEXT PRIMARY KEY`
- `player_username` `TEXT NOT NULL UNIQUE`
- `password_hash` `TEXT NOT NULL`
- `stream_platform` `TEXT NULL`
- `stream_channel_id` `TEXT NULL`
- `stream_url` `TEXT NULL`
- `created_at_utc` `TEXT NOT NULL`
- `updated_at_utc` `TEXT NOT NULL`
- `last_login_at_utc` `TEXT NULL`

说明：

- 在 V3 中，`player_username` 被有意定义为不可变，并作为登录名使用
- V3 不支持 `player_username` 的 rename、alias 或冲突恢复流程
- 如果这些假设后续失效，可以在未来迁移中引入独立的 `login_name`

### `installations`

建议字段：

- `installation_id` `TEXT PRIMARY KEY`
- `player_account_id` `TEXT NOT NULL`
- `public_key` `TEXT NOT NULL`
- `status` `TEXT NOT NULL`
- `created_at_utc` `TEXT NOT NULL`
- `last_seen_at_utc` `TEXT NULL`
- `revoked_at_utc` `TEXT NULL`

建议状态值：

- `active`
- `revoked`
- `stale`

### `installation_observations`

建议字段：

- `installation_id` `TEXT NOT NULL`
- `observed_player_account_id` `TEXT NOT NULL`
- `observed_player_username` `TEXT NOT NULL`
- `observed_at_utc` `TEXT NOT NULL`
- `signature` `TEXT NOT NULL`
- `status` `TEXT NOT NULL`

建议状态值：

- `observed`
- `confirmed`
- `stale`

这张表用于审计 mod 在激活前或激活过程中上报的内容。它不是业务归属的事实来源。归属仍然由 `users.player_account_id` 决定。

规则：

- 这张表只接收来自已存在 installation 的已签名 observation
- bootstrap 阶段的本地 observation 不直接写入该表
- 运行时 observation 必须带 installation 请求签名

## 本地共享文件

使用：

```text
GameRoot/
  BazaarPlusPlus/
    Identity/
      player-observation.bpp
      installation.bpp
      installation.key
```

这个目录是 installer 与 mod 之间共享的运行时数据根目录。

### `player-observation.bpp`

写入方：

- mod

读取方：

- installer

用途：

- 发布当前最新的游戏身份观察结果

负载应包含：

- `player_account_id`
- `player_username`
- `observed_at_utc`
- 如果可用，可包含可选的本地 installation hint

说明：

- 这是 bootstrap 阶段的本地输入
- installer 读取它来驱动首次注册和激活 UI
- 在 installation 尚未存在时，它不是服务端事实数据

### `installation.bpp`

写入方：

- installer

读取方：

- mod

用途：

- 发布在线请求所需的已激活 installation 元数据

负载应包含：

- `installation_id`
- `player_account_id`
- `api_base_url`
- `public_key`
- `status`
- `created_at_utc`

### `installation.key`

写入方：

- installer

读取方：

- mod

用途：

- 保存运行时请求签名所使用的 installation 私钥

私钥绝不能嵌入到 `installation.bpp` 中。

## 本地文件格式

不要使用 JSON。

使用一个轻量二进制信封格式：

- 4 字节 magic
- 2 字节 schema version
- 2 字节 flags
- 4 字节 payload length
- payload bytes
- 32 字节 payload checksum

建议的 magic：

- `BPP1`

校验规则：

- magic 未知则拒绝
- version 不支持则拒绝
- length 非法则拒绝
- checksum 不匹配则拒绝

这种格式只能提高篡改和损坏的成本，但它不是信任根。

## 信任模型

### V3 能证明什么

V3 能证明：

- 某个请求来自一个已激活的 installation
- 该请求由该 installation 的私钥签名
- 该 installation 归属于某个特定的 `player_account_id`

### V3 不能证明什么

V3 无法从密码学上证明游戏侧读取到的 `player_account_id` 一定不可能被伪造，因为这个值仍然是 mod 本地读取的。

因此，V3 应将游戏身份视为：

- client-observed
- installer-confirmed

而不是第三方可验证身份。

### 真正的信任边界

真正的信任边界是：

- 服务端上的 installation 注册
- 服务端保存的 installation 公钥
- 每次请求的 installation 签名校验

本地文件完整性检查只是便利性和安全性的辅助措施。

## 用户与激活流程

### 首次使用流程

1. 用户启动已安装 mod 的游戏。
2. mod 读取 `player_account_id` 和 `player_username`。
3. mod 写入 `player-observation.bpp`。
4. 用户打开 installer。
5. installer 检测到 observation 数据，并允许首次注册。
6. 用户设置密码。
7. installer 展示一个针对当前游戏账号的显式确认步骤。
8. 确认之后，installer 在本地生成新的 installation 密钥对。
9. installer 调用服务端执行最终激活。
10. 服务端在一个原子事务中：
   - 检查 `player_account_id` 尚未被占用
   - 创建 `users`
   - 创建 `installations`
   - 返回 `installation_id`
11. installer 写入 `installation.bpp` 和 `installation.key`。
12. mod 开始使用 installation-signed 的运行时请求。

### 现有用户流程

1. 用户打开 installer。
2. 如果没有 observation 文件，installer 仍然允许查看已有账号数据。
3. 如果存在 observation 文件，installer 展示观察到的 `player_username`。
4. 用户通过 `player_username + password` 登录。
5. 如果观察到的 `player_account_id` 与登录用户一致，installer 可以刷新或重新创建 installation 材料。
6. 如果观察到的 `player_account_id` 不一致，installer 必须明确说明游戏账号信息已变化，并要求重新登录后才能生成新的 mod 密钥。

### 显式确认要求

observation 是自动的。

binding/activation 是显式的。

installer 在将观察到的身份转化为已激活 installation 上下文之前，必须要求一次清晰的确认步骤。

### 原子激活要求

首次占用 `player_account_id` 的时机只能发生在最终激活阶段。

服务端不得在 bootstrap observation 到达时提前创建用户，也不得在确认之前预占该账号。

最终激活必须以单个原子事务完成：

- 检查账号是否未被占用
- 创建用户
- 创建 installation
- 返回激活结果

## Installer 行为规则

### 尚未存在 observation 时

首次启动时，在 mod 写出观察到的玩家身份数据之前，installer 不能完成注册或激活。

允许的行为：

- 展示 onboarding
- 说明用户必须先启动游戏/mod

### 已登录但没有新的 observation 时

installer 应允许：

- 查看账号数据
- 查看直播资料字段
- 其他非激活类功能

在这种状态下，installer 不应强制要求重新登录。

### 已登录且观察到的身份发生变化时

installer 应：

- 明显提示观察到的游戏身份已经变化
- 在生成新的 mod 密钥前要求重新登录

installer 仍应允许：

- 查看现有资料数据
- 其他与 mod 认证无关的功能

installer 应阻止：

- installation 激活
- installation 密钥刷新
- 其他会为新观察到账号授权 mod 的操作

## Mod 运行时在线层

mod 应提供一个统一的运行时服务层，例如：

- `ModOnlineClient`

这不只是上传层，而是 mod 的完整在线通信层。

建议职责：

- 读取 `installation.bpp`
- 读取 `installation.key`
- 对请求签名
- 上传 run bundle 或其他制品
- 查询 ghost battles
- 下载 replay payloads
- 发布 player observations

可以在共享层之下挂载特性子客户端：

- `RunBundleClient`
- `GhostBattleClient`
- `ReplayClient`
- `ObservationClient`

它们应共用以下能力：

- installation 元数据加载器
- 请求签名器
- route builder
- auth/session 错误处理

## 请求认证

每个 V3 认证请求都应包含：

- `installation_id`
- timestamp
- content hash
- signature

签名输入必须是一个 canonical request string，由以下内容构成：

- HTTP method
- 规范化后的 route path
- 规范化后的 query string
- `installation_id`
- timestamp
- content hash

签名逻辑不能依赖临时性的序列化输出，也不能依赖传输层 header 的排列顺序。

建议校验流程：

1. 解析 `installation_id`
2. 确认 installation 处于 active 状态
3. 加载 installation 公钥
4. 校验 timestamp 窗口
5. 校验 body hash
6. 基于 canonical request string 校验请求签名
7. 从 installation 记录中解析出 `player_account_id`

请求认证模型不应依赖：

- 旧的 `client_id`
- 旧的 `install_id`
- 事后进行的 `bind-player`

## 服务端 API 面

### V3 API Base URL

V3 使用独立域名：

- `https://mod-api-v3.bazaarplusplus.com`

installer 与 mod 的 V3 配置都应只指向这个域名。

旧域名可以继续服务旧客户端，直到旧客户端退场。

建议的 V3 路由：

- `POST /activate`
- `POST /login`
- `POST /installations`
- `POST /installations/observations`
- `POST /run-bundles`
- `GET /ghost-battles`
- `POST /ghost-battles/:battleId/replay-link`
- `GET /replays/:token`

### V3 服务端配置

以下配置由服务端决定，不由 mod 客户端传入：

- `ghost_query_lookback_days`
  - 控制 `GET /ghost-battles` 默认向后查询多久
  - 建议默认值：`3`
- `run_bundle_retention_days`
  - 控制 RunBundle artifact 在对象存储中的保留时长
  - 建议默认值：`5`
- `battle_ingest_min_rating`
  - 控制 battle projection 的入表 rating 门槛
  - 当某条 battle 的 rating 大于等于该值时，直接入 `battles` 表
- `battle_ingest_min_day_if_below_rating`
  - 控制低于 rating 门槛时的补充 day 门槛
  - 当某条 battle 的 rating 低于 `battle_ingest_min_rating` 时，只有 `battle.day` 大于等于该值才入 `battles` 表

这些值应被视为服务端部署配置，而不是客户端协议字段。

### `POST /activate`

用途：

- 仅用于首次激活
- 在 installer 显式确认后完成首次账号创建与首个 installation 创建

适用条件：

- 当前观察到的 `player_account_id` 尚未注册为用户
- 当前设备尚未存在已激活的 installation

输入应包含：

- 观察到的 `player_account_id`
- 观察到的 `player_username`
- 密码
- 可选的直播资料字段
- installation 公钥

服务端必须在一个原子事务内完成：

- 检查 `player_account_id` 是否未被占用
- 创建用户
- 创建 installation
- 返回 `installation_id`

### `POST /login`

用途：

- 通过 `player_username + password` 认证现有用户

服务端解析出对应的 `player_account_id`，并返回一个正常的 installer session。

### `POST /installations`

用途：

- 仅用于已存在用户的新 installation 激活
- 不创建用户，只为已登录用户创建新的 installation

适用条件：

- 用户已经通过 `POST /login` 登录
- 当前观察到的 `player_account_id` 与登录用户一致
- installer 已完成显式确认

输入应包含：

- 当前 installer session
- 当前观察到的 `player_account_id`
- installation 公钥

输出应包含：

- `installation_id`
- active installation status

服务端必须完成：

- 校验 session 对应的用户身份
- 校验观察到的 `player_account_id` 与当前登录用户一致
- 创建新的 installation
- 返回 `installation_id`

### `POST /installations/observations`

用途：

- 记录 mod 观察到的身份声明，用于审计和 installer UX

规则：

- 这个路由只接收来自已激活 installation 的运行时 observation
- 每条 observation 都必须携带正常的 V3 请求签名
- 这个路由本身不会完成激活

bootstrap observation 的来源是本地 `player-observation.bpp`，而不是这个服务端接口。

### `POST /run-bundles`

用途：

- 接收 V3 的 indexed bundle upload
- 将轻量 projection 写入查询表
- 将完整 artifact 原文写入对象存储

规则：

- 服务端只将 `projection` 用作查询真源
- 服务端只将 `artifact` 用作回放与归档真源
- 服务端首版不对 `projection` 与 `artifact` 做一致性校验
- 如果两者不一致，查询结果以 `projection` 为准，回放下载以 `artifact` 为准

## RunBundle 协议与存储

### 总体模型

V3 RunBundle 上传请求不是单一 opaque blob，而是一个 indexed bundle upload，请求体显式分成两部分：

- `projection`
- `artifact`

其中：

- `projection` 负责服务端查询入库
- `artifact` 负责完整原文归档与 replay 下载

注意：

- artifact 是否存储，与某条 battle projection 是否最终进入 `battles` 表，是两个独立决策
- V3 首版会先保存整包 artifact，再按服务端 gate 决定哪些 battle projection 进入查询表

### 顶层请求模型

```text
RunBundleUploadRequestV2
- schema_version: int
- installation_id: string
- player_account_id: string
- plugin_version: string
- game_version: string?
- submitted_at_utc: string

- run_projection: RunProjectionV2
- battle_projections: BattleProjectionV2[]

- artifact_codec: string
- artifact_bytes: byte[]
```

### Run projection

```text
RunProjectionV2
- run_id: string
- status: string
- hero_id: string?
- hero_name: string?
- player_rank: string?
- player_rating: int?
- player_position: int?
- started_at_utc: string?
- ended_at_utc: string
- final_day: int?
- final_wins: int?
- final_losses: int?
- final_player_rank: string?
- final_player_rating: int?
- final_player_position: int?
```

这是 `runs` 表的直接来源。

其中：

- `player_rank` / `final_player_rank` 只表示 run 级 rank 名字，例如 `Gold`
- `player_rating` / `final_player_rating` 表示 run 级 rating
- `player_position` / `final_player_position` 表示传奇排行榜名次
- `position` 只在 rank 为 `Legendary` 且 leaderboard cache 有值时采集；其他情况必须为 `null`
- 不要继续使用 `mmr` 这个旧名字；V3 的规范字段名统一为 `rating`
- run projection 不应只采终局 `rating`，而应同时采 opening 和 final 两组 run 级 rank/rating/position 快照

### Battle projections

```text
BattleProjectionV2
- battle_id: string
- run_id: string
- recorded_at_utc: string
- day: int?

- player_name: string?
- player_account_id: string?
- player_hero: string?
- player_rank: string?
- player_rating: int?
- player_level: int?

- opponent_name: string?
- opponent_account_id: string?
- opponent_hero: string?
- opponent_rank: string?
- opponent_rating: int?
- opponent_level: int?

- result: string?

- replay_available: bool
```

这是 `battles` 表的直接来源。

其中字段语义应固定为：

- `player_rank` / `opponent_rank` 只表示 rank 名字，例如 `Gold`
- `player_level` / `opponent_level` 表示玩家等级
- 不要把 rank 内部分段或级别编码进 `player_rank` / `opponent_rank`
- 如果游戏侧存在诸如 “Gold 5” 这样的显示，应拆成 rank 名字和独立等级来源，而不是把整个字符串塞进 rank 名字字段

### Artifact

`artifact` 是真正的大对象，建议使用：

- MessagePack 序列化
- gzip 压缩

建议 codec：

```text
application/x-bpp-runbundle+msgpack+gzip
```

`artifact_bytes` 的内容建议为：

```text
gzip(messagepack(RunArtifactV2))
```

`RunArtifactV2` 应包含每个 battle 回放所需的完整 artifact，而不只是 replay 三消息。

```text
RunArtifactV2
- schema_version: int
- run_id: string
- battles: RunArtifactBattleV2[]
```

```text
RunArtifactBattleV2
- battle_id: string
- manifest: BattleManifestArtifactV2
- replay: ReplayPayloadArtifactV2
```

```text
BattleManifestArtifactV2
- run_id: string?
- recorded_at_utc: string
- day: int?
- result: string?
- participants: BattleParticipantsArtifactV2
- snapshots: BattleSnapshotsArtifactV2
```

```text
BattleParticipantsArtifactV2
- player_name: string?
- player_account_id: string?
- player_hero: string?
- player_rank: string?
- player_rating: int?
- player_level: int?
- opponent_name: string?
- opponent_account_id: string?
- opponent_hero: string?
- opponent_rank: string?
- opponent_rating: int?
- opponent_level: int?
```

这些 participants 字段沿用与 projection 相同的语义：

- `player_rank` / `opponent_rank` 只表示 rank 名字
- `player_level` / `opponent_level` 表示玩家等级

```text
BattleSnapshotsArtifactV2
- player_hand: CardSetCaptureArtifactV2
- player_skills: CardSetCaptureArtifactV2
- opponent_hand: CardSetCaptureArtifactV2
- opponent_skills: CardSetCaptureArtifactV2
```

```text
ReplayPayloadArtifactV2
- version: int
- spawn_message: byte[]
- combat_message: byte[]
- despawn_message: byte[]
```

服务端首版不解析这个 artifact。

这样设计的原因是：

- 当前 replay 启动链路需要完整 battle manifest 和 replay payload
- `snapshots` 已经属于 battle manifest 的一部分，不应再额外拆成独立顶层 artifact
- 协议层使用 V3 DTO 名称，而不是直接复用 mod 运行时类名

### 客户端组装顺序

建议客户端按固定顺序组装：

1. 从本地 SQLite 读取 `run_projection`
2. 从本地 battle manifest 读取 `battle_projections`
3. 从本地 battle manifest 读取完整 manifest artifact，包括 participants、result、snapshots
4. 从 replay payload 文件读取完整 replay
5. 组装 `RunArtifactV2`
6. 序列化并压缩得到 `artifact_bytes`
7. 组装 `RunBundleUploadRequestV2`
8. 对最终请求体做 hash 和 installation 请求签名

### 本地存储

本地 source of truth 仍然是：

- run SQLite
- battle manifest
- replay payload 文件

本地不应把完整 RunBundle 作为长期事实源保存。

本地只需要保存 run 级 upload state，例如：

```text
run_upload_state
- run_id
- dirty
- last_attempt_at_utc
- last_uploaded_at_utc
- uploaded_payload_hash
- uploaded_schema_version
- last_uploaded_object_key
- retry_count
- last_error
```

### 服务端对象存储

对象存储只保存 `artifact_bytes`。

建议对象 key：

```text
run-bundles/<player_account_id>/<installation_id>/<run_id>/<payload_hash>.mpack.gz
```

对象写入时应同时附带服务端配置的保留策略：

- RunBundle artifact 的过期时间不由客户端指定
- 服务端根据 `run_bundle_retention_days` 计算对象过期时间
- 建议默认值为 5 天
- 该值后续可以按部署环境独立调整

如果对象已经过期或不可再读取：

- replay 请求不应返回 500
- 服务端应返回明确的业务错误
- 建议返回 `410 Gone`
- 建议错误码：`artifact_expired`

### 服务端表设计

#### `run_bundles`

```text
- bundle_id
- installation_id
- player_account_id
- run_id
- payload_hash
- schema_version
- object_key
- codec
- size_bytes
- submitted_at_utc
- created_at_utc
```

建议唯一键：

```text
UNIQUE (installation_id, run_id, payload_hash)
```

#### `runs`

```text
- run_id
- installation_id
- player_account_id
- bundle_id
- status
- hero_id
- hero_name
- player_rank
- player_rating
- player_position
- started_at_utc
- ended_at_utc
- final_day
- final_wins
- final_losses
- final_player_rank
- final_player_rating
- final_player_position
- updated_at_utc
```

#### `battles`

```text
- battle_id
- run_id
- installation_id
- player_account_id
- bundle_id
- recorded_at_utc
- day
- player_name
- player_account_id_in_payload
- player_hero
- player_rank
- player_rating
- player_level
- opponent_name
- opponent_account_id
- opponent_hero
- opponent_rank
- opponent_rating
- opponent_level
- result
- replay_available
- updated_at_utc
```

建议索引重点放在：

```text
INDEX battles(opponent_account_id, recorded_at_utc DESC)
INDEX battles(run_id, recorded_at_utc DESC)
```

V3 首版建议先不要额外创建以下索引：

- `runs(player_account_id, ended_at_utc DESC)`
- `battles(player_account_id, ...)`
- `battles(bundle_id, ...)`
- `battles(day, ...)`
- `battles(player_rank, ...)`
- `battles(player_rating, ...)`
- `runs(player_rank, ...)`
- `runs(player_rating, ...)`
- `runs(player_position, ...)`
- `runs(final_player_rank, ...)`
- `runs(final_player_rating, ...)`
- `runs(final_player_position, ...)`

等对应查询真正落地后，再按实际访问路径补充。

### Battle projection 入表 gate

`battles` 表不是无条件写入所有 `battle_projections`。

服务端应在写入投影前执行一个可配置的入表 gate：

```text
if battle_rating >= battle_ingest_min_rating:
    写入 battles 表
else:
    只有当 battle_day >= battle_ingest_min_day_if_below_rating 时才写入 battles 表
```

这里：

- `battle_rating` 指 battle projection 自身的 rating 字段，而不是 run 级聚合字段
- `battle_day` 指 battle projection 自身的 `day` 字段
- gate 只影响 `battles` 查询投影，不影响 artifact 原文存储
- 因此某个 bundle 上传成功后，可能出现 artifact 已保存，但其中部分 battle 不进入 `battles` 表的情况

### 服务端上传校验

方案 C 下，服务端只校验：

- installation 请求签名
- `installation_id` 有效且处于 active 状态
- `player_account_id` 与 installation 归属一致
- timestamp / body hash
- `schema_version` 支持
- `run_projection.run_id` 非空
- `battle_projections[].battle_id` 非空且不重复
- 每条 `battle_projection.run_id == run_projection.run_id`

服务端不做：

- artifact 解包
- projection / artifact 一致性校验
- 从 artifact 反推 battle 投影

在通过基础校验后，服务端还应对 `battle_projections` 应用 battle ingestion gate，只将通过 gate 的记录写入 `battles` 表。

### `against me` 查询

`against me` 查询完全依赖 `battles` 表，不依赖 artifact 解包。

查询条件应为：

- `battles.opponent_account_id = 当前用户的 player_account_id`
- `recorded_at_utc >= now - ghost_query_lookback_days`
- 按 `recorded_at_utc DESC, battle_id DESC` 排序

其中：

- 查询时间窗口不由客户端传入
- 服务端统一根据 `ghost_query_lookback_days` 裁剪范围
- 建议默认值为 3 天
- 如果后续需要变更查询窗口，应通过服务端配置修改，而不是修改客户端请求

### Replay 下载

Replay 下载继续使用两段式：

- `POST /ghost-battles/:battleId/replay-link`
- `GET /replays/:token`

流程：

1. 按 `battle_id` 查询 `battles`
2. 校验当前 installation 对应的 `player_account_id` 是否有权访问该 battle
3. 签发短期 token
4. 下载时按 `bundle_id -> object_key` 读取 artifact
5. 客户端或下载端再从 artifact 中找到对应 `battle_id`

如果第 4 步发现 artifact 已过期或对象已不存在：

- 服务端应返回 `410 Gone`
- 错误码应为 `artifact_expired`
- 不应把对象过期伪装成鉴权失败或内部错误

### `POST /ghost-battles/:battleId/replay-link`

用途：

- 在 installation-authenticated 上下文中校验 battle 归属，并签发短期 replay 下载令牌

规则：

- 服务端必须校验该 battle 是否属于当前 `player_account_id`
- 只有校验通过后才返回下载令牌

### `GET /replays/:token`

用途：

- 使用短期 token 下载 replay 内容

规则：

- token 必须短期有效
- token 应与 battle 和请求者归属绑定
- 如果 token 仍有效，但底层 artifact 已过期，应返回 `410 Gone` 和 `artifact_expired`

## 上传与查询语义

mod 仍然直接上传。

installer 不是请求代理。

因此：

- installer 提供身份初始化和激活状态
- mod 提供运行时上传和在线请求

所有 V3 上传数据都应以以下字段为键：

- `player_account_id`
- `installation_id`

不要在 V3 模型中保留可空的 uploader/player 关联字段。

如果未来引入 run bundle upload，应直接接在新的 V3 服务面上，而不是反向改造旧 endpoint。

## 冲突处理

V3 首版应采用最简单的硬规则：

- 一个 `player_account_id` 只属于一个用户
- 无申诉流程
- 无自动转移
- 无接管流程
- 无混服归属对账流程

如果观察到的 `player_account_id` 不属于当前登录用户，installer 必须拒绝为需要新 installation key 的 mod 操作执行激活。

对于首次激活，如果在最终提交时发现 `player_account_id` 已被占用，服务端必须拒绝激活，而不是覆盖、迁移或共享归属。

## 非目标

这个设计不包含：

- 第三方或官方游戏账号验证
- 账号申诉或转移流程
- 混服归属恢复
- 每个用户支持多个直播平台
- 由 installer 代理 mod 在线流量
- 向后兼容复用旧认证模型

## 建议

建议将 V3 作为全新的身份与上传模型推进，而不是在当前服务上做渐进式修改。

这个设计里最重要的决策是：

- `player_account_id` 是业务用户主键
- installer 负责激活与密钥发放
- mod 负责运行时通信
- 绑定必须经过 installer 的显式确认
- 安装级请求签名替代旧客户端注册流程

这些决策消除了当前“请求真实性”和“业务归属”之间的歧义，并为未来的上传变更，包括 run-bundle upload，提供了稳定基础。
