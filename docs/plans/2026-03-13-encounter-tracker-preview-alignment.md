# Encounter Tracker Preview Alignment Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Align `EncounterTracker` and its consumers with the current structured monster preview model while fixing stale encounter cache behavior.

**Architecture:** `EncounterTracker` remains the encounter cache producer, but it now maps monster data from `MonsterDatabase` by encounter template id and stores structured preview fields. Consumers stop depending on legacy string lists and run lifecycle code actively clears cache state.

**Tech Stack:** C# 12, .NET console-style test projects, Bazaar game runtime assemblies, BepInEx mod code

---

### Task 1: Add failing tests for encounter tracker support helpers

**Files:**
- Create: `tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`
- Create: `tests/EncounterTrackerState.Tests/Program.cs`
- Modify: `BazaarPlusPlus.csproj`

**Step 1: Write the failing test**

Add assertions that:

- supported states are `Encounter`, `Choice`, `Loot`, `Pedestal`
- unsupported states such as `Combat`, `PVPCombat`, `LevelUp` are rejected
- clearing helper resets all three encounter cache fields

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`
Expected: FAIL because the helper methods do not exist yet

**Step 3: Write minimal implementation**

Expose small internal helpers from `EncounterTracker` and a public/internal reset entry point usable by tests and lifecycle code.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`
Expected: PASS

### Task 2: Add failing tests for structured preview conversion

**Files:**
- Create: `tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj`
- Create: `tests/EncounterPreviewConversion.Tests/Program.cs`
- Modify: `Models/RunInfo.cs`
- Modify: `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`

**Step 1: Write the failing test**

Add assertions that a structured cached preview with board cards and skills can be converted into `PreviewCardSpec` lists and that item/skill template ids are preserved.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj`
Expected: FAIL because legacy conversion is still string-list based

**Step 3: Write minimal implementation**

Replace `RunInfo.MonsterPreview.Items/Skills` with structured preview entry lists and update runtime fallback conversion accordingly.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj`
Expected: PASS

### Task 3: Update encounter tracker and consumers

**Files:**
- Modify: `Game/EncounterTracker.cs`
- Modify: `Models/ModState.cs`
- Modify: `Models/RunInfo.cs`
- Modify: `Game/DebugPanel/DebugPanel.cs`
- Modify: `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`

**Step 1: Write the failing integration-oriented assertions**

Use the new focused tests as regression coverage and add any missing assertions needed for changed field names or state reset behavior.

**Step 2: Run tests to verify they still fail for the right reason**

Run:

```bash
dotnet run --project tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj
dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj
```

Expected: FAIL only because production code still uses legacy behavior

**Step 3: Write minimal implementation**

- restrict encounter updates to supported states
- map preview cache from `MonsterDatabase.TryGetByEncounterId(card.TemplateId, out monster)`
- reset cache from run end / interruption
- update debug panel rendering to structured preview entries

**Step 4: Run tests to verify they pass**

Run:

```bash
dotnet run --project tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj
dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj
```

Expected: PASS

### Task 4: Run regression checks

**Files:**
- Test: `tests/MonsterLockToggleGate.Tests/Program.cs`
- Test: `tests/ItemEnchantPreview.Tests/Program.cs`

**Step 1: Run existing targeted tests**

Run:

```bash
dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj
dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj
```

Expected: PASS

**Step 2: Build the main project if environment references are available**

Run: `dotnet build BazaarPlusPlus.csproj`
Expected: PASS if local game assemblies are available; otherwise record the environment limitation
