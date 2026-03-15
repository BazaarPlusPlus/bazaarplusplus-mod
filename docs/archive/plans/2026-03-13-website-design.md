# BazaarPlusPlus Website Design

## Goal

为 BazaarPlusPlus 设计一个面向普通玩家的双语官网，首发目标聚焦两件事：

- 让用户快速理解 BazaarPlusPlus 是什么，以及它能带来什么体验提升
- 让 Windows 用户顺利完成下载，明确看到 macOS 仍处于 `Coming Soon`

官网同时承担轻量社区入口职责，但不把文档、路线图或开发者信息作为首屏重点。

---

## Target Audience

主要用户是已经在玩 **The Bazaar** 的普通玩家。

这意味着官网设计应遵循以下原则：

- 先讲结果，不先讲技术实现
- 文案优先描述体验收益，而不是 `BepInEx`、patch、runtime 之类术语
- 下载路径要短，首屏就能看到平台状态和下载入口
- 给进阶用户保留 GitHub、Releases、Issues 等入口，但不让这些信息干扰普通用户转化

---

## Product Framing Based on Current Plugin

官网表达必须以 **当前已经落地、玩家可感知的功能** 为基础，而不是以技术栈或未来规划为基础。

截至当前版本，BazaarPlusPlus 对普通玩家最有价值的能力主要是：

- `Monster Preview`
  - 在 monster 相关场景下提供更完整的 board / skill 预览
  - 这是最有辨识度的玩法增强点之一
- `Combat Status Bar`
  - 在战斗过程中提供更清晰的时间、节奏与速度控制信息
  - 属于玩家可以立即感知到的体验提升
- `Installer`
  - 降低安装和更新门槛
  - 这是转化优势，但不是首页唯一卖点

因此官网首页的价值表达不应只围绕“安装方便”展开，而应采用下面的优先级：

1. 先讲装上以后能获得什么体验提升
2. 再讲它如何帮助玩家更轻松地安装和使用
3. 最后再补充技术背景与进阶入口

官网可以使用 “quality-of-life mod” 或“增强体验 mod”来概括定位，但不能停留在这个抽象层级；首屏与卖点区应尽量落到更具体的玩家收益，例如：

- 看清怪物构成
- 更容易理解战斗节奏
- 更方便安装和更新

同时需要明确官网与安装器的角色边界：

- 官网负责价值表达、平台状态说明和下载转化
- 安装器负责实际检测、安装、更新与设置体验
- 官网不应做成“安装器网页版”

---

## Site Structure

首发版本采用轻量双页结构，并为中英文分别提供独立路由。

### Routes

- `/zh`
- `/en`
- `/zh/download`
- `/en/download`

### Navigation

全站导航保持简单，建议只保留以下入口：

- `Home`
- `Download`
- `GitHub`

如果后续有 Discord 或社区群，再追加 `Community`，但首发阶段不强制。

`Release Notes` 建议不作为首屏主导航入口，而是放在：

- 下载页的 `Version Information`
- 页脚或次级入口

这样可以保留版本透明度，同时避免对普通玩家形成主流程干扰。

### Why This Structure

采用“首页 + 下载页”的原因：

- 首页负责价值表达和下载转化
- 下载页负责平台状态、主下载、备用下载地址和安装说明
- 双语前缀路由比页面内切换文案更适合分享、SEO 和后续扩展

---

## Sitemap

### 1. Home

`/zh` 与 `/en` 为官网首页，承担品牌说明和下载转化。

建议区块顺序如下：

1. `Hero`
   - 一句话说明 BazaarPlusPlus 是什么，并尽量落到玩家收益
   - 主按钮：`Download for Windows`
   - 次按钮：`View Download Options`
   - 明确标注：`macOS Coming Soon`
   - 避免只强调“安装器”或“mod 框架”，而忽略实际功能价值

2. `Key Benefits`
   - 3 到 5 个核心卖点
   - 每个卖点只讲用户感知结果，不讲实现细节
   - 推荐优先围绕以下 3 类收益组织：
     - 更清晰地看怪物构成与预览信息
     - 更容易掌握战斗节奏与状态
     - 更方便完成安装与后续更新

3. `Feature Showcase`
   - 首发优先展示插件实际功能，而不是只展示安装器
   - 理想顺序是：游戏内截图 / 功能示意图 > 安装器界面截图
   - 如果暂时缺少游戏内素材，可以用抽象展示图或安装器截图占位
   - 后续逐步替换为游戏内截图或 GIF

4. `Platform Status`
   - `Windows: Ready`
   - `macOS: Coming Soon`
   - 该区块应成为首页高辨识信息之一

5. `Community Entry`
   - GitHub
   - Alternative Downloads
   - Release Notes
   - Report Issues
   - 该区块应偏轻量，不应压过下载动作和功能价值

6. `FAQ`
   - 回答普通玩家最关心的安装与支持问题

### 2. Download

`/zh/download` 与 `/en/download` 为下载页，承担实际下载与安装引导。

建议区块顺序如下：

1. `Download Header`
   - 页面标题
   - 一句简短说明

2. `Platform Cards`
   - `Windows` 卡片
     - 状态：`Ready`
     - 主按钮：直接下载安装包
     - 次级链接：两个备用下载地址
   - `macOS` 卡片
     - 状态：`Coming Soon`
     - 按钮置灰
     - 一句说明：支持仍在开发中

3. `Installation Steps`
   - 使用 3 步以内说明安装流程

4. `Version Information`
   - 当前版本号
   - 发布时间
   - 更新日志入口

5. `FAQ`
   - 只保留与下载、安装、平台支持直接相关的问题

---

## Content Strategy

### Homepage Messaging

首页文案应优先回答以下问题：

1. 这是什么？
2. 为什么值得装？
3. 我现在能不能下载？

推荐表达方式：

- 用 “quality-of-life mod” 或“增强体验 mod”描述定位
- 用“更清晰”“更顺手”“更方便安装和更新”这类词描述收益
- 不要只说“这是一个 mod”或“这是一个安装器”，要把收益具体到玩家能感知的场景
- 首页文案建议优先描述功能价值，安装便捷放在次一级信息
- 避免首屏出现大段技术解释

推荐的一句话方向：

- 中文：`看清怪物构成，掌控战斗节奏，更轻松地安装和使用 BazaarPlusPlus。`
- English: `See monster loadouts more clearly, follow combat pace at a glance, and install BazaarPlusPlus with less friction.`

### Download Messaging

下载页文案应优先回答以下问题：

1. 我用的系统现在是否支持？
2. 我应该点哪个下载按钮？
3. 如果主链接失效，还有没有备用地址？

平台状态必须用短词统一表达：

- `Windows Ready`
- `macOS Coming Soon`

不要在不同页面混用其他表达，例如 “Available now”“Under construction”“Soon” 等。

---

## First Draft Copy

以下文案为首发版本初稿，目标是先满足官网上线需要，而不是一次性把所有品牌表达打磨到最终形态。

### Home Draft

#### `/zh`

##### Hero

- Kicker: `BazaarPlusPlus`
- Title: `看清怪物构成，掌控战斗节奏。`
- Subtitle: `为 The Bazaar 玩家打造的增强体验 mod，帮助你更清晰地预览关键信息，并更轻松地完成安装与使用。`
- Primary CTA: `下载 Windows 版`
- Secondary CTA: `查看下载选项`
- Platform Note: `macOS Coming Soon`

##### Key Benefits

1. `更完整的怪物预览`
   - `在关键场景下更直观地查看 monster 的 board 与 skill 信息。`
2. `更清晰的战斗节奏`
   - `用更明确的状态与速度信息辅助你理解战斗过程。`
3. `更顺手的安装体验`
   - `通过独立安装器降低安装和更新门槛，不必先理解复杂术语。`

##### Feature Showcase

- Section Title: `把重要信息放到你真正会看的地方`
- Section Body: `BazaarPlusPlus 当前重点提升的是玩家在预览与战斗过程中的信息可读性。首发阶段可以先展示功能截图或静态示意图，后续再补充更完整的游戏内演示。`

##### Platform Status

- Section Title: `平台状态`
- Windows Card:
  - Label: `Windows`
  - Status: `Ready`
  - Body: `当前首发平台，可直接下载并安装。`
- macOS Card:
  - Label: `macOS`
  - Status: `Coming Soon`
  - Body: `支持仍在开发中，当前版本暂不提供下载。`

##### Community Entry

- Section Title: `更多入口`
- GitHub: `查看源码与项目主页`
- Alternative Downloads: `备用下载地址`
- Release Notes: `查看版本更新说明`
- Report Issues: `遇到问题时提交反馈`

##### FAQ

1. `BazaarPlusPlus 是什么？`
   - `它是一个面向 The Bazaar 玩家、强调信息可读性与使用便利性的增强体验 mod。`
2. `我需要懂技术才能安装吗？`
   - `不需要。首发官网会优先提供面向普通玩家的下载与安装入口。`
3. `现在支持 macOS 吗？`
   - `截至 2026-03-13，官网首发阶段只主推 Windows 下载；macOS 仍显示为 Coming Soon。`
4. `在哪里看更新内容？`
   - `可在下载页或 Release Notes 中查看当前版本和更新说明。`

#### `/en`

##### Hero

- Kicker: `BazaarPlusPlus`
- Title: `See monster loadouts. Follow combat at a glance.`
- Subtitle: `A quality-of-life mod for The Bazaar that makes key information easier to read and the install experience easier to follow.`
- Primary CTA: `Download for Windows`
- Secondary CTA: `View Download Options`
- Platform Note: `macOS Coming Soon`

##### Key Benefits

1. `Clearer monster previews`
   - `View monster board and skill information more directly in the moments that matter.`
2. `More readable combat flow`
   - `Track combat pace, timing, and speed with clearer on-screen status cues.`
3. `Less friction to install`
   - `Use a dedicated installer so regular players can get started without learning the full technical stack first.`

##### Feature Showcase

- Section Title: `Surface the information that actually helps during play`
- Section Body: `BazaarPlusPlus currently focuses on preview clarity and combat readability. Launch visuals can start with feature screenshots or static mockups, then expand into fuller in-game captures later.`

##### Platform Status

- Section Title: `Platform Status`
- Windows Card:
  - Label: `Windows`
  - Status: `Ready`
  - Body: `Available at launch with a direct download path.`
- macOS Card:
  - Label: `macOS`
  - Status: `Coming Soon`
  - Body: `Support is still in development and is not available for launch yet.`

##### Community Entry

- Section Title: `More Links`
- GitHub: `Project home and source code`
- Alternative Downloads: `Backup download links`
- Release Notes: `See what's new`
- Report Issues: `Report problems if something breaks`

##### FAQ

1. `What is BazaarPlusPlus?`
   - `It is a quality-of-life mod for The Bazaar focused on better information visibility and easier setup.`
2. `Do I need technical knowledge to install it?`
   - `No. The launch site should prioritize a simple download and install flow for regular players.`
3. `Is macOS supported right now?`
   - `As of March 13, 2026, the launch website only promotes Windows downloads, while macOS remains Coming Soon.`
4. `Where can I read the latest changes?`
   - `Use the download page or the release notes link to check the current version and changelog.`

### Download Draft

#### `/zh/download`

##### Download Header

- Title: `下载 BazaarPlusPlus`
- Subtitle: `选择适合你的平台，使用主下载或备用地址开始安装。`

##### Platform Cards

- Windows:
  - Title: `Windows`
  - Status: `Ready`
  - Body: `推荐首发平台。点击主下载按钮即可开始。`
  - Primary CTA: `下载 Windows 安装包`
  - Secondary Links:
    - `备用下载 1`
    - `备用下载 2`
- macOS:
  - Title: `macOS`
  - Status: `Coming Soon`
  - Body: `支持仍在开发中，当前版本暂不提供安装包。`
  - Disabled CTA: `即将推出`

##### Installation Steps

1. `下载 BazaarPlusPlus 安装包。`
2. `运行安装器，并选择你的 The Bazaar 安装目录。`
3. `完成安装后启动游戏，按需在设置中调整功能选项。`

##### Version Information

- Label: `当前版本`
- Publish Label: `发布时间`
- Release Notes CTA: `查看更新日志`

##### Download FAQ

1. `主下载链接失效怎么办？`
   - `请使用主按钮下方提供的两个备用下载地址。`
2. `安装后没有看到效果怎么办？`
   - `先确认安装器是否执行完成，再重新启动游戏。若问题仍存在，可查看 Release Notes 或提交 Issue。`
3. `macOS 什么时候上线？`
   - `首发阶段不承诺具体日期，只展示 Coming Soon。`

#### `/en/download`

##### Download Header

- Title: `Download BazaarPlusPlus`
- Subtitle: `Pick your platform and start with the primary download or a backup link.`

##### Platform Cards

- Windows:
  - Title: `Windows`
  - Status: `Ready`
  - Body: `This is the recommended launch platform with a direct install path.`
  - Primary CTA: `Download for Windows`
  - Secondary Links:
    - `Backup Download 1`
    - `Backup Download 2`
- macOS:
  - Title: `macOS`
  - Status: `Coming Soon`
  - Body: `Support is still in development, so no installer is provided at launch.`
  - Disabled CTA: `Coming Soon`

##### Installation Steps

1. `Download the BazaarPlusPlus installer.`
2. `Run it and point it to your The Bazaar install directory.`
3. `Launch the game and adjust settings if needed.`

##### Version Information

- Label: `Current Version`
- Publish Label: `Published`
- Release Notes CTA: `View Release Notes`

##### Download FAQ

1. `What if the main download link is unavailable?`
   - `Use one of the two backup download links listed under the primary button.`
2. `What if I install it and do not see changes in-game?`
   - `Make sure the installer completed successfully, then restart the game. If the issue remains, check the release notes or report it.`
3. `When is macOS coming?`
   - `The launch site does not promise a specific date. It stays labeled as Coming Soon for now.`

---

## i18n Design

官网首发支持：

- 简体中文
- English

### Routing Model

采用语言前缀路由，而不是只做前端文案切换：

- `/zh`
- `/en`

理由：

- 每种语言都有稳定链接，便于分享
- 搜索引擎更容易识别页面语言
- 后续新增页面时结构清晰，不需要重做路由

### Translation Scope

建议按页面和复用层组织文案：

- `common`
- `home`
- `download`
- `faq`

需要保证以下内容全部本地化：

- 导航文案
- Hero 标题和按钮
- 平台状态标签
- FAQ
- 下载说明

下载链接本身可以共用，但说明文字不能混用中英文。

---

## Visual Direction

整体视觉方向定为：

**首屏偏游戏感，内容区和下载页偏产品感。**

### Visual Goals

- 保留 The Bazaar 相关题材应有的氛围感
- 不做成泛用 SaaS 官网
- 不做成纯宣传页，确保下载信息足够清晰

### Style Principles

1. `Atmospheric Hero`
   - 首屏有明显氛围背景
   - 可使用暗色纹理、柔和光晕、卡牌式边框或旧纸质感
   - 但不让背景影响按钮和正文可读性

2. `Product Clarity`
   - 功能区和下载区采用更规整的布局
   - 平台卡、下载按钮、版本信息要有清晰层级
   - 用卡片与留白建立信息秩序

3. `Player-First Typography`
   - 标题可沿用安装器现有的奇幻气质字体，例如 `Cinzel` / `IM Fell`
   - 正文使用高可读性字体
   - 控制装饰性字体使用范围，避免影响长文本阅读

4. `Non-Generic Color Palette`
   - 避免常见紫白 SaaS 配色
   - 推荐以暖金、深红、煤黑、旧纸色作为主基调
   - 强调色用于下载按钮和平台状态

### Suggested Palette Direction

建议配色方向如下：

- Background: 深煤黑 / 炭灰
- Surface: 暗棕黑 / 带纹理深色面板
- Primary Accent: 暖金
- Secondary Accent: 深红或铜色
- Text: 暖白 / 浅米色
- Disabled State: 冷灰偏棕

### Component Feel

建议组件风格：

- Hero 按钮：强对比、明确的主次层级
- 平台卡：强调状态标签和下载动作
- FAQ 卡片：简洁，不使用过强装饰
- 导航：轻量、固定、可快速切换语言

---

## Asset Strategy

当前可直接复用的资产基础包括：

- 项目现有 `favicon`
- 安装器中已有的字体资源
- BazaarPlusPlus 现有品牌名与界面风格
- 安装器现有的中英文文案结构与语气

当前缺失的官网关键素材包括：

- 游戏内截图
- 功能演示 GIF 或短视频
- 更完整的官网视觉主 KV

因此首发阶段建议：

- 优先补 1 到 2 张插件实际功能截图
- 在缺少功能截图时，再用安装器界面截图或抽象展示图占位
- 网站结构优先落地
- 等功能演示素材准备好后，再替换 showcase 区域

---

## Download Experience Requirements

下载体验必须满足以下规则：

1. 首页首屏就能看到 Windows 已可用
2. 下载页必须存在主下载按钮
3. 下载页主按钮下方提供两个备用下载地址
4. macOS 状态必须明确标注为 `Coming Soon`
5. 不承诺具体 macOS 发布时间，避免产生错误预期

建议下载页上的信息层级为：

- 第一层：主下载按钮
- 第二层：备用下载地址
- 第三层：版本信息与安装步骤

---

## Out of Scope

本次官网首发设计不包含以下内容：

- 完整文档站
- 开发者 API 或技术实现说明页
- 复杂博客系统
- 用户账户系统
- 自动更新服务后台
- 多平台安装器管理面板

这些能力可以在官网完成首发后再逐步扩展。

---

## Launch Checklist

官网上线前至少准备以下内容：

- Windows 主下载链接
- 两个备用下载地址
- 当前版本号
- Release Notes 链接
- GitHub 链接
- 中英文首页文案
- 中英文下载页文案
- 至少 1 张可展示的截图或占位视觉

在这些内容齐备后，就可以先发布第一个版本。
