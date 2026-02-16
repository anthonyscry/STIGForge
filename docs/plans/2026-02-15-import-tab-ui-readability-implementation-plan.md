# Import Tab UI Readability Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve Import tab readability by introducing a clearer section hierarchy and scan-friendly visual rhythm without changing any Import behavior.

**Architecture:** Keep all existing bindings and commands in `ImportView` intact, and apply presentation-only changes in XAML. Add lightweight unit tests that treat `ImportView.xaml` as a contract surface, asserting required section labels and key binding markers so readability refactors remain safe. Validate with targeted tests, full unit tests, and a Windows-targeted app build.

**Tech Stack:** WPF XAML, .NET 8/net48 solution build, xUnit, FluentAssertions, dotnet CLI

---

### Task 1: Add ImportView Readability Contract Tests

**Files:**
- Create: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void ImportView_ContainsRequiredReadabilitySections()
{
  var xaml = LoadImportViewXaml();

  xaml.Should().Contain("Primary Actions");
  xaml.Should().Contain("Machine Context");
  xaml.Should().Contain("Content Library");
  xaml.Should().Contain("Pack Details");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests.ImportView_ContainsRequiredReadabilitySections"`
Expected: FAIL because new readability section labels are not in current `ImportView.xaml` yet.

**Step 3: Write minimal implementation helper in test file**

```csharp
private static string LoadImportViewXaml()
{
  var dir = new DirectoryInfo(AppContext.BaseDirectory);
  while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STIGForge.sln")))
    dir = dir.Parent;

  dir.Should().NotBeNull("repo root must be discoverable from test runtime directory");
  var path = Path.Combine(dir!.FullName, "src", "STIGForge.App", "Views", "ImportView.xaml");
  return File.ReadAllText(path);
}
```

**Step 4: Run test to verify it still fails for the right reason**

Run: same command as Step 2
Expected: FAIL on missing section text (not file-path/runtime errors).

**Step 5: Commit**

```bash
git add tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "test(ui): add import view readability contract baseline"
```

### Task 2: Introduce Top-Level Section Hierarchy in ImportView

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void ImportView_PreservesPrimaryActionBindings_WhenSectioned()
{
  var xaml = LoadImportViewXaml();

  xaml.Should().Contain("Command=\"{Binding ScanImportFolderCommand}\"");
  xaml.Should().Contain("Command=\"{Binding OpenImportFolderCommand}\"");
  xaml.Should().Contain("Command=\"{Binding ComparePacksCommand}\"");
  xaml.Should().Contain("Command=\"{Binding OpenContentPickerCommand}\"");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests"`
Expected: FAIL because Task 1 section-label test is still failing before XAML updates.

**Step 3: Write minimal implementation**

```xml
<TextBlock Text="Primary Actions" FontWeight="SemiBold" />
<TextBlock Text="Machine Context" FontWeight="SemiBold" />
<TextBlock Text="Content Library" FontWeight="SemiBold" />
<TextBlock Text="Pack Details" FontWeight="SemiBold" />
```

Add these headings in `ImportView.xaml` while preserving existing command bindings and control names.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests"`
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/Views/ImportView.xaml tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "refactor(ui): add section hierarchy to import view"
```

### Task 3: Improve Machine Context and Library Readability Bands

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void ImportView_ContainsMachineAndLibraryReadabilityLabels()
{
  var xaml = LoadImportViewXaml();

  xaml.Should().Contain("Scan context");
  xaml.Should().Contain("Library filters");
  xaml.Should().Contain("Library actions");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests.ImportView_ContainsMachineAndLibraryReadabilityLabels"`
Expected: FAIL with missing readability labels.

**Step 3: Write minimal implementation**

```xml
<TextBlock Text="Scan context" Foreground="{DynamicResource TextMutedBrush}" />
<TextBlock Text="Library filters" Foreground="{DynamicResource TextMutedBrush}" />
<TextBlock Text="Library actions" Foreground="{DynamicResource TextMutedBrush}" />
```

Apply these label bands with conservative spacing and existing brushes, without changing list bindings, commands, or selection behavior.

**Step 4: Run test to verify it passes**

Run: same command as Step 2
Expected: PASS

**Step 5: Commit**

```bash
git add src/STIGForge.App/Views/ImportView.xaml tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "refactor(ui): clarify machine context and library readability bands"
```

### Task 4: Add Readability Safety for Long Text and Status Placement

**Files:**
- Modify: `src/STIGForge.App/Views/ImportView.xaml`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void ImportView_UsesTextWrappingOnLongStatusAndDetailFields()
{
  var xaml = LoadImportViewXaml();

  xaml.Should().Contain("Text=\"{Binding MachineScanSummary}\"", "machine summary must remain readable");
  xaml.Should().Contain("Text=\"{Binding PackDetailRoot}\"", "detail root path must remain readable");
  xaml.Should().Contain("TextWrapping=\"Wrap\"", "long status/details should wrap");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests.ImportView_UsesTextWrappingOnLongStatusAndDetailFields"`
Expected: FAIL until wrapping/placement adjustments are complete.

**Step 3: Write minimal implementation**

```xml
<TextBlock Text="{Binding MachineScanSummary}" TextWrapping="Wrap" />
<TextBlock Text="{Binding SelectedContentSummary}" TextWrapping="Wrap" />
<TextBlock Text="{Binding PackDetailRoot}" TextWrapping="Wrap" />
```

Ensure status and detail text remain in predictable section positions and preserve existing bindings.

**Step 4: Run targeted tests and app build**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests"`
Expected: PASS

Run: `dotnet build src/STIGForge.App/STIGForge.App.csproj -f net8.0-windows -p:EnableWindowsTargeting=true`
Expected: Build succeeds with 0 errors.

**Step 5: Commit**

```bash
git add src/STIGForge.App/Views/ImportView.xaml tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "refactor(ui): harden import view readability for long text and status"
```

### Task 5: Final Verification Gate

**Files:**
- Modify: `docs/WpfGuide.md`

**Step 1: Run full unit tests**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`
Expected: PASS

**Step 2: Run solution build with Windows targeting**

Run: `dotnet build STIGForge.sln -p:EnableWindowsTargeting=true`
Expected: PASS

**Step 3: Document Import tab readability structure**

```markdown
- Import tab now emphasizes four readability zones: Primary Actions, Machine Context, Content Library, and Pack Details.
- Behavior and command bindings are unchanged; improvements are presentation-only.
```

**Step 4: Commit**

```bash
git add docs/WpfGuide.md
git commit -m "docs(ui): document import tab readability hierarchy"
```

## Execution Notes

- Use `@test-driven-development` for each task.
- If test/build output is unexpected, stop and use `@systematic-debugging` before changing code.
- Before claiming completion, run `@verification-before-completion` with command output evidence.
