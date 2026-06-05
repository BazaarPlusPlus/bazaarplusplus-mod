# 重构执行 Prompt v2：把 6 个程序集收进 `src/`（每项目一目录）

> 读者是一个**对此前分析一无所知的全新 agent**。本文件自包含，不依赖任何对话上下文。
> 工作目录：`bazaarplusplus-mod/`（一个独立 git 仓库；不要在父目录 `bpp/` 跑仓库级命令）。

---

## 0. 当前事实与目标终态

### 当前代码事实

当前仓库把 6 个 `.csproj` 与源码目录平铺在仓库根目录。这个结论来自代码，不来自文档：

- 主插件 csproj 在根目录，且为了避免 SDK 默认通配吞掉兄弟项目源码，显式排除 `tests/`、`decompiled/`、`ModApi/`、`Storage/`、`BazaarAgent/`、`Localization/`、`BazaarAgentHost/` 等路径（`BazaarPlusPlus.csproj:32-62`）。
- 主插件直接引用根目录里的 3 个兄弟 csproj（`BazaarPlusPlus.csproj:135-139`）。
- 纯子程序集用 `Compile Remove="**/*.cs"` + `Compile Include="<目录>/**/*.cs"` 锁住自己的源码锥（例如 `BazaarPlusPlus.ModApi.csproj:18-22`、`BazaarPlusPlus.Storage.csproj:17-20`、`BazaarPlusPlus.Localization.csproj:15-18`、`BazaarPlusPlus.BazaarAgent.csproj:16-19`）。
- `BazaarPlusPlus.BazaarAgentHost.csproj` 同样用 `Compile Remove/Include` 锁住 `BazaarAgentHost/**/*.cs`，并引用根目录里的 `BazaarPlusPlus.BazaarAgent.csproj` 与 `BazaarPlusPlus.csproj`（`BazaarPlusPlus.BazaarAgentHost.csproj:22-26,77-82`）。
- `Directory.Build.props` 需要按项目名给多个根级 csproj 分配独立 `obj/`、`bin/`（`Directory.Build.props:7-35`）。
- `run.sh` 当前直接构建根目录 `BazaarPlusPlus.csproj` 与 `BazaarPlusPlus.BazaarAgentHost.csproj`（`run.sh:51-63,66-81`）。
- `tests/Architecture.Tests/CoreLayeringTests.cs` 不只依赖测试工程引用路径，还直接用 repo root 拼生产源码路径与 csproj 路径，例如 `Core/`、`Game/`、`GameInterop/`、`BazaarAgent/`、`BazaarAgentHost/`、`BazaarPlusPlus.csproj`、`BazaarPlusPlus.BazaarAgentHost.csproj`（`CoreLayeringTests.cs:33,80,123,300,344,414,512,551`）。这些源码路径迁移必须和测试源码同步完成。

### 目标终态

每个程序集拥有一个独立项目目录，全部放进 `src/`，使项目的默认编译锥天然不重叠，并删除为根目录平铺而存在的构建胶水。

```
bazaarplusplus-mod/
├── src/
│   ├── BazaarPlusPlus/
│   │   ├── BazaarPlusPlus.csproj
│   │   ├── Plugin.cs
│   │   ├── BppComposition.cs
│   │   ├── BppPluginMetadata.cs
│   │   ├── Core/
│   │   ├── Game/
│   │   ├── GameInterop/
│   │   ├── Infrastructure/
│   │   ├── Patches/
│   │   ├── Properties/
│   │   ├── Data/
│   │   └── Resources/
│   ├── BazaarPlusPlus.ModApi/
│   │   ├── BazaarPlusPlus.ModApi.csproj
│   │   └── ... source files formerly under ModApi/
│   ├── BazaarPlusPlus.Storage/
│   ├── BazaarPlusPlus.Localization/
│   ├── BazaarPlusPlus.BazaarAgent/
│   └── BazaarPlusPlus.BazaarAgentHost/
├── tests/
├── decompiled/
├── docs/
├── native/
├── Directory.Build.props
├── run.sh
├── README.md
├── README_en.md
└── CLAUDE.md
```

### Non-goals

- Do not split or reorganize main-plugin internals. Move `Core/`, `Game/`, `GameInterop/`, `Infrastructure/`, and `Patches/` as intact directories under `src/BazaarPlusPlus/`.
- Do not move `tests/`, `docs/`, `decompiled/`, or `native/`.
- Do not edit business logic, DTOs, namespaces, `RootNamespace`, or `AssemblyName`.
- Do not edit `decompiled/`; it is read-only reference code.
- Do not bump `BppVersion` for this physical-layout-only refactor unless the user explicitly asks.

---

## 1. Hard Rules

1. Code is the source of truth. Re-run `rg`/`grep` before editing rather than trusting counts in this prompt.
2. Use `git mv` for every moved tracked file or directory.
3. Keep output DLL names unchanged:
   - `BazaarPlusPlus`
   - `BazaarPlusPlus.ModApi`
   - `BazaarPlusPlus.Storage`
   - `BazaarPlusPlus.Localization`
   - `BazaarPlusPlus.BazaarAgent`
   - `BazaarPlusPlus.BazaarAgentHost`
4. Keep commits/package boundaries reviewable. No formatting pass unless necessary; if formatting touches unrelated files, do not include those changes.
5. Each work package must compile/test as far as the local environment allows, then stop for human review before continuing.

---

## 2. Work Packages

### Package 1 - Physical Move + Build/Test Path Repair

This package is atomic. After moving files, the repo is broken until csproj paths, scripts, test project files, and architecture-test source paths are repaired.

#### 1.1 Preflight

```bash
git status --short --branch
git switch -c refactor/src-layout
```

If the worktree is not clean, stop and report the dirty files. Do not stash or revert user changes without explicit instruction.

#### 1.2 Move child assemblies into `src/<AssemblyName>/`

```bash
mkdir -p src
git mv ModApi          src/BazaarPlusPlus.ModApi
git mv Storage         src/BazaarPlusPlus.Storage
git mv Localization    src/BazaarPlusPlus.Localization
git mv BazaarAgent     src/BazaarPlusPlus.BazaarAgent
git mv BazaarAgentHost src/BazaarPlusPlus.BazaarAgentHost

git mv BazaarPlusPlus.ModApi.csproj          src/BazaarPlusPlus.ModApi/
git mv BazaarPlusPlus.Storage.csproj         src/BazaarPlusPlus.Storage/
git mv BazaarPlusPlus.Localization.csproj    src/BazaarPlusPlus.Localization/
git mv BazaarPlusPlus.BazaarAgent.csproj     src/BazaarPlusPlus.BazaarAgent/
git mv BazaarPlusPlus.BazaarAgentHost.csproj src/BazaarPlusPlus.BazaarAgentHost/
```

#### 1.3 Move the main plugin into `src/BazaarPlusPlus/`

```bash
mkdir -p src/BazaarPlusPlus
git mv Core Game GameInterop Infrastructure Patches Properties Data Resources src/BazaarPlusPlus/
git mv Plugin.cs BppComposition.cs BppPluginMetadata.cs src/BazaarPlusPlus/
git mv BazaarPlusPlus.csproj src/BazaarPlusPlus/
```

Leave these at repo root: `tests/`, `decompiled/`, `docs/`, `native/`, `Directory.Build.props`, `run.sh`, `README*.md`, `CLAUDE.md`, `AGENTS.md`, `LICENSE`, `CONTEXT.md`, and repo config files.

#### 1.4 Update `src/BazaarPlusPlus/BazaarPlusPlus.csproj`

- Delete the root-layout-only exclusion group that currently removes `tests/**`, `tools/**`, `decompiled/**`, child assembly directories, and `**/bin/**;**/obj/**` (`BazaarPlusPlus.csproj:32-62` before the move).
- Keep the embedded resource item group. After the move, `Data\...` and `Resources\...` remain relative to `src/BazaarPlusPlus/BazaarPlusPlus.csproj`, so the resource names and runtime loader behavior stay unchanged.
- Change `BPPInstallerSourcePath`:
  - from `$(ProjectRoot)/../bazaarplusplus-installer/src-tauri/resources`
  - to `$(ProjectRoot)/../../../bazaarplusplus-installer/src-tauri/resources`
- Change project references:
  - `BazaarPlusPlus.ModApi.csproj` -> `../BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj`
  - `BazaarPlusPlus.Storage.csproj` -> `../BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj`
  - `BazaarPlusPlus.Localization.csproj` -> `../BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj`
- Do not change game `<Reference HintPath="$(ManagedPath)\...">` entries, `ManagedPath`/`GamePath` probing, `CopyToBepInExPlugins`, `CopyToInstallerSource`, `PackageInstallerSource`, or `BuildAll` semantics.

#### 1.5 Update pure child csprojs

In each of these files, delete the `Compile Remove="**/*.cs"` + `Compile Include="<old-directory>/**/*.cs"` item group:

- `src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj`
- `src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj`
- `src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj`
- `src/BazaarPlusPlus.BazaarAgent/BazaarPlusPlus.BazaarAgent.csproj`

Keep package refs, `AssemblyName`, `RootNamespace`, `TargetFramework`, nullable settings, and versions unchanged.

#### 1.6 Update `BazaarPlusPlus.BazaarAgentHost.csproj`

In `src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj`:

- Delete `Compile Remove="**/*.cs"` + `Compile Include="BazaarAgentHost/**/*.cs"`.
- Change `BPPInstallerSourcePath` to `$(ProjectRoot)/../../../bazaarplusplus-installer/src-tauri/resources`.
- Change project references:
  - `BazaarPlusPlus.BazaarAgent.csproj` -> `../BazaarPlusPlus.BazaarAgent/BazaarPlusPlus.BazaarAgent.csproj`
  - `BazaarPlusPlus.csproj` -> `../BazaarPlusPlus/BazaarPlusPlus.csproj`
- Preserve the existing `Private="false"` on the main-plugin reference.
- Do not change `ManagedPath` probing, game references, host copy targets, or production re-zip behavior.

#### 1.7 Simplify `Directory.Build.props`

Remove the root-layout-only project-name-specific `BaseIntermediateOutputPath` / `BaseOutputPath` blocks (`Directory.Build.props:7-35` before the move).

Keep:

```xml
<Project>
  <PropertyGroup>
    <BppVersion>...</BppVersion>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

Root `Directory.Build.props` will still be found by projects under `src/**` and `tests/**`.

#### 1.8 Update `run.sh`

Change the build targets only:

- `dotnet build BazaarPlusPlus.csproj` -> `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `dotnet build BazaarPlusPlus.BazaarAgentHost.csproj` -> `dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj`

Do not change:

- `find tests -mindepth 2 -maxdepth 2`
- `csharpier format .`
- `INSTALLER_SQLITE`, because `run.sh` remains at repo root.
- command-line flags (`--with-bazaaragent`, `--prod`).

#### 1.9 Update test project references and source links

Find all test csproj paths that point out of `tests/`:

```bash
grep -rn 'Include="\.\./\.\.\|Include="\.\.\\\.\.' tests/*/*.csproj
```

Update by rule, preserving each file's existing slash style where practical:

- `../../BazaarPlusPlus.csproj` -> `../../src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `..\..\BazaarPlusPlus.csproj` -> `..\..\src\BazaarPlusPlus\BazaarPlusPlus.csproj`
- `../../BazaarPlusPlus.<Sub>.csproj` -> `../../src/BazaarPlusPlus.<Sub>/BazaarPlusPlus.<Sub>.csproj`
- `..\..\BazaarPlusPlus.<Sub>.csproj` -> `..\..\src\BazaarPlusPlus.<Sub>\BazaarPlusPlus.<Sub>.csproj`
- `../../Core/...`, `../../Game/...`, `../../GameInterop/...`, `../../Infrastructure/...`, `../../Patches/...` -> `../../src/BazaarPlusPlus/<same path>`
- `..\..\Core\...`, `..\..\Game\...`, `..\..\GameInterop\...`, `..\..\Infrastructure\...`, `..\..\Patches\...` -> `..\..\src\BazaarPlusPlus\<same path>`

Do not rely on old expected counts. Current local evidence showed many more source-linked files than the earlier draft counted; grep is authoritative.

#### 1.10 Update `tests/Architecture.Tests/CoreLayeringTests.cs`

This is part of Package 1, not optional documentation cleanup. The test currently encodes repo-root production paths and will fail after the move.

Make the architecture test layout-aware with small helpers, for example:

```csharp
private static string MainSourceRoot(string repoRoot) => Path.Combine(repoRoot, "src", "BazaarPlusPlus");
private static string ProjectRoot(string repoRoot, string projectName) => Path.Combine(repoRoot, "src", projectName);
```

Then update direct path assumptions:

- `Path.Combine(repoRoot, "Core")` -> under `src/BazaarPlusPlus/Core`
- `Path.Combine(repoRoot, "Game", ...)` -> under `src/BazaarPlusPlus/Game/...`
- `Path.Combine(repoRoot, "GameInterop")` -> under `src/BazaarPlusPlus/GameInterop`
- `Path.Combine(repoRoot, "BppComposition.cs")` -> `src/BazaarPlusPlus/BppComposition.cs`
- `Path.Combine(repoRoot, "BazaarAgent")` -> `src/BazaarPlusPlus.BazaarAgent`
- `Path.Combine(repoRoot, "BazaarAgentHost", ...)` -> `src/BazaarPlusPlus.BazaarAgentHost/...`
- `Path.Combine(repoRoot, "BazaarPlusPlus.csproj")` -> `src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `Path.Combine(repoRoot, "BazaarPlusPlus.BazaarAgentHost.csproj")` -> `src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj`
- The scan of top-level `*.cs` in the BazaarPlusPlus assembly should scan top-level `*.cs` under `src/BazaarPlusPlus/`.
- The loop over `"Core", "Game", "GameInterop", "Patches", "Infrastructure"` should resolve beneath `src/BazaarPlusPlus/`.

Rewrite the csproj-shape assertion that currently requires:

```xml
<Compile Remove="BazaarAgent/**" />
<Compile Remove="BazaarAgentHost/**" />
```

Those removes are root-layout glue and should disappear. Replace that part with assertions that fit the new layout:

- `src/BazaarPlusPlus/BazaarPlusPlus.csproj` contains no `ProjectReference` to `BazaarPlusPlus.BazaarAgent.csproj` or `BazaarPlusPlus.BazaarAgentHost.csproj`.
- `src/BazaarPlusPlus/BazaarPlusPlus.csproj` still unconditionally scrubs `BazaarPlusPlus.BazaarAgent.dll` and `BazaarPlusPlus.BazaarAgentHost.dll` from Debug plugin output and both Release installer payload trees.
- Child source directories are outside the main project directory, so no `Compile Remove="BazaarAgent/**"` / `BazaarAgentHost/**` assertion is needed.

Keep the behavioral/layering intent of the tests. Only update filesystem anchors and root-layout-specific csproj-shape expectations.

#### 1.11 Clean stale root build outputs

```bash
rm -rf obj bin
```

These are ignored build outputs from the old root-level output layout.

#### 1.12 Package 1 verification

Run focused builds first:

```bash
dotnet build src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj
dotnet build src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj
dotnet build src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj
dotnet build src/BazaarPlusPlus.BazaarAgent/BazaarPlusPlus.BazaarAgent.csproj
```

Then run main/host builds if game assemblies are available:

```bash
./run.sh build
./run.sh build --with-bazaaragent
```

Then tests:

```bash
set -o pipefail
./run.sh test 2>&1 | tee /tmp/bpp-test.log
grep -n "Failed test projects:" /tmp/bpp-test.log && echo "test failures present" || echo "no failed test project marker"
```

`run.sh test` currently returns non-zero on failed test projects (`run.sh:122-126` before the move). `set -o pipefail` is still required when piping through `tee`, otherwise the pipeline can hide the left-hand exit code.

If the local machine cannot resolve game assemblies, mark only the main/host builds and game-coupled tests as `to-verify`; still run the pure child assembly builds and any pure tests that do not require game DLLs.

Stop after Package 1. Report verification results and wait for review.

---

### Package 2 - Current Documentation Sync

Package 2 updates current docs to match the new physical layout. Do not rewrite archived historical plans unless they are actively used as current guidance.

#### 2.1 Update primary repo docs

Update at minimum:

- `CLAUDE.md`
- `README.md`
- `README_en.md`
- `docs/mod-features-overview.md`
- `docs/features/bazaar-agent.md`
- `docs/reference/bazaar-agent-decision-surface.md`

Use grep, not memory:

```bash
rg -n "All six|repo root|BazaarAgent/|BazaarAgentHost/|ModApi/|Storage/|Localization/|Plugin.cs|BppComposition.cs|Core/|GameInterop/|BazaarPlusPlus\\.csproj|BazaarPlusPlus\\.BazaarAgentHost\\.csproj" CLAUDE.md README.md README_en.md docs -g '!design/archive/**'
```

Required semantic updates:

- Build command examples should point at `src/BazaarPlusPlus/BazaarPlusPlus.csproj` when they name a csproj directly.
- Architecture sections should say project files live under `src/<AssemblyName>/`.
- Main plugin internals should be described as `src/BazaarPlusPlus/Core`, `src/BazaarPlusPlus/Game`, etc.
- BazaarAgent pure core should be described as `src/BazaarPlusPlus.BazaarAgent`.
- BazaarAgent host should be described as `src/BazaarPlusPlus.BazaarAgentHost`.
- The `run.sh` usage remains unchanged.

#### 2.2 Add a regression guard for future root-level csprojs

Add or extend an architecture test so future independent assemblies do not return to repo-root flat layout. Preferred guard:

- root directory may contain no `BazaarPlusPlus*.csproj`;
- expected production csprojs must exist under `src/<AssemblyName>/<AssemblyName>.csproj`;
- root may keep `Directory.Build.props`.

Put this in `tests/Architecture.Tests/CoreLayeringTests.cs` unless there is a better existing architecture-test file.

#### 2.3 Documentation verification

```bash
rg -n "All six csproj files live in the repo root|root directory `BazaarAgent/`|根目录 `BazaarAgent/`|BazaarAgentHost/" CLAUDE.md README.md README_en.md docs -g '!design/archive/**'
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
```

Stop after Package 2. Report diff and verification results; wait for review.

---

### Package 3 - Full Verification and Handoff

Run the final matrix available on this machine:

```bash
./run.sh all
./run.sh all --with-bazaaragent
set -o pipefail
./run.sh test 2>&1 | tee /tmp/bpp-test.log
grep -n "Failed test projects:" /tmp/bpp-test.log && echo "test failures present" || echo "no failed test project marker"
git diff --check
```

Review the diff shape:

```bash
git diff --stat
git diff --name-status
git diff -- 'src/**/*.cs'
```

Expected shape:

- production `.cs` files are moves only; no namespace/business-logic edits;
- csproj files only change paths and remove root-layout compile/output glue;
- `run.sh` only changes csproj paths;
- tests change path anchors and architecture assertions for the new layout;
- current docs describe `src/<AssemblyName>/`.

If the user asked for commit/finalization, follow repo wrap-up rules after self-review: commit only the intended files. Do not merge to `master`, push, or delete branches unless the user explicitly asked for that finalization in the current task.

---

## 3. Rollback

All implementation changes should live on `refactor/src-layout`. To discard the branch:

```bash
git switch master
git branch -D refactor/src-layout
```

If still on the branch with uncommitted work and the user explicitly approves discarding it:

```bash
git reset --hard HEAD
git switch master
git branch -D refactor/src-layout
```

Do not run destructive commands without explicit approval.

---

## 4. One-line Handoff

> impl this design `docs/refactor-src-layout-prompt.md` exactly. This is a physical layout refactor only: move all 6 assemblies under `src/<AssemblyName>/` with `git mv`, remove root-layout csproj/props glue, update run.sh, test csprojs, `tests/Architecture.Tests/CoreLayeringTests.cs`, and current docs. Do not change namespaces, AssemblyName, RootNamespace, DTOs, or behavior. Each package must build/test as far as the local environment allows, then stop for review.
