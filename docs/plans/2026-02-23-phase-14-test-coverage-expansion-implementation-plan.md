# Phase 14 Test Coverage Expansion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver Phase 14 quality gates by publishing branch/line coverage in CI, enforcing 80% scoped line coverage for critical assemblies, and introducing mutation baseline policy with a regression gate toggle.

**Architecture:** Keep collection/reporting separate from enforcement. Use PowerShell release-style scripts under `tools/release` to parse Cobertura coverage reports, write auditable artifacts, and apply deterministic threshold checks for critical assemblies only. Introduce mutation as a baseline-first policy with optional enforcement so rollout is stable and reversible.

**Tech Stack:** .NET 8, xUnit, GitHub Actions, PowerShell 5.1/7, coverlet.collector, Stryker.NET

---

### Task 1: Coverage Gate Policy + Script (TDD)

**Files:**
- Create: `tools/release/coverage-gate-policy.json`
- Create: `tools/release/Invoke-CoverageGate.ps1`
- Create: `tests/STIGForge.IntegrationTests/Release/CoverageGateScriptTests.cs`
- Create: `tests/STIGForge.IntegrationTests/Release/Fixtures/coverage-pass.cobertura.xml`
- Create: `tests/STIGForge.IntegrationTests/Release/Fixtures/coverage-fail.cobertura.xml`
- Modify: `tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task CoverageGate_Fails_WhenScopedLineCoverageBelowThreshold()
{
    var repoRoot = FindRepoRoot();
    var scriptPath = Path.Combine(repoRoot, "tools", "release", "Invoke-CoverageGate.ps1");
    var policyPath = Path.Combine(repoRoot, "tools", "release", "coverage-gate-policy.json");
    var coveragePath = Path.Combine(repoRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-fail.cobertura.xml");

    var result = await RunPowerShellAsync($"-File \"{scriptPath}\" -CoverageReportPath \"{coveragePath}\" -PolicyPath \"{policyPath}\"");

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("line threshold");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CoverageGateScriptTests"`
Expected: FAIL because `Invoke-CoverageGate.ps1` and policy file do not exist yet.

**Step 3: Write minimal implementation**

```powershell
param(
  [Parameter(Mandatory = $true)][string]$CoverageReportPath,
  [Parameter(Mandatory = $true)][string]$PolicyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$policy = Get-Content -Path $PolicyPath -Raw | ConvertFrom-Json
[xml]$report = Get-Content -Path $CoverageReportPath -Raw

$threshold = [double]$policy.lineThresholdPercent
$critical = @($policy.criticalAssemblies)
$linesValid = 0
$linesCovered = 0

foreach ($pkg in @($report.coverage.packages.package)) {
  if ($critical -contains [string]$pkg.name) {
    $linesValid += [double]$pkg.'lines-valid'
    $linesCovered += [double]$pkg.'lines-covered'
  }
}

if ($linesValid -le 0) { throw "No scoped coverage lines found for critical assemblies." }

$linePercent = [math]::Round(($linesCovered / $linesValid) * 100, 2)
if ($linePercent -lt $threshold) {
  throw "Coverage gate failed: scoped line threshold $threshold% not met (actual $linePercent%)."
}

Write-Host "Coverage gate passed: scoped line coverage $linePercent% (threshold $threshold%)." -ForegroundColor Green
```

`coverage-gate-policy.json` initial content:

```json
{
  "lineThresholdPercent": 80,
  "criticalAssemblies": [
    "STIGForge.Build",
    "STIGForge.Apply",
    "STIGForge.Verify",
    "STIGForge.Infrastructure"
  ]
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CoverageGateScriptTests"`
Expected: PASS for both pass and fail fixture scenarios (pass fixture exits 0, fail fixture exits non-zero).

**Step 5: Commit**

```bash
git add tools/release/coverage-gate-policy.json tools/release/Invoke-CoverageGate.ps1 tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj tests/STIGForge.IntegrationTests/Release/CoverageGateScriptTests.cs tests/STIGForge.IntegrationTests/Release/Fixtures/coverage-pass.cobertura.xml tests/STIGForge.IntegrationTests/Release/Fixtures/coverage-fail.cobertura.xml
git commit -m "test(14-01): add scoped coverage gate policy and script tests"
```

### Task 2: Coverage Summary Script + Branch Visibility (TDD)

**Files:**
- Create: `tools/release/Invoke-CoverageReport.ps1`
- Create: `tests/STIGForge.IntegrationTests/Release/CoverageReportScriptTests.cs`
- Create: `tests/STIGForge.IntegrationTests/Release/Fixtures/coverage-mixed.cobertura.xml`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task CoverageReport_WritesJsonAndMarkdown_WithLineAndBranchMetrics()
{
    var repoRoot = FindRepoRoot();
    var scriptPath = Path.Combine(repoRoot, "tools", "release", "Invoke-CoverageReport.ps1");
    var coveragePath = Path.Combine(repoRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "coverage-mixed.cobertura.xml");
    var outputRoot = Path.Combine(Path.GetTempPath(), "stigforge-coverage-report-" + Guid.NewGuid().ToString("N"));

    var result = await RunPowerShellAsync($"-File \"{scriptPath}\" -CoverageReportPath \"{coveragePath}\" -OutputRoot \"{outputRoot}\"");

    result.ExitCode.Should().Be(0);
    File.Exists(Path.Combine(outputRoot, "coverage-summary.json")).Should().BeTrue();
    File.ReadAllText(Path.Combine(outputRoot, "coverage-report.md")).Should().Contain("Branch Coverage");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CoverageReportScriptTests"`
Expected: FAIL because `Invoke-CoverageReport.ps1` does not exist yet.

**Step 3: Write minimal implementation**

```powershell
param(
  [Parameter(Mandatory = $true)][string]$CoverageReportPath,
  [Parameter(Mandatory = $true)][string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

[xml]$report = Get-Content -Path $CoverageReportPath -Raw
$lineRate = [double]$report.coverage.'line-rate'
$branchRate = [double]$report.coverage.'branch-rate'

$summary = [ordered]@{
  generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
  totals = [ordered]@{
    lineCoveragePercent = [math]::Round($lineRate * 100, 2)
    branchCoveragePercent = [math]::Round($branchRate * 100, 2)
  }
  packages = @(@($report.coverage.packages.package) | ForEach-Object {
    [ordered]@{
      name = [string]$_.name
      lineCoveragePercent = [math]::Round(([double]$_.'line-rate') * 100, 2)
      branchCoveragePercent = [math]::Round(([double]$_.'branch-rate') * 100, 2)
    }
  })
}

$summaryPath = Join-Path $OutputRoot "coverage-summary.json"
$reportPath = Join-Path $OutputRoot "coverage-report.md"
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$md = @(
  "# Coverage Report"
  ""
  "- Line Coverage: $($summary.totals.lineCoveragePercent)%"
  "- Branch Coverage: $($summary.totals.branchCoveragePercent)%"
) -join [Environment]::NewLine
Set-Content -Path $reportPath -Value $md -Encoding UTF8
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~CoverageReportScriptTests"`
Expected: PASS; JSON and Markdown artifacts are generated and contain branch metrics.

**Step 5: Commit**

```bash
git add tools/release/Invoke-CoverageReport.ps1 tests/STIGForge.IntegrationTests/Release/CoverageReportScriptTests.cs tests/STIGForge.IntegrationTests/Release/Fixtures/coverage-mixed.cobertura.xml
git commit -m "feat(14-02): add coverage summary reporting with branch metrics"
```

### Task 3: CI Wiring for Collection, Reporting, and Line Gate

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`

**Step 1: Write the failing test/check**

Create a local verification command (failing before CI wiring):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageReport.ps1 -CoverageReportPath .\.artifacts\test-coverage\ci\coverage.cobertura.xml -OutputRoot .\.artifacts\test-coverage\ci\report
```

Expected: FAIL initially because CI has not been updated to generate coverage reports in the expected path.

**Step 2: Run check to verify it fails**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageReport.ps1 -CoverageReportPath .\.artifacts\test-coverage\ci\coverage.cobertura.xml -OutputRoot .\.artifacts\test-coverage\ci\report`
Expected: FAIL with missing coverage report file.

**Step 3: Write minimal implementation**

Update `.github/workflows/ci.yml` with new steps:

```yaml
- name: Collect coverage reports
  shell: pwsh
  run: |
    $out = '.\.artifacts\test-coverage\ci'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    dotnet test .\tests\STIGForge.UnitTests\STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --nologo --collect:"XPlat Code Coverage" --results-directory "$out\unit"
    dotnet test .\tests\STIGForge.IntegrationTests\STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --nologo --filter 'FullyQualifiedName!~E2E' --collect:"XPlat Code Coverage" --results-directory "$out\integration"

- name: Publish branch and line coverage summary
  shell: pwsh
  run: |
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageReport.ps1 -CoverageReportPath .\.artifacts\test-coverage\ci\coverage.cobertura.xml -OutputRoot .\.artifacts\test-coverage\ci\report

- name: Enforce scoped line coverage gate
  shell: pwsh
  run: |
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageGate.ps1 -CoverageReportPath .\.artifacts\test-coverage\ci\coverage.cobertura.xml -PolicyPath .\tools\release\coverage-gate-policy.json

- name: Upload coverage artifacts
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-coverage-ci-${{ github.run_id }}
    path: ./.artifacts/test-coverage/ci
    if-no-files-found: warn
```

Also add a short "Coverage gate" local command section to `README.md`.

**Step 4: Run checks to verify they pass**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --collect:"XPlat Code Coverage" --results-directory .\.artifacts\test-coverage\local\unit`
- `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~E2E" --collect:"XPlat Code Coverage" --results-directory .\.artifacts\test-coverage\local\integration`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageReport.ps1 -CoverageReportPath .\.artifacts\test-coverage\local\coverage.cobertura.xml -OutputRoot .\.artifacts\test-coverage\local\report`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageGate.ps1 -CoverageReportPath .\.artifacts\test-coverage\local\coverage.cobertura.xml -PolicyPath .\tools\release\coverage-gate-policy.json`

Expected: PASS with generated summary artifacts and green gate when threshold is met.

**Step 5: Commit**

```bash
git add .github/workflows/ci.yml README.md
git commit -m "feat(14-03): wire coverage reporting and scoped CI gate"
```

### Task 4: Mutation Baseline Policy + Optional Regression Gate (TDD)

**Files:**
- Create: `.config/dotnet-tools.json`
- Create: `tools/release/mutation-policy.json`
- Create: `tools/release/Invoke-MutationPolicy.ps1`
- Create: `tests/STIGForge.IntegrationTests/Release/MutationPolicyScriptTests.cs`
- Create: `tests/STIGForge.IntegrationTests/Release/Fixtures/mutation-current-pass.json`
- Create: `tests/STIGForge.IntegrationTests/Release/Fixtures/mutation-current-regress.json`
- Modify: `.github/workflows/ci.yml`

**Step 1: Write the failing test**

```csharp
[Fact]
public async Task MutationPolicy_Fails_WhenRegressionExceedsAllowedDrop_AndEnforcementEnabled()
{
    var repoRoot = FindRepoRoot();
    var script = Path.Combine(repoRoot, "tools", "release", "Invoke-MutationPolicy.ps1");
    var baseline = Path.Combine(repoRoot, "tools", "release", "mutation-policy.json");
    var current = Path.Combine(repoRoot, "tests", "STIGForge.IntegrationTests", "Release", "Fixtures", "mutation-current-regress.json");

    var result = await RunPowerShellAsync($"-File \"{script}\" -PolicyPath \"{baseline}\" -CurrentResultPath \"{current}\" -Enforce");

    result.ExitCode.Should().NotBe(0);
    result.Output.Should().Contain("mutation regression");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~MutationPolicyScriptTests"`
Expected: FAIL because mutation policy script/config do not exist yet.

**Step 3: Write minimal implementation**

```powershell
param(
  [Parameter(Mandatory = $true)][string]$PolicyPath,
  [Parameter(Mandatory = $true)][string]$CurrentResultPath,
  [switch]$Enforce
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$policy = Get-Content -Path $PolicyPath -Raw | ConvertFrom-Json
$current = Get-Content -Path $CurrentResultPath -Raw | ConvertFrom-Json

$baseline = [double]$policy.baselineMutationScorePercent
$allowedDrop = [double]$policy.allowedRegressionPercent
$floor = $baseline - $allowedDrop
$actual = [double]$current.mutationScorePercent

if ($Enforce -and $actual -lt $floor) {
  throw "mutation regression: score $actual% is below floor $floor% (baseline $baseline%, allowed drop $allowedDrop%)."
}

Write-Host "Mutation policy check complete. score=$actual floor=$floor enforce=$($Enforce.IsPresent)" -ForegroundColor Green
```

`mutation-policy.json` initial content:

```json
{
  "baselineMutationScorePercent": 65.0,
  "allowedRegressionPercent": 2.0,
  "scope": [
    "STIGForge.Build",
    "STIGForge.Apply",
    "STIGForge.Verify"
  ]
}
```

**Step 4: Run tests/checks to verify they pass**

Run:
- `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~MutationPolicyScriptTests"`
- `dotnet tool restore`
- `dotnet stryker --version`

Expected: PASS for policy tests; local tool manifest restores Stryker successfully.

**Step 5: Commit**

```bash
git add .config/dotnet-tools.json tools/release/mutation-policy.json tools/release/Invoke-MutationPolicy.ps1 tests/STIGForge.IntegrationTests/Release/MutationPolicyScriptTests.cs tests/STIGForge.IntegrationTests/Release/Fixtures/mutation-current-pass.json tests/STIGForge.IntegrationTests/Release/Fixtures/mutation-current-regress.json .github/workflows/ci.yml
git commit -m "feat(14-04): add mutation baseline policy with optional regression gate"
```

### Task 5: Documentation + Requirement Traceability + Final Verification

**Files:**
- Modify: `.planning/ROADMAP.md`
- Modify: `.planning/STATE.md`
- Modify: `.planning/REQUIREMENTS.md`
- Create: `.planning/phases/14-test-coverage-expansion/14-01-SUMMARY.md`
- Modify: `docs/testing/StabilityBudget.md`

**Step 1: Write failing verification checklist**

Create checklist asserting Phase 14 criteria are currently unmet:

```markdown
- [ ] Scoped line coverage gate enforced at 80%
- [ ] Branch coverage shown in CI summary artifacts
- [ ] PR-blocking gate in ci.yml for scoped line coverage
- [ ] Mutation baseline collected with enforcement toggle
```

**Step 2: Run verification commands to show current failure/gaps**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0`
- `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~Coverage|FullyQualifiedName~MutationPolicy"`

Expected: identify failing/new checks before final completion.

**Step 3: Write minimal documentation and traceability updates**

Add exact outcomes, artifact locations, and how to run locally:
- coverage artifacts root: `.artifacts/test-coverage/*`
- mutation artifacts root: `.artifacts/mutation/*`
- enforcement toggle source: CI variable + `tools/release/mutation-policy.json`

**Step 4: Run full verification to green**

Run:
- `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~RebootCoordinator"`
- `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName!~E2E"`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-CoverageGate.ps1 -CoverageReportPath .\.artifacts\test-coverage\local\coverage.cobertura.xml -PolicyPath .\tools\release\coverage-gate-policy.json`

Expected: PASS; requirements and planning docs are consistent with implemented behavior.

**Step 5: Commit**

```bash
git add .planning/ROADMAP.md .planning/STATE.md .planning/REQUIREMENTS.md .planning/phases/14-test-coverage-expansion/14-01-SUMMARY.md docs/testing/StabilityBudget.md
git commit -m "docs(14-05): document coverage and mutation gate rollout"
```

## Notes for Executor

- Required execution skill: `@superpowers:executing-plans`
- Keep strict TDD discipline per task; do not skip failing-test proof.
- Keep scope to critical assemblies only (YAGNI).
- Prefer one commit per task for clean rollback and review.
