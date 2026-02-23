# .NET 8-Only Cutover Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Standardize STIGForge runtime targets on .NET 8 only, remove net48 compatibility constraints, and keep the solution green.

**Architecture:** Convert multi-target class libraries from `net48;net8.0` to `net8.0`, remove net48-only references/conditionals, then simplify compatibility code that existed only for framework support. Keep runtime behavior unchanged while removing legacy target friction. Validate through full solution tests and targeted documentation alignment.

**Tech Stack:** .NET 8 SDK, C#/.csproj multi-target cleanup, xUnit solution tests, Markdown docs.

---

### Task 1: Baseline and fail-first checks

**Files:**
- Verify: `src/STIGForge.Shared/STIGForge.Shared.csproj`
- Verify: `src/STIGForge.Core/STIGForge.Core.csproj`
- Verify: `src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj`
- Verify: `src/STIGForge.Content/STIGForge.Content.csproj`
- Verify: `src/STIGForge.Apply/STIGForge.Apply.csproj`
- Verify: `src/STIGForge.Verify/STIGForge.Verify.csproj`
- Verify: `src/STIGForge.Build/STIGForge.Build.csproj`
- Verify: `src/STIGForge.Export/STIGForge.Export.csproj`
- Verify: `src/STIGForge.Reporting/STIGForge.Reporting.csproj`
- Verify: `src/STIGForge.Content/Import/ContentPackImporter.cs`

**Step 1: Run pre-change target scan**

Run: `rg --line-number "net48|TargetFrameworks>net48;net8.0|Condition=\"'\$\(TargetFramework\)' == 'net48'\"" src/STIGForge.*/*.csproj`

Expected: matches are found (current state is not yet cut over).

**Step 2: Run pre-change compatibility-shim scan**

Run: `rg --line-number "private static string GetRelativePath\(|private static string AppendDirSeparator\(|candidate\[candidate.Length - 1\]" src/STIGForge.Content/Import/ContentPackImporter.cs`

Expected: matches are found (current code still carries net48-safe shim logic).

**Step 3: Capture baseline status**

Run: `git status --short`

Expected: baseline visible before edits.

### Task 2: Convert class libraries to .NET 8-only

**Files:**
- Modify: `src/STIGForge.Shared/STIGForge.Shared.csproj`
- Modify: `src/STIGForge.Core/STIGForge.Core.csproj`
- Modify: `src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj`
- Modify: `src/STIGForge.Content/STIGForge.Content.csproj`
- Modify: `src/STIGForge.Apply/STIGForge.Apply.csproj`
- Modify: `src/STIGForge.Verify/STIGForge.Verify.csproj`
- Modify: `src/STIGForge.Build/STIGForge.Build.csproj`
- Modify: `src/STIGForge.Export/STIGForge.Export.csproj`
- Modify: `src/STIGForge.Reporting/STIGForge.Reporting.csproj`

**Step 1: Replace multi-target declarations with single target**

Apply this exact conversion in each file currently using dual targets:

```xml
<TargetFrameworks>net48;net8.0</TargetFrameworks>
```

becomes:

```xml
<TargetFramework>net8.0</TargetFramework>
```

**Step 2: Remove net48-only conditional references**

- Delete this block from `src/STIGForge.Core/STIGForge.Core.csproj`:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
</ItemGroup>
```

- Delete this block from `src/STIGForge.Content/STIGForge.Content.csproj`:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <Reference Include="System.IO.Compression" />
</ItemGroup>
```

- Delete this block from `src/STIGForge.Export/STIGForge.Export.csproj`:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <Reference Include="System.IO.Compression" />
  <Reference Include="System.IO.Compression.FileSystem" />
</ItemGroup>
```

- Delete this block from `src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj`:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <Reference Include="System.Security" />
</ItemGroup>
```

**Step 3: Normalize net8-only package condition in Infrastructure**

Move this package reference into the main package `ItemGroup` in `src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj`:

```xml
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.0.5" />
```

Then remove the `Condition="'$(TargetFramework)' == 'net8.0'"` wrapper.

**Step 4: Verify net48 target removal from source projects**

Run: `rg --line-number "net48|Condition=\"'\$\(TargetFramework\)' == 'net48'\"" src/STIGForge.*/*.csproj`

Expected: no matches.

**Step 5: Commit project-target cutover**

Run:
`git add src/STIGForge.Shared/STIGForge.Shared.csproj src/STIGForge.Core/STIGForge.Core.csproj src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj src/STIGForge.Content/STIGForge.Content.csproj src/STIGForge.Apply/STIGForge.Apply.csproj src/STIGForge.Verify/STIGForge.Verify.csproj src/STIGForge.Build/STIGForge.Build.csproj src/STIGForge.Export/STIGForge.Export.csproj src/STIGForge.Reporting/STIGForge.Reporting.csproj && git commit -m "chore(runtime): standardize core libraries on net8 only"`

Expected: commit succeeds with csproj-only runtime cleanup.

### Task 3: Remove net48 compatibility shim in content importer

**Files:**
- Modify: `src/STIGForge.Content/Import/ContentPackImporter.cs`

**Step 1: Write failing check for shim patterns**

Run: `rg --line-number "private static string GetRelativePath\(|private static string AppendDirSeparator\(|candidate\[candidate.Length - 1\]" src/STIGForge.Content/Import/ContentPackImporter.cs`

Expected: matches found before cleanup.

**Step 2: Restore modern API usage and remove shim methods**

Make these exact replacements:

```csharp
GetRelativePath(extractionRoot, sourceFile)
```

becomes:

```csharp
Path.GetRelativePath(extractionRoot, sourceFile)
```

```csharp
GetRelativePath(extractionRoot, path)
```

becomes:

```csharp
Path.GetRelativePath(extractionRoot, path)
```

```csharp
GetRelativePath(extractionRoot, fileDirectory)
```

becomes:

```csharp
Path.GetRelativePath(extractionRoot, fileDirectory)
```

```csharp
candidate[candidate.Length - 1]
```

becomes:

```csharp
candidate[^1]
```

Then remove these two methods entirely:

- `private static string GetRelativePath(string root, string path)`
- `private static string AppendDirSeparator(string path)`

**Step 3: Run targeted project build**

Run: `dotnet build src/STIGForge.Content/STIGForge.Content.csproj -f net8.0`

Expected: build succeeds.

**Step 4: Commit shim cleanup**

Run:
`git add src/STIGForge.Content/Import/ContentPackImporter.cs && git commit -m "refactor(import): remove legacy framework path compatibility shim"`

Expected: commit succeeds with importer cleanup only.

### Task 4: Update active architecture documentation

**Files:**
- Modify: `docs/Architecture.md`

**Step 1: Identify outdated dual-target statement**

Run: `rg --line-number "net48|both `net8.0` and `net48`|API constraints" docs/Architecture.md`

Expected: at least one match showing outdated runtime claim.

**Step 2: Rewrite the runtime section for net8-only policy**

Replace wording that claims dual targeting with wording that states:

- Core/Infrastructure (and supporting class libraries) are standardized on `.NET 8`.
- Modern API constraints previously required for net48 compatibility are removed.

**Step 3: Validate active docs no longer reference net48 policy**

Run: `rg --line-number "net48" docs/Architecture.md`

Expected: no matches.

**Step 4: Commit doc alignment**

Run:
`git add docs/Architecture.md && git commit -m "docs(architecture): align runtime policy to net8-only baseline"`

Expected: commit succeeds.

### Task 5: Full verification and final checkpoint

**Files:**
- Verify: `src/STIGForge.*/*.csproj`
- Verify: `src/STIGForge.Content/Import/ContentPackImporter.cs`
- Verify: `docs/Architecture.md`

**Step 1: Run full solution validation**

Run: `dotnet test STIGForge.sln`

Expected: all projects compile and all tests pass.

**Step 2: Confirm no net48 in active source project targets**

Run: `rg --line-number "net48" src/STIGForge.*/*.csproj docs/Architecture.md`

Expected: no matches.

**Step 3: Review final change set**

Run:
- `git status --short`
- `git diff --stat`

Expected: only intended runtime/doc cleanup files are changed.

**Step 4: Commit final verification marker**

Run:
`git add -A && git commit -m "chore(runtime): complete net8-only cutover verification"`

Expected: final checkpoint commit succeeds with validated migration state.
