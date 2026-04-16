# Installer-Mod 认证简化设计

## 关于这份文档

这份文档是对 [2026-04-07-installer-mod-v3-auth-design.md](2026-04-07-installer-mod-v3-auth-design.md) 的修订，基于实际运行中观察到的两类问题：

1. 当前三个本地文件（`player-observation.bpp` / `installation.bpp` / `installation.key`）分离写入，原子性不足，schema 演进靠自定义二进制信封管理
2. 安装级 RSA 签名鉴权对这个规模的项目来说是过度设计，真实需求只是"知道这个请求来自哪个已登录用户"

本次修订的核心是大幅简化：砍掉 installation 概念、改用 per-user bearer token、本地 IPC 从三个文件换成一个共享 SQLite。

设计哲学上的明确立场：**不追求抵御恶意攻击者，只追求识别已登录用户身份**。同机他人访问、用户手动篡改本地文件、token 本地泄露，这些都**不在防御范围**。真实威胁模型是"单机用户日常使用中的意外情况"，而不是"对抗性环境"。

## 目标

- 消除现有"installation + 签名私钥"模型带来的本地文件原子性、schema 演进、迁移复杂度
- 把 Installer 和 Mod 之间的本地 IPC 从三个二进制信封文件收敛到一个共享 SQLite
- 把请求鉴权从安装级 RSA 签名收敛到 per-user bearer token
- 建立清晰的对称性：**上传不鉴权（放宽数据收集）、查询鉴权（约束数据消费）**

## 非目标

以下事项**不在本次改造范围内**。如果未来真的需要，单独立项。

### 安全 / 防御层面

- Token 轮转 / refresh token 机制
- 2FA / 多因素认证
- 异常登录检测（高频登录、IP 地理异常等）
- Token 客户端加密存储（本地 SQLite 裸存不透明字符串，仅靠 OS 文件权限）
- 防伪造 battle projection 的强约束（继续靠 KV gating 控表大小，不追求密码学保证）
- Mod 本地数据反作弊 / 反篡改

### 用户自助流程

- 密码找回 / 重置（密码丢失 = 账号丢失，走运营手动处理）
- 账号申诉 / 转移 / 合并
- 用户名改名 / alias
- "当前在哪几台设备登录"的可视化管理 UI

### 迁移 / 兼容

- 老客户端自动迁移端点（不存在 `/migrate-token` 之类桥梁）
- 旧 `installation.bpp` / `installation.key` 的自动清理或迁移
- 保留旧签名认证路径（彻底硬切，旧签名 header 被服务端无视）

### 架构扩展

- Installer↔Mod 更复杂的 IPC（socket / 管道 / 本地 HTTP）
- 替代 `/installations/observations` 的审计端点
- 跨项目 / 多游戏复用认证

### 业务功能

- 直播平台整合（`users.stream_*` 字段保留但不在本次范围）
- 观战 / 好友 / 社交图谱

## 核心决策概览

| 维度 | 决定 |
|------|------|
| 身份材料 | 单张长期 bearer token，绑定 `player_account_id`，永久有效只能显式撤销 |
| Installation 概念 | 全部砍掉——服务端、客户端、对象存储 key，全移除 |
| Installer ↔ Mod 本地交互 | 共享一个 SQLite DB（`BazaarPlusPlus/Identity/identity.db`），代替三个二进制信封文件 |
| 上传 / 写入端点 | 不鉴权（`/run-bundles`），放宽收集 |
| 查询 / 下载端点 | 要 bearer token，按 token 绑定的 `player_account_id` 决定能看什么 |
| `KNOWN_PLAYER_ACCOUNTS` KV | 保留，动机是控 `battles` 表大小（非安全措施） |
| 迁移 | 硬切，不处理老文件，mod 没 token 时日志打 warning 后走匿名 |

## 本地共享 SQLite

### 文件位置与访问模式

- 路径：`<GameRoot>/BazaarPlusPlus/Identity/identity.db`
- 访问模式：WAL（`PRAGMA journal_mode=WAL`），允许 Installer 写、Mod 并发读
- 版本管理：`PRAGMA user_version = 1`，后续 schema 变更走 migration
- 文件权限：Installer 创建后收成用户私有（POSIX `0600` / Windows user-only ACL）。不是强安全边界，但能挡掉同机其他用户。

### Schema

```sql
-- 由 Installer 登录后写入；Mod 读
CREATE TABLE auth (
  id                INTEGER PRIMARY KEY CHECK (id = 1),  -- 单行强约束
  token             TEXT    NOT NULL,                    -- 32B 随机 base64url，不透明
  player_account_id TEXT    NOT NULL,
  player_username   TEXT    NOT NULL,
  issued_at_utc     TEXT    NOT NULL
);

-- 由 Mod 在游戏内观察到玩家身份时写入；Installer 读
CREATE TABLE player_observation (
  id                INTEGER PRIMARY KEY CHECK (id = 1),  -- 单行
  player_account_id TEXT    NOT NULL,
  player_username   TEXT    NOT NULL,
  observed_at_utc   TEXT    NOT NULL
);
```

### 读写矩阵

| 表 | Installer | Mod |
|----|-----------|-----|
| `auth` | 读写（登录成功 `INSERT OR REPLACE`；登出 `DELETE`） | 只读 |
| `player_observation` | 只读 | 读写（启动时 `INSERT OR REPLACE`） |

每个进程只写自己的一张表，写入冲突概率极低。读写冲突交给 WAL 处理。写入遇到 `SQLITE_BUSY` 时指数退避重试 3 次即可，不做复杂的 lock 管理。

### 关键语义

- **`auth` 存在 = 已登录**；`auth` 不存在 = 未登录 / 已登出
- **登出 = DELETE**，不是置 null 不是 soft delete
- **Mod 的冲突检测**：启动时比较 `auth.player_account_id` vs 游戏内实际观察到的 `player_account_id`；不一致时 token 一律不发出，日志打 warning "请用 Installer 重新登录"
- **Installer UI 的 onboarding**：只读 `player_observation` 决定首屏是"请先启动游戏"还是"检测到账号 X，登录？"

### Token 格式

32 字节 `crypto.randomBytes` → base64url，**不透明字符串**。不用 JWT：服务端持有 DB，查表即知身份，不需要在客户端自证。

## 服务端 API 面

### 鉴权矩阵

| 端点 | 方法 | 鉴权 | 改动 |
|------|------|------|------|
| `/activate` | POST | 密码 | 改（砍 installation 公钥字段） |
| `/login` | POST | 密码 | 改（返回 token，不再是 installer session） |
| `/logout` | POST | Bearer | 新增 |
| `/installations` | POST | — | 删除 |
| `/installations/observations` | POST | — | 删除 |
| `/run-bundles` | POST | 无 | 改（去掉 installation_id） |
| `/ghost-battles` | GET | Bearer | 改（player_account_id 从 query 改为 token 取） |
| `/ghost-battles/:id/replay-link` | POST | Bearer | 改（同上） |
| `/replays/:token` | GET | 短期下载 token | 不变 |

### 端点形状

#### POST /activate

```
req:  { player_account_id, player_username, password }
resp: { token, player_account_id, player_username }
err:  409 player_account_id_taken
      409 player_username_taken
      400 invalid_request
```

服务端原子事务：

1. 检查 `player_account_id` 未被占用
2. 检查 `player_username` 未被占用
3. 创建 `users` 行（含 `password_hash`）
4. 创建 `tokens` 行，生成 token
5. 返回 token + 用户信息

#### POST /login

```
req:  { player_username, password }
resp: { token, player_account_id, player_username }
err:  401 invalid_credentials
```

每次 login 在 `tokens` 表**新增一行**，旧 token 不动。多机器各自保有自己的 token；某台机器登出只撤销自己那张。更新 `users.last_login_at_utc`。

#### POST /logout

```
req:  Authorization: Bearer <token>
resp: 204 No Content
err:  401 invalid_token
```

`UPDATE tokens SET revoked_at_utc = now() WHERE token = ?`。只撤销调用者自己那张。

#### GET /ghost-battles

```
req:  Authorization: Bearer <token>
      query: limit (optional, 默认 200, max 200)
resp: { battles: [...] }
err:  401 invalid_token
```

**关键变更**：`player_account_id` 从 URL query 参数改为从 token 解析——消除"谁都能指定任意 ID 查询"的漏洞，同时简化 API 契约。

查询条件保持：

- `battles.opponent_account_id = token.player_account_id`
- `recorded_at_utc >= now - ghost_query_lookback_days`
- 按 `recorded_at_utc DESC, battle_id DESC` 排序

#### POST /run-bundles

```
req:  { schema_version, player_account_id, run_projection, battle_projections, artifact_codec, artifact_bytes }
resp: { bundle_id }
err:  400 invalid_run_bundle_request
```

**完全不鉴权**。`player_account_id` 由客户端自报，服务端不校验它对应的 user 是否真的存在——未注册的 account_id 照收。`KNOWN_PLAYER_ACCOUNTS` KV gating 逻辑不变（见下方章节）。

对象存储 key 变更为：

```
run-bundles/<player_account_id>/<run_id>/<payload_hash>.mpack.gz
```

#### POST /ghost-battles/:id/replay-link

鉴权改为 Bearer token；内部 owner check 改成"battle 的 player_account_id 等于 token 的 player_account_id"（当前是按 installation 归属校验）。

#### GET /replays/:token

不变。短期下载 token 在签发时绑定身份，不依赖 bearer。

### 设计选择说明

**`/login` 每次新发 token，不复用现有**。理由：多机器独立登出的心智比"共享一张"清晰，服务端成本可忽略。这与"per-user token 粒度"不冲突——粒度的选择讲的是 token 不绑 installation 实体，但允许一个 user 有多张活 token。

**没有 `/me` 端点**。Installer 不需要二次校验 token，本地 SQLite 的 `auth` 行即真相；失效交给后续 API 的 401 暴露。

## 服务端 Schema 变更

### 新增：`tokens` 表

```sql
CREATE TABLE tokens (
  token              TEXT    PRIMARY KEY,       -- 32B 随机 base64url
  player_account_id  TEXT    NOT NULL REFERENCES users(player_account_id),
  issued_at_utc      TEXT    NOT NULL,
  revoked_at_utc     TEXT    NULL,              -- 登出 / 显式撤销时置值
  last_used_at_utc   TEXT    NULL               -- 调试用
);
CREATE INDEX tokens_by_user ON tokens(player_account_id, revoked_at_utc);
```

- `FOREIGN KEY → users` 保证只能发给注册过的用户
- `last_used_at_utc` 只为排障用，可选；如果写入成本敏感可砍

### 删除：两张表

```sql
DROP TABLE installation_observations;
DROP TABLE installations;
```

### 修改：`runs` / `battles` / `run_bundles`

三张表都有 `installation_id` 列。采用**保守保留**策略：

- 列保留，允许 NULL
- 新代码写 NULL，查询不再引用
- 后续有空再 `ALTER TABLE ... DROP COLUMN` 清理

### `run_bundles` 唯一键迁移

- 当前：`UNIQUE (installation_id, run_id, payload_hash)`
- 新：`UNIQUE (player_account_id, run_id, payload_hash)`

依赖 `run_id` 是客户端 GUID，两机不会撞。迁移做法：`DROP INDEX` 旧的 + `CREATE UNIQUE INDEX` 新的（不是 `ALTER TABLE`，成本低）。

### 对象存储 key

- 新上传：`run-bundles/<player_account_id>/<run_id>/<payload_hash>.mpack.gz`
- 旧对象**不动**

**关键约束**：replay 下载路径必须走 `run_bundles.object_key` 存的字符串字面量，**不得算法式拼 key**。这样老对象（key 里含 `installation_id`）仍然可读，新对象按新 key 写。实现时必须守住这条。

### `users` 表

基本不动。继续用 `player_account_id` 做 PK，`player_username` UNIQUE，`password_hash` 存密码哈希。登录成功时 `UPDATE last_login_at_utc`。没有新增列。

### `KNOWN_PLAYER_ACCOUNTS` KV

**代码逻辑不动**。匿名上传继续 seed KV，继续只把 opponent 在 KV 里的 battle 写进表。

**这是控 `battles` 表大小的产品逻辑，不是鉴权逻辑**。动机：`battles` 表只服务"ghost vs me"查询，只有"对手也是 BPP 用户"的对局才有价值。代码中必须用注释明确标注这个动机，避免未来被误当作安全机制而删除或"强化"。

## Mod 运行时行为

### 启动序列

1. Mod 加载时打开 `<GameRoot>/BazaarPlusPlus/Identity/identity.db`（WAL、只读 + 写自己的表）
2. 读取 `auth` 单行 → 拿到 token、`auth.player_account_id`、`auth.player_username`
3. 同时从游戏内观察到的 `player_account_id` + `player_username`，`INSERT OR REPLACE INTO player_observation`
4. 比对 `auth.player_account_id` vs 观察到的 `player_account_id`

| 情况 | token 可用性 | 日志 |
|------|-------------|------|
| DB 文件不存在 | 不可用 | `warn: no identity.db, please login via Installer` |
| `auth` 行不存在 | 不可用 | `warn: not logged in, please login via Installer` |
| match | 可用 | `info: logged in as <username>` |
| mismatch（换了游戏账号） | 不可用 | `warn: game account changed, please re-login via Installer` |

"不可用" = 所有需要 Bearer 的请求直接跳过，**不尝试匿名打**（那些端点会 401，重复打没意义）。上传 / 写入端点（`/run-bundles`）不管 token 状态都照常发。

### 401 中途处理

任何带 Bearer 的请求收到 401：

1. 视为 token 已被服务端撤销
2. `DELETE FROM auth WHERE id = 1`（清本地凭证）
3. 日志 warning + 转入"不可用"状态
4. 本轮会话不再重试，不循环、不自动重登

用户下次跑 Installer 重新登录即可恢复。

### Mod 在线层封装

Mod 里统一一个 `ModOnlineClient`（V3 spec 里已经提过这个名字，架构保留）：

- 构造时加载 `auth` 行 → 持 token 在内存
- 暴露子客户端：
  - `RunBundleClient`（无 auth）
  - `GhostBattleClient`（Bearer）
  - `ReplayClient`（Bearer + 短期下载 token）
- 401 拦截统一在 `ModOnlineClient` 层处理，子客户端无需感知

之前 V3 spec 里的 `ObservationClient`（对应 `/installations/observations`）**删除**——端点没了，职责转到"写本地 `player_observation` 表"。

### 换账号场景的具体行为

换游戏账号**不**自动清 `auth` 行，只是这一会话内不发 Bearer 请求。理由：用户可能只是临时切小号看一局，让他自己通过 Installer 决定是否切账号。这避免了"误删导致 Installer 必须重登"的烦人副作用。

## Installer 运行时行为

### 启动时

1. 打开 / 创建 `<GameRoot>/BazaarPlusPlus/Identity/identity.db`（若新建则设文件权限为用户私有 `0600` / Windows user-only ACL；首次创建时执行建表 SQL 和 `PRAGMA user_version = 1`）
2. 读取 `auth` 单行
3. 读取 `player_observation` 单行
4. UI 分支：
   - 无 `auth` 且无 `player_observation` → "请先启动游戏让 mod 上报身份"
   - 无 `auth` 但有 `player_observation` → 展示"检测到账号 `<username>`，登录或注册"
   - 有 `auth` 且 `auth.player_account_id == player_observation.player_account_id` → "已登录为 `<username>`"
   - 有 `auth` 但 `auth.player_account_id != player_observation.player_account_id` → 明显提示"游戏账号已变化，需要登出后重新登录"

### 首次注册流程

1. UI 收集 `password`
2. 用 `player_observation.player_account_id` + `player_observation.player_username` + `password` 调 `POST /activate`
3. 成功 → `INSERT OR REPLACE INTO auth` 写入返回的 token
4. 失败（`player_account_id_taken` / `player_username_taken`）→ 引导用户改用登录流程

### 现有用户登录流程

1. UI 收集 `player_username` + `password`
2. 调 `POST /login`
3. 成功 → `INSERT OR REPLACE INTO auth` 写入返回的 token
4. 如果返回的 `player_account_id` 与 `player_observation.player_account_id` 不一致 → 提示用户"登录账号与游戏当前账号不匹配"（但仍保留写入，UI 层警示用户自行决定）

### 登出流程

必须是两步，顺序不可反：

1. 用当前 `auth.token` 调 `POST /logout`（撤销服务端 token）
2. `DELETE FROM auth WHERE id = 1`（清本地凭证）

如果第 1 步网络失败：仍然执行第 2 步，但在 UI 上提示"本地已登出，服务端 token 可能仍有效"。服务端 token 永久有效的兜底是：下次用同账号登录会发新 token，老 token 理论上仍有效但没人持有。这是可接受的。

### Installer 对 `player_observation` 的使用

Installer **只读** `player_observation`，绝不写入。Mod 才是 observation 的唯一写入者。

## 发布顺序与依赖

### Step 1 — 服务端先上

- 迁移 D1 schema：
  - 新建 `tokens` 表
  - `installations` / `installation_observations` 表 DROP
  - `runs` / `battles` / `run_bundles` 的 `installation_id` 列改为允许 NULL
  - `run_bundles` 唯一键从 `(installation_id, run_id, payload_hash)` 改为 `(player_account_id, run_id, payload_hash)`
- 部署新 Worker 代码：
  - `/installations` + `/installations/observations`：直接返回 404
  - `/run-bundles`：删除签名校验分支，只要 payload 有效就收
  - `/ghost-battles` / `/ghost-battles/:id/replay-link`：要求 `Authorization: Bearer <token>`，缺失或无效返回 401
  - `/activate` / `/login` / `/logout`：实现新 token 逻辑

**老客户端的过渡行为**：老 mod 还在发带签名的请求——服务端把签名 header **无视**而不是报错。

- 上传端点：正常收下（签名被无视不影响业务）
- 查询端点：401（老 mod 已有 401 降级处理，日志报错但不崩溃）

### Step 2 — Installer + Mod 配对发布

这一步必须捆绑。Installer 的新版本包同时带上：

- 新 Installer 代码（写 `identity.db`）
- 新 mod DLL（读 `identity.db`、写 `player_observation` 表）

用户下载新 Installer → 登录 → 写入 token → mod 自动用新 token。两者分开发布会制造人为的状态错位。

### Step 3 — 监控 + 清理窗口

上线后两周看指标：

- `/ghost-battles` 401 率：预期尖峰后衰减（老 mod 用户陆续升级）
- `/activate` + `/login` 调用量：预期上升
- `/run-bundles` 量：应该保持稳定（上传不受影响）

两周后，如果 401 率降到可接受水平，可选地把 `runs` / `battles` / `run_bundles` 的 `installation_id` 列 DROP 掉。不做这步代码也能正常跑。

### 回滚预案

**服务端崩溃**：直接 rollback Worker 到前一版本。D1 schema 的新增表（`tokens`）和列改为 NULL 都是 additive，老代码能兼容。仅需回滚 Worker 代码，不需回滚 schema。

**Installer / Mod 崩溃**：用户自行回退到老版本 Installer 重装即可；他们的老 `installation.bpp` + `installation.key` 还在磁盘上。但此时服务端已经开始 401 读端点，所以老 mod 的 ghost 功能本来就不工作——换句话说，回滚 Installer 无法救 ghost 功能，只能救上传，而上传其实没坏过。所以 Installer / Mod 层面**不设回滚预案**，出问题就 forward fix 紧急发新版。

### 老文件完全不处理

- `installation.bpp` 和 `installation.key` 留在磁盘，新代码一眼不看
- 新 Installer **不主动清理**（避免引入清理 bug），留给用户手动删除或系统清理

### 前置检查清单

1. D1 迁移在 staging 跑过一次并验证
2. 新 Worker 代码在 staging 跑通：匿名上传、注册、登录、ghost 查询、replay 下载全链路
3. 新 Installer + Mod 打包产物已 ready（但未发布）
4. 服务端部署 → 冒烟测试
5. 确认后发布 Installer

## V3 spec 的覆写范围

本次修订**完全替代** [2026-04-07-installer-mod-v3-auth-design.md](2026-04-07-installer-mod-v3-auth-design.md) 中的以下章节：

- "安装身份" → 删除（installation 概念整个砍）
- "本地共享文件" + "本地文件格式" → 替换为本文档的"本地共享 SQLite"章节
- "信任模型" → 简化为"不鉴权写 + 鉴权读 + token 即凭证"
- "请求认证" → 替换为 Bearer token 方案
- 所有 installation 签名相关章节 → 删除

保留不动的章节：

- "用户身份"（`player_account_id` 主键语义）
- "V3 显式约束"（用户名不可改、无冲突恢复）
- "RunBundle 协议与存储"（除 `installation_id` 字段外）
- "Battle projection 入表 gate"（KV 逻辑保留）
