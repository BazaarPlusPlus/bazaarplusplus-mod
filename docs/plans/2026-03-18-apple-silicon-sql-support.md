# Apple Silicon SQL Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restrict BazaarPlusPlus macOS SQL runtime support to Apple Silicon (`osx-arm64`) and make Intel macOS builds fail explicitly.

**Architecture:** Keep the existing Windows SQLite runtime handling unchanged, but remove the macOS `osx-x64` fallback from the mod project file. Add a build-time guard so macOS hosts that are not `arm64` fail fast instead of producing a misleading package, then update contract tests and compatibility docs to match.

**Tech Stack:** MSBuild, .NET, SQLitePCLRaw, C# contract tests, Markdown docs

---

### Task 1: Lock the contract test to Apple Silicon-only macOS support

**Files:**
- Modify: `tests/RunLoggingModels.Tests/Program.cs`
- Test: `tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

**Step 1: Write the failing test**

Add assertions that require `BazaarPlusPlus.csproj` to:
- declare `MacSqliteRuntimeRid` as `osx-arm64`
- reject `osx-x64`
- include an explicit Apple Silicon-only macOS build error

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`
Expected: FAIL because the project file still allows `osx-x64`.

**Step 3: Commit**

```bash
git add tests/RunLoggingModels.Tests/Program.cs
git commit -m "test: require apple silicon macos sqlite support"
```

### Task 2: Enforce Apple Silicon-only macOS SQLite packaging

**Files:**
- Modify: `BazaarPlusPlus.csproj`
- Test: `tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

**Step 1: Write minimal implementation**

Update the project file to:
- set `MacSqliteRuntimeRid` to `osx-arm64`
- remove the `osx-x64` fallback
- add a macOS host validation target that errors on non-`arm64`

**Step 2: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`
Expected: PASS

**Step 3: Commit**

```bash
git add BazaarPlusPlus.csproj tests/RunLoggingModels.Tests/Program.cs
git commit -m "build: restrict macos sqlite runtime to apple silicon"
```

### Task 3: Document the compatibility boundary

**Files:**
- Modify: `README.md`

**Step 1: Update docs**

Add a short compatibility note that macOS SQL-backed features are supported only on Apple Silicon and that Intel macOS is unsupported.

**Step 2: Re-run targeted verification**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`
Expected: PASS

**Step 3: Commit**

```bash
git add README.md
git commit -m "docs: clarify apple silicon macos sqlite support"
```
