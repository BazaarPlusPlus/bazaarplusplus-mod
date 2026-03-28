# Legendary EndOfMatch Rating Line Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Legendary-only rating delta line under the existing end-of-match rank display, formatted like `1420 -> 1436 (+16)` with colored delta text and original-game typography reuse.

**Architecture:** Keep the native end-of-run rank UI and animation flow intact, then attach a small supplemental `TextMeshProUGUI` line via Harmony patching on `EndOfRunRankController`. Extract formatting and Legendary gating into a small helper so behavior is testable without Unity scene integration, while the patch layer only resolves data, clones TMP styling, and updates visibility/text.

**Tech Stack:** C#, Harmony, TextMeshPro, xUnit

---

### Task 1: Add a failing formatter test

**Files:**
- Create: `tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`
- Create: `tests/EndOfRunRatingDisplay.Tests/LegendaryEndOfMatchRatingFormatterTests.cs`
- Test: `tests/EndOfRunRatingDisplay.Tests/LegendaryEndOfMatchRatingFormatterTests.cs`

**Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(1420, 1436, "<color=#6DD16B>(+16)</color>")]
[InlineData(1436, 1420, "<color=#E06767>(-16)</color>")]
[InlineData(1436, 1436, "<color=#D8D8D8>(0)</color>")]
public void BuildLine_FormatsBeforeAfterAndColoredDelta(int before, int after, string expectedDelta)
{
    var line = LegendaryEndOfMatchRatingFormatter.BuildLine(before, after);

    Assert.Equal($"{{before}} -> {{after}} {expectedDelta}".Replace("{before}", before.ToString()).Replace("{after}", after.ToString()), line);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: FAIL because `LegendaryEndOfMatchRatingFormatter` does not exist yet.

**Step 3: Write minimal implementation**

Create a small helper with:
- `BuildLine(int before, int after)`
- delta color selection for positive, negative, zero
- no Unity dependencies

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add tests/EndOfRunRatingDisplay.Tests Game/EndOfRun/LegendaryEndOfMatchRatingFormatter.cs
git commit -m "Add Legendary end-of-match rating formatter"
```

### Task 2: Add Legendary gating tests

**Files:**
- Modify: `tests/EndOfRunRatingDisplay.Tests/LegendaryEndOfMatchRatingFormatterTests.cs`
- Create: `Game/EndOfRun/LegendaryEndOfMatchRatingDisplayPolicy.cs`
- Test: `tests/EndOfRunRatingDisplay.Tests/LegendaryEndOfMatchRatingFormatterTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void ShouldShow_ReturnsFalse_ForNonLegendary()
{
    Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow("Diamond", 1420, 1436));
}

[Fact]
public void ShouldShow_ReturnsTrue_ForLegendaryWithBothRatings()
{
    Assert.True(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow("Legendary", 1420, 1436));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: FAIL because the policy helper does not exist yet.

**Step 3: Write minimal implementation**

Add a helper that returns true only when:
- rank is Legendary
- before/after ratings both exist

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add tests/EndOfRunRatingDisplay.Tests Game/EndOfRun/LegendaryEndOfMatchRatingDisplayPolicy.cs
git commit -m "Gate end-of-match rating line to Legendary only"
```

### Task 3: Patch the native end-of-run rank controller

**Files:**
- Create: `Patches/EndOfRun/LegendaryEndOfMatchRatingPatches.cs`
- Modify: `Game/EndOfRun/LegendaryEndOfMatchRatingFormatter.cs`
- Modify: `Game/EndOfRun/LegendaryEndOfMatchRatingDisplayPolicy.cs`
- Test: `tests/EndOfRunRatingDisplay.Tests/LegendaryEndOfMatchRatingFormatterTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void PatchSource_TargetsEndOfRunRankController_AndUsesLegendaryGate()
{
    var source = File.ReadAllText("../../../Patches/EndOfRun/LegendaryEndOfMatchRatingPatches.cs");

    Assert.Contains("EndOfRunRankController", source, StringComparison.Ordinal);
    Assert.Contains("Legendary", source, StringComparison.Ordinal);
    Assert.Contains("RatingBeforeRun", source, StringComparison.Ordinal);
    Assert.Contains("RatingAfterRun", source, StringComparison.Ordinal);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: FAIL because the patch source file does not exist yet.

**Step 3: Write minimal implementation**

Implement a Harmony patch that:
- hooks the native end-of-run rank controller after init/display refresh
- resolves before/after rating from native runtime data
- clones a nearby rank `TextMeshProUGUI` style for font/material/alignment
- inserts a smaller line below the rank label
- uses formatter output with TMP rich text color tags
- hides the line for all non-Legendary results or missing ratings

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add Patches/EndOfRun/LegendaryEndOfMatchRatingPatches.cs Game/EndOfRun tests/EndOfRunRatingDisplay.Tests
git commit -m "Show Legendary end-of-match rating delta"
```

### Task 4: Verify with focused project checks

**Files:**
- Modify: `Patches/EndOfRun/LegendaryEndOfMatchRatingPatches.cs`
- Modify: `Game/EndOfRun/LegendaryEndOfMatchRatingFormatter.cs`
- Modify: `Game/EndOfRun/LegendaryEndOfMatchRatingDisplayPolicy.cs`
- Test: `tests/EndOfRunRatingDisplay.Tests/LegendaryEndOfMatchRatingFormatterTests.cs`

**Step 1: Run focused tests**

Run: `dotnet test tests/EndOfRunRatingDisplay.Tests/EndOfRunRatingDisplay.Tests.csproj`

Expected: PASS

**Step 2: Run the smallest relevant build**

Run: `dotnet build BazaarPlusPlus.csproj -p:ManagedPath="<game managed path>"`

Expected: PASS

**Step 3: Inspect for regressions**

Check:
- line is not shown for non-Legendary ranks
- line keeps original rank label intact
- delta color appears via TMP rich text

**Step 4: Commit**

```bash
git add Patches/EndOfRun Game/EndOfRun tests/EndOfRunRatingDisplay.Tests docs/plans/2026-03-28-legendary-endofmatch-rating-line.md
git commit -m "Add Legendary end-of-match rating line"
```
