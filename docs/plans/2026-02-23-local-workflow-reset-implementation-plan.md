# Local Workflow Reset (Setup-Import-Scan) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a local-machine-first `Setup -> Import -> Scan` pipeline that enforces strict setup validation, uses imported STIGs as canonical authority, and emits a normalized `mission.json` artifact.

**Architecture:** Add a dedicated local workflow orchestrator service with explicit stage contracts. Keep scanner outputs as evidence overlays mapped to imported canonical checklist items, with `unmapped` warnings instead of stage failure. Wire the workflow through CLI first, then expose in App after contracts are stable.

**Tech Stack:** .NET 8, C#, System.CommandLine, Microsoft.Extensions.DependencyInjection/Hosting, xUnit, existing STIGForge.Content import parsers and STIGForge.Verify runners.

---

### Task 1: Define workflow contracts and normalized mission model

**Files:**
- Create: `src/STIGForge.Core/Models/LocalWorkflowMission.cs`
- Modify: `src/STIGForge.Core/Abstractions/Services.cs`
- Test: `tests/STIGForge.UnitTests/Core/LocalWorkflowMissionContractTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void MissionModel_ContainsCanonicalChecklistAndUnmappedCollections()
{
  var mission = new LocalWorkflowMission();

  Assert.NotNull(mission.CanonicalChecklist);
  Assert.NotNull(mission.ScannerEvidence);
  Assert.NotNull(mission.UnmappedEvidence);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~LocalWorkflowMissionContractTests"`
Expected: FAIL because `LocalWorkflowMission` does not exist.

**Step 3: Write minimal implementation**

```csharp
public sealed class LocalWorkflowMission
{
  public List<CanonicalChecklistItem> CanonicalChecklist { get; set; } = new();
  public List<ScannerEvidenceItem> ScannerEvidence { get; set; } = new();
  public List<ScannerEvidenceItem> UnmappedEvidence { get; set; } = new();
}
```

Also add `ILocalWorkflowService` and request/result DTOs in `Services.cs`.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~LocalWorkflowMissionContractTests"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Core/Models/LocalWorkflowMission.cs src/STIGForge.Core/Abstractions/Services.cs tests/STIGForge.UnitTests/Core/LocalWorkflowMissionContractTests.cs
git commit -m "feat(workflow): add local workflow mission contracts"
```

### Task 2: Implement strict setup path/tool validation stage

**Files:**
- Create: `src/STIGForge.Infrastructure/Workflow/LocalSetupValidator.cs`
- Modify: `src/STIGForge.Infrastructure/Paths/PathBuilder.cs`
- Modify: `src/STIGForge.Cli/CliHostFactory.cs`
- Test: `tests/STIGForge.UnitTests/Infrastructure/LocalSetupValidatorTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Validate_Throws_WhenRequiredEvaluateStigPathMissing()
{
  var sut = new LocalSetupValidator();
  var req = new LocalWorkflowSetupRequest { EvaluateStigToolRoot = "C:/missing" };

  Assert.Throws<InvalidOperationException>(() => sut.Validate(req));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~LocalSetupValidatorTests"`
Expected: FAIL because validator is not implemented.

**Step 3: Write minimal implementation**

```csharp
public void Validate(LocalWorkflowSetupRequest request)
{
  if (string.IsNullOrWhiteSpace(request.EvaluateStigToolRoot) || !Directory.Exists(request.EvaluateStigToolRoot))
    throw new InvalidOperationException("Setup failed: Evaluate-STIG tool root is required and must exist.");
}
```

Register validator in host DI and ensure default import root remains app-rooted.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~LocalSetupValidatorTests"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Infrastructure/Workflow/LocalSetupValidator.cs src/STIGForge.Infrastructure/Paths/PathBuilder.cs src/STIGForge.Cli/CliHostFactory.cs tests/STIGForge.UnitTests/Infrastructure/LocalSetupValidatorTests.cs
git commit -m "feat(workflow): enforce strict setup tool validation"
```

### Task 3: Build Import stage canonical checklist projection

**Files:**
- Create: `src/STIGForge.Content/Import/CanonicalChecklistProjector.cs`
- Modify: `src/STIGForge.Content/Import/ImportInboxScanner.cs`
- Modify: `src/STIGForge.Content/Import/ImportInboxModels.cs`
- Test: `tests/STIGForge.UnitTests/Content/CanonicalChecklistProjectorTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Project_UsesImportedStigAsCanonicalAuthority()
{
  var imported = new[] { new ImportedChecklistSource("WIN11_STIG", "SV-0001") };
  var result = CanonicalChecklistProjector.Project(imported);

  Assert.Contains(result.Items, x => x.RuleId == "SV-0001" && x.Authority == "import");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~CanonicalChecklistProjectorTests"`
Expected: FAIL because projector does not exist.

**Step 3: Write minimal implementation**

```csharp
public static CanonicalChecklist Project(IEnumerable<ImportedChecklistSource> sources)
  => new()
  {
    Items = sources.Select(s => new CanonicalChecklistItem
    {
      StigId = s.StigId,
      RuleId = s.RuleId,
      Authority = "import"
    }).ToList()
  };
```

Keep this stage DRY and YAGNI: no scanner merge logic yet.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~CanonicalChecklistProjectorTests"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Content/Import/CanonicalChecklistProjector.cs src/STIGForge.Content/Import/ImportInboxScanner.cs src/STIGForge.Content/Import/ImportInboxModels.cs tests/STIGForge.UnitTests/Content/CanonicalChecklistProjectorTests.cs
git commit -m "feat(import): project canonical checklist from imported STIG content"
```

### Task 4: Implement Scan stage evidence mapping with unmapped warnings

**Files:**
- Create: `src/STIGForge.Verify/Workflow/ScannerEvidenceMapper.cs`
- Create: `src/STIGForge.Verify/Workflow/LocalWorkflowService.cs`
- Modify: `src/STIGForge.Verify/VerificationWorkflowService.cs`
- Modify: `src/STIGForge.Cli/CliHostFactory.cs`
- Test: `tests/STIGForge.UnitTests/Verify/ScannerEvidenceMapperTests.cs`
- Test: `tests/STIGForge.UnitTests/Verify/LocalWorkflowServiceTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Map_PlacesUnknownScannerFindingsIntoUnmappedWarnings()
{
  var canonical = new[] { new CanonicalChecklistItem { RuleId = "SV-1" } };
  var findings = new[] { new ScannerEvidenceItem { RuleId = "SV-X" } };

  var result = ScannerEvidenceMapper.Map(canonical, findings);

  Assert.Single(result.Unmapped);
  Assert.Equal("SV-X", result.Unmapped[0].RuleId);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ScannerEvidenceMapperTests"`
Expected: FAIL because mapper service does not exist.

**Step 3: Write minimal implementation**

```csharp
if (!canonicalByRule.TryGetValue(finding.RuleId, out var _))
{
  unmapped.Add(finding with { MappingStatus = "unmapped" });
  diagnostics.Add($"Unmapped scanner item: {finding.RuleId}");
}
```

In `LocalWorkflowService`, orchestrate Setup/Import/Scan and write `mission.json`.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ScannerEvidenceMapperTests|FullyQualifiedName~LocalWorkflowServiceTests"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Verify/Workflow/ScannerEvidenceMapper.cs src/STIGForge.Verify/Workflow/LocalWorkflowService.cs src/STIGForge.Verify/VerificationWorkflowService.cs src/STIGForge.Cli/CliHostFactory.cs tests/STIGForge.UnitTests/Verify/ScannerEvidenceMapperTests.cs tests/STIGForge.UnitTests/Verify/LocalWorkflowServiceTests.cs
git commit -m "feat(scan): map scanner evidence onto canonical checklist with unmapped warnings"
```

### Task 5: Add CLI entrypoint for local workflow mission

**Files:**
- Create: `src/STIGForge.Cli/Commands/LocalWorkflowCommands.cs`
- Modify: `src/STIGForge.Cli/Program.cs`
- Modify: `src/STIGForge.Cli/Commands/Helpers.cs`
- Test: `tests/STIGForge.UnitTests/Cli/LocalWorkflowCommandsTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task WorkflowLocal_WritesMissionJsonPathToStdout()
{
  var exit = await CliTestHarness.InvokeAsync("workflow-local --import-root C:/tmp/import --evaluate-stig-root C:/tools/Evaluate-STIG");
  Assert.Equal(0, exit);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~LocalWorkflowCommandsTests"`
Expected: FAIL because command is not registered.

**Step 3: Write minimal implementation**

```csharp
var cmd = new Command("workflow-local", "Run local Setup->Import->Scan mission");
cmd.SetHandler(async (...) => { var result = await workflow.RunAsync(request, CancellationToken.None); Console.WriteLine(result.MissionJsonPath); });
```

Register in `Program.cs` next to existing command groups.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~LocalWorkflowCommandsTests"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.Cli/Commands/LocalWorkflowCommands.cs src/STIGForge.Cli/Program.cs src/STIGForge.Cli/Commands/Helpers.cs tests/STIGForge.UnitTests/Cli/LocalWorkflowCommandsTests.cs
git commit -m "feat(cli): add workflow-local command for setup-import-scan mission"
```

### Task 6: Add App setup defaults + operator visibility for v1 mission output

**Files:**
- Modify: `src/STIGForge.App/MainViewModel.ToolDefaults.cs`
- Modify: `src/STIGForge.App/MainViewModel.Import.cs`
- Modify: `src/STIGForge.App/Views/ImportView.xaml.cs`
- Test: `tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void ImportView_ShowsWorkflowLocalMissionOutputField()
{
  var xaml = LoadImportViewXaml();
  Assert.Contains("MissionJsonPath", xaml, StringComparison.Ordinal);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests"`
Expected: FAIL because output field binding is absent.

**Step 3: Write minimal implementation**

```csharp
public string MissionJsonPath { get => _missionJsonPath; set => Set(ref _missionJsonPath, value); }
```

Set default roots to app path/import and show latest mission artifact path.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ImportViewLayoutContractTests"`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/STIGForge.App/MainViewModel.ToolDefaults.cs src/STIGForge.App/MainViewModel.Import.cs src/STIGForge.App/Views/ImportView.xaml.cs tests/STIGForge.UnitTests/Views/ImportViewLayoutContractTests.cs
git commit -m "feat(app): surface local workflow defaults and mission output path"
```

### Task 7: Documentation + full verification pass

**Files:**
- Modify: `README.md`
- Modify: `docs/plans/2026-02-23-local-workflow-reset-design.md`

**Step 1: Write failing docs test/check (manual checklist)**

Create a checklist in PR notes requiring:
- command present in README
- strict setup gate behavior documented
- `mission.json` schema summary documented

**Step 2: Run verification commands**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj`
- `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --filter "FullyQualifiedName~CliCommandTests|FullyQualifiedName~VerifyCommandFlowTests"`

Expected: PASS with no new failures.

**Step 3: Add minimal docs implementation**

Add README section:

```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- workflow-local --import-root .\.stigforge\import --evaluate-stig-root C:\tools\Evaluate-STIG
```

Document strict setup gating and unmapped warning behavior.

**Step 4: Re-run verification commands**

Run the same test commands again.
Expected: PASS.

**Step 5: Commit**

```bash
git add README.md docs/plans/2026-02-23-local-workflow-reset-design.md
git commit -m "docs(workflow): document local setup-import-scan mission usage"
```

## Execution Notes

- Follow @superpowers:test-driven-development for every code change (RED -> GREEN -> REFACTOR).
- Use @superpowers:verification-before-completion before any status claim.
- Keep commits small and stage-bounded (one task == one commit).
- Do not expand to Harden/Verify consolidation in this plan (YAGNI).
