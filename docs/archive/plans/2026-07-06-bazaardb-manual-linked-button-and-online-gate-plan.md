---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master 88f7f5ae (2026-07-06); manual "I already linked" button + online gate (channel != Ptr, Unknown-as-Online) both shipped; all 7 steps + Decisions/ServerHealth tests present at HEAD 7a68e8bb.

# BazaarDB 绑定卡片:手动"我已绑定"按钮 + Online 版本 gate — 实施计划

- 日期:2026-07-06
- 状态:待实施(另一 Agent 执行,完成后由本会话 review)
- 范围:仅 `bazaarplusplus-mod`,History Panel 的 BazaarDB 账号绑定卡片

## 1. 背景:现有绑定逻辑

绑定卡片位于 History Panel(F8)左侧栏,整条链路如下:

### 1.1 可见性 gate(现状只有一个条件)

- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs:176` —
  `AccountCardVisible = _dependencies?.IsBazaarDbDataSharingEnabled?.Invoke() ?? false`
- 该委托在 `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelMount.cs:75` 绑定为
  `() => services.Config.BazaarDbUploadEnabled?.Value ?? false`,
  即 BepInEx 配置 `[BazaarDB] UploadScreenshots`(设置界面里的"数据共建"开关,
  定义在 `src/BazaarPlusPlus/Core/Config/BppConfig.cs:167`)。
- 视图侧在 `Ui/HistoryPanelUiToolkitView.cs:266` 用 `AccountCardVisible` 控制整卡 display。

### 1.2 绑定(redeem)流程

- 用户在卡片展开态输入 10 位一次性绑定码 →
  `HistoryPanelCoordinator.TryRedeemBazaarDbAccountAsync`
  (`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs:568`)→
  `BazaarDbLinkClient.RedeemAsync` POST 到 `https://bazaardb.gg/api/profile/link/redeem`
  (`src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs:53`)。
- 只有 200 成功(`OutcomeConfirmsLink`,`HistoryPanelCoordinator.cs:671`)才会:
  - `_state.LocalLinkedHint = true`、收起表单;
  - `BazaarDbAccountLinkStore.SaveHint(accountId)` 持久化本机提示
    (`src/BazaarPlusPlus/Game/HistoryPanel/AccountLink/BazaarDbAccountLinkStore.cs:12`,
    PlayerPrefs key = `BPP.HistoryPanel.BazaarDbLinkedName.<escaped accountId>`)。

### 1.3 关键事实(本需求的动机)

- **"已绑定"状态是纯本机 PlayerPrefs 提示,没有服务端读回接口**
  (store 内注释明确说明,`BazaarDbAccountLinkStore.cs:14-15`)。
  用户在机器 A 绑定后,机器 B 永远显示"未绑定";绑定码一次性且 10 分钟有效,
  用户没有理由在每台机器重复绑定 → 需要一个手动"我已绑定"入口,直接写本机提示。
- 每次打开面板 `OnPanelShown` → `RefreshAccountLinkIdentityFromGame`
  (`HistoryPanelCoordinator.cs:885`)会从 store 重读 hint 覆盖 `_state.LocalLinkedHint`,
  所以手动标记**必须走 `SaveHint` 持久化**,只改内存 state 下次打开就丢。

### 1.4 Online / PTR 渠道判定(第二个 gate 的依据)

- `IGameBuildInfo.Channel` ∈ `{Online, Ptr, Unknown}`
  (`src/BazaarPlusPlus/Core/Runtime/IGameBuildInfo.cs:4`),启动时由
  `GameBuildInfoResolver.Resolve()` 判定,经 `services.GameBuild` 暴露
  (`src/BazaarPlusPlus/Core/Runtime/IBppServices.cs:19`)。
- **既有策略(必须遵守)**:`Unknown` 必须按 `Online` 对待 —— 检测失败不能改变
  online 行为(`IGameBuildInfo.cs:9-11` 的注释;`BackgroundUploadPump.cs:34`
  也是用 `Channel == GameBuildChannel.Ptr` 判 PTR、其余放行的写法)。
  因此 gate 写成 `Channel != GameBuildChannel.Ptr`,不要写 `Channel == Online`。

## 2. 需求

1. **手动"我已绑定"按钮**:在绑定表单(展开态)增加一个次级按钮"我已绑定"。
   点击后不发任何网络请求,直接把本机标记为已绑定(SaveHint + LocalLinkedHint),
   收起表单,折叠行显示"已绑定 BazaarDB"(现有 Linked 状态文案与绿色高亮,
   `HistoryPanelUiToolkitView.cs:275`)。
2. **额外的 Online gate**:绑定卡片的可见条件从「数据共建开启」变为
   「数据共建开启 **且** 游戏为 online 版本(非 PTR)」。

## 3. 实施步骤

### Step 1 — 纯决策函数:卡片可见性 gate

在 `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelDecisions.cs` 增加静态纯函数:

```csharp
// Account-link card gate: data sharing must be on AND the build must not be PTR.
// Unknown is treated like Online by policy (see IGameBuildInfo) so a channel
// detection failure can never hide the card on a production build.
internal static bool IsAccountLinkCardAvailable(
    bool dataSharingEnabled,
    GameBuildChannel channel
) => dataSharingEnabled && channel != GameBuildChannel.Ptr;
```

注意 `HistoryPanelDecisions` 需要 `using BazaarPlusPlus.Core.Runtime;`。

### Step 2 — 组合 gate 到 mount 装配处

`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelMount.cs:75` 改为:

```csharp
() =>
    HistoryPanelDecisions.IsAccountLinkCardAvailable(
        services.Config.BazaarDbUploadEnabled?.Value ?? false,
        services.GameBuild.Channel
    )
```

同时把这条依赖链上的命名从 `isBazaarDbDataSharingEnabled` /
`IsBazaarDbDataSharingEnabled` 改为 `isBazaarDbAccountLinkAvailable` /
`IsBazaarDbAccountLinkAvailable`(语义已不再是"数据共建开关"本身):

- `HistoryPanelDependencies.cs:53,62,77`(参数、赋值、属性)
- `HistoryPanelFactory.cs:20,49`(参数、透传)
- `HistoryPanel.UiToolkit.cs:176`(调用点)
- `Ui/HistoryPanelUiToolkitView.cs:264-265` 的注释同步改写
  (现在的注释只提"数据共建",要补上 online gate)。

保持 `Func<bool>?` 形态(每次 BuildUiModel 求值,配置/渠道变化即时生效;
渠道启动后不变,但配置可在游戏内切换)。

### Step 3 — 文案:三语 LocalizedTextSet

`src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.AccountLink.cs`
按现有模式增加(en / 简体 / 繁体):

```csharp
private static readonly LocalizedTextSet AlreadyLinkedElsewhereButtonText = new(
    "I already linked",
    "我已绑定",
    "我已綁定"
);

internal static string AlreadyLinkedElsewhereButton() =>
    Resolve(AlreadyLinkedElsewhereButtonText);
```

命名避开已有的 `AlreadyLinkedText`(那是 409 错误 banner)。

### Step 4 — Coordinator:手动标记动作

`HistoryPanelCoordinator.cs` 增加方法(放在 `ToggleAccountLinkForm` 附近):

```csharp
public void MarkAccountLinkedManually()
{
    if (_state.AccountLinkInProgress)
        return;

    var accountId = RefreshAccountLinkIdentityFromGame(clearBanner: false);
    if (string.IsNullOrWhiteSpace(accountId))
    {
        SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure);
        _requestUiRefresh();
        return;
    }

    _state.LocalLinkedHint = true;
    _state.AccountLinkExpanded = false;
    _accountLinkStore.SaveHint(accountId);
    SetAccountLinkBanner(null, StatusSeverity.Neutral);
    BppLog.Info("HistoryPanel", $"BazaarDB link marked manually account={accountId}");
    _requestUiRefresh();
}
```

要点:

- 必须 `SaveHint`(见 1.3,只改 state 会在下次打开面板时被覆盖)。
- 复用 `RefreshAccountLinkIdentityFromGame(clearBanner: false)` 获取当前 accountId,
  与 redeem 流程一致;未登录时给出既有 SignedOut banner。
- 不发网络请求、不触碰 `_linkClient`。
- 收起表单后折叠行自然显示 Linked 文案 + "重新绑定"按钮(现有逻辑,
  `HistoryPanel.UiToolkit.cs:181-187`),无须额外 success banner。

### Step 5 — HistoryPanel 桥接

`HistoryPanel` 是 partial 类,Controller 侧已有 `SubmitAccountLinkCode` /
`ToggleAccountLinkForm` 这类转发方法(`HistoryPanelController.cs:110` 附近)。
同样增加:

```csharp
private void MarkAccountLinkedManually()
{
    _coordinator?.MarkAccountLinkedManually();
}
```

### Step 6 — UI 模型与视图

1. `HistoryPanelUiToolkitModel`(`HistoryPanel.UiToolkit.cs`)加字段:
   - `public string AccountAlreadyLinkedButtonText { get; set; } = string.Empty;`
   - `public bool AccountAlreadyLinkedButtonVisible { get; set; }`
2. `BuildUiModel()` 填充:
   - 文案 = `HistoryPanelText.AccountLink.AlreadyLinkedElsewhereButton()`
   - 可见 = 表单展开(`accountFormVisible`)且当前未标记已绑定
     (`!isBazaarDbLinked`;已绑定用户走"重新绑定"路径,不需要这个按钮),
     且未在绑定中(复用 `AccountLinkInputEnabled` 控制 enabled 即可)。
3. `Ui/HistoryPanelUiToolkitView.cs` + `Ui/HistoryPanelUiToolkitView.Tree.cs`:
   - 构造函数新增一个 `Action markAccountLinkedManually` 参数
     (紧跟 `toggleAccountLinkForm` 之后,`HistoryPanelUiToolkitView.cs:110` 的参数表),
     字段 `_markAccountLinkedManually`,null 检查与既有参数一致;
   - `HistoryPanel.UiToolkit.cs:22-37`(`EnsureUi` 的构造调用)按相同位置传入
     Step 5 的桥接方法 —— **构造函数是长位置参数表,新增参数的位置必须与
     调用点一一对应,这是本改动最容易错的点**;
   - `BuildAccountLinkCard`(`HistoryPanelUiToolkitView.Tree.cs:388`)在主 CTA
     `_accountLinkButton`(:513)与 `_accountHint`(:530)之间加一个次级按钮
     `_accountAlreadyLinkedButton`:
     - `CreateButton(..., () => _markAccountLinkedManually(), 0f, Sizes.ButtonCompactHeight, fixedWidth: false)`
     - 样式走次级配色 `StyleButton(btn, Colors.HistoryButtonBackground, Colors.HistoryFooterSecondaryText)`
       (与 `_accountRowAction` / `_accountCollapseButton` 一致,视觉上弱于主 CTA);
     - `width = Length.Percent(100f)`、`marginTop = UiSpacing.Xs`,与主按钮同宽但更矮;
   - `Refresh(...)`(`HistoryPanelUiToolkitView.cs:301` 主按钮块附近)增加:
     - `_accountAlreadyLinkedButton.text/tooltip = model.AccountAlreadyLinkedButtonText`
     - `display = model.AccountAlreadyLinkedButtonVisible ? Flex : None`
     - `SetEnabled(model.AccountLinkInputEnabled)`
   - `Dispose` / 字段声明处与其他 account 元素保持一致。

### Step 7 — 测试

两个既有 exe-runner(`<OutputType>Exe</OutputType>`,用 `dotnet run --project` 跑,
不是 `dotnet test`):

1. `tests/HistoryPanelDecisions.Tests`:为 `IsAccountLinkCardAvailable` 加用例——
   - `(true, Online) == true`
   - `(true, Unknown) == true`(Unknown 按 Online 对待,这是最重要的断言)
   - `(true, Ptr) == false`
   - `(false, Online) == false`
2. `tests/HistoryPanelServerHealth.Tests/Program.cs` 已用反射覆盖 AccountLink 文案
   与 store(:75、:152):补一条断言新文案方法 `AlreadyLinkedElsewhereButton` 存在
   且三语返回非空;若该文件对 `HistoryPanelUiToolkitModel` 有字段断言,同步补齐。

不要为"点按钮 → SaveHint"写 mock 调用序列测试(工作区规则:no coverage-theater)。

## 4. 明确不做的事

- 不改 redeem 网络协议、`BazaarDbLinkClient`、server 端任何东西。
- 不给"我已绑定"加二次确认或撤销(点错的用户可用"重新绑定"走正常 redeem 覆盖;
  hint 本来就只是本机展示提示,不影响上传链路)。
- 不把 online gate 应用到数据上传链路(`BackgroundUploadPump` 已自带 PTR gate)。
- 不改设置界面"数据共建"开关本身的可见性。

## 5. 验证方法

1. `./run.sh build` 编译通过(Debug 自动拷贝到 BepInEx/plugins)。
2. `dotnet run --project tests/HistoryPanelDecisions.Tests/HistoryPanelDecisions.Tests.csproj -c Debug`
3. `dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj -c Debug`
4. `./run.sh format`(csharpier;若重排了本改动以外的文件,commit 时排除)。
5. 游戏内手工验收(通过 Steam 启动,App ID 1617400):
   - 数据共建开 + online 版本:F8 面板出现绑定卡;展开表单可见次级按钮"我已绑定";
     点击后表单收起,折叠行显示绿色"已绑定 BazaarDB"+"重新绑定";
     关闭并重开面板、重启游戏后仍显示已绑定(PlayerPrefs 持久化)。
   - 数据共建关:卡片消失(回归)。
   - PTR 版本(如可测):数据共建开也不显示卡片;否则以 Decisions 单测覆盖为准。
   - 未登录状态展开表单不可达(现有行为,`hasAccount` gate),无须新处理。

## 6. Review 交接

实施完成后由原会话 review,重点核对:

- 构造函数位置参数是否与调用点对齐(Step 6 的高风险点);
- 手动标记是否走了 `SaveHint`(而非只改内存 state);
- gate 是否用 `!= Ptr`(而非 `== Online`,Unknown 语义);
- 文案三语齐全、无 tofu;
- diff 范围是否越出本计划(工作区规则:不顺手改无关内容)。
