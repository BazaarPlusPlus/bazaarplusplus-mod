---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master dd9fdc8f (2026-07-02); executor Tasks 1-3 shipped; two dead banner writes kept intentionally; Task 4 in-game matrix / Task 5 wrap-up were human stages.

# BazaarDB Account-Link Card: On-Demand Collapse Plan

> **For the executor agent:** this document is self-contained — you need no conversation context. Execute Tasks 1–3 in order with the checkbox (`- [ ]`) steps; every task ends buildable. Verification commands and expected outcomes are inline. Read the **Executor contract** section before touching anything. Task 4 (in-game matrix) and Task 5 (merge/wrap-up) are NOT yours — stop after Task 3 and hand back for review.

## Executor contract

- Work on a feature branch off `master` (e.g. `bazaardb-link-on-demand-collapse`). Commit per task with clear imperative messages, no conventional-commit prefixes.
- **Stop point:** after Task 3's build + tests pass, push nothing, merge nothing. Leave the branch for review (a separate reviewer diffs it against this plan) and the in-game matrix (Task 4) for the user.
- If you build from a git worktree under `bazaarplusplus-mod/.claude/worktrees/...`, pass `-p:BPPInstallerSourcePath=<abs-path-to-installer-resources>` on every `dotnet build` — the relative installer path does not resolve from worktree depth and both Debug and Release fail with MSB3030 otherwise. Do NOT "fix" `BazaarPlusPlus.csproj` for this.
- Test projects named below are **exe-runners**: `dotnet run --project …`, never `dotnet test`. Their exit code can be 0 despite failures — grep the full output for failure lines.
- Do not edit anything under `decompiled/`, `docs/MEMORY.md`, `docs/INDEX.md`, `docs/plans/`, or the repo `.rules`/`CLAUDE.md` files.
- Deviations: if the code at HEAD no longer matches a cited anchor, re-locate by symbol name and note the drift in your handoff summary; do not improvise design changes. Anything genuinely blocking → stop and report instead of working around.
- `./run.sh format` (csharpier) before the final commit; if it reformats files outside your change, exclude them from the commit.

**Goal:** The HistoryPanel BazaarDB account-link card stops being an always-visible hero block. By default it renders as one quiet status row; the full 10-cell binding form appears only when the player explicitly clicks the bind/re-link action, and collapses back afterwards.

**Architecture:** Reuse the existing view-model-driven show/hide mechanism (`HistoryPanelUiToolkitModel` bools applied in `Refresh()`). `HistoryPanelState.AccountLinkExpanded` changes meaning from "derived: form forced open whenever unlinked" to "pure UI disclosure flag, default collapsed". The view gains a collapsed row (status label + compact action button) and swaps the card chrome between quiet (collapsed) and hero (expanded) per refresh. No new subsystems, no server/wire changes.

**Tech stack:** C# 12 / netstandard2.1, Unity UI Toolkit (no UXML/USS — style-in-code with `Infrastructure/UiTokens`), exe-runner test projects.

## Global constraints

- Wire contract untouched: `BazaarDbLinkClient` and `POST https://bazaardb.gg/api/profile/link/redeem` stay exactly as-is (`src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs:53`).
- Code entry stays trim-only, case-sensitive, 10 cells, Enter-submits, paste-to-fill (`Ui/HistoryPanelUiToolkitView.Tree.cs:466-500,549-685`).
- Only a 200 redeem persists the linked hint — `OutcomeConfirmsLink` and its tests are untouched (`HistoryPanelCoordinator.cs:666-667`).
- Localized strings asserted by `tests/HistoryPanelServerHealth.Tests/Program.cs:74-113` (Title/Linked/InvalidOrExpired/Offline/Relink/ServerBusy) must keep their current values in all three locales.
- Test projects here are **exe-runners**: run with `dotnet run --project …`, not `dotnet test`.
- All file:line anchors below are as of master `10c51db0`.

---

## 1. Background / problem

The account-link card (added 2026-06-24, reshaped by `244db552` into a segmented-input "hero card") is the visually heaviest block on the HistoryPanel operation rail:

- It is the **only element on the rail with a 2px accent border** (`UiStyle.Border(_accountCard.style, Borders.Accent, Colors.HistoryTitleText)`, `Tree.cs:396`), plus panel-radius chrome and `UiSpacing.Xl` padding.
- When unlinked it always shows the full flow: bold title, why-text, a 36px-tall 10-cell code input, a full-width primary-blue CTA, and a hint line (`Tree.cs:388-547`) — roughly 150px of vertical space at the very top of the rail (above the filters, `Tree.cs:190-192`).
- The form is forced open whenever unlinked: `RefreshAccountLinkIdentityFromGame` sets `AccountLinkExpanded = true` for any not-linked account (`HistoryPanelCoordinator.cs:889-890`), and the model derives `AccountLinkFormVisible = hasAccount && !isBazaarDbLinked` (`HistoryPanel.UiToolkit.cs:185`).

User requirement: **按需绑定** (the binding form appears only on demand) and **未绑定时不抢眼** (the unbound default presentation must be subtle).

The original design draft already wanted a "collapsing link card" (`docs/drafts/2026-06-24-bazaardb-account-link-history-panel.md`, commit `abe92bf8`) and explicitly rejected moving binding into the settings dock (`BppSettingsDockDefinition` cannot host a `TextField`). This plan supersedes the *presentation* sections of that draft; the wire/contract/hint-store decisions there remain in force.

## 2. Goals / non-goals

Goals:

1. Default (collapsed) presentation in every state is one quiet row: status text + at most one compact button.
2. The full form (why + code cells + CTA + hint + banner) renders only after an explicit click, and collapses again on success or on 收起.
3. Fix a latent UX bug for free: clicking 重新绑定 today *deletes* the persisted linked hint immediately (`HistoryPanelCoordinator.cs:687`), so closing the panel without re-submitting permanently shows "unlinked" even though the server link still stands. With disclosure decoupled from linked-state, the hint survives until a new redeem succeeds.

Non-goals (out of scope):

- No change to `BazaarDbLinkClient`, the redeem contract, outcome classification, or banner copy for redeem outcomes.
- No unlink flow (no endpoint exists), no server read-back, no settings-dock entry.
- No animation (the mod has no transition helper; instant `DisplayStyle` swaps are the established pattern).
- No change to the `AccountCardVisible` gate: the card as a whole still renders only when `BazaarDB.UploadScreenshots` consent is on (`HistoryPanelMount.cs:59-66` → `HistoryPanel.UiToolkit.cs:170`).

## 3. Current state map (for the implementer)

Data flow: view button → `HistoryPanelController.SubmitAccountLinkCode/ToggleAccountLinkExpanded` (`HistoryPanelController.cs:107-116`) → `HistoryPanelCoordinator.TryRedeemBazaarDbAccountAsync/ToggleAccountLinkExpanded` (`HistoryPanelCoordinator.cs:563-693`) → mutate `HistoryPanelState` (`HistoryPanelState.cs:69-79`) → `_requestUiRefresh()` → `BuildUiModel` (`HistoryPanel.UiToolkit.cs:148-186`) → `HistoryPanelUiToolkitView.Refresh` (`Ui/HistoryPanelUiToolkitView.cs:235-285`).

State fields (`HistoryPanelState.cs`): `AccountLinkInProgress` (:69), `CachedAccountId` (:71), `LocalLinkedHint` (:73, PlayerPrefs presence hint via `BazaarDbAccountLinkStore`), `AccountLinkExpanded` (:75), `AccountLinkBannerMessage/Severity` (:77-79).

Current derivations (`HistoryPanel.UiToolkit.cs`):

```
isBazaarDbLinked        = LocalLinkedHint && !AccountLinkExpanded          (:148)
hasAccount              = CachedAccountId non-blank                        (:149)
AccountLinkFormVisible  = hasAccount && !isBazaarDbLinked                  (:185)
```

`RefreshAccountLinkIdentityFromGame` (`HistoryPanelCoordinator.cs:868-892`) currently *derives* `AccountLinkExpanded` from linked-state (unlinked/signed-out → forced `true`). It runs on every panel open (`OnPanelShown`, :62), at redeem start (:575), after redeem on account switch (:640), and on re-link toggle (:685).

View elements (`Tree.cs:388-547`): `_accountCard`, title row (`_accountTitle` + `_accountRelinkButton`), `_accountSignedOut`, `_accountWhy`, `_accountCodeRow` + `_accountCodeCells[10]`, `_accountLinkButton`, `_accountHint`, `_accountBanner`, `_accountLinkedBadge`. Visibility bindings: `Ui/HistoryPanelUiToolkitView.cs:235-285`.

## 4. Design

### 4.1 New state semantics

`AccountLinkExpanded` becomes a **pure UI disclosure flag**, owned by explicit commands only:

| Event | Effect on `AccountLinkExpanded` |
| --- | --- |
| Panel shown (`OnPanelShown`) | `false` (always open collapsed) |
| Row action click (绑定 / 重新绑定) or 收起 click | toggled via new `ToggleAccountLinkForm()` |
| Redeem succeeds | `false` (unchanged, `HistoryPanelCoordinator.cs:651`) |
| Identity refresh finds **no account** | `false` (signed-out never leaves a form open) |
| Identity refresh with an account | untouched (a refresh mid-flow must not slam the form shut) |

`LocalLinkedHint` becomes a straight mirror of the store: `store.IsLinked(accountId)` on refresh, `true` + `SaveHint` on confirmed redeem. **`BazaarDbAccountLinkStore.Clear` is no longer called** (the only call site was the re-link toggle) and is deleted.

New derivations in `BuildUiModel`:

```
hasAccount              = CachedAccountId non-blank                  (unchanged)
isBazaarDbLinked        = LocalLinkedHint                            (no longer && !Expanded)
AccountLinkFormVisible  = hasAccount && AccountLinkExpanded
collapsed row visible   = !AccountLinkFormVisible                    (within a visible card)
```

State matrix after the change:

| State | Collapsed row | Form |
| --- | --- | --- |
| Data-sharing off | *(entire card hidden — unchanged)* | — |
| Signed-out | `SignedOut()` text, **no button** | hidden |
| Unlinked, collapsed *(new default)* | `NotLinked()` muted text + compact `[绑定…]` | hidden |
| Unlinked, expanded | hidden | full form + `[收起]` in title row |
| Linking in progress | hidden | form, inputs + CTA + 收起 disabled, Pending banner |
| Linked, collapsed | `Linked()` success-tinted text + compact `[重新绑定]` | hidden |
| Linked, expanded (re-link) | hidden | full form (hint retained until new 200) |

Banner visibility moves from `!IsBazaarDbLinked && text` to `AccountLinkFormVisible && text` — a collapsed row must never trail a stale banner. Known consequence (accepted, red-team verified): two existing banner writes become unreachable dead UI — the post-success `SetAccountLinkBanner` (`HistoryPanelCoordinator.cs:656-659`, runs after :651 already collapsed the form) and the signed-out submit banner (:579, the refresh at :575 now force-collapses first). Both are harmless (no stale-banner leak; the collapsed row conveys the same state) — leave the writes in place rather than special-casing the success path.

### 4.2 Visual spec

Collapsed (quiet — mirrors the filter slot's section chrome one block below, `Tree.cs:687-696`):

```
┌───────────────────────────────────────────┐  bg Colors.HistorySectionBackground
│ BazaarDB 未绑定                 [绑定…]   │  Radius Radii.Md, Padding UiSpacing.Md
└───────────────────────────────────────────┘  NO border, single row ~32px
```

- Status label: `FontSmall(12)` Normal; color `Colors.HistoryFooterSecondaryText` (unlinked / signed-out) or `Colors.StatusCompletedText` (linked); tooltip = `Why()` so the value proposition survives the collapse. Accepted tradeoff: the why-copy drops from always-visible body text to hover-only — discoverability of the feature is deliberately deprioritized in favor of the 不抢眼 requirement (tooltip-on-Label is established prior art, e.g. `Ui/HistoryPanelUiToolkitView.cs:249`).
- Action button: the exact compact recipe already used by the re-link button (`Tree.cs:414-431`): `CreateButton(text, cb, 0f, Sizes.ButtonCompactHeight, fixedWidth: false)`, `minWidth Sizes.InlinePillMinWidth`, `StyleButton(Colors.HistoryButtonBackground, Colors.HistoryFooterSecondaryText)`.

Expanded (hero — unchanged from today, plus a collapse control):

```
┌═══════════════════════════════════════════┐  bg HistoryFooterBackground
│ Link BazaarDB account            [收起]   │  Radius Radii.Panel
│ 绑定后在 bazaardb.gg 查看你的对局与战绩   │  Border Accent(2) HistoryTitleText
│ [_][_][_][_][_][_][_][_][_][_]            │  Padding UiSpacing.Xl
│ [        绑定账号 (primary)       ]       │
│ 前往 bazaardb.gg 获取绑定码 · 10 分钟有效 │
│ (banner slot)                             │
└═══════════════════════════════════════════┘
```

The accent chrome is *earned*: it only appears while the player is actively in the binding flow. Chrome is swapped per refresh by a small helper (see Task 3), consistent with `ApplyStatusSeverity`-style dynamic restyling (`Ui/HistoryPanelUiToolkitView.DynamicStyles.cs:203-250`).

Dropped elements: `_accountLinkedBadge` (green pill → folded into row-status coloring), `_accountSignedOut` (folded into row status). `_accountRelinkButton` is repurposed as the in-card `[收起]` button; the row action button is new.

### 4.3 Copy (new strings only)

| Key | en | zh-CN | zh-Hant |
| --- | --- | --- | --- |
| `NotLinked` | `BazaarDB not linked` | `BazaarDB 未绑定` | `BazaarDB 未綁定` |
| `RowBind` | `Link…` | `绑定…` | `綁定…` |
| `Collapse` | `Hide` | `收起` | `收起` |

The row's unlinked action is `RowBind`, **not** `Button()`: the row button is a disclosure trigger (opens the form), while `Button()`/「绑定账号」is the in-form commit CTA — reusing the commit label for a disclosure toggle would promise a link and deliver a form (red-team finding). The trailing ellipsis is the "opens a further step" convention. The linked row action reuses `Relink()` unchanged (it has always opened the form rather than performing the re-link).

Reused: `SignedOut()` (signed-out row), `Linked()` (linked row), `Button()` (form CTA only), `Relink()` (linked row action), `Why()` (expanded card + collapsed-row tooltip), `Hint()`, all banner strings.

### 4.4 Alternatives considered

- **Entry point as a chip in the overview chips row** (`_countChip/_battleChip/_databaseChip`, `Tree.cs:204-230`): rejected — that row is read-only status plus one probe button; hiding an account *action* behind a status chip muddies the row's semantics and discoverability, and the expanded form still needs a home at card position anyway.
- **Move binding to the settings dock**: already rejected in the 2026-06-24 design (dock entries cannot host a `TextField`); HistoryPanel is also the natural "see your runs on bazaardb.gg" context. Not re-litigated.
- **Keep the card but only de-emphasize chrome (no collapse)**: fails the 按需 requirement — the 10-cell input would still render permanently.

## 5. Implementation tasks

### Task 1: Coordinator/state — make `AccountLinkExpanded` a pure disclosure flag

**Files:**
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs:59-66,683-693,868-892`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelState.cs:75`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/AccountLink/BazaarDbAccountLinkStore.cs:25-29`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelController.cs:113-116`

**Interfaces produced:** `HistoryPanelCoordinator.ToggleAccountLinkForm()` (public, replaces `ToggleAccountLinkExpanded()`), `HistoryPanel.ToggleAccountLinkForm()` (controller wrapper, replaces `ToggleAccountLinkExpanded()`).

- [ ] **Step 1.1** — `RefreshAccountLinkIdentityFromGame` stops writing `AccountLinkExpanded` except to force-collapse on signed-out; `LocalLinkedHint` becomes a store mirror:

```csharp
private string? RefreshAccountLinkIdentityFromGame(bool clearBanner = true)
{
    var accountId = NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId());

    _state.CachedAccountId = accountId;
    if (clearBanner)
        SetAccountLinkBanner(null, StatusSeverity.Neutral);
    if (string.IsNullOrWhiteSpace(accountId))
    {
        _state.LocalLinkedHint = false;
        // Signed-out must never leave the form open; with an account present the
        // disclosure flag is owned by ToggleAccountLinkForm / redeem success only.
        _state.AccountLinkExpanded = false;
        return null;
    }

    _state.LocalLinkedHint = _accountLinkStore.IsLinked(accountId);
    return accountId;
}
```

- [ ] **Step 1.2** — `OnPanelShown` (`:59-66`) opens collapsed every time; add one line before the identity refresh:

```csharp
public void OnPanelShown()
{
    _session.Begin();
    _state.AccountLinkExpanded = false;
    RefreshAccountLinkIdentityFromGame();
    _state.ReplayActionInProgress = false;
    _state.IsVisible = true;
    RefreshSectionOnEntry();
}
```

- [ ] **Step 1.3** — replace `ToggleAccountLinkExpanded` (`:683-693`) with a real toggle that no longer clears the persisted hint:

```csharp
public void ToggleAccountLinkForm()
{
    if (_state.AccountLinkInProgress)
        return; // never yank the form out from under an in-flight redeem

    if (_state.AccountLinkExpanded)
    {
        _state.AccountLinkExpanded = false;
        SetAccountLinkBanner(null, StatusSeverity.Neutral);
        _requestUiRefresh();
        return;
    }

    // Re-read identity + hint (also clears the banner). If the player signed out since the
    // last refresh, the refresh already force-collapsed — do not re-open the form.
    var accountId = RefreshAccountLinkIdentityFromGame();
    if (string.IsNullOrWhiteSpace(accountId))
    {
        _requestUiRefresh();
        return;
    }

    _state.AccountLinkExpanded = true;
    _requestUiRefresh();
}
```

- [ ] **Step 1.4** — delete `BazaarDbAccountLinkStore.Clear` (`BazaarDbAccountLinkStore.cs:25-29`; its only call site was the old toggle). Update the class comment if it references clearing. `SaveHint`/`IsLinked`/`BuildPrefsKey` untouched.

- [ ] **Step 1.5** — rename the controller wrapper (`HistoryPanelController.cs:113-116`):

```csharp
private void ToggleAccountLinkForm()
{
    _coordinator?.ToggleAccountLinkForm();
}
```

and its use in the view construction call (`HistoryPanel.UiToolkit.cs:30`, the method-group argument after `SubmitAccountLinkCode` at :29 inside the view ctor call at :22-37). Update the state-field comment on `HistoryPanelState.AccountLinkExpanded` (:75) to say "pure UI disclosure flag; default collapsed" if a comment exists.

- [ ] **Step 1.6** — build + targeted tests:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj -c Debug
```

Expected: build succeeds; exe-runner prints all-pass (grep its output for failures — exit code alone is not sufficient per repo rule). Intermediate state at this commit (red-team corrected): the unlinked card still shows the **full form** — the old derivation `AccountLinkFormVisible = hasAccount && !isBazaarDbLinked` (`HistoryPanel.UiToolkit.cs:185`) is untouched until Task 3, and unlinked+collapsed yields `isBazaarDbLinked=false` → form visible exactly as today. The only observable Task-1 delta is in the re-link flow (the toggle no longer clears the persisted hint). Task 3 lands the collapsed visual.

### Task 2: Strings — `NotLinked` + `RowBind` + `Collapse`

**Files:**
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.AccountLink.cs`
- Modify: `tests/HistoryPanelServerHealth.Tests/Program.cs:74-113` (extend the existing localization assertion table)

- [ ] **Step 2.1** — add to `HistoryPanelText.AccountLink` (same shape as existing sets):

```csharp
private static readonly LocalizedTextSet NotLinkedText = new(
    "BazaarDB not linked",
    "BazaarDB 未绑定",
    "BazaarDB 未綁定"
);

private static readonly LocalizedTextSet RowBindText = new(
    "Link…",
    "绑定…",
    "綁定…"
);

private static readonly LocalizedTextSet CollapseText = new(
    "Hide",
    "收起",
    "收起"
);

internal static string NotLinked() => Resolve(NotLinkedText);

internal static string RowBind() => Resolve(RowBindText);

internal static string Collapse() => Resolve(CollapseText);
```

- [ ] **Step 2.2** — extend the localization table in `tests/HistoryPanelServerHealth.Tests/Program.cs` with the three new keys, following the file's existing per-string assertion pattern (`Program.cs:74-113`). Purpose: zh-Hant locale-parity / tofu guard, same as the sibling assertions — not behavior coverage. Re-run the exe-runner; expected all-pass.

### Task 3: View — collapsed row, dynamic chrome, rebind

**Files:**
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:388-547`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs:22-23,51-63,108-135,235-285`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs:148-186,241-288` (model fields)

**Interfaces:** view ctor param `Action relinkBazaarDbAccount` renamed `Action toggleAccountLinkForm`; model gains `AccountRowStatusText`, `AccountRowActionText`, `AccountRowActionVisible`, `AccountLinkCollapseText`; model drops `AccountSignedOutText`, `AccountSignedOutVisible`, `AccountLinkedBadgeText`, `AccountRelinkButtonText`.

- [ ] **Step 3.1** — field/delegate rename in `HistoryPanelUiToolkitView.cs`: `_relinkBazaarDbAccount` → `_toggleAccountLinkForm` (:23, :108-109, :126-129). Replace element fields (:51-63): drop `_accountSignedOut`, `_accountLinkedBadge`, rename `_accountRelinkButton` → `_accountCollapseButton`; add:

```csharp
private VisualElement? _accountCollapsedRow;
private Label? _accountRowStatus;
private Button? _accountRowAction;
```

- [ ] **Step 3.2** — restructure `BuildAccountLinkCard` (`Tree.cs:388-547`). The card container loses its static chrome (applied per-state in Step 3.4); prepend the collapsed row; keep the entire existing form cluster (title row, why, code row, CTA, hint, banner) unchanged except the title-row button becomes the collapse control:

```csharp
private void BuildAccountLinkCard(VisualElement rail)
{
    _accountCard = new VisualElement();
    _accountCard.style.flexDirection = FlexDirection.Column;
    _accountCard.style.flexShrink = 0f;
    _accountCard.style.marginTop = UiSpacing.Xl;
    rail.Add(_accountCard); // chrome (bg/radius/border/padding) applied per-state in ApplyAccountCardChrome

    // ── Collapsed row: one quiet line, mirrors the filter slot's section weight ──
    _accountCollapsedRow = new VisualElement();
    _accountCollapsedRow.style.flexDirection = FlexDirection.Row;
    _accountCollapsedRow.style.alignItems = Align.Center;
    _accountCollapsedRow.style.minWidth = 0f;
    _accountCard.Add(_accountCollapsedRow);

    _accountRowStatus = CreateLabel(
        Sizes.FontSmall,
        FontStyle.Normal,
        Colors.HistoryFooterSecondaryText
    );
    _accountRowStatus.style.flexGrow = 1f;
    _accountRowStatus.style.flexShrink = 1f;
    _accountRowStatus.style.minWidth = 0f;
    _accountRowStatus.style.whiteSpace = WhiteSpace.NoWrap;
    _accountRowStatus.style.overflow = Overflow.Hidden;
    _accountCollapsedRow.Add(_accountRowStatus);

    _accountRowAction = CreateButton(
        HistoryPanelText.AccountLink.RowBind(),
        _toggleAccountLinkForm,
        0f,
        Sizes.ButtonCompactHeight,
        fixedWidth: false
    );
    _accountRowAction.style.flexGrow = 0f;
    _accountRowAction.style.flexShrink = 0f;
    _accountRowAction.style.flexBasis = StyleKeyword.Auto;
    _accountRowAction.style.minWidth = Sizes.InlinePillMinWidth;
    _accountRowAction.style.marginLeft = UiSpacing.Sm;
    StyleButton(_accountRowAction, Colors.HistoryButtonBackground, Colors.HistoryFooterSecondaryText);
    _accountCollapsedRow.Add(_accountRowAction);

    // ── Expanded form (existing structure) ──
    var titleRow = new VisualElement();
    // … titleRow + _accountTitle exactly as today (Tree.cs:400-412) …

    _accountCollapseButton = CreateButton(
        HistoryPanelText.AccountLink.Collapse(),
        _toggleAccountLinkForm,
        0f,
        Sizes.ButtonCompactHeight,
        fixedWidth: false
    );
    // … same compact styling as the old relink button (Tree.cs:421-430) …
    titleRow.Add(_accountCollapseButton);

    // … _accountWhy, _accountCodeRow + cells, _accountLinkButton, _accountHint,
    //     _accountBanner exactly as today (Tree.cs:443-546); delete the
    //     _accountSignedOut and _accountLinkedBadge blocks (Tree.cs:433-441,454-464) …
}
```

- [ ] **Step 3.3** — model changes in `HistoryPanel.UiToolkit.cs`. Derivations (:148-149,170-186):

```csharp
var isBazaarDbLinked = _state.LocalLinkedHint;
var hasAccount = !string.IsNullOrWhiteSpace(_state.CachedAccountId);
var accountFormVisible = hasAccount && _state.AccountLinkExpanded;
```

Model assignments (replacing the current account-link block):

```csharp
AccountCardVisible = _dependencies?.IsBazaarDbDataSharingEnabled?.Invoke() ?? false,
IsBazaarDbLinked = isBazaarDbLinked,
AccountTitleText = HistoryPanelText.AccountLink.Title(),
AccountWhyText = HistoryPanelText.AccountLink.Why(),
AccountHintText = HistoryPanelText.AccountLink.Hint(),
AccountRowStatusText = !hasAccount
    ? HistoryPanelText.AccountLink.SignedOut()
    : isBazaarDbLinked
        ? HistoryPanelText.AccountLink.Linked()
        : HistoryPanelText.AccountLink.NotLinked(),
AccountRowActionText = isBazaarDbLinked
    ? HistoryPanelText.AccountLink.Relink()
    : HistoryPanelText.AccountLink.RowBind(),
AccountRowActionVisible = hasAccount,
AccountLinkCollapseText = HistoryPanelText.AccountLink.Collapse(),
AccountLinkButtonText = _state.AccountLinkInProgress
    ? HistoryPanelText.AccountLink.Linking()
    : HistoryPanelText.AccountLink.Button(),
AccountLinkButtonEnabled = !_state.AccountLinkInProgress && hasAccount,
AccountLinkInputEnabled = !_state.AccountLinkInProgress && hasAccount,
AccountLinkBannerText = _state.AccountLinkBannerMessage,
AccountLinkBannerSeverity = _state.AccountLinkBannerSeverity,
AccountLinkFormVisible = accountFormVisible,
```

Model class (:241-288): add `AccountRowStatusText`, `AccountRowActionText`, `AccountRowActionVisible`, `AccountLinkCollapseText` (all string/bool with the usual `= string.Empty` defaults); delete `AccountSignedOutText`, `AccountSignedOutVisible`, `AccountLinkedBadgeText`, `AccountRelinkButtonText`.

- [ ] **Step 3.4** — rebind in `Refresh` (`Ui/HistoryPanelUiToolkitView.cs:235-285`), replacing the current account block:

```csharp
var accountFormVisible = model.AccountLinkFormVisible;
_accountCard!.style.display = model.AccountCardVisible ? DisplayStyle.Flex : DisplayStyle.None;
ApplyAccountCardChrome(accountFormVisible);

_accountCollapsedRow!.style.display = accountFormVisible ? DisplayStyle.None : DisplayStyle.Flex;
_accountRowStatus!.text = StablePanelText.Compact(model.AccountRowStatusText, 96);
_accountRowStatus.tooltip = model.AccountWhyText;
_accountRowStatus.style.color = model.IsBazaarDbLinked
    ? Colors.StatusCompletedText
    : Colors.HistoryFooterSecondaryText;
_accountRowAction!.text = model.AccountRowActionText;
_accountRowAction.tooltip = model.AccountRowActionText;
_accountRowAction.style.display = model.AccountRowActionVisible
    ? DisplayStyle.Flex
    : DisplayStyle.None;

_accountTitle!.text = model.AccountTitleText;
_accountTitle.style.display = accountFormVisible ? DisplayStyle.Flex : DisplayStyle.None;
_accountCollapseButton!.text = model.AccountLinkCollapseText;
_accountCollapseButton.SetEnabled(model.AccountLinkInputEnabled);
_accountCollapseButton.style.display = accountFormVisible ? DisplayStyle.Flex : DisplayStyle.None;
_accountWhy!.text = StablePanelText.Compact(model.AccountWhyText, 112);
_accountWhy.tooltip = model.AccountWhyText;
_accountWhy.style.display = accountFormVisible ? DisplayStyle.Flex : DisplayStyle.None;
_accountCodeRow!.style.display = accountFormVisible ? DisplayStyle.Flex : DisplayStyle.None;
if (_accountCodeCells != null)
{
    foreach (var cell in _accountCodeCells)
        cell.SetEnabled(model.AccountLinkInputEnabled);
}
if (!accountFormVisible || !model.AccountCardVisible)
    ClearCodeCells();
_accountLinkButton!.text = model.AccountLinkButtonText;
_accountLinkButton.tooltip = model.AccountLinkButtonText;
_accountLinkButton.style.display = accountFormVisible ? DisplayStyle.Flex : DisplayStyle.None;
_linkSubmitAllowedByModel = model.AccountLinkButtonEnabled;
UpdateAccountCodeFeedback();
_accountHint!.text = StablePanelText.Compact(model.AccountHintText, 120);
_accountHint.tooltip = model.AccountHintText;
_accountHint.style.display = accountFormVisible ? DisplayStyle.Flex : DisplayStyle.None;
_accountBanner!.text = StablePanelText.Compact(model.AccountLinkBannerText, 120);
_accountBanner.tooltip = model.AccountLinkBannerText ?? string.Empty;
_accountBanner.style.display =
    accountFormVisible && !string.IsNullOrWhiteSpace(model.AccountLinkBannerText)
        ? DisplayStyle.Flex
        : DisplayStyle.None;
ApplyStatusSeverity(_accountBanner, model.AccountLinkBannerSeverity);
```

Note `_accountTitle` gains an explicit display binding (it was previously always visible; in the collapsed state the row replaces it).

Chrome helper (new, in `Ui/HistoryPanelUiToolkitView.DynamicStyles.cs` next to the other `Apply*` helpers):

```csharp
private void ApplyAccountCardChrome(bool expanded)
{
    if (_accountCard == null)
        return;

    var s = _accountCard.style;
    if (expanded)
    {
        s.backgroundColor = Colors.HistoryFooterBackground;
        UiStyle.Radius(s, Radii.Panel);
        UiStyle.Border(s, Borders.Accent, Colors.HistoryTitleText);
        UiStyle.Padding(s, UiSpacing.Xl);
    }
    else
    {
        s.backgroundColor = Colors.HistorySectionBackground;
        UiStyle.Radius(s, Radii.Md);
        UiStyle.Border(s, 0f, Colors.HistorySectionBackground); // width 0 clears the frame
        UiStyle.Padding(s, UiSpacing.Md);
    }
}
```

(`UiStyle.Border(s, 0f, color)` verified against `Infrastructure/UiTokens/UiStyle.cs:59-71` — it writes all four border widths, so 0f cleanly clears the frame.)

- [ ] **Step 3.5** — build + full test sweep + format:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod
./run.sh build
./run.sh test        # grep output for "Failed test projects:" — exit code alone insufficient
./run.sh format      # keep the commit scoped if csharpier touches unrelated files
```

### Task 4: In-game verification matrix (NOT the executor — user/reviewer stage, after code review)

Launch through Steam only (`open "steam://run/1617400"`), Debug build auto-copied to `BepInEx/plugins`. Check `<GameDir>/BepInEx/LogOutput.log` for `[BPP][HistoryPanel]` errors after each step.

- [ ] 4.1 `BazaarDB / UploadScreenshots` config off → no account row/card at all.
- [ ] 4.2 Consent on, signed out → single muted row "登录《The Bazaar》后即可绑定", no button, no accent border anywhere.
- [ ] 4.3 Signed in, unlinked → muted row "BazaarDB 未绑定" + `[绑定…]`; hovering the row shows the why-tooltip; rail content below sits ~120px higher than before; the row is visually distinguishable from the filter slot below it (open question #1).
- [ ] 4.4 Click `[绑定…]` → hero card expands in place (accent chrome); cell chrome renders correctly on first expand; typing auto-advances; paste fills all 10; Backspace clears-then-steps-back; Enter submits at 10 chars.
- [ ] 4.5 Submit a garbage code → Failure banner "绑定码无效或已过期…", form stays open.
- [ ] 4.6 Click `[收起]` → collapses to the unlinked row; banner gone; expand again → banner still clear, cells empty.
- [ ] 4.7 Redeem a real code from bazaardb.gg → auto-collapse to green "已绑定 BazaarDB" + `[重新绑定]`; close/reopen panel → still linked.
- [ ] 4.8 Click `[重新绑定]` → form opens; click `[收起]` **without submitting** → row still shows 已绑定 (hint retained — this is the intended behavior change vs. today).
- [ ] 4.9 While a redeem is in flight (slow network or immediate check) → cells/CTA/收起 disabled, "绑定中..." pending banner.
- [ ] 4.10 Switch locale zh ⇄ en ⇄ zh-Hant → row/button/banner strings all swap; no tofu.

### Task 5: Wrap-up (NOT the executor — happens after review + in-game matrix)

- [ ] Reviewer walks §7b against the branch diff; findings go back to the executor (or are fixed at review) before anything merges.
- [ ] After the Task 4 matrix passes and the user confirms: merge working branch to `master`, push, delete merged branch (settled wrap-up flow).

## 6. Risks

- **Hidden-at-build code cells**: `_accountCodeCells` style themselves via `AttachToPanelEvent` (`Tree.cs:493-495`). `DisplayStyle.None` elements still attach to the panel, and today's linked state already builds the cells hidden without issue — no regression expected, but 4.4 explicitly verifies cell chrome after first expand.
- **Layout shift on expand** pushes the filter slot and everything below down ~120px while binding. Transient and intentional; matrix 4.4 sanity-checks that the rail's flex body absorbs it (railBody `flexGrow=1`, `Tree.cs:235-241`).
- **Two adjacent quiet panels**: the collapsed row's proposed chrome is identical to the filter slot directly below (`Tree.cs:693-695`) — they could read as one undifferentiated block. Matrix 4.3 checks distinguishability in-game; fall back to open question #1's bare-row option if it reads badly.
- **Dead banner writes** (documented in §4.1): the success and signed-out-submit banner writes become unreachable under the new gating — intentional, do not "fix" them mid-implementation.
- **`HistoryPanelServerHealth.Tests` reflection coupling**: it reflects `OutcomeConfirmsLink`, `RedeemBannerSeverity`, `BuildPrefsKey`, and the six existing strings — none renamed/removed by this plan. Deleting `Clear` and renaming `ToggleAccountLinkExpanded` are not referenced there (red-team verified repo-wide, 2026-07-02).

## 7. Settled decisions (do not re-open during implementation)

1. **Collapsed-row chrome**: quiet section background like the filter slot, as spec'd in §4.2 / Step 3.4. If the in-game matrix (4.3) later shows it reads as one block with the filter slot, the fallback is a bare row — that is a *review-stage* call, not an executor call.
2. **en copy for `Collapse`**: `Hide`.
3. **Autofocus first code cell on expand**: NOT implemented — deliberately out of scope. (If ever revisited: it needs a one-shot deferred `schedule.Execute(...)`, not an inline `Focus()` in the same `Refresh` that flips the row's `display` — UITK focus on a just-unhidden element may silently no-op; the codebase has no prior art for it.)

## 7b. Reviewer checklist (post-implementation; not for the executor)

- [ ] `git diff master...<branch>` touches only the files listed in Tasks 1–3 (plus test Program.cs); no drive-by edits.
- [ ] State machine matches §4.1 exactly: grep confirms the only writers of `AccountLinkExpanded` are `OnPanelShown` (false), `ToggleAccountLinkForm`, redeem-success (false), and the signed-out branch of `RefreshAccountLinkIdentityFromGame` (false).
- [ ] No caller of `BazaarDbAccountLinkStore.Clear` remains and the method is gone; `SaveHint`/`IsLinked`/`BuildPrefsKey` untouched.
- [ ] View binding parity: every element listed in Step 3.4 is bound; dropped model fields (`AccountSignedOutText/Visible`, `AccountLinkedBadgeText`, `AccountRelinkButtonText`) have no residual references.
- [ ] Row action label is `RowBind()` when unlinked / `Relink()` when linked — never `Button()`.
- [ ] Banner display gates on `AccountLinkFormVisible`; the two dead banner writes (§4.1) were left in place, not "fixed".
- [ ] New strings exist in all three locales; `tests/HistoryPanelServerHealth.Tests` output greped clean; `./run.sh test` output has no "Failed test projects:".
- [ ] Then hand to the user for the Task 4 in-game matrix; Task 5 wrap-up only after that passes.

## 8. References

- Current implementation commits: `52d571ba` (card added), `36334d64` (review fixes), `244db552` (10-cell hero redesign).
- Design/contract source: `docs/drafts/2026-06-24-bazaardb-account-link-history-panel.md`, `docs/drafts/2026-06-24-bazaardb-account-link-implementation-plan.md` (wire contract, 409-permanent, no read-back/unlink, case-sensitive 10-min codes — all still authoritative).
- This plan supersedes only the presentation/disclosure sections of the above.
