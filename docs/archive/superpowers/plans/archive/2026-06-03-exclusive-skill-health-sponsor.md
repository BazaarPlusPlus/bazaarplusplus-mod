---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Exclusive Skill Health Sponsor Implementation Plan

> **Status: IMPLEMENTED — 历史归档（spent plan）。** 本计划描述的工作已全部落地（`CollectionHeroScope`、`ModApiHealthClient`、health probe、`BPPSupporterLinks`）；复选框未回填不代表有未完成项。注意：本文多处沿用了旧命名 `BazaarDbScreenshot...`，实际代码为 `BazaarDbSnapshot...`。保留为历史记录，勿据此重做。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Skill-tab hero filtering so it only shows skills exclusive to the selected hero, add Bazaar++ server health probing to BazaarDB upload startup/retry paths, and route sponsor clicks by language.

**Architecture:** Keep collection filtering as BPP-owned structured rules inside `Game/CollectionPanel`; do not move product filtering policy into `GameInterop`. Upgrade the existing `bazaarplusplus-server` `/health` endpoint, add a `ModApi` health client with RTT/timestamp result data, and have the BazaarDB screenshot uploader log-and-skip when health fails. Keep sponsor link selection in the narrow `Game/Supporters` module and compute the URL at click time from the current game language.

**Tech Stack:** C# 12, `netstandard2.1`, executable C# test projects, `HttpClient`, Newtonsoft.Json, Unity/BepInEx logging, Cloudflare Workers TypeScript, Vitest.

---

## Review Revisions

- Rollout dependency: server and mod changes are commit/review-separable, but not deploy-independent. The server `/health` contract must deploy before the mod health gate ships; do not add a temporary `{ ok: true }` compatibility fallback in the mod client.
- Skill scope terminology: `Common` is a real selectable scope. Treat exact `Common`-only skills as global/Common scope results, not as "hero-exclusive" skills. Concrete heroes still require exact one-hero ownership.
- Boundary: source offer pools keep answering "what can this source offer" and may include shared selected-hero skills; final `CollectionFilterEngine` filtering owns exclusive Skill visibility.
- Health tests: the ModApi health client tests must cover missing timestamp and exception paths, not just invalid timestamp and HTTP failure. Upload-service tests should also prove no pending rows, missing account id, and missing image file do not probe health.
- Commit hygiene: server repo health work must be committed separately from mod repo work, and unrelated existing server `.rules` changes must not be staged.

---

## Current State Evidence

- `Game/CollectionPanel/Data/CollectionFilterEngine.cs:45-46` currently treats a hero filter as "any hero overlaps"; `AnyHeroMatch` at `Game/CollectionPanel/Data/CollectionFilterEngine.cs:99-110` therefore includes shared skills.
- `Game/CollectionPanel/Data/CollectionCardVm.cs:18-20` already carries `Heroes`, `Tags`, and `HiddenTags`; `Game/CollectionPanel/Data/CollectionCardVm.From.cs:22-28` projects those fields from the live template.
- `Game/CollectionPanel/CollectionPanel.cs:626-649` disables hero filtering whenever any source chip is selected, so trainer/source selection can bypass the hero-skill filter unless the Skill tab is explicitly excluded from that bypass.
- `Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:77-80` also uses "contains selected hero" for source pools; final Skill-tab filtering must still run after source pool resolution.
- `/Users/yxinyu/codes/bpp/bazaarplusplus-server/src/index.ts:16-18` already has `GET /health`, but it only returns `{ ok: true }`; the server test at `/Users/yxinyu/codes/bpp/bazaarplusplus-server/test/health.test.ts:5-10` locks that old shape.
- `ModApi/ModApiRoutes.cs:10-15` has upload/query routes but no health route. `ModApi/Clients/ModOnlineClient.cs:18-20` exposes routes and the shared client.
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs:122-128` drives retry attempts through `StartupUploadAttemptRunner`; `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs:36-102` uploads pending screenshots directly without probing service health first.
- `Game/Supporters/Ui/BPPSupporterAttributionRow.cs:14` hardcodes `https://bazaarplusplus.com/`, and `Game/Supporters/Ui/BPPSupporterAttributionRow.cs:184-187` opens that exact URL regardless of language.
- `Game/Settings/LanguageCodeMatcher.cs:8-19` already recognizes Chinese language codes including `zh`, `zh-CN`, `zh-Hans`, `zh-TW`, `zh-Hant`, `zh-HK`, and `zh-MO`.

---

## File Structure

Collection skill filtering:

- Create `Game/CollectionPanel/Data/CollectionHeroScope.cs`
  - Own exact hero-scope predicates for catalog filters.
  - For Skills, concrete selected hero means exclusive: `Heroes.Count == 1 && Heroes contains selectedHero`.
  - For Skills, selected `Common` means exact global/Common scope: `Heroes.Count == 1 && Heroes contains EHero.Common`.
  - Missing hero ownership (`Heroes.Count == 0`) returns false under Skill hero filtering.
- Modify `Game/CollectionPanel/Data/CollectionFilterEngine.cs`
  - Replace the current `AnyHeroMatch` call with `CollectionHeroScope.MatchesFilter(...)`.
- Modify `Game/CollectionPanel/CollectionPanel.cs`
  - Preserve item/source behavior, but keep `ApplyHeroFilter=true` on Skill tab even when a trainer/source chip is selected.
- Modify `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
  - Link the new `CollectionHeroScope.cs`.
- Modify `tests/CollectionFilterEngine.Tests/Program.cs`
  - Add regression cases for exclusive, shared, global/Common, and missing-hero skills.

Server health:

- Create `/Users/yxinyu/codes/bpp/bazaarplusplus-server/src/features/health.ts`
  - Return `{ status: "ok", server_time_utc: "<ISO-8601 UTC>" }`.
- Modify `/Users/yxinyu/codes/bpp/bazaarplusplus-server/src/index.ts`
  - Replace inline `/health` response with `handleHealth`.
- Modify `/Users/yxinyu/codes/bpp/bazaarplusplus-server/test/health.test.ts`
  - Assert status and parseable timestamp.
- Modify `/Users/yxinyu/codes/bpp/bazaarplusplus-server/docs/api-reference.md`
  - Document the new response shape.

Mod health probing:

- Modify `ModApi/ModApiRoutes.cs`
  - Add `Health = BuildAbsolute("/health")`.
- Create `ModApi/Models/ModApiHealthResponse.cs`
  - JSON DTO with `Status` and `ServerTimeUtc`.
- Create `ModApi/Clients/ModApiHealthClient.cs`
  - `ProbeAsync(...)` does `GET /health`, records RTT and probe time, validates `status=="ok"` and parseable timestamp.
- Modify `tests/ModApi.Tests/RoutesTests.cs`
  - Assert `routes.Health`.
- Create `tests/ModApi.Tests/HealthClientTests.cs`
  - Cover success, bad status, missing timestamp, HTTP failure, and exception paths.
- Modify `tests/ModApi.Tests/Program.cs`
  - Run `HealthClientTests.Run()`.

BazaarDB upload integration:

- Modify `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs`
  - Probe health after the first buildable upload snapshot is available, before the first upload request.
  - On failed health: log warning, return without marking screenshots failed; retry gate will try again later.
  - On successful health: log RTT/server timestamp at Debug or Info level.
- Modify `tests/BazaarDbScreenshotUploadService.Tests/Program.cs`
  - Update request-count assertions for health + upload.
  - Add explicit health-failure regression that leaves pending rows pending and does not increment attempts.

Sponsor link language routing:

- Create `Game/Supporters/BPPSupporterLinks.cs`
  - `ResolveSponsorUrl(languageCode)` returns `https://bazaarplusplus.com` for Chinese and `https://bazaarplusplus.com/?lang=en` otherwise.
- Modify `Game/Supporters/Ui/BPPSupporterAttributionRow.cs`
  - Use `BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode())` for tooltip and click.
  - Compute at click time, so in-game language switches do not reuse a stale URL.
- Modify `tests/Supporters.Tests/Supporters.Tests.csproj`
  - Link `BPPSupporterLinks.cs`.
- Modify `tests/Supporters.Tests/Program.cs`
  - Add link tests for Chinese, English, other languages, blank language, and duplicate-query prevention.

Docs:

- Modify `docs/reverse-engineering/network-interface-inventory.md`
  - Add `GET /health` to the mod API inventory, since the mod will actively use it.
- Modify `docs/mod-features-overview.md`
  - Mention that BazaarDB screenshot upload first probes server health and degrades to logs/retry on failure.

---

### Task 0: Review-Only Red Team Before Implementation

**Status:** Review and plan revision complete on 2026-06-03. Continuation request satisfied the confirmation gate; implementation proceeded on `yxinyux/exclusive-skill-health-sponsor`.

**Files:**
- Read only: this plan
- Read only: files listed in Current State Evidence

- [x] **Step 1: Run an independent review of the plan before patching**

Use a review-only pass. Do not edit files in this step. The review must specifically challenge:

```text
1. Does "exclusive skill" belong in CollectionFilterEngine, source offer resolution, or both?
2. Does keeping exact Common-only Skill results conflict with "global skills should not be misjudged as hero-exclusive"?
3. Is health probing placed late enough to avoid pointless network calls, but early enough to avoid uploading into a dead backend?
4. Does the sponsor URL helper stay inside Game/Supporters instead of becoming generic infrastructure?
5. Are server-repo changes separable from mod-repo changes for commit/review?
```

- [x] **Step 2: Revise this plan from review findings and send it back for confirmation**

Expected: no implementation starts until the revised plan is confirmed.

---

### Task 1: Add Exclusive Skill Hero Scope Tests

**Files:**
- Modify: `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
- Modify: `tests/CollectionFilterEngine.Tests/Program.cs`

- [ ] **Step 1: Link the new helper file in the test project**

Add this compile link after `CollectionFilterContext.cs`:

```xml
<Compile
  Include="..\..\Game\CollectionPanel\Data\CollectionHeroScope.cs"
  Link="CollectionHeroScope.cs"
/>
```

- [ ] **Step 2: Add failing exclusive-skill tests**

Insert after the existing Skill tag filter assertions in `tests/CollectionFilterEngine.Tests/Program.cs`:

```csharp
var vanessaExclusiveSkill = Card(
    "Vanessa Exclusive Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Vanessa }
);
var sharedHeroSkill = Card(
    "Shared Hero Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Vanessa, EHero.Dooley }
);
var commonSkill = Card(
    "Common Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common }
);
var commonSharedSkill = Card(
    "Common Shared Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common, EHero.Vanessa }
);
var missingHeroSkill = Card(
    "Missing Hero Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: Array.Empty<EHero>()
);
var exclusiveSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
exclusiveSkillFilter.Heroes.Add(EHero.Vanessa);
var exclusiveSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    exclusiveSkillFilter
);
AssertSequence(
    exclusiveSkillResult,
    new[] { vanessaExclusiveSkill.Id },
    "Skill hero filtering should return only skills exclusive to the selected hero."
);

var exclusiveSkillSourceResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    exclusiveSkillFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[]
        {
            sharedHeroSkill.Id,
            commonSkill.Id,
            commonSharedSkill.Id,
            missingHeroSkill.Id,
            vanessaExclusiveSkill.Id,
        },
        ApplyHeroFilter = true,
    }
);
AssertSequence(
    exclusiveSkillSourceResult,
    new[] { vanessaExclusiveSkill.Id },
    "Trainer/source pools should still be ANDed with exclusive skill hero filtering."
);

var commonSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
commonSkillFilter.Heroes.Add(EHero.Common);
var commonSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    commonSkillFilter
);
AssertSequence(
    commonSkillResult,
    new[] { commonSkill.Id },
    "Common skill filtering should only show skills explicitly scoped to Common/global."
);
```

- [ ] **Step 3: Run the failing test**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: FAIL. The current `AnyHeroMatch` path includes `sharedHeroSkill`, and source-selected Skill filtering is not yet guaranteed by `CollectionPanel`.

---

### Task 2: Implement Exclusive Skill Hero Scope

**Files:**
- Create: `Game/CollectionPanel/Data/CollectionHeroScope.cs`
- Modify: `Game/CollectionPanel/Data/CollectionFilterEngine.cs`
- Modify: `Game/CollectionPanel/CollectionPanel.cs`

- [ ] **Step 1: Create the hero-scope helper**

Add `Game/CollectionPanel/Data/CollectionHeroScope.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionHeroScope
{
    public static bool MatchesFilter(CollectionCardVm card, CollectionFilterState filter)
    {
        if (filter.Heroes.Count == 0)
            return true;

        if (card.Type == ECardType.Skill)
        {
            var selectedHero = filter.SelectedHero;
            return selectedHero.HasValue && MatchesSkillHeroScope(card.Heroes, selectedHero.Value);
        }

        return AnyHeroMatch(card.Heroes, filter.Heroes);
    }

    public static bool MatchesSkillHeroScope(IReadOnlyCollection<EHero> cardHeroes, EHero hero)
    {
        return cardHeroes.Count == 1 && Contains(cardHeroes, hero);
    }

    private static bool AnyHeroMatch(
        IReadOnlyCollection<EHero> cardHeroes,
        HashSet<EHero> filterHeroes
    )
    {
        foreach (var hero in cardHeroes)
        {
            if (filterHeroes.Contains(hero))
                return true;
        }
        return false;
    }

    private static bool Contains(IReadOnlyCollection<EHero> values, EHero target)
    {
        foreach (var value in values)
        {
            if (value == target)
                return true;
        }
        return false;
    }
}
```

- [ ] **Step 2: Use the helper in the filter engine**

Change `Game/CollectionPanel/Data/CollectionFilterEngine.cs`:

```csharp
if (heroFilterCount > 0 && !CollectionHeroScope.MatchesFilter(card, filter))
    continue;
```

Delete the old private `AnyHeroMatch(...)` method from `CollectionFilterEngine`; keep `AnyTagMatch` and `AnyMerchantMatch`.

- [ ] **Step 3: Keep Skill hero filtering active after source selection**

Change the `CollectionFilterContext` construction in `Game/CollectionPanel/CollectionPanel.cs`:

```csharp
new CollectionFilterContext
{
    OfferedCardIds = offeredCardIds,
    ApplyHeroFilter = !hasSelectedSource || _filter.ActiveType == ECardType.Skill,
}
```

This preserves the existing Item/source behavior from `tests/CollectionFilterEngine.Tests/Program.cs:337-352` while preventing trainer/source selection from bypassing exclusive Skill filtering.

- [ ] **Step 4: Run the focused filtering tests**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: PASS and output `CollectionFilterEngine checks passed.`

---

### Task 3: Add Source-Selection Regression Coverage

**Files:**
- Modify: `tests/CollectionSourceFiltering.Tests/Program.cs`

- [ ] **Step 1: Add a source resolver fixture that includes shared Skill ownership**

Insert after the existing SelectedHero source resolver assertions around `tests/CollectionSourceFiltering.Tests/Program.cs:567-616`:

```csharp
var selectedHeroTrainerRule = BuildSingleEntry(
    "Selected Hero Trainer",
    CollectionSourceKind.Trainer,
    """{ "heroMode": "SelectedHero" }"""
);
var selectedHeroTrainerCards = new[]
{
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000001"),
        ECardType.Skill,
        [EHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000002"),
        ECardType.Skill,
        [EHero.Vanessa, EHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000003"),
        ECardType.Skill,
        [EHero.Common]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000004"),
        ECardType.Skill,
        Array.Empty<EHero>()
    ),
};
var selectedHeroTrainerPool = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroTrainerRule,
    EHero.Vanessa,
    selectedHeroTrainerCards
);
AssertSet(
    selectedHeroTrainerPool.OfferedCardIds,
    new[] { selectedHeroTrainerCards[0].Id, selectedHeroTrainerCards[1].Id },
    "Source pools may include shared selected-hero skills; final Skill filtering owns exclusivity."
);
```

- [ ] **Step 2: Run the source tests**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected: PASS. This locks the boundary: source rules answer "offered by this source"; `CollectionFilterEngine` answers "exclusive to selected hero".

---

### Task 4: Upgrade Server Health Response

**Files:**
- Create: `/Users/yxinyu/codes/bpp/bazaarplusplus-server/src/features/health.ts`
- Modify: `/Users/yxinyu/codes/bpp/bazaarplusplus-server/src/index.ts`
- Modify: `/Users/yxinyu/codes/bpp/bazaarplusplus-server/test/health.test.ts`
- Modify: `/Users/yxinyu/codes/bpp/bazaarplusplus-server/docs/api-reference.md`

- [ ] **Step 1: Write the failing server health test**

Replace the body assertion in `test/health.test.ts` with:

```ts
const body = (await response.json()) as {
  status?: string;
  server_time_utc?: string;
};
expect(body.status).toBe("ok");
expect(typeof body.server_time_utc).toBe("string");
expect(Number.isNaN(Date.parse(body.server_time_utc!))).toBe(false);
```

- [ ] **Step 2: Run the failing server test**

Run:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-server
npm test -- test/health.test.ts
```

Expected: FAIL because current `/health` returns `{ ok: true }`.

- [ ] **Step 3: Add the health handler**

Create `src/features/health.ts`:

```ts
import { json } from "../http/json";

export function handleHealth(): Response {
  return json({
    status: "ok",
    server_time_utc: new Date().toISOString(),
  });
}
```

- [ ] **Step 4: Route `/health` through the handler**

Modify `src/index.ts`:

```ts
import { handleHealth } from "./features/health";
```

Change the static route:

```ts
{ method: "GET", path: "/health", handle: () => handleHealth() },
```

- [ ] **Step 5: Update server API docs**

Change the `GET /health` response example in `docs/api-reference.md`:

```json
{
  "status": "ok",
  "server_time_utc": "2026-06-03T00:00:00.000Z"
}
```

- [ ] **Step 6: Verify server health**

Run:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-server
npm run check
npm test -- test/health.test.ts
```

Expected: both PASS.

---

### Task 5: Add ModApi Health Route And Client

**Files:**
- Modify: `ModApi/ModApiRoutes.cs`
- Create: `ModApi/Models/ModApiHealthResponse.cs`
- Create: `ModApi/Clients/ModApiHealthClient.cs`
- Modify: `tests/ModApi.Tests/RoutesTests.cs`
- Create: `tests/ModApi.Tests/HealthClientTests.cs`
- Modify: `tests/ModApi.Tests/Program.cs`

- [ ] **Step 1: Add failing route test**

In `tests/ModApi.Tests/RoutesTests.cs`, after `QueryGhostBattles` assertions:

```csharp
if (routes.Health != "https://mod-api-v4.bazaarplusplus.com/health")
    throw new InvalidOperationException($"Unexpected Health: {routes.Health}");
```

Run:

```bash
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
```

Expected: FAIL because `Health` does not exist.

- [ ] **Step 2: Add the route**

Modify `ModApi/ModApiRoutes.cs`:

```csharp
Health = BuildAbsolute("/health");
```

Add the property:

```csharp
public string Health { get; }
```

- [ ] **Step 3: Add the DTO**

Create `ModApi/Models/ModApiHealthResponse.cs`:

```csharp
#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

public sealed class ModApiHealthResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("server_time_utc")]
    public string ServerTimeUtc { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Add the health client**

Create `ModApi/Clients/ModApiHealthClient.cs`:

```csharp
#nullable enable
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class ModApiHealthClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public ModApiHealthClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<ModApiHealthProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient
                .GetAsync(_routes.Health, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    $"http_{(int)response.StatusCode}"
                );

            var parsed = JsonConvert.DeserializeObject<ModApiHealthResponse>(body);
            if (parsed == null || !string.Equals(parsed.Status, "ok", StringComparison.OrdinalIgnoreCase))
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "health_status_not_ok"
                );

            if (!DateTime.TryParse(parsed.ServerTimeUtc, out var serverTimeUtc))
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );

            return ModApiHealthProbeResult.Success(
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                parsed.Status,
                serverTimeUtc.ToUniversalTime()
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ModApiHealthProbeResult.Failure(
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                ex.Message
            );
        }
    }
}

public readonly struct ModApiHealthProbeResult
{
    private ModApiHealthProbeResult(
        bool succeeded,
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        string? status,
        DateTime? serverTimeUtc,
        string? error
    )
    {
        Succeeded = succeeded;
        ProbedAtUtc = probedAtUtc;
        RoundTripMilliseconds = roundTripMilliseconds;
        Status = status;
        ServerTimeUtc = serverTimeUtc;
        Error = error;
    }

    public bool Succeeded { get; }
    public DateTime ProbedAtUtc { get; }
    public long RoundTripMilliseconds { get; }
    public string? Status { get; }
    public DateTime? ServerTimeUtc { get; }
    public string? Error { get; }

    public static ModApiHealthProbeResult Success(
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        string status,
        DateTime serverTimeUtc
    ) => new(true, probedAtUtc, roundTripMilliseconds, status, serverTimeUtc, null);

    public static ModApiHealthProbeResult Failure(
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        string error
    ) => new(false, probedAtUtc, roundTripMilliseconds, null, null, error);
}
```

- [ ] **Step 5: Add health client tests**

Create `tests/ModApi.Tests/HealthClientTests.cs`:

```csharp
#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;

internal static class HealthClientTests
{
    public static void Run()
    {
        var routes =
            ModApiRoutes.TryCreate("https://example.invalid")
            ?? throw new InvalidOperationException("TryCreate returned null for valid URL");

        var successHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
            ),
        });
        var successClient = new ModApiHealthClient(new HttpClient(successHandler), routes);
        var success = successClient.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(success.Succeeded, "Successful health response should be available.");
        Assert(success.Status == "ok", "Successful health response should expose status.");
        Assert(success.ServerTimeUtc.HasValue, "Successful health response should expose server time.");
        Assert(success.RoundTripMilliseconds >= 0, "Successful health probe should expose RTT.");
        Assert(
            successHandler.Requests[0].RequestUri!.ToString() == "https://example.invalid/health",
            "Health client should call the health route."
        );

        var badStatusClient = new ModApiHealthClient(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"status\":\"degraded\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
                ),
            })),
            routes
        );
        var badStatus = badStatusClient.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(!badStatus.Succeeded, "Non-ok health status should fail availability.");
        Assert(badStatus.Error == "health_status_not_ok", "Bad status should have a stable error.");

        var missingTimestampClient = new ModApiHealthClient(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}"),
            })),
            routes
        );
        var missingTimestamp = missingTimestampClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!missingTimestamp.Succeeded, "Missing server timestamp should fail availability.");
        Assert(
            missingTimestamp.Error == "server_time_invalid",
            "Missing timestamp should have the same stable error as an invalid timestamp."
        );

        var legacyShapeClient = new ModApiHealthClient(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}"),
            })),
            routes
        );
        var legacyShape = legacyShapeClient.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(
            !legacyShape.Succeeded,
            "Legacy { ok: true } health shape should not satisfy the new timestamp/status contract."
        );

        var badTimestampClient = new ModApiHealthClient(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\",\"server_time_utc\":\"bad\"}"),
            })),
            routes
        );
        var badTimestamp = badTimestampClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!badTimestamp.Succeeded, "Invalid server timestamp should fail availability.");
        Assert(badTimestamp.Error == "server_time_invalid", "Bad timestamp should have a stable error.");

        var httpFailureClient = new ModApiHealthClient(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            routes
        );
        var httpFailure = httpFailureClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!httpFailure.Succeeded, "HTTP failure should fail availability.");
        Assert(httpFailure.Error == "http_503", "HTTP failure should include status code.");

        var exceptionClient = new ModApiHealthClient(
            new HttpClient(new RecordingHandler(_ => throw new HttpRequestException("network down"))),
            routes
        );
        var exceptionFailure = exceptionClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!exceptionFailure.Succeeded, "Transport exceptions should fail availability.");
        Assert(
            !string.IsNullOrWhiteSpace(exceptionFailure.Error),
            "Transport exception failures should expose an error."
        );
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
```

- [ ] **Step 6: Run ModApi tests**

Modify `tests/ModApi.Tests/Program.cs`:

```csharp
RoutesTests.Run();
CodecTests.Run();
HealthClientTests.Run();
Console.WriteLine("All ModApi tests passed.");
```

Run:

```bash
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
```

Expected: PASS.

---

### Task 6: Gate BazaarDB Upload With Health Probe

**Files:**
- Modify: `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs`
- Modify: `tests/BazaarDbScreenshotUploadService.Tests/Program.cs`

- [ ] **Step 1: Add failing health behavior tests**

Update the happy-path handler to route by method/path:

```csharp
var handler = new RecordingHandler(req =>
{
    if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath == "/health")
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"status\":\"ok\",\"server_time_utc\":\"2026-06-03T00:00:00.000Z\"}"
            ),
        };
    }

    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"status\":\"ok\"}"),
    };
});
```

Change the request count assertion:

```csharp
Assert(handler.Requests.Count == 2, "Service should GET health then POST one upload request.");
Assert(
    handler.Requests[0].Method == HttpMethod.Get
        && handler.Requests[0].RequestUri?.AbsolutePath == "/health",
    "First request should be the health probe."
);
```

Add a new health-failure test after the existing missing-image-file test, so its still-pending row cannot interfere with the transient/permanent upload cases:

```csharp
SeedRunScreenshotWithFile(
    dbPath,
    screenshotsDir,
    "shot-health-fail",
    "2026-04-08_20-32-25-000_final_run-r1.png"
);
ensureBackfilled.Invoke(store, []);
{
    var handler = new RecordingHandler(req => new HttpResponseMessage(
        HttpStatusCode.ServiceUnavailable
    ));
    var client = new HttpClient(handler);
    var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
    var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
    var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
    task.GetAwaiter().GetResult();

    Assert(
        GetUploadStatus(dbPath, "shot-health-fail") == "pending",
        "Health failure should leave pending screenshots pending for retry."
    );
    Assert(
        GetUploadAttempts(dbPath, "shot-health-fail") == 0,
        "Health failure should not count as a screenshot upload attempt."
    );
    Assert(handler.Requests.Count == 1, "Health failure should not continue into upload POST.");
    client.Dispose();
}
```

For transient/permanent upload tests, route `GET /health` to 200 and the upload `POST` to 503/400 respectively. Expected request counts become 2 when a POST is reached. Keep the missing-image-file test at `handler.Requests.Count == 0`; the implementation below probes health only after a snapshot is successfully built.

Also add precondition regressions in isolated temp DB/store fixtures so their pending rows cannot affect later cases:

```csharp
Assert(
    noPendingHandler.Requests.Count == 0,
    "Service should not probe health when there are no pending uploads."
);
Assert(
    noAccountHandler.Requests.Count == 0,
    "Service should not probe health before the player account id is available."
);
```

- [ ] **Step 2: Run the failing upload-service test**

Run:

```bash
dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/BazaarDbScreenshotUploadService.Tests.csproj
```

Expected: FAIL because the service currently does not request `/health`.

- [ ] **Step 3: Probe health once before the first real upload**

In `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs`, add a nullable probe result before the loop:

```csharp
ModApiHealthProbeResult? healthProbe = null;
```

Then, inside the existing `foreach`, place this block after the `snapshot == null` permanent-failure guard and before `client.UploadScreenshotAsync(...)`:

```csharp
if (!healthProbe.HasValue)
{
    healthProbe = await new ModApiHealthClient(_httpClient, _routes)
        .ProbeAsync(cancellationToken)
        .ConfigureAwait(false);
    if (!healthProbe.Value.Succeeded)
    {
        BppLog.Warn(
            "BazaarDbScreenshotUploadService",
            $"Bazaar++ service health probe failed error={healthProbe.Value.Error ?? "unknown"} rtt_ms={healthProbe.Value.RoundTripMilliseconds} probed_at_utc={healthProbe.Value.ProbedAtUtc:O}; retrying later."
        );
        return;
    }

    BppLog.Debug(
        "BazaarDbScreenshotUploadService",
        $"Bazaar++ service health ok rtt_ms={healthProbe.Value.RoundTripMilliseconds} server_time_utc={healthProbe.Value.ServerTimeUtc:O} probed_at_utc={healthProbe.Value.ProbedAtUtc:O}."
    );
}
```

The local ordering should remain:

```csharp
var client = new BazaarDbScreenshotClient(_httpClient, _routes);
ModApiHealthProbeResult? healthProbe = null;
foreach (var screenshotId in pending)
{
    ...
    var snapshot = _store.TryBuildSnapshot(screenshotId, playerAccountId);
    if (snapshot == null)
    {
        _store.MarkPermanentFailure(...);
        continue;
    }

    // healthProbe block here
    var result = await client.UploadScreenshotAsync(snapshot.Payload, cancellationToken);
    ...
}
```

- [ ] **Step 4: Run upload-service tests**

Run:

```bash
dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/BazaarDbScreenshotUploadService.Tests.csproj
```

Expected: PASS and output `BazaarDbScreenshotUploadService checks passed.`

---

### Task 7: Route Sponsor URL By Language

**Files:**
- Create: `Game/Supporters/BPPSupporterLinks.cs`
- Modify: `Game/Supporters/Ui/BPPSupporterAttributionRow.cs`
- Modify: `tests/Supporters.Tests/Supporters.Tests.csproj`
- Modify: `tests/Supporters.Tests/Program.cs`

- [ ] **Step 1: Add failing sponsor-link tests**

Modify `tests/Supporters.Tests/Supporters.Tests.csproj` to link the new file:

```xml
<Compile Include="../../Game/Supporters/BPPSupporterLinks.cs" Link="BPPSupporterLinks.cs" />
```

Add `TestSponsorLinks();` after `TestSponsorActionText();`, then add:

```csharp
static void TestSponsorLinks()
{
    AssertEqual(
        "https://bazaarplusplus.com",
        BPPSupporterLinks.ResolveSponsorUrl("zh-CN"),
        "Chinese sponsor URL should use the canonical no-query domain."
    );
    AssertEqual(
        "https://bazaarplusplus.com",
        BPPSupporterLinks.ResolveSponsorUrl("zh-Hant"),
        "Traditional Chinese sponsor URL should use the canonical no-query domain."
    );
    AssertEqual(
        "https://bazaarplusplus.com/?lang=en",
        BPPSupporterLinks.ResolveSponsorUrl("en"),
        "English sponsor URL should force the English site."
    );
    AssertEqual(
        "https://bazaarplusplus.com/?lang=en",
        BPPSupporterLinks.ResolveSponsorUrl("de-DE"),
        "Non-Chinese sponsor URL should force the English site."
    );
    AssertEqual(
        "https://bazaarplusplus.com/?lang=en",
        BPPSupporterLinks.ResolveSponsorUrl(string.Empty),
        "Unknown language should fall back to English."
    );
    AssertFalse(
        BPPSupporterLinks.ResolveSponsorUrl("en").Contains("?lang=en?lang=en"),
        "Sponsor URL should not append duplicate lang parameters."
    );
}
```

Run:

```bash
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
```

Expected: FAIL because `BPPSupporterLinks` does not exist.

- [ ] **Step 2: Add the link helper**

Create `Game/Supporters/BPPSupporterLinks.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterLinks
{
    private const string ChineseSponsorUrl = "https://bazaarplusplus.com";
    private const string EnglishSponsorUrl = "https://bazaarplusplus.com/?lang=en";

    public static string ResolveSponsorUrl(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode) ? ChineseSponsorUrl : EnglishSponsorUrl;
    }
}
```

- [ ] **Step 3: Use it in the attribution row**

Modify `Game/Supporters/Ui/BPPSupporterAttributionRow.cs`:

Delete:

```csharp
private const string SupportUrl = "https://bazaarplusplus.com/";
```

Change `CreateSponsorButton`:

```csharp
var button = new Button(OpenSupportPage) { text = $"{SponsorIcon} {text}" };
button.tooltip = BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode());
```

Change `OpenSupportPage`:

```csharp
private static void OpenSupportPage()
{
    Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode()));
}
```

- [ ] **Step 4: Run supporter tests**

Run:

```bash
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
```

Expected: PASS and output `Supporters checks passed.`

---

### Task 8: Docs, Full Verification, And Runtime Checks

**Files:**
- Modify: `docs/reverse-engineering/network-interface-inventory.md`
- Modify: `docs/mod-features-overview.md`

- [ ] **Step 1: Update docs**

Add to the mod API inventory:

```markdown
| GET | `/health` | JSON `{ status, server_time_utc }` | `ModApiHealthClient` | Bazaar++ service availability probe before upload/sync work. |
```

Add to the BazaarDB upload overview:

```markdown
上传前先请求 `GET /health` 记录 RTT、探测时间和服务端时间戳；失败时只记录日志并等待下一轮重试，不标记截图上传失败，也不阻塞游戏主流程。
```

- [ ] **Step 2: Run full focused mod verification**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj
dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/BazaarDbScreenshotUploadService.Tests.csproj
dotnet run --project tests/Supporters.Tests/Supporters.Tests.csproj
git diff --check
dotnet build BazaarPlusPlus.csproj --no-restore
```

Expected: all PASS. The build should copy the Debug mod DLL to the local BepInEx plugin folder if the game install is found.

- [ ] **Step 3: Run full focused server verification**

Run:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-server
npm run check
npm test
```

Expected: all PASS.

- [ ] **Step 4: Runtime validation through Steam**

Runtime note from implementation: The game was launched through Steam with the Debug DLL copied into BepInEx, BepInEx loaded `BazaarPlusPlus 4.0.0`, and the log recorded `CollectionPanelLoad` entries under the new build. Computer Use Accessibility clicks did not activate Unity buttons; a lower-level CGEvent click opened the CollectionPanel once. The exact Skill-tab matrix and sponsor URL click-through remain covered by focused tests rather than a full synthetic UI click-through.

Launch the game through Steam only:

```bash
open "steam://run/1617400"
```

Validate:

```text
1. In Collection Panel Skill tab, select Vanessa/Dooley/Pygmalien/etc. Shared multi-hero skills and missing-hero skills do not appear for a concrete hero.
2. Select a trainer/source chip while on Skill tab. The source pool remains active, but only exclusive skills for the selected hero remain visible.
3. Enable BazaarDB screenshot upload and watch BepInEx/LogOutput.log. Successful health probes log RTT/server_time_utc; failed probes log a warning and no main-flow exception.
4. In Chinese language, sponsor click opens https://bazaarplusplus.com.
5. In non-Chinese language, sponsor click opens https://bazaarplusplus.com/?lang=en.
6. Switch language and click sponsor again; the URL is recalculated and never becomes ...?lang=en?lang=en or ...?lang=en&lang=en.
```

Read the runtime log:

```bash
tail -n 200 "$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log"
```

- [ ] **Step 5: Review and finalization**

Review diffs separately by repo:

```bash
git -C /Users/yxinyu/codes/bpp/bazaarplusplus-mod diff --stat
git -C /Users/yxinyu/codes/bpp/bazaarplusplus-mod diff
git -C /Users/yxinyu/codes/bpp/bazaarplusplus-server diff --stat
git -C /Users/yxinyu/codes/bpp/bazaarplusplus-server diff
```

Keep commits scoped:

```bash
git -C /Users/yxinyu/codes/bpp/bazaarplusplus-server status --short
git -C /Users/yxinyu/codes/bpp/bazaarplusplus-mod status --short
```

If execution used feature branches, commit server and mod changes separately, merge the working branch back to the repo default branch after verification, push, then delete already-merged branches.

---

## Acceptance Mapping

- "专属英雄技能筛选只返回当前英雄专属技能": Task 1 and Task 2 add exact-one-hero Skill filtering.
- "共享技能、非独占技能、未知归属技能回归验证": Task 1 covers shared, Common/global, and empty `Heroes`.
- "dbConnect 能请求 `/health` 并根据返回时间与状态判断服务可达": Task 4 and Task 5 add timestamp/status route and validated client probe result.
- "`/health` 失败有可见处理且不影响主流程": Task 6 logs warning, returns without marking upload failure, and lets retry continue.
- "sponsor 中文无参数，非中文 `?lang=en`": Task 7 adds deterministic URL helper and tests.
- "不重复拼接 `?lang=`": Task 7 uses fixed constants and tests duplicate-query prevention.
