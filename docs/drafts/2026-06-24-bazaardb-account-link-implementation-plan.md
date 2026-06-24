# BazaarDB Account Link — Implementation Plan

Buildable plan for the History Panel account-link card. The UX rationale and
state mockup live in the companion design doc
[`2026-06-24-bazaardb-account-link-history-panel.md`](./2026-06-24-bazaardb-account-link-history-panel.md);
this doc is the *how* — file-by-file, sequenced into commits, with the code that
encodes the non-obvious traps.

Status: plan only, no code written. Confirmed decisions: **direct POST to
bazaardb.gg**, **collapsing link card in the History Panel rail**. All `file:line`
anchors below were verified against live code; the UITK TextField API was verified
by compiling a probe against the game's `UnityEngine.UIElementsModule.dll`.

## Scope

A player pastes a one-time code from bazaardb.gg into a card in the History Panel
rail. The mod POSTs `{ code, account_id }` to
`https://bazaardb.gg/api/profile/link/redeem`. `account_id` is read invisibly
(`BppClientCacheBridge.TryGetProfileAccountId()` — the same id sent on uploads);
the player only supplies the code. On success the card collapses to a quiet
"Linked as @name" badge.

Out of scope: any server-side change (we call the third-party host directly); an
unlink flow (no endpoint exists); a settings-dock secondary entry (dropped for v1).

## Decision register (reconciled across the drill drafts)

These resolve conflicts between the per-area drafts; the plan below follows them.

1. **`409 already_linked` is a FAILURE, not success.** The integration guide
   defines `409` as "that game account is already linked to a **different**
   BazaarDB user." It must render a red Failure banner, must **not** set the
   local linked hint, and must **not** collapse to the badge. (One drill draft
   optimistically treated 409 as terminal-linked — that is wrong and is corrected
   here.) Only `200` sets the hint and collapses.
2. **One result type, `Outcome`-based.** `BazaarDbLinkResult` exposes a
   `BazaarDbLinkOutcome` enum (`Linked / InvalidOrExpired / AlreadyLinked /
   MissingFields / ServerError / Transport`) plus `StatusCode` and `Error`
   (formatted detail, for logging). The coordinator switches on `Outcome`.
3. **Dedicated bare `HttpClient`, not the shared online client.** The redeem
   call must carry **no auth header** (the code is the credential) and hit a
   different host. Build a fresh `HttpClient` via
   `BppHttpClientFactory.Create(version, userAgentSuffix: "BazaarDbLink")` rather
   than reusing `ModOnlineClient.HttpClient`, so no mod-api-v4 header/BaseAddress
   can leak onto bazaardb.gg.
4. **Client is identity-agnostic; the coordinator resolves identity.**
   `RedeemAsync(code, accountId, ct)` takes the id as a parameter. No
   resolver-`Func` is threaded through mount/factory (one drill proposed that;
   it is unnecessary). The coordinator calls `BppClientCacheBridge` directly, like
   `GhostBattleSyncService` does.
5. **Endpoint URL has one source of truth.** A
   `public const string BazaarDbLinkClient.DefaultRedeemEndpoint =
   "https://bazaardb.gg/api/profile/link/redeem"` so composition and tests share it.
6. **Trim only, never transform the code.** The alphabet
   `ABCDEFGHJKMNPQRSTUVWXYZ23456789` is case-sensitive; uppercasing corrupts input.
   Enforced in three places (view value-changed callback, view submit, client).

## Verified facts the code must respect

- `ModApiJsonPost.PostJsonAsync` returns `SuccessResult(statusCode)` on 2xx
  **without reading the body**, and reads `FailureBody` only on non-2xx.
  [`ModApiJsonPost.cs:16-38`](../../src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs)
  ⇒ treat any 2xx as `Linked`; branch the error body on non-2xx. (If bazaardb.gg
  could ever return `200 {linked:false}`, the client would need to bypass this
  helper and read the 2xx body — see open questions.)
- `ModApiErrorFormatter.FormatHttpFailure(int, string)` and `Truncate(string)`
  both exist and are `public static`. [`ModApiErrorFormatter.cs:9,17`](../../src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs)
- **UITK TextField (Unity 6000.x, compile-probed):** `maxLength`, `isDelayed`,
  `selectAllOnFocus` are direct setters (no CS0618). Placeholder **must** be
  `tf.textEdition.placeholder = "…"` — `tf.placeholderText` is CS1061 in this
  build. `tf.textEdition.hidePlaceholderOnFocus = true` works.
- **`evt.PreventDefault()` is CS0618-obsolete** in this Unity build → use
  `evt.StopPropagation()`.
- `LocalizedTextSet(en, zhCN, zhHant)` — zh-Hant is the **positional 3rd arg**
  (do not confuse with the 6-arg ctor whose 3rd arg is German).
  [`LocalizedTextSet.cs:11`](../../src/BazaarPlusPlus.Localization/LocalizedTextSet.cs)
- Coordinator async template to clone: `TryCheckServerHealthAsync`
  [`HistoryPanelCoordinator.cs:477-554`](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs).
  Session guard: `_session.Version` / `.Token` / `.IsCurrent(v)`
  [`HistoryPanelSessionScope.cs:15-48`](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelSessionScope.cs).
- Coordinator ctor takes everything off `dependencies` (no probe/client args in the
  signature) — the link client rides on `HistoryPanelDependencies`, so the ctor
  *signature* and its call site in `HistoryPanel.cs` are unchanged.

---

## Phase 1 — Transport (ModApi, game-free, unit-testable)

New `src/BazaarPlusPlus.ModApi/Models/BazaarDbProfileLinkRedeemRequest.cs`:

```csharp
#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

public sealed class BazaarDbProfileLinkRedeemRequest
{
    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("account_id")]
    public string AccountId { get; set; } = string.Empty;
}
```

New `src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs` — the reconciled
`Outcome` result + the verified classification logic:

```csharp
#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.ModApi.Clients;

public enum BazaarDbLinkOutcome
{
    Linked,          // 200
    InvalidOrExpired,// 400 invalid_or_expired
    AlreadyLinked,   // 409 already_linked — bound to a DIFFERENT BazaarDB user
    MissingFields,   // 400 Missing code or account_id
    ServerError,     // 5xx
    Transport,       // network/DNS/TLS/timeout
}

public readonly struct BazaarDbLinkResult
{
    private BazaarDbLinkResult(BazaarDbLinkOutcome outcome, int? statusCode, string? error)
    {
        Outcome = outcome;
        StatusCode = statusCode;
        Error = error;
    }

    public BazaarDbLinkOutcome Outcome { get; }
    public int? StatusCode { get; }
    public string? Error { get; }

    public bool Succeeded => Outcome == BazaarDbLinkOutcome.Linked;

    public static BazaarDbLinkResult Linked() => new(BazaarDbLinkOutcome.Linked, 200, null);
    public static BazaarDbLinkResult From(BazaarDbLinkOutcome o, int? status, string? error) =>
        new(o, status, error);
}

/// <summary>
/// Redeems a one-time BazaarDB profile-link code. Posts to a FIXED full URI (bazaardb.gg),
/// no auth header. Code is trimmed only (case-sensitive alphabet). 409 == already linked to a
/// DIFFERENT BazaarDB user and is permanent.
/// </summary>
public sealed class BazaarDbLinkClient
{
    public const string DefaultRedeemEndpoint = "https://bazaardb.gg/api/profile/link/redeem";

    private readonly HttpClient _httpClient;
    private readonly Uri _redeemEndpoint;

    public BazaarDbLinkClient(HttpClient httpClient, Uri redeemEndpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _redeemEndpoint = redeemEndpoint ?? throw new ArgumentNullException(nameof(redeemEndpoint));
    }

    public async Task<BazaarDbLinkResult> RedeemAsync(
        string code, string accountId, CancellationToken cancellationToken)
    {
        var trimmedCode = code?.Trim() ?? string.Empty; // Trim ONLY — case-sensitive alphabet.
        if (trimmedCode.Length == 0 || string.IsNullOrWhiteSpace(accountId))
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.MissingFields, 400, "missing_field");

        var payload = new BazaarDbProfileLinkRedeemRequest { Code = trimmedCode, AccountId = accountId };
        try
        {
            var result = await ModApiJsonPost
                .PostJsonAsync(_httpClient, _redeemEndpoint.AbsoluteUri, payload, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess) // 2xx body is not read; contract: 200 == linked.
                return BazaarDbLinkResult.Linked();

            return Classify(result.StatusCode, result.FailureBody!);
        }
        catch (OperationCanceledException)
        {
            throw; // matches the health/snapshot convention; coordinator owns the cancel.
        }
        catch (Exception ex)
        {
            return BazaarDbLinkResult.From(
                BazaarDbLinkOutcome.Transport, null, ModApiErrorFormatter.Truncate(ex.Message));
        }
    }

    private static BazaarDbLinkResult Classify(int statusCode, string failureBody)
    {
        var error = ExtractErrorCode(failureBody);                 // {"error":"…"} or null
        var detail = ModApiErrorFormatter.FormatHttpFailure(statusCode, failureBody);

        // Branch on BOTH the error string and the status — bazaardb.gg is unverified, so keep
        // the status fallback if the body shape differs.
        if (error == "already_linked" || statusCode == 409)
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.AlreadyLinked, statusCode, detail);
        if (error == "invalid_or_expired")
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.InvalidOrExpired, statusCode, detail);
        if (error != null && error.StartsWith("Missing", StringComparison.OrdinalIgnoreCase))
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.MissingFields, statusCode, detail);
        if (statusCode >= 500)
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.ServerError, statusCode, detail);
        // Any other 4xx: permanent-ish; surface as InvalidOrExpired (closest user-actionable copy).
        return BazaarDbLinkResult.From(BazaarDbLinkOutcome.InvalidOrExpired, statusCode, detail);
    }

    private static string? ExtractErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var error = Newtonsoft.Json.Linq.JObject.Parse(body)["error"]?.Value<string>()?.Trim();
            return string.IsNullOrWhiteSpace(error) ? null : error;
        }
        catch (Newtonsoft.Json.JsonException) { return null; }
    }
}
```

New `tests/ModApi.Tests/BazaarDbLinkClientTests.cs` — **exe-runner** shape (this
project is `OutputType=Exe` + `Program.cs`, run with `dotnet run`, not
`dotnet test`). Copy the `RecordingHandler` + private `Assert` idiom from
`BazaarDbSnapshotClientTests.cs`. Cases:

- `200` → `Outcome == Linked`; assert the wire body has `code` **case-preserved**
  (`"AbcD23"`, not `"ABCD23"`) and snake_case `account_id` — proves the load-bearing
  case-sensitivity rule.
- `400 {"error":"invalid_or_expired"}` → `InvalidOrExpired`.
- `409 {"error":"already_linked"}` → `AlreadyLinked`.
- `500 {"error":"Redeem failed"}` → `ServerError`.
- empty code → `MissingFields` with **zero** HTTP calls (assert `handler.Requests.Count == 0`).

Edit `tests/ModApi.Tests/Program.cs` — add `BazaarDbLinkClientTests.Run();` before
the final `Console.WriteLine`. (The csproj needs no edit — default compile glob.)

**Phase-1 gate:** `dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj`.
Grep the full output for `Failed`/the per-runner failure lines (per the repo trap,
don't tail-truncate). Commit: *"Add BazaarDbLinkClient for profile-link redeem"*.

## Phase 2 — Persistence + localization (game-coupled, no UI)

New `src/BazaarPlusPlus/Game/HistoryPanel/AccountLink/BazaarDbAccountLinkStore.cs`
— account-scoped `PlayerPrefs`, key idiom copied verbatim from
`CollectionPanelHeroPreference.BuildPrefsKey` (`{prefix}.{anonymous |
Uri.EscapeDataString(scope)}`). UX-hint only (no read-back exists). API:

```csharp
public void SaveHint(string accountId, string? displayName); // on 200 only
public bool TryLoadHint(string accountId, out string? displayName);
public void Clear(string accountId);                          // for "Re-link"
```

Prefix `"BPP.HistoryPanel.BazaarDbLinkedName"` (distinct from CollectionPanel's).
Stores the **game** display name as the badge label (see open question on naming).

New `src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.AccountLink.cs` —
a partial of `HistoryPanelText` exposing a nested `AccountLink` static class.
`LocalizedTextSet(en, zhCN, zhHant)` constants + accessors; `{0}` keys
(`LinkedAs`, `Identity`) resolve the template then `string.Format` at the accessor
(matching the resolve-then-interpolate idiom in `HistoryPanelText.Shared.cs`). Keys
and copy come from the design doc's copy table. v1 ships en/zh-CN/zh-Hant; other
locales fall back to English (open question on matching the snapshot feature's
6-locale coverage).

**Phase-2 gate:** `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` (Debug;
from a worktree add `-p:BPPInstallerSourcePath=<abs>`). Commit: *"Add account-link
local hint store and localized copy"*.

## Phase 3 — State + coordinator action + inline banner

`HistoryPanelState.cs` — add after `ServerHealthProbeInProgress` ([:67](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelState.cs)):

```csharp
public bool AccountLinkInProgress { get; set; }
public string? CachedAccountId { get; set; }
public string? CachedDisplayName { get; set; }
public bool LocalLinkedHint { get; set; }
public bool AccountLinkExpanded { get; set; }
public string? AccountLinkBannerMessage { get; set; }   // SEPARATE from StatusMessage
public StatusSeverity AccountLinkBannerSeverity { get; set; }
```

`HistoryPanelCoordinator.cs` edits:

- Fields after `_session` ([:23](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)):
  `private readonly BazaarDbLinkClient? _linkClient;` and
  `private readonly BazaarDbAccountLinkStore _accountLinkStore = new();`
- Ctor body after `_serverHealthProbe = …` ([:39](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)):
  `_linkClient = dependencies.AccountLinkClient;`
- `OnPanelShown` ([:54](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)):
  call `CacheAccountLinkIdentity();` after `_session.Begin()`.
- `OnPanelHidden` ([~:62](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)):
  add `_state.AccountLinkInProgress = false;` alongside the other in-progress resets
  (a redeem that outlives a panel-hide otherwise wedges the re-entry guard).
- `SubmitAccountLinkCode(string?)`, `ToggleAccountLinkExpanded()`,
  `CacheAccountLinkIdentity()`, `SetAccountLinkBanner(string?, StatusSeverity)`
  (writes only the `AccountLink*` pair — never `StatusMessage`), and the action
  below, inserted after `TryCheckServerHealthAsync` ([:554](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)).

The action — clones the health template, with the **corrected** `Outcome` mapping
(200 collapses; **409 is a Failure that does NOT collapse or set the hint**):

```csharp
public async Task TryRedeemBazaarDbAccountAsync(string code)
{
    if (_state.AccountLinkInProgress)
    {
        SetAccountLinkBanner(HistoryPanelText.AccountLink.AlreadyRunning(), StatusSeverity.Neutral);
        _requestUiRefresh(); return;
    }

    var accountId = _state.CachedAccountId ?? BppClientCacheBridge.TryGetProfileAccountId();
    _state.CachedAccountId = accountId;
    if (string.IsNullOrWhiteSpace(accountId))
    { SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure); _requestUiRefresh(); return; }
    if (string.IsNullOrEmpty(code))
    { SetAccountLinkBanner(HistoryPanelText.AccountLink.EmptyCode(), StatusSeverity.Failure); _requestUiRefresh(); return; }
    if (_linkClient == null)
    { SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure); _requestUiRefresh(); return; }

    _state.AccountLinkInProgress = true;
    SetAccountLinkBanner(HistoryPanelText.AccountLink.Linking(), StatusSeverity.Pending);
    _requestUiRefresh();

    var sessionVersion = _session.Version;
    BazaarDbLinkResult result;
    try { result = await _linkClient.RedeemAsync(code, accountId!, _session.Token); }
    catch (OperationCanceledException)
    {
        if (!_session.IsCurrent(sessionVersion)) return;
        _state.AccountLinkInProgress = false; SetAccountLinkBanner(null, StatusSeverity.Neutral); _requestUiRefresh(); return;
    }
    catch (Exception ex)
    {
        if (!_session.IsCurrent(sessionVersion)) return;
        _state.AccountLinkInProgress = false;
        SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
        BppLog.Error("HistoryPanel", "Failed to redeem BazaarDB link code", ex); _requestUiRefresh(); return;
    }

    if (!_session.IsCurrent(sessionVersion)) return;
    _state.AccountLinkInProgress = false;

    switch (result.Outcome)
    {
        case BazaarDbLinkOutcome.Linked:
            _state.LocalLinkedHint = true;
            _state.AccountLinkExpanded = false;
            _accountLinkStore.SaveHint(accountId!, _state.CachedDisplayName);
            SetAccountLinkBanner(
                HistoryPanelText.AccountLink.LinkedAs(_state.CachedDisplayName ?? string.Empty),
                StatusSeverity.Success);
            BppLog.Info("HistoryPanel", $"BazaarDB link redeemed account={accountId}");
            break;

        case BazaarDbLinkOutcome.AlreadyLinked:   // CORRECTED: different user → failure, no hint, no collapse
            SetAccountLinkBanner(HistoryPanelText.AccountLink.AlreadyLinked(), StatusSeverity.Failure);
            break;
        case BazaarDbLinkOutcome.InvalidOrExpired:
        case BazaarDbLinkOutcome.MissingFields:   // view guards empty; defensive only
            SetAccountLinkBanner(HistoryPanelText.AccountLink.InvalidOrExpired(), StatusSeverity.Failure);
            break;
        case BazaarDbLinkOutcome.ServerError:
            SetAccountLinkBanner(HistoryPanelText.AccountLink.ServerBusy(), StatusSeverity.Failure);
            break;
        default: // Transport
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            break;
    }
    _requestUiRefresh();
}
```

`HistoryPanelDependencies.cs` — add `public BazaarDbLinkClient? AccountLinkClient
{ get; }` as a new (nullable) last ctor param + assignment, mirroring
`ServerHealthProbe`; forward `null` from the delegating 4-arg overload.

`HistoryPanelController.cs` — add a `SubmitAccountLinkCode(string?)` wrapper beside
`TryCheckServerHealth()` ([:101](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelController.cs)):
`_coordinator?.SubmitAccountLinkCode(code);`.

**Phase-3 gate:** project compiles once Phase 4 supplies `AccountLinkClient` at the
factory (land Phases 3–5 together, or stub the dependency). Commit (with Phase 4):
*"Wire BazaarDB redeem action through the History Panel coordinator"*.

## Phase 4 — Composition wiring (dedicated HttpClient)

`Plugin.cs` `BuildOnlineServices` ([:131-147](../../src/BazaarPlusPlus/Plugin.cs)) —
after the online client, build a **dedicated bare** client and keep a field:

```csharp
var linkHttpClient = BppHttpClientFactory.Create(
    productVersion, userAgentSuffix: "BazaarDbLink", timeout: /* default */ null);
_bazaarDbLinkClient = new BazaarDbLinkClient(
    linkHttpClient, new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint));
```

Thread it as a `Func<BazaarDbLinkClient?>` into `HistoryPanelMount` (new ctor param,
mirroring `Func<ModOnlineClient?> onlineClient`), then through
`HistoryPanelFactory.Create(runtime, onlineClient, accountLinkClient)` into the
`HistoryPanelDependencies` ctor's new `AccountLinkClient` arg. The mount is the
GameInterop-aware seam; no resolver `Func`s are needed (decision 4). The coordinator
ctor signature and `HistoryPanel.cs` are unchanged (client rides on `dependencies`).

**Phase-4 gate:** `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` Debug —
the only project referencing HistoryPanel + GameInterop + ModApi; a clean compile
proves the Mount → Factory → Dependencies → Coordinator threading.

## Phase 5 — View: model + card + Refresh

`HistoryPanel.UiToolkit.cs` — add to `HistoryPanelUiToolkitModel` ([~:291](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs)):
`IsBazaarDbLinked`, `AccountLinkedBadgeText`, `AccountIdentityText`,
`AccountLinkButtonText`, `AccountLinkButtonEnabled`, `AccountLinkInputEnabled`,
`AccountLinkBannerText`, `AccountLinkBannerSeverity`. Populate them in `BuildUiModel`
from state (view does no formatting):

```csharp
IsBazaarDbLinked      = state.LocalLinkedHint && !state.AccountLinkExpanded,
AccountLinkedBadgeText= HistoryPanelText.AccountLink.LinkedAs(state.CachedDisplayName ?? ""),
AccountIdentityText   = string.IsNullOrWhiteSpace(state.CachedAccountId)
                          ? HistoryPanelText.AccountLink.SignedOut()
                          : HistoryPanelText.AccountLink.Identity(state.CachedDisplayName ?? ""),
AccountLinkButtonText = state.AccountLinkInProgress
                          ? HistoryPanelText.AccountLink.Linking()
                          : HistoryPanelText.AccountLink.Button(),
AccountLinkButtonEnabled = !state.AccountLinkInProgress && !string.IsNullOrWhiteSpace(state.CachedAccountId),
AccountLinkInputEnabled  = !state.AccountLinkInProgress && !string.IsNullOrWhiteSpace(state.CachedAccountId),
AccountLinkBannerText    = state.AccountLinkBannerMessage,
AccountLinkBannerSeverity= state.AccountLinkBannerSeverity,
```

`HistoryPanelUiToolkitView.cs` — add the 8 element-handle fields; append an
`Action<string> linkBazaarDbAccount` ctor param + null-guarded assignment (mirrors
`_setRunHero`); add the card-drive block in `Refresh()` after the status-label block
([:212](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs))
(see the design doc / drill draft — drives text/visibility/severity, constructs
nothing, and **never writes `_accountCodeField.value`** to avoid clobbering typing).

`HistoryPanelUiToolkitView.Tree.cs` — `BuildAccountLinkCard(rail);` between
`rail.Add(_subtitle)` ([:187](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs))
and `BuildFilterSlot(rail)` ([:189](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)),
plus the `BuildAccountLinkCard` method. The TextField — the first in the codebase —
encodes every verified trap:

```csharp
_accountCodeField = new TextField();
_accountCodeField.maxLength = 10;
_accountCodeField.isDelayed = true;
_accountCodeField.selectAllOnFocus = true;
_accountCodeField.textEdition.placeholder = HistoryPanelText.AccountLink.Placeholder(); // NOT placeholderText
_accountCodeField.textEdition.hidePlaceholderOnFocus = true;
_accountCodeField.style.flexGrow = 1f;
_accountCodeField.style.flexShrink = 1f;
_accountCodeField.style.minWidth = 0f;                 // row/ScrollView flex trap guard
_accountCodeField.style.height = Sizes.ButtonStandardHeight;
_accountCodeField.RegisterValueChangedCallback(evt =>  // TRIM ONLY — case-sensitive alphabet
{
    var trimmed = evt.newValue?.Trim() ?? string.Empty;
    if (!string.Equals(trimmed, evt.newValue, System.StringComparison.Ordinal))
        _accountCodeField!.SetValueWithoutNotify(trimmed); // re-entrancy-safe
});
_accountCodeField.RegisterCallback<KeyDownEvent>(evt =>
{
    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
    evt.StopPropagation();                              // PreventDefault() is CS0618 here
    SubmitAccountLink();
});
```

The Link button uses `CreateButton(..., fixedWidth:false)` then **forces
`flexGrow=0; flexShrink=0; flexBasis=Auto; minWidth=…`** so it doesn't eat the
field's width in the row (the button-in-flex trap). Card styled like
`selectedDetailCard` (`HistoryFooterBackground`, `Radii.Md`, thin border,
`UiSpacing.Lg` padding); inline `_accountBanner` cloned from the `StatusLabel`
pattern ([Tree.cs:333-349](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)).

`HistoryPanel.UiToolkit.cs` `EnsureUi` — pass the new callback to the view ctor
(wired to `HistoryPanelController.SubmitAccountLinkCode`). **The ctor-arity change
and this call site must land in the same commit** or it's a compile error.

**Phase-5 gate:** `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` clean
(catches `placeholder` vs `placeholderText`, the ctor-arity ripple, missing model
fields). Then `./run.sh format` and keep the reflow in the same commit.

## Phase 6 — In-game verification

`./run.sh build` (Debug auto-copies to `BepInEx/plugins`), launch via Steam
(`open "steam://run/1617400"`), open History (F8). Verify, against `LogOutput.log`
(`[BPP][HistoryPanel]`):

1. Card renders between the supporter subtitle and the Runs/Battles tabs.
2. Signed-out (no profile) → "Sign in to The Bazaar to link"; field disabled.
3. Paste a **real** code → success → collapses to "Linked as @name"; reopen panel
   → still collapsed (hint persisted).
4. Bad/expired code → red "Code invalid or expired".
5. A code already bound to another BazaarDB user → red "already linked to a
   different user"; **no** collapse, **no** hint.
6. Pull network → "Could not reach BazaarDB"; field stays usable.

This is also the **contract verification spike** (decision register / open
questions): capture the real status + body for each case and reconcile the
`Classify` branch + copy if bazaardb.gg differs from the guide.

## Test & verification matrix

| Layer | How | Why not more |
| --- | --- | --- |
| `BazaarDbLinkClient` | exe-runner unit tests (Phase 1) | pure, game-free; the `Outcome` mapping + case-preservation are the load-bearing logic |
| coordinator mapping | covered indirectly by client tests + in-game | `BppClientCacheBridge`/`PlayerPrefs` aren't mockable here; don't build a fake-Unity harness |
| store / i18n | none (match untested prior art) | copy + PlayerPrefs; no coverage-theater (repo rule) |
| view / card | in-game smoke (Phase 6) | UITK needs a live `PanelSettings`; not unit-testable |

## Risk register

- **Unverified third-party contract.** All status/error strings are spec. The
  `Classify` switch branches on *both* the `error` string and the status code so a
  shape mismatch still classifies correctly for retry decisions; Phase 6 pins it.
- **`200 {linked:false}` blind spot.** `ModApiJsonPost` discards the 2xx body, so
  any 2xx reads as linked. Acceptable per the contract (200 == ok); if that ever
  changes, the client must read the 2xx body itself.
- **Stale local hint.** No read-back exists; the badge can claim "Linked" when the
  server disagrees. Mitigated by always offering "Re-link" (clears the hint +
  reopens the form). A genuine re-link may legitimately `409`.
- **Header leak.** Decision 3 (dedicated bare `HttpClient`) prevents any mod-api-v4
  auth/UA/BaseAddress from reaching bazaardb.gg.
- **Timeout semantics.** `HttpClient.Timeout` surfaces as `TaskCanceledException`
  (an `OperationCanceledException`) even when the caller token didn't fire; the
  client rethrows it (matching health/snapshot convention) and the coordinator's
  cancel arm clears the banner. If a timeout should instead be a retryable
  `Transport`, inspect `cancellationToken.IsCancellationRequested` before rethrow.
- **First TextField in the codebase.** API is Unity-version-sensitive
  (`textEdition.placeholder`); re-probe if the game updates Unity.

## Open decisions still needing a human

1. **Badge name source.** The 200 body isn't read and the contract doesn't
   guarantee a BazaarDB display name, so "Linked as @name" uses the **game**
   display name (`TryGetProfileDisplayUsername()`). Acceptable? Or drop the name →
   "Linked to BazaarDB"? (Plan assumes the former.)
2. **Re-link affordance.** With no unlink endpoint, "Re-link" only clears the
   **local** hint and reopens the form (server still enforces first-link-wins +
   cooldown). Confirm that's the intended meaning vs hiding it entirely.
3. **Locale coverage.** v1 ships en/zh-CN/zh-Hant (English fallback elsewhere); the
   sibling BazaarDB-upload label ships 6 locales. Match it, or accept the fallback?
4. **Timeout → Transport vs rethrow** (see risk register).

## Commit sequence

1. Phase 1 — `Add BazaarDbLinkClient for profile-link redeem` (+ tests)
2. Phase 2 — `Add account-link local hint store and localized copy`
3. Phases 3–5 — `Add BazaarDB account-link card to the History Panel` (state +
   coordinator + wiring + view land together; the ctor-arity + model-field
   dependencies make a partial split non-compiling)
4. Phase 6 — verify in-game; reconcile copy/classification from the real contract;
   amend if needed.

Per repo wrap-up: review the diff, commit, merge the working branch to `master`,
push, delete merged branches. `Release Notes:` → `Added: Link your BazaarDB account
from the History Panel`.
