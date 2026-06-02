# AutoBazaar（optional host）

AutoBazaar 是一个本地回环 HTTP 决策表面：把当前游戏状态发布为版本化快照（`GET /v1/context`），并接受外部 agent 提交的单个动作（`POST /v1/actions`）。mod 只做传输与校验，所有策略决策属于外部工具（决策 agent 现住在独立仓库 `bazaarplusplus-agent`）。

> **状态：默认物理不安装。** 主插件默认构建不编译 `Game/AutoBazaarHost/`，也不引用或复制 `BazaarPlusPlus.AutoBazaar.dll`。需要安装 Host 时显式构建：`./run.sh build --with-autobazaar-host`（底层 MSBuild 属性是 `-p:EnableAutoBazaarHost=true`）。安装后还要把 `BepInEx/config/BazaarPlusPlus.cfg` 里的 `[AutoBazaar] Enabled` 改为 `true`，loopback HTTP server 才会启动。纯协议、transport、validation、queue 与 runtime controller 位于根目录 `AutoBazaar/`（`BazaarPlusPlus.AutoBazaar.csproj`），Unity / BepInEx / 游戏 DLL 适配层位于 `Game/AutoBazaarHost/`。

## 文档

- **wire 契约**（端点、字段、动作集）：[../reference/auto-bazaar-http-api-v1.md](../reference/auto-bazaar-http-api-v1.md)。字段名是稳定契约，勿改名。
- **字段推导**（`AutoBazaarGameContextReader` 如何从游戏状态填充每个字段）：[../reference/auto-bazaar-decision-surface.md](../reference/auto-bazaar-decision-surface.md)。
- **历史设计**：mountable 化的落地见 [../design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md](../design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md)（as-built 泛化为 `ComponentMount<T>`，见 [ADR-0002](../adr/0002-mountable-feature-registry.md)）。决策 agent 本身住在独立仓库 `bazaarplusplus-agent`。
