---
status: abandoned
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# The Bazaar 联网与离线化逆向文档

本文档集基于当前仓库的反编译源码、已安装游戏目录和 mod 源码做静态分析。过程只读取源码/资源和新增文档，没有编译代码。

> **快照口径：截至 2026-05-21 的反编译构建静态分析。** 字节大小、行数、路由表等会随游戏更新漂移；依赖具体数值前请对照 `decompiled/` 复核。下方 `network-interface-inventory.md` / `data-structure-catalog.md` / `session-command-protocol.md` / `decompile-and-data-notes.md` 是事实型 RE 参考；`proposals/` 下两篇是**尚未实现**的离线模式设计提案。

## 文档目录

- [network-interface-inventory.md](network-interface-inventory.md)  
  官方客户端、旧版服务、静态 CDN、Addressables、mod 上传/同步、BazaarAgent 本地 HTTP 的接口清单、数据结构和业务逻辑。
- [data-structure-catalog.md](data-structure-catalog.md)  
  所有主要接口 DTO、MessagePack DTO、mod DTO、BazaarAgent DTO 和本地 fixture DTO 建议。
- [session-command-protocol.md](session-command-protocol.md)  
  `/sessions`、`/commands`、`DELETE /sessions` 的 MessagePack 协议、命令/消息 DTO、客户端状态流和失败恢复逻辑。
- [offline-local-run-design.md](../../plans/reverse-engineering/offline-local-run-design.md)
  将游戏改成本地运行、不依赖网络的完整方案，包括最小实现、推荐架构、替换点、接口实现方式和风险边界。
- [predefined-match-and-random-system-design.md](../../plans/reverse-engineering/predefined-match-and-random-system-design.md)
  预定义对局、共享种子、可复现随机系统、对战系统和回放/校验方案。
- [decompile-and-data-notes.md](decompile-and-data-notes.md)  
  安装目录、`Magic` 搜索、可反编译程序集、`GameData.db.zip`、Addressables 资源和数据包结构记录。
- `2026-06-15-shop-entry-1..5-*.md`（2026-07-10 归档）  
  「进商店逻辑」五篇系列：旧客户端 `BazaarCardDealer` 铺货/单卡概率逐行逆向 + client/server 边界。分析对象是 legacy 客户端 dealer（线上铺货已由服务端 GameSim 权威）；其落地功能（Collection 商店概率浮层）在 a8fee8b9 未发布即移除，系列按该 commit 的明确意图保留为参考。

## 结论摘要

当前游戏不是传统 WebSocket 长连接。主流程分成三类网络：

1. 启动和账号/经济系统走 JSON REST，基地址是 `Config.NetURL`。
2. 进入 run 之后的所有玩法命令走 HTTP POST + MessagePack，基地址是 `Config.SocketURL`，核心端点只有 `/sessions` 和 `/commands`。
3. 静态数据、维护公告、翻译和 Addressables 走 CDN/Unity 下载通道，并带 `x-secret` header。

要做“本地运行、不依赖网络”，推荐保留客户端现有协议和状态机，在本机提供一个 Local TempoNet Facade + Local Game Session Server，再用补丁把 `Config.NetURL`、`Config.SocketURL`、`Config.DataURL`、`Config.MaintenanceDataURL` 指向本地。这样可以最大程度复用客户端 UI、`AppState`、`Cmd`、`NetMessageProcessor` 和 `GameSim`/`CombatSim` 处理链路。

预定义对局的关键不是只固定一个 seed，而是固定“静态数据版本 + run 初始状态 + 随机流命名 + 玩家命令序列 + 对手/商店/奖励/战斗来源”。推荐用 fixture 驱动本地 session server：客户端仍发送原始 `INetCommand`，本地引擎按 fixture 和确定性 RNG 产生同样的 `INetMessage`。
