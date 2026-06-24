# BazaarDB Account Link in the History Panel

Draft for letting a player bind their in-game Bazaar account to their BazaarDB
profile from inside the mod, by pasting a one-time link code into the History
Panel.

## Summary

BazaarDB ([bazaardb.gg](https://bazaardb.gg)) is the community DB and the source
of truth for accounts and the in-game-account → BazaarDB-profile mapping. It
exposes an integration-agnostic redeem endpoint: the player generates a one-time
code on bazaardb.gg, pastes it into our app, and we `POST { code, account_id }`.
On success the player's runs/snapshots get attributed to their BazaarDB profile.

The mod already sends `player.account_id` on every upload, so the player never
types an id — we read it invisibly and only ask for the code.

Chosen design (confirmed 2026-06-24):

- **Transport:** direct `POST https://bazaardb.gg/api/profile/link/redeem` (per
  the integration guide — no auth header, separate `HttpClient`). Not routed
  through `mod-api-v4` / `ModApiRoutes`.
- **UI:** a self-contained **link card** in the History Panel's right rail that
  collapses to a one-line "Linked as @name" badge once done.

Status: design only. No code written. One external dependency must be verified
before the error-copy table is final (see
[External contract](#external-contract-unverified)).

## Why the History Panel

The History Panel is where a player reviews their runs and battles — the exact
context where "see these on bazaardb.gg" is meaningful. It already gates several
features on `player.account_id` (ghost sync, snapshot upload), so the identity
plumbing it needs is already present.

## External contract (unverified)

`bazaardb.gg` is a **third-party host the mod has never called**. The redeem
contract below exists only in the integration guide handed to us — it appears
nowhere in `bazaarplusplus-server` or this repo. Treat it as unverified until a
real round-trip confirms it.

```http
POST https://bazaardb.gg/api/profile/link/redeem
Content-Type: application/json

{ "code": "ABCDEFGHJK", "account_id": "<local player.account_id>" }
```

| Status | Body | Meaning |
| --- | --- | --- |
| `200` | `{ "linked": true }` | linked |
| `400` | `{ "error": "invalid_or_expired" }` | wrong / used / >10 min old |
| `409` | `{ "error": "already_linked" }` | this game account already bound to a **different** BazaarDB user |
| `400` | `{ "error": "Missing code or account_id" }` | empty field |
| `500` | `{ "error": "Redeem failed" }` | transient |

Contract rules that constrain the UI:

- **Code is case-sensitive.** Alphabet `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no
  `0/O`, `1/I/L`). **Do not uppercase or transform input** — trim surrounding
  whitespace and send as typed. (Uppercasing is at best a no-op on a valid code
  and risks corrupting input; the guide explicitly calls the code
  case-sensitive.)
- **One-time, 10-minute TTL.** Expired → user clicks the button again on
  bazaardb.gg for a fresh code.
- **One game account ↔ one BazaarDB user** (first-link-wins). `409` is the
  honest, permanent rejection — no client retry.
- **`account_id` is trusted as reported** and must be the **same** id sent as
  `player.account_id` in uploads, or the link won't attribute runs.
- **No read-back / unlink endpoint.** We cannot query "is this account linked?".
  Any local "linked" memory is a UX hint only.

### Pre-implementation verification spike

Before writing the error mapping, do one throwaway round-trip: `POST` one
known-good code and one known-bad code to the real endpoint and capture the
actual status codes and JSON body shape. This only tunes the copy table below;
it does not change the structure.

## Identity: `account_id` is read, never typed

`BppClientCacheBridge.TryGetProfileAccountId()` is the trusted source — the same
value the snapshot/run-bundle upload path uses for `player.account_id`. It reads
`ClientCache.Profile.AccountId` by reflection and returns `null` when the profile
is unavailable (not logged in, or profile not yet loaded).

- Account id accessor. [`src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs:26-34`](../../src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs)
- Human-readable name for the "Linking as / Linked as @name" confirmation:
  `TryGetProfileDisplayUsername()` (falls back to `Username`). [`src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs:36-58`](../../src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs)
- Gating precedent — snapshot upload skips the batch with a log line, no
  exception, when the account id is null/whitespace. The link card mirrors this
  with a "sign in to link" line. [`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs:72-79`](../../src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs)

Read both the id and the display name once when the panel opens and cache them in
state — do not call reflection from `Refresh()` every frame.

## UX: collapsing link card

### Placement

A new `BuildAccountLinkCard(rail)` slot inserted between the supporter subtitle
and the filter slot in the operation rail — high in the rail, visible on panel
open (F8), no scroll, no new tab. Insertion point:

- After `rail.Add(_subtitle)` ([`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:187`](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs))
- Before `BuildFilterSlot(rail)` ([`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:189`](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs))

Style it like the existing `selectedDetailCard` (bordered, `Radii.Md`,
`UiSpacing.Lg` padding) so it reads as native chrome. [`src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs:241-251`](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)

Two structural rules from the rail's design:

- **Build once, re-skin in `Refresh()`.** `Refresh()` updates element properties
  but constructs nothing — every card element (`_accountCard`,
  `_accountCodeField`, `_accountLinkButton`, `_accountBanner`, …) is created in
  the build pass and stored as a view field, then shown/hidden/relabeled in
  `Refresh()` via `display = Flex/None`.
- **Do not reuse `_statusLabel` for link feedback.** The shared footer banner is
  clobbered by transient replay/delete/sync/health messages. The card carries its
  **own** inline `_accountBanner` (same visual pattern, separate element).

### States

| State | Condition | Card shows |
| --- | --- | --- |
| signed out | `account_id` null/empty | muted line "Sign in to The Bazaar to link"; no field/button |
| unlinked / idle | logged in, no local hint | identity line, why-line, paste field, Link button, hint line |
| busy | redeem in flight | field + button disabled, button → "Linking…", banner Pending |
| linked | `200` or local hint set | collapsed to "Linked as @name" badge + quiet "Re-link" |
| error invalid/expired | `400 invalid_or_expired` | form stays, banner Failure, Link re-enabled |
| error already-linked | `409 already_linked` | form stays, banner Failure, **permanent** (no retry) |
| error server | `500` | form stays, banner transient-tone, Link re-enabled |
| offline | transport exception | form stays, banner "Could not reach BazaarDB" |

### Flow

1. Panel opens → cache `account_id` + display name + the local "linked" hint.
2. Signed out → render the non-interactive "sign in" line.
3. Unlinked → render the form. Field: `maxLength=10`, `selectAllOnFocus=true`,
   `isDelayed=true`, `minWidth=0` so it shares the row; placeholder "Paste your
   link code". Link button enabled once the trimmed value is non-empty.
4. User pastes the code and presses Enter (`KeyDownEvent`, `KeyCode.Return`,
   `PreventDefault`) or clicks Link.
5. View invokes a new `_linkBazaarDbAccount(code)` callback → coordinator action.
6. On `200`: persist the local hint, collapse to the badge, clear the field.
   On error: map per the table; keep the pasted code so the user can fix it.

### Copy (en / zh-CN / zh-Hant)

`{name}` is `TryGetProfileDisplayUsername()`. Short labels carry no trailing
period.

| Key | en | zh-CN | zh-Hant |
| --- | --- | --- | --- |
| `card.title` | Link BazaarDB account | 绑定 BazaarDB 账号 | 綁定 BazaarDB 帳號 |
| `card.why` | See your runs and stats on bazaardb.gg | 绑定后在 bazaardb.gg 查看你的对局与战绩 | 綁定後在 bazaardb.gg 查看你的對局與戰績 |
| `identity.loggedIn` | This account: @{name} | 当前账号：@{name} | 當前帳號：@{name} |
| `identity.signedOut` | Sign in to The Bazaar to link | 登录《The Bazaar》后即可绑定 | 登入《The Bazaar》後即可綁定 |
| `code.placeholder` | Paste your link code | 粘贴绑定码 | 貼上綁定碼 |
| `code.hint` | Get a code at bazaardb.gg · valid 10 min · case-sensitive | 前往 bazaardb.gg 获取绑定码 · 10 分钟有效 · 区分大小写 | 前往 bazaardb.gg 取得綁定碼 · 10 分鐘有效 · 區分大小寫 |
| `button.link` | Link | 绑定 | 綁定 |
| `button.linking` | Linking… | 绑定中… | 綁定中… |
| `linked.badge` | Linked as @{name} | 已绑定：@{name} | 已綁定：@{name} |
| `linked.relink` | Re-link | 重新绑定 | 重新綁定 |
| `status.success` | Linked as @{name} | 已绑定为 @{name} | 已綁定為 @{name} |
| `err.empty` | Enter your link code | 请输入绑定码 | 請輸入綁定碼 |
| `err.invalidOrExpired` | Code invalid or expired — generate a new one | 绑定码无效或已过期，请重新生成 | 綁定碼無效或已過期，請重新產生 |
| `err.alreadyLinked` | This game account is already linked to another BazaarDB user | 该游戏账号已绑定到其他 BazaarDB 用户 | 該遊戲帳號已綁定到其他 BazaarDB 使用者 |
| `err.server` | BazaarDB is unavailable — try again | BazaarDB 暂时不可用，请稍后重试 | BazaarDB 暫時無法使用，請稍後重試 |
| `err.offline` | Could not reach BazaarDB — check your connection | 无法连接 BazaarDB，请检查网络 | 無法連線 BazaarDB，請檢查網路 |

## Error mapping

| Response | Card behavior | Local hint | Recovery |
| --- | --- | --- | --- |
| `200 {linked:true}` | collapse to badge, `status.success` (Success) | set | none |
| `400 invalid_or_expired` | banner `err.invalidOrExpired` (Failure), form stays | — | paste a fresh code |
| `409 already_linked` | banner `err.alreadyLinked` (Failure), **no retry** | — | unlink on bazaardb.gg |
| `400 Missing …` | client guard `err.empty`, never POSTed | — | paste code / sign in |
| `500 Redeem failed` | banner `err.server` (transient tone), Link re-enabled | — | retry |
| transport / offline | banner `err.offline` | — | retry online |
| `account_id` null at submit | revert to `identity.signedOut`, no POST | — | sign in |

Note: `OperationCanceledException` (panel closed / account switched mid-request)
is silently dropped via the session-version guard — no banner.

## Coordinator action

Add `TryRedeemBazaarDbAccountAsync(string code)` to `HistoryPanelCoordinator`,
cloning the structure of `TryCheckServerHealthAsync` verbatim: re-entry guard →
in-progress flag → Pending banner → snapshot `_session.Version` → `await` the
client → on `OperationCanceledException`/`Exception` check `_session.IsCurrent`
and bail → on result, check `IsCurrent`, map to Success/Failure banner + persist
hint → `_requestUiRefresh()`. [`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs:477-554`](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)

Before the POST: resolve `account_id` via
`BppClientCacheBridge.TryGetProfileAccountId()`; if null/empty, short-circuit to
the signed-out state. If the trimmed code is empty, surface `err.empty` and don't
POST.

## Transport: `BazaarDbLinkClient`

New `src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs`, modeled on
`BazaarDbSnapshotClient` (readonly-struct result with permanent/transient split).
[`src/BazaarPlusPlus.ModApi/Clients/BazaarDbSnapshotClient.cs:11-75`](../../src/BazaarPlusPlus.ModApi/Clients/BazaarDbSnapshotClient.cs)

- Constructed with a **fixed** `Uri("https://bazaardb.gg/api/profile/link/redeem")`
  — **not** `ModApiRoutes` (that's `mod-api-v4` only). No auth header.
- Reuse `ModApiJsonPost.PostJsonAsync<T>` + `ModApiSerialization.SerializerSettings`
  (snake_case → emits `account_id`). [`src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs:16-38`](../../src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs)
- **Trap:** `ModApiJsonPost` returns `SuccessResult(statusCode)` on 2xx and reads
  the body **only on failure**. For this contract that's fine (status `200` =
  linked is the whole success signal), but the client must read the **error**
  body to split `400 invalid_or_expired` from `409 already_linked` — use
  `ModApiErrorFormatter`/the raw `FailureBody` to branch on the `error` string.
  [`src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs:17-34`](../../src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs)
- Map: `200` → linked; `409` and `400 invalid_or_expired`/`Missing…` →
  permanent; `500`/network → transient.
- **HttpClient:** reuse the shared online `HttpClient` (it's a generic client;
  only `ModApiRoutes` is v4-specific) or spin a dedicated one via
  `BppHttpClientFactory.Create(version, userAgentSuffix: "BazaarDbLink")` as the
  snapshot feed does. [`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadFeed.cs:43-59`](../../src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadFeed.cs)

Payload DTO: keep the graph `public` (MessagePack/Mono trap, even though this is
JSON — match the repo convention).

## Local "linked" hint

Account-scoped `PlayerPrefs`, copying `CollectionPanelHeroPreferenceStore`'s
key-scoping (account id → username fallback). [`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs:50-76`](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs)

- Stores only a boolean + the display name, **purely as a UX hint** to skip
  re-prompting — never presented as authoritative (no read-back exists).
- Set on `200`; read on panel open; re-scoped on account switch.
- The "Re-link" affordance always stays. A stale hint surfaces honestly as a
  `409` if the server disagrees.

## Localization

New partial `HistoryPanelText.AccountLink.cs` (following `HistoryPanelText.Chrome.cs`):
`LocalizedTextSet` constants + `internal static string` accessors for every copy
key, resolved through `L.Resolve(...)`. zh-Hant via the third `LocalizedTextSet`
arg where it differs from zh-CN.

## Implementation checklist

1. `BazaarDbLinkClient` + `BazaarDbLinkResult` struct + request DTO (ModApi).
2. Wire its `HttpClient`/instance into composition (`Plugin.BuildOnlineServices`),
   pass to `HistoryPanelMount` alongside the existing online client.
3. `HistoryPanelState` fields: `AccountLinkInProgress`, `CachedAccountId`,
   `CachedDisplayName`, `LocalLinkedHint`, `PendingLinkCode`, inline banner
   text/severity pair.
4. `TryRedeemBazaarDbAccountAsync` on the coordinator.
5. `BuildAccountLinkCard` in the rail + the first `TextField` in the codebase
   (mind the `minWidth=0` shrink and `isDelayed` traps).
6. Extend `HistoryPanelUiToolkitModel` + `BuildUiModel` + `Refresh()` to drive
   the card from state each pass; add the `_linkBazaarDbAccount` view callback.
7. `BazaarDbAccountLinkStore` (account-scoped `PlayerPrefs`).
8. `HistoryPanelText.AccountLink.cs` copy.
9. Testable core (client + coordinator action) lands first, game-free, behind a
   per-feature test project; the card wiring is verified in-game.

## Decisions

- **Transport — direct to bazaardb.gg.** Confirmed 2026-06-24. Matches the
  guide's "any app can implement it" framing; no server change/deploy.
- **Placement — collapsing link card.** Confirmed 2026-06-24.

## Alternatives considered (rejected)

Three concepts were scored adversarially; the link card won on discoverability
while folding in the strip's collapse-to-badge idea.

- **Quiet identity strip** — a single muted line that expands inline. Lowest
  footprint but discoverability ~2/10 (invisible by design). Rejected as primary;
  its collapse-to-badge behavior was adopted into the card's linked state.
- **Settings-dock entry + overlay** — a dock row that opens a small overlay, with
  a one-line nudge in the panel. Most consistent with the existing snapshot-upload
  opt-in, but the dock row physically can't host a `TextField`
  (`BppSettingsDockDefinition` is toggle/activate-wired), so it becomes two
  surfaces. Rejected for v1; a dock entry remains a possible v2 secondary entry.
- **Proxy via `mod-api-v4`** — add `/bazaardb/link/redeem` to
  `bazaarplusplus-server` and own the contract. More robust (team-owned error
  shape, retries, logging) but requires a server endpoint + deploy. Rejected for
  v1 per the direct-transport decision; revisit if the third-party contract
  proves unstable.
