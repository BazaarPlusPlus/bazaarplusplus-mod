> **Status: ASPIRATIONAL - never implemented.** No rate-limit patch shipped and the primary beneficiary (AutoBazaar) is parked. Kept for the TargetMethod()-resilience reasoning.

# Bypass Game-Client HTTP Rate Limit Design

**Status:** Draft for plan authoring
**Date:** 2026-05-17
**Owner:** BazaarPlusPlus mod

## 1. Problem

The game's HTTP client (`Core.Data.Providers.HttpDataProvider`) self-throttles outbound requests per endpoint: any second call to the same endpoint within 1.0 s is denied locally and never reaches the network, returning a synthetic `HttpStatusCode.TooManyRequests`. The denial bubbles up through `Cmd.ExecuteAndProcess` into `NetworkManager.HandleCommandFailure`, which calls `PopupManager.ShowGenericPopup` with title **"Action Failed"** and body **"Too many requests were sent. Please wait a moment and try again."**.

For AutoBazaar agent flows — and for human clicks that happen to land on a same-endpoint sequence — this popup interrupts play even though no actual server load was generated.

Authoritative source-of-truth in the decompiled game code:
- `decompiled/TheBazaarRuntime/Core.Data.Providers/HttpDataProvider.cs:138-141` — denial site
- `decompiled/TheBazaarRuntime/Core.Data.Providers/HttpDataProvider.cs:229-265` — `IsRequestAllowed` logic
- `decompiled/TheBazaarRuntime/Networking/HttpGameClient.cs:490-491` — 429 → user message mapping
- `decompiled/TheBazaarRuntime/Networking/NetworkManager.cs:185-199` — popup rendering site

## 2. Goal

Make every user click and every AutoBazaar-dispatched action actually issue its network request, by neutralizing the client-side `IsRequestAllowed` gate. The popup will no longer fire for synthetic client-side denials, because the synthetic denials will no longer happen.

## 3. Non-goals

- **Not** suppressing server-returned 429s. If the real server responds 429, the existing "Action Failed" popup will still render. That is a legitimate signal (account-level rate limiting, server policy) and must remain visible.
- **Not** patching the `NetMessageError MessageId == "RateLimited"` channel (`decompiled/TheBazaarRuntime/TheBazaar/NetErrorHandler.cs`). That channel is server-pushed and unrelated.
- **Not** touching `Cmd.cs`'s `undoOnFailure` path. With no synthetic 429s, that path will no longer trigger for this reason.
- **Not** adding a config toggle. The bypass is unconditional per user decision during brainstorming (see §6).
- **Not** adding new tests. The change is a one-method Harmony Prefix returning a constant; there is no meaningful automated test seam, which `.rules` explicitly permits ("If there is no meaningful automated test seam for a change, it is acceptable to ship without adding a new test").

## 4. Design

### 4.1 Patch target

A single Harmony Prefix against:

```
Core.Data.Providers.HttpDataProvider.IsRequestAllowed(string endpoint) : bool
```

The Prefix sets `__result = true` and returns `false` to skip the original method body. As a consequence, `_requestDictionary` (the per-endpoint `RequestFrequency` accounting table) is never populated. Grep confirms `_requestDictionary` is referenced only inside `IsRequestAllowed` itself, so leaving it empty has no other observable effect.

The patch class uses the bare-`[HarmonyPatch]` + `private static MethodBase? TargetMethod()` resolution pattern already established in this codebase (see [CombatReplayVisualPatches.cs:27-49](../../../Patches/Combat/CombatReplayVisualPatches.cs:27)), with `AccessTools.Method` resolving the target by name. This makes the patch **resilient to game refactors**: if the target type or method is renamed in a future game patch, `TargetMethod()` returns `null`, Harmony silently skips this patch class, and the rest of the mod loads unaffected. The hard-bound form `[HarmonyPatch(typeof(T), "name")]` used elsewhere in the mod would throw and abort `PatchAll()` — unacceptable here because the target is a `private` game-internal method with a higher refactor probability than the stable public surfaces other patches hook.

### 4.2 File layout

| File | Status | Responsibility |
|---|---|---|
| `Patches/Networking/HttpRateLimitBypassPatch.cs` | **new** | Single `[HarmonyPatch]` class with `TargetMethod()` + one `[HarmonyPrefix]`. ~25 LOC, modeled on `Patches/Combat/CombatReplayVisualPatches.cs`'s `TargetMethod()` pattern. |

No other source file changes. No config changes. No test project changes.

### 4.3 Registration

`Plugin.cs` already calls `_harmony.PatchAll()` at startup, which auto-discovers every `[HarmonyPatch]` class in the assembly. The new file requires no manual registration.

### 4.4 Why Prefix-skip over Postfix-override

| Aspect | Prefix returning `false` (chosen) | Postfix overriding `__result` |
|---|---|---|
| `_requestDictionary` state | Stays empty (no entries ever created) | Continues to grow per endpoint; `CallCount` increments unboundedly within 60 s windows |
| Per-call overhead | Negligible — original body skipped | Full original body still executes |
| Risk if game code ever reads dictionary elsewhere | Empty state, easy to reason about | Inflated counters (`int` cannot overflow in practice) |

Prefix-skip is the cleaner choice given `_requestDictionary` has no other readers.

## 5. Risks and rollback

**R1 — Game patch / refactor renames `IsRequestAllowed` or moves it.**
`TargetMethod()` returns `null`, Harmony skips registering this patch class entirely, and the rest of the mod loads normally. The bypass silently lapses and the "Action Failed / Too many requests" popup returns to baseline game behavior. Detection is by user observation (popup reappears); fix is to update the type/method names inside `TargetMethod()` and rebuild. Without the `TargetMethod()` guard, the alternative — `[HarmonyPatch(typeof(HttpDataProvider), "IsRequestAllowed")]` — would throw inside `PatchAll()`, propagate out of `Plugin.Awake`'s try/catch as a rethrow (see [Plugin.cs:69-74](../../../Plugin.cs:69)), and disable the entire mod; we explicitly reject that failure mode.

**R2 — Server-side rate limiting catches up.**
The bypass does not raise total request volume — AutoBazaar's own 1.0 s `ActionMinDelaySeconds` still gates outbound action cadence. What changes is per-endpoint concurrency: requests that previously got bounced locally now reach the server, which may reject some itself. Server-returned 429s will surface as the same "Action Failed" popup, which we leave intact precisely so this case stays visible.

**R3 — Account-level penalty for unusual traffic shape.**
Unknown. The publisher's server policy is opaque. AutoBazaar use is opt-in mod behavior and the user accepts this exposure.

**Rollback:** delete `Patches/Networking/HttpRateLimitBypassPatch.cs`. No other artifacts to revert.

## 6. Open decisions (resolved during brainstorming)

| Decision | Choice | Rationale |
|---|---|---|
| Trigger gating | **Always-on**, no config | User does not want a toggle for this. Manual-play sessions also benefit. |
| Scope | **Client-side `IsRequestAllowed` only**; leave server-returned 429 popup intact | Server 429s are legitimate signals that must remain visible. |
| Test coverage | **None** | One-method Prefix returning a constant; no meaningful test seam, permitted by `.rules`. |

## 7. Acceptance

The change is acceptable when, in a manual smoke test:

1. With the mod loaded, rapidly clicking the same in-game action (e.g., repeated reroll) no longer raises the "Action Failed / Too many requests were sent" popup that the baseline game shows.
2. The game's network log (BepInEx log + `AppLogger`) shows the requests actually being issued (`Requesting: <endpoint>` entries) rather than being denied locally.
3. With the mod removed (or the patch file deleted and rebuilt), the popup behavior returns — confirming the patch is the only thing suppressing it.

No automated verification target. See §3 and the `.rules` "no meaningful automated test seam" guidance.
