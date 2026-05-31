> **Status: IMPLEMENTED (historical).** Shipped 2026-05-31. Current state: [Game/Supporters](../../../Game/Supporters/) and [CardSetPreviewRuntime.cs](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs).

# BPPSupporters Design

## Scope

`BPPSupporters` is a small game-side module for selecting one supporter attribution for display. It owns supporter list loading, caching, fallback entries, and the hard-coded tier sampling policy.

The module is not a generic sampling abstraction. The weighted roll exists only because supporter attribution needs tier-aware display frequency.

## Current Problem

The current supporter flow lives inside `Game/CardSetPreview/CardSetPreviewSponsorCatalog.cs`. That makes CardSet preview responsible for:

- Fetching `https://bpp-static.bazaarplusplus.com/supporter-list.json`
- Reading and writing the local supporter cache
- Maintaining fallback supporter entries
- Filtering malformed entries
- Sampling by tier weight
- Returning the name and tier used by the sponsor panel

Only the last result is meaningful to CardSet preview. The data source and sampling policy are reusable display infrastructure for Bazaar++ supporters, not CardSet preview behavior.

## Proposed Module

Create `Game/Supporters/` with `BPPSupporters` as the public entry point for game code:

```csharp
namespace BazaarPlusPlus.Game.Supporters;

internal readonly struct BPPSupporterSample
{
    public string Name { get; init; }

    public int Tier { get; init; }

    public bool HasValue => !string.IsNullOrWhiteSpace(Name) && Tier > 0;
}

internal static class BPPSupporters
{
    public static BPPSupporterSample Sample();
}
```

Callers should not pass weights, random sources, or selection policies. They ask for one supporter sample and receive a name plus tier.

## Internal Files

- `Game/Supporters/BPPSupporters.cs`
  - The only intended caller-facing API.
  - Calls the catalog to get current entries, then calls the sampler.

- `Game/Supporters/BPPSupporterSample.cs`
  - Small value result: `Name`, `Tier`, `HasValue`.

- `Game/Supporters/BPPSupporterEntry.cs`
  - Internal JSON DTO for `{ "name": "...", "tier": 4 }`.

- `Game/Supporters/BPPSupporterCatalog.cs`
  - Owns remote URL, HTTP client, one-hour cache, temp cache file, fallback entries, background refresh, and entry sanitization.
  - Preserves the current non-blocking behavior: use cached or fallback entries immediately, refresh in the background when needed.

- `Game/Supporters/BPPSupporterSampler.cs`
  - Owns the tier grouping and sampling logic.
  - Keeps packed tier weights private and hard-coded.

- `Game/Supporters/BPPSupporterAttributionText.cs`
  - Formats localized display copy such as `Supported by Alice` or `由 Alice 支持`.
  - This stays in the supporters module because it is supporter attribution language, not CardSet preview language.

## Sampling Policy

The sampler filters invalid entries, groups entries by tier, picks a tier bucket by hard-coded weight, then picks uniformly inside that bucket.

The tier weights are private implementation details:

```csharp
private static float ResolveTierWeight(int tier)
{
    return tier switch
    {
        4 => 6f,
        3 => 4f,
        2 => 2f,
        _ => 1f,
    };
}
```

No interface or config should expose these weights unless the product requirement changes. A later caller should not be able to turn `BPPSupporters` into a generic random picker by passing custom weights.

For tests, use an internal overload that accepts a roll provider:

```csharp
internal static BPPSupporterSample Sample(
    IReadOnlyList<BPPSupporterEntry> entries,
    Func<float> randomValue
)
```

Production code uses `UnityEngine.Random.value`.

## CardSet Preview Integration

CardSet preview should stop depending on `CardSetPreviewSponsorCatalog`. It should ask the supporters module for a sample:

```csharp
var supporter = BPPSupporters.Sample();
```

When `supporter.HasValue` is false, CardSet preview keeps the sponsor panel hidden. When true, CardSet preview passes:

- `supporter.Name` as the highlighted name
- `supporter.Tier` for visual color/chrome
- `BPPSupporterAttributionText.FormatSupportedBy(supporter.Name, languageCode)` for display text

Existing CardSet preview UI classes may keep their sponsor-oriented field names initially (`SponsorText`, `SponsorName`, `SponsorTier`) to keep the migration small. Renaming UI fields from sponsor to supporter can be a separate cleanup if the call sites become confusing.

## Non-Goals

- Do not create `Infrastructure/Sampling`.
- Do not expose a weight selector or random source in the public API.
- Do not generalize final build recommendation loading into this module.
- Do not move CardSet preview UI rendering into `Game/Supporters`.
- Do not add new user-facing documentation for this design-only change.

## Verification

This is mostly a move-and-rename refactor with one small behavior seam. Verification should stay targeted:

- Add or extend a lightweight executable test project for `BPPSupporterAttributionText`.
- Test `BPPSupporterSampler` with deterministic roll values:
  - Empty input returns no sample.
  - Blank names and non-positive tiers are ignored.
  - Tier weights preserve the current 4/3/2/default distribution.
  - Selection inside a tier bucket is uniform by index range.
- Run `dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj` if attribution text remains covered there.
- Run `dotnet build BazaarPlusPlus.csproj` after moving production files.

`BuildAll` is not required unless the implementation also touches packaging or build copy behavior.
