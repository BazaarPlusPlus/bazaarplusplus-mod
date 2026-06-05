# BazaarAgent（optional host）

BazaarAgent 是一个本地回环 HTTP 决策表面：把当前游戏状态发布为版本化快照（`GET /v1/context`），并接受外部 agent 提交的单个动作（`POST /v1/actions`）。mod 只做传输与校验，所有策略决策属于外部工具（决策 agent 现住在独立仓库 `bazaarplusplus-agent`）。

> **状态：默认物理不安装。** BazaarAgent host 是一个独立的可选 BepInEx 插件（`BazaarPlusPlus.BazaarAgentHost.dll`，声明 `[BepInDependency(BazaarPlusPlus)]`）。主插件默认构建只产出 `BazaarPlusPlus.dll` 并主动从插件目录清除两个 host dll；需要时按需构建 host 自己的 csproj：`./run.sh build --with-bazaaragent`（会一并带上主插件与纯核心）。host dll 安装后会自动启动固定 `127.0.0.1:47900` loopback HTTP server，没有额外启用开关或端口配置。host 通过 BazaarPlusPlus 发布的 public facade（`BazaarAgentGameBridge`）读取游戏状态——BazaarPlusPlus 本身并不知道 BazaarAgent 的存在。纯协议、transport、validation、queue 与 runtime controller 位于根目录 `BazaarAgent/`（`BazaarPlusPlus.BazaarAgent.csproj`），Unity / BepInEx / 游戏 DLL 适配层位于 `BazaarAgentHost/`（`BazaarPlusPlus.BazaarAgentHost.csproj`）。

## 文档

- **wire 契约**（端点、字段、动作集）：[../reference/bazaar-agent-http-api-v1.md](../reference/bazaar-agent-http-api-v1.md)。字段名是稳定契约，勿改名。
- **字段推导**（`BazaarAgentGameContextReader` 如何从游戏状态填充每个字段）：[../reference/bazaar-agent-decision-surface.md](../reference/bazaar-agent-decision-surface.md)。
- **历史设计**：mountable 化的落地见 [../design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md](../design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md)（as-built 泛化为 `ComponentMount<T>`，见 [ADR-0002](../adr/0002-mountable-feature-registry.md)）。决策 agent 本身住在独立仓库 `bazaarplusplus-agent`。
