---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at 7ddfb8ff "Add Private Pitchfork and Stickybeans sources"; all 4 tasks shipped verbatim — OtherHeroes enum/resolver, both JSON catalog entries, tests (counts 71/49/22).

# Private Pitchfork and Stickybeans Collection Sources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Private Pitchfork and Stickybeans to the Collection Panel source catalog without adding day/source-availability schema.

**Architecture:** Reuse the existing static collection-source catalog and offer-pool resolver. Private Pitchfork is expressible as an existing `NeutralOnly` rule with `tagsNone: ["Loot"]`; Stickybeans needs one new source hero mode, `OtherHeroes`, to model `TSpawnBehaviorExcludePlayerHero` by excluding Common/neutral cards and excluding cards that include the selected UI hero.

**Tech Stack:** C# 12, netstandard2.1 mod code, exe-runner test project `tests/CollectionSourceFiltering.Tests`, JSON source catalog `src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json`.

---

## Evidence and Scope

Live `GameData.db` facts verified from `~/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache/GameData.db`:

- Private Pitchfork template id: `15b88e74-024e-40a3-a811-aa5810e68ca2`
- Private Pitchfork template fields: `StartingTier = Gold`, `Heroes = ["Common"]`, description `Sells Neutral items`
- Private Pitchfork spawn query: `ConstraintHero(Common) AND ConstraintCardType(Item) AND NOT ConstraintTag(Loot)`
- Stickybeans template id: `d0276b47-be8a-4bbc-ab55-9f92b352480a`
- Stickybeans template fields: `StartingTier = Gold`, `Heroes = ["Common"]`, description `Sells items from other Heroes`
- Stickybeans spawn behavior: `TSpawnBehaviorDownShiftTier` and `TSpawnBehaviorExcludePlayerHero`

Implementation decision:

- Do not model `Days 6+`.
- Do not add `availableFromDay`, `sourceStartingTier`, or any day/source availability schema.
- Do not use `startingTier: Gold` for either source. In this codebase `startingTier` means offered-card starting-tier filtering, and it also suppresses the day gate via `CollectionSourceEntry.SuppressDayGate`.
- Stickybeans excludes Common/neutral items. It sells other concrete heroes' items only.

## File Structure

- Modify `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceEnums.cs`
  - Add `OtherHeroes` to `CollectionSourceHeroMode`.
- Modify `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs`
  - Add resolver logic for `OtherHeroes`.
  - Add one helper for concrete non-Common hero checks.
- Modify `src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json`
  - Add Private Pitchfork source entry.
  - Add Stickybeans source entry.
- Modify `tests/CollectionSourceFiltering.Tests/Program.cs`
  - Add red tests for the `OtherHeroes` mode.
  - Add current catalog tests for the two new source entries and their offer-pool behavior.
  - Update locked catalog counts.

## Task 1: Add Failing Tests for `OtherHeroes`

**Files:**
- Modify: `tests/CollectionSourceFiltering.Tests/Program.cs`

- [ ] **Step 1: Add resolver-level red tests near the existing `FixedHero` / `AllHeroes` tests**

Add this block after the existing fixed-hero merchant/trainer tests and before the `AllHeroes` test:

```csharp
var otherHeroesEntry = BuildSingleEntry(
    "Other Heroes",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "OtherHeroes" }"""
);
var otherHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
        ECardType.Item,
        [EHero.Common]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"),
        ECardType.Item,
        [EHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000003"),
        ECardType.Item,
        [EHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000004"),
        ECardType.Item,
        [EHero.Dooley, EHero.Stelle]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000005"),
        ECardType.Item,
        [EHero.Vanessa, EHero.Dooley]
    ),
};
var otherHeroesForVanessa = CollectionSourceOfferPoolResolver.Resolve(
    otherHeroesEntry,
    EHero.Vanessa,
    otherHeroCards
);
AssertSet(
    otherHeroesForVanessa.OfferedCardIds,
    new[] { otherHeroCards[2].Id, otherHeroCards[3].Id },
    "OtherHeroes rules should exclude Common cards and cards that include the selected UI hero."
);
var otherHeroesWithoutSelectedHero = CollectionSourceOfferPoolResolver.Resolve(
    otherHeroesEntry,
    selectedHero: null,
    otherHeroCards
);
AssertSet(
    otherHeroesWithoutSelectedHero.OfferedCardIds,
    new[]
    {
        otherHeroCards[1].Id,
        otherHeroCards[2].Id,
        otherHeroCards[3].Id,
        otherHeroCards[4].Id,
    },
    "OtherHeroes rules should include all non-Common hero cards when no UI hero is selected."
);
```

- [ ] **Step 2: Run the test and verify it fails for the missing enum value**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected: the test project fails while parsing `"OtherHeroes"` with an invalid enum value error from `CollectionSourceCatalog.ParseEnum`.

## Task 2: Implement `OtherHeroes`

**Files:**
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceEnums.cs`
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs`

- [ ] **Step 1: Add the enum value**

Change `CollectionSourceHeroMode` to:

```csharp
internal enum CollectionSourceHeroMode
{
    SelectedHero,
    AllHeroes,
    FixedHero,
    NeutralOnly,
    OtherHeroes,
}
```

- [ ] **Step 2: Add resolver logic**

In `CollectionSourceOfferPoolResolver.MatchesHero`, add this case after `NeutralOnly`:

```csharp
case CollectionSourceHeroMode.OtherHeroes:
    return MatchesOtherHero(cardHeroes, selectedHero);
```

Add this helper near `MatchesExclusiveHero`:

```csharp
private static bool MatchesOtherHero(IReadOnlyCollection<EHero> cardHeroes, EHero? selectedHero)
{
    if (Contains(cardHeroes, EHero.Common))
        return false;

    if (!ContainsConcreteHero(cardHeroes))
        return false;

    if (!selectedHero.HasValue || selectedHero.Value == EHero.Common)
        return true;

    return !Contains(cardHeroes, selectedHero.Value);
}

private static bool ContainsConcreteHero(IReadOnlyCollection<EHero> cardHeroes)
{
    foreach (var hero in cardHeroes)
    {
        if (hero != EHero.Common)
            return true;
    }
    return false;
}
```

- [ ] **Step 3: Run the resolver tests and verify they pass**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected: the `OtherHeroes` tests pass, but later catalog-count tests still fail until the two new source entries are added and counts are updated.

## Task 3: Add Catalog Entries and Catalog Tests

**Files:**
- Modify: `src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json`
- Modify: `tests/CollectionSourceFiltering.Tests/Program.cs`

- [ ] **Step 1: Update locked catalog counts in tests**

In `tests/CollectionSourceFiltering.Tests/Program.cs`, update:

```csharp
AssertEqual(
    71,
    currentCatalog.Count,
    "Current source catalog should include the 71 known sources after adding Private Pitchfork and Stickybeans."
);
AssertEqual(
    49,
    currentCatalog.Count(entry => entry.Kind == CollectionSourceKind.Merchant),
    "Current source catalog should include the 49 known merchants."
);
AssertEqual(
    22,
    currentCatalog.Count(entry => entry.Kind == CollectionSourceKind.Trainer),
    "Current source catalog should preserve the 22 known trainers."
);
```

- [ ] **Step 2: Add catalog red tests for Private Pitchfork**

Add this block in the current-catalog section after the The Tester test block:

```csharp
var privatePitchfork = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Private Pitchfork", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("15b88e74-024e-40a3-a811-aa5810e68ca2"),
    privatePitchfork.PortraitTemplateId,
    "Private Pitchfork should use the live merchant template id as its portrait id."
);
AssertValues(
    privatePitchfork.SourceTemplateIds.ToArray(),
    new[] { Guid.Parse("15b88e74-024e-40a3-a811-aa5810e68ca2") },
    "Private Pitchfork should index its live merchant template id."
);
var pitchforkNeutralItem = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Common],
    tags: [ECardTag.Tool]
);
var pitchforkNeutralLoot = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Common],
    tags: [ECardTag.Loot]
);
var pitchforkHeroItem = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Vanessa],
    tags: [ECardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            privatePitchfork,
            EHero.Vanessa,
            new[] { pitchforkNeutralItem, pitchforkNeutralLoot, pitchforkHeroItem }
        )
        .OfferedCardIds,
    new[] { pitchforkNeutralItem.Id },
    "Private Pitchfork should offer neutral non-Loot items only."
);
```

- [ ] **Step 3: Add catalog red tests for Stickybeans**

Add this block immediately after the Private Pitchfork block:

```csharp
var stickybeans = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Stickybeans", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("d0276b47-be8a-4bbc-ab55-9f92b352480a"),
    stickybeans.PortraitTemplateId,
    "Stickybeans should use the live merchant template id as its portrait id."
);
AssertValues(
    stickybeans.AvailableHeroes.ToArray(),
    new[]
    {
        EHero.Vanessa,
        EHero.Dooley,
        EHero.Pygmalien,
        EHero.Karnok,
        EHero.Mak,
        EHero.Stelle,
        EHero.Jules,
    },
    "Stickybeans should be visible for concrete heroes and hidden for Common."
);
var stickybeansCommon = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000001"),
    ECardType.Item,
    [EHero.Common]
);
var stickybeansVanessa = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000002"),
    ECardType.Item,
    [EHero.Vanessa]
);
var stickybeansDooley = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000003"),
    ECardType.Item,
    [EHero.Dooley]
);
var stickybeansSharedOther = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000004"),
    ECardType.Item,
    [EHero.Dooley, EHero.Stelle]
);
var stickybeansSharedSelected = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000005"),
    ECardType.Item,
    [EHero.Vanessa, EHero.Dooley]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            stickybeans,
            EHero.Vanessa,
            new[]
            {
                stickybeansCommon,
                stickybeansVanessa,
                stickybeansDooley,
                stickybeansSharedOther,
                stickybeansSharedSelected,
            }
        )
        .OfferedCardIds,
    new[] { stickybeansDooley.Id, stickybeansSharedOther.Id },
    "Stickybeans should offer non-Common items that do not include the selected UI hero."
);
```

- [ ] **Step 4: Run the test and verify catalog entries are missing**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected: failure because `Private Pitchfork` and `Stickybeans` are not present yet.

- [ ] **Step 5: Add Private Pitchfork JSON entry**

Insert this object in `collection-sources.json` under the `tier-specialist` merchant group. Use `order: 5` to avoid renumbering existing tier-specialist entries:

```json
{
  "kind": "Merchant",
  "group": "tier-specialist",
  "order": 5,
  "name": "Private Pitchfork",
  "availableHeroes": [],
  "description": "Sells Neutral items",
  "portraitTemplateId": "15b88e74-024e-40a3-a811-aa5810e68ca2",
  "sourceTemplateIds": [
    "15b88e74-024e-40a3-a811-aa5810e68ca2"
  ],
  "offerSegments": [
    {
      "key": "normal",
      "kind": "Normal",
      "rule": {
        "heroMode": "NeutralOnly",
        "tagsNone": [
          "Loot"
        ]
      }
    }
  ]
}
```

- [ ] **Step 6: Add Stickybeans JSON entry**

Insert this object in the `other-hero` merchant group after Jules. Use `order: 7` to avoid renumbering existing hero-specific merchants:

```json
{
  "kind": "Merchant",
  "group": "other-hero",
  "order": 7,
  "name": "Stickybeans",
  "availableHeroes": [
    "Vanessa",
    "Dooley",
    "Pygmalien",
    "Karnok",
    "Mak",
    "Stelle",
    "Jules"
  ],
  "description": "Sells items from other Heroes",
  "portraitTemplateId": "d0276b47-be8a-4bbc-ab55-9f92b352480a",
  "sourceTemplateIds": [
    "d0276b47-be8a-4bbc-ab55-9f92b352480a"
  ],
  "offerSegments": [
    {
      "key": "normal",
      "kind": "Normal",
      "rule": {
        "heroMode": "OtherHeroes"
      }
    }
  ]
}
```

- [ ] **Step 7: Run the catalog test and verify it passes**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected:

```text
Collection source filtering checks passed.
```

## Task 4: Format, Verify, and Review

**Files:**
- Verify all changed files.

- [ ] **Step 1: Format the C# test file**

Run:

```bash
csharpier format tests/CollectionSourceFiltering.Tests/Program.cs
```

Expected: CSharpier completes without errors.

- [ ] **Step 2: Re-run targeted source tests**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected:

```text
Collection source filtering checks passed.
```

- [ ] **Step 3: Run diff whitespace check**

Run:

```bash
git diff --check
```

Expected: no output and exit code 0.

- [ ] **Step 4: Review the implementation diff**

Run:

```bash
git diff -- src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceEnums.cs \
  src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs \
  src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json \
  tests/CollectionSourceFiltering.Tests/Program.cs
```

Expected:

- Only `OtherHeroes` enum/resolver behavior changed in C# production code.
- Only Private Pitchfork and Stickybeans entries added to `collection-sources.json`.
- Tests cover resolver-level behavior and live-catalog behavior.

- [ ] **Step 5: Optional full-suite run**

Run:

```bash
./run.sh test
```

Expected in the current local environment: all runnable tests pass, but two unrelated `net8.0` compatibility test projects may fail to start if .NET 8 runtime is not installed:

- `tests/CollectionItemLoadArtPatchCompatibility.Tests/CollectionItemLoadArtPatchCompatibility.Tests.csproj`
- `tests/NativeCardPreviewCompatibility.Tests/NativeCardPreviewCompatibility.Tests.csproj`

If those two are the only failures and the error is `Framework: 'Microsoft.NETCore.App', version '8.0.0'`, record that as an environment gap rather than a regression from this change.

## Commit Guidance

If implementation and verification are clean, make one scoped commit:

```bash
git add \
  src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceEnums.cs \
  src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs \
  src/BazaarPlusPlus/Data/CollectionSources/collection-sources.json \
  tests/CollectionSourceFiltering.Tests/Program.cs
git commit -m "Add Private Pitchfork and Stickybeans sources"
```

Do not include unrelated formatting churn or generated artifacts.
