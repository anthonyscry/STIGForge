# STIGForge Comprehensive Testing & E2E Integration Plan

## Lab Environment: triton-ajt Hyper-V Host

**Host**: triton-ajt (Windows 10 Pro, 32 GB RAM, .NET 8.0.417)
**Existing VMs**: DC01 (Off), SRV01 (Running, 192.168.50.20), SRV02 (Off)
**Network**: AppLockerNet (isolated), Default Switch (NAT)

---

## Phase 0: VM Provisioning & Baseline

### 0.1  -  Provision Test Matrix VMs

| VM Name | OS | Role | STIG Profile | RAM | vCPU | Network |
|---------|-----|------|--------------|-----|------|---------|
| DC01 | Server 2022 | Domain Controller | WinSvr2022_DC | 2 GB | 2 | AppLockerNet |
| SRV01 | Server 2022 | Member Server | WinSvr2022_MS | 2 GB | 2 | AppLockerNet + Default |
| SRV02 | Server 2022 | Standalone (no domain) | WinSvr2022_MS | 2 GB | 2 | AppLockerNet |

#### Provisioning Steps (per VM)

```powershell
# 1. Restore to clean baseline checkpoint
Import-Module Hyper-V
Restore-VMCheckpoint -VMName "SRV01" -Name "Pre-Testing_2026-03-07_1656" -Confirm:$false
Start-VM -Name "SRV01"

# 2. Install prerequisites via PowerShell Direct
$cred = Get-Credential -UserName "Administrator"
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    # .NET 8 Runtime (for CLI)
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "$env:TEMP\dotnet-install.ps1"
    & "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -Runtime dotnet

    # Enable WinRM for remote test execution
    Enable-PSRemoting -Force -SkipNetworkProfileCheck
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value "*" -Force

    # Install PowerSTIG DSC modules
    Install-Module -Name PowerSTIG -Force -AllowClobber
    Install-Module -Name SecurityPolicyDsc -Force
    Install-Module -Name AuditPolicyDsc -Force

    # Create test working directory
    New-Item -Path "C:\STIGForge-Test" -ItemType Directory -Force
}
```

#### Tool Installation (per VM)

| Tool | Source | Install Path | Required For |
|------|--------|-------------|-------------|
| Evaluate-STIG | DISA | `C:\EvaluateSTIG` | Scan & Verify steps |
| SCC (cscc.exe) | DISA | `C:\SCC` | SCAP scanning |
| LGPO.exe | Microsoft Security Compliance Toolkit | `C:\Windows\System32` | GPO application |
| PowerSTIG | PSGallery | PowerShell module path | DSC-based hardening |

### 0.2  -  Create Test Checkpoints

After provisioning each VM, create a "test-ready" checkpoint:

```powershell
foreach ($vm in @("DC01", "SRV01", "SRV02")) {
    Checkpoint-VM -Name $vm -SnapshotName "TestReady_$(Get-Date -Format 'yyyy-MM-dd_HHmm')"
}
```

---

## Phase 1: Unit & Contract Tests (Host Only)

**Location**: triton-ajt host, `C:\STIGForge`
**Duration**: ~30 seconds
**Prereq**: None (runs on host)

### 1.1  -  Full Unit Test Suite

```powershell
dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj `
    --configuration Release `
    --logger "trx;LogFileName=unit-tests.trx" `
    --results-directory .\.artifacts\test-results
```

**Expected**: 927 tests pass, 0 failures

### 1.2  -  Integration Tests (Host)

```powershell
dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj `
    --configuration Release `
    --logger "trx;LogFileName=integration-tests.trx" `
    --results-directory .\.artifacts\test-results
```

**Expected**: 102 tests pass

### 1.3  -  XAML Contract Validation

```powershell
dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj `
    --filter "FullyQualifiedName~DashboardViewContractTests" `
    --configuration Release
```

**Validates**: Tab structure, step cards, compliance chart, converter registrations

### 1.4  -  Release Gate (Dry Run)

```powershell
./tools/release/Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\host
```

**Validates**: Coverage gate, mutation policy, security gate, upgrade/rebase contracts, quarterly regression

---

## Phase 2: UI Smoke Tests (SRV01  -  Interactive Session)

**Location**: SRV01 VM (requires GUI/RDP session)
**Duration**: ~5 minutes
**Prereq**: .NET 8 SDK, STIGForge published binaries

### 2.1  -  Deploy to VM

```powershell
# From host
$cred = Get-Credential -UserName "Administrator"
$session = New-PSSession -VMName "SRV01" -Credential $cred

# Publish self-contained
dotnet publish src/STIGForge.App/STIGForge.App.csproj `
    -c Release -r win-x64 --self-contained `
    -o .\.artifacts\publish\app

# Copy to VM
Copy-Item -ToSession $session -Path ".\.artifacts\publish\app" -Destination "C:\STIGForge-Test\App" -Recurse -Force
Copy-Item -ToSession $session -Path "tests" -Destination "C:\STIGForge-Test\tests" -Recurse -Force
```

### 2.2  -  FlaUI Smoke Tests

Run from RDP session on SRV01 (requires interactive desktop):

```powershell
dotnet test C:\STIGForge-Test\tests\STIGForge.App.UiTests\STIGForge.App.UiTests.csproj `
    --configuration Release `
    --filter "Category=UI" `
    --logger "trx;LogFileName=ui-smoke.trx" `
    --results-directory C:\STIGForge-Test\.artifacts\ui-smoke
```

### 2.3  -  Manual UI Verification Checklist

| # | Test | Steps | Expected |
|---|------|-------|----------|
| 1 | App launches | Double-click STIGForge.App.exe | Main window appears, no crash |
| 2 | Theme switching | Settings → Theme toggle | Dark/Light/HighContrast all render correctly |
| 3 | HighContrast mode | Windows Settings → HighContrast → On | All brushes distinguish Success/Warning/Danger |
| 4 | Tab navigation | Click each tab header | All 4 tabs render, glyph indicators appear |
| 5 | Wizard mode | Click wizard toggle button | Step indicator shows 6 steps, Back/Next work |
| 6 | Wizard step click | Click step 1 circle from step 3 | Navigates back to Setup |
| 7 | Settings dialog | Click gear icon → verify all fields | MaxLength enforced, Browse buttons work |
| 8 | About dialog | Click info icon | Version from assembly (not hardcoded), close button sized correctly |
| 9 | Keyboard shortcuts | Press F1, Ctrl+R, Alt+I, etc. | All bindings functional |
| 10 | Restart confirmation | Results tab → Restart Workflow | MessageBox confirmation appears |
| 11 | Empty donut chart | Before any scan | "No chart data" placeholder shown |
| 12 | Compliance stats icons | After Verify completes | Checkmark/X/warning glyphs next to Pass/Fail/Other |
| 13 | Screen reader | Narrator ON → tab through UI | AutomationProperties read correctly |

---

## Phase 3: CLI E2E Tests (All VMs)

**Location**: Each VM via PowerShell Direct or WinRM
**Duration**: ~15 minutes per VM
**Prereq**: STIGForge CLI published, STIG content packs available

### 3.1  -  Deploy CLI to All VMs

```powershell
dotnet publish src/STIGForge.Cli/STIGForge.Cli.csproj `
    -c Release -r win-x64 --self-contained `
    -o .\.artifacts\publish\cli

foreach ($vm in @("SRV01", "SRV02")) {
    $cred = Get-Credential -UserName "Administrator"
    $session = New-PSSession -VMName $vm -Credential $cred
    Copy-Item -ToSession $session -Path ".\.artifacts\publish\cli" -Destination "C:\STIGForge-Test\Cli" -Recurse -Force
    Copy-Item -ToSession $session -Path "tests\fixtures" -Destination "C:\STIGForge-Test\fixtures" -Recurse -Force
    Remove-PSSession $session
}
```

### 3.2  -  Import Pipeline Tests

```powershell
# Test: Import STIG content from ZIP archive
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Import a STIG benchmark ZIP
    & $sf import --input "C:\STIGForge-Test\fixtures\sample-stig.zip" --output "C:\STIGForge-Test\output" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Import failed with exit code $LASTEXITCODE" }

    # Verify database was populated
    & $sf list-packs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "list-packs failed" }

    # Verify content integrity
    & $sf verify-integrity 2>&1
}
```

### 3.3  -  Scan Baseline Tests

```powershell
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Run Evaluate-STIG scan (requires admin + tool installed)
    & $sf scan `
        --evaluate-stig-path "C:\EvaluateSTIG\Evaluate-STIG.ps1" `
        --output "C:\STIGForge-Test\output\scan-baseline" `
        --machine localhost 2>&1

    if ($LASTEXITCODE -ne 0) { throw "Scan baseline failed" }

    # Verify CKL output was generated
    $cklFiles = Get-ChildItem "C:\STIGForge-Test\output\scan-baseline" -Filter "*.ckl" -Recurse
    if ($cklFiles.Count -eq 0) { throw "No CKL files generated from scan" }

    Write-Host "Scan baseline: $($cklFiles.Count) CKL files generated"
}
```

### 3.4  -  Harden (Apply) Tests

```powershell
# IMPORTANT: Always checkpoint before hardening
Checkpoint-VM -Name "SRV01" -SnapshotName "PreHarden_$(Get-Date -Format 'yyyy-MM-dd_HHmm')"

Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Apply hardening (DSC + LGPO + registry)
    & $sf harden `
        --output "C:\STIGForge-Test\output\harden" `
        --machine localhost 2>&1

    if ($LASTEXITCODE -ne 0) { throw "Harden failed" }

    # Verify applied fixes count
    $hardenLog = Get-Content "C:\STIGForge-Test\output\harden\harden-summary.json" -Raw | ConvertFrom-Json
    Write-Host "Applied fixes: $($hardenLog.appliedCount)"
}
```

### 3.5  -  Verify (Post-Harden) Tests

```powershell
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Run verification scan after hardening
    & $sf verify `
        --evaluate-stig-path "C:\EvaluateSTIG\Evaluate-STIG.ps1" `
        --output "C:\STIGForge-Test\output\verify" `
        --machine localhost 2>&1

    if ($LASTEXITCODE -ne 0) { throw "Verify failed" }

    # Check compliance improvement
    $verifyResult = Get-Content "C:\STIGForge-Test\output\verify\verify-summary.json" -Raw | ConvertFrom-Json
    Write-Host "Post-harden compliance: $($verifyResult.compliancePercent)%"

    if ($verifyResult.compliancePercent -lt 50) {
        Write-Warning "Compliance below 50% - hardening may not have applied correctly"
    }
}
```

### 3.6  -  Export & Prove Tests

```powershell
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Generate eMASS submission package
    & $sf export `
        --format ckl,csv,xccdf `
        --output "C:\STIGForge-Test\output\export" 2>&1

    if ($LASTEXITCODE -ne 0) { throw "Export failed" }

    # Verify all export formats generated
    $exportDir = "C:\STIGForge-Test\output\export"
    @("*.ckl", "*.csv", "*.xml") | ForEach-Object {
        $files = Get-ChildItem $exportDir -Filter $_ -Recurse
        if ($files.Count -eq 0) { throw "No $_ files in export output" }
        Write-Host "Export format $_: $($files.Count) files"
    }
}
```

---

## Phase 4: Full Workflow E2E (Import → Scan → Harden → Verify → Export)

**Location**: SRV01 (domain-joined) and SRV02 (standalone)
**Duration**: ~30 minutes per VM
**Prereq**: All tools installed, clean checkpoint

### 4.1  -  Full Pipeline Test Script

```powershell
param(
    [Parameter(Mandatory)]
    [string]$VMName,

    [Parameter(Mandatory)]
    [PSCredential]$Credential,

    [string]$StigContentPath = "C:\STIGForge-Test\fixtures",
    [string]$OutputRoot = "C:\STIGForge-Test\output\e2e-$(Get-Date -Format 'yyyyMMdd-HHmm')"
)

# 0. Pre-test checkpoint
Import-Module Hyper-V
Checkpoint-VM -Name $VMName -SnapshotName "E2E_Pre_$(Get-Date -Format 'yyyy-MM-dd_HHmm')"

$result = Invoke-Command -VMName $VMName -Credential $Credential -ScriptBlock {
    param($OutputRoot, $StigContentPath)
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"
    $errors = @()
    $timings = @{}

    # ── Step 1: Import ──
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $sf import --input $StigContentPath --output "$OutputRoot\import" 2>&1
    $sw.Stop()
    $timings["Import"] = $sw.ElapsedMilliseconds
    if ($LASTEXITCODE -ne 0) { $errors += "Import failed (exit $LASTEXITCODE)" }

    # ── Step 2: Scan Baseline ──
    $sw.Restart()
    & $sf scan --evaluate-stig-path "C:\EvaluateSTIG\Evaluate-STIG.ps1" --output "$OutputRoot\scan" --machine localhost 2>&1
    $sw.Stop()
    $timings["Scan"] = $sw.ElapsedMilliseconds
    if ($LASTEXITCODE -ne 0) { $errors += "Scan failed (exit $LASTEXITCODE)" }

    # ── Step 3: Harden ──
    $sw.Restart()
    & $sf harden --output "$OutputRoot\harden" --machine localhost 2>&1
    $sw.Stop()
    $timings["Harden"] = $sw.ElapsedMilliseconds
    if ($LASTEXITCODE -ne 0) { $errors += "Harden failed (exit $LASTEXITCODE)" }

    # ── Step 4: Verify ──
    $sw.Restart()
    & $sf verify --evaluate-stig-path "C:\EvaluateSTIG\Evaluate-STIG.ps1" --output "$OutputRoot\verify" --machine localhost 2>&1
    $sw.Stop()
    $timings["Verify"] = $sw.ElapsedMilliseconds
    if ($LASTEXITCODE -ne 0) { $errors += "Verify failed (exit $LASTEXITCODE)" }

    # ── Step 5: Export ──
    $sw.Restart()
    & $sf export --format ckl,csv,xccdf --output "$OutputRoot\export" 2>&1
    $sw.Stop()
    $timings["Export"] = $sw.ElapsedMilliseconds
    if ($LASTEXITCODE -ne 0) { $errors += "Export failed (exit $LASTEXITCODE)" }

    [PSCustomObject]@{
        VM = $env:COMPUTERNAME
        Errors = $errors
        Timings = $timings
        OutputRoot = $OutputRoot
        Passed = ($errors.Count -eq 0)
    }
} -ArgumentList $OutputRoot, $StigContentPath

# Report
Write-Host "`n═══════ E2E Results: $VMName ═══════"
Write-Host "Status: $(if ($result.Passed) { 'PASS' } else { 'FAIL' })"
$result.Timings.GetEnumerator() | ForEach-Object {
    Write-Host ("  {0}: {1:N0} ms" -f $_.Key, $_.Value)
}
if ($result.Errors.Count -gt 0) {
    Write-Host "`nErrors:" -ForegroundColor Red
    $result.Errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
```

### 4.2  -  Cross-VM Matrix Execution

```powershell
$testMatrix = @(
    @{ VMName = "SRV01"; Profile = "WinSvr2022_MS_DomainJoined" }
    @{ VMName = "SRV02"; Profile = "WinSvr2022_MS_Standalone" }
)

$results = foreach ($entry in $testMatrix) {
    Write-Host "Running E2E on $($entry.VMName) ($($entry.Profile))..."
    & .\tests\e2e\Invoke-E2EPipeline.ps1 -VMName $entry.VMName -Credential $cred
}

# Summary table
$results | Format-Table VM, Passed, @{N="Duration(s)";E={
    ($_.Timings.Values | Measure-Object -Sum).Sum / 1000
}} -AutoSize
```

---

## Phase 5: Regression & Rollback Tests

### 5.1  -  Snapshot Rollback Verification

After hardening, verify rollback works:

```powershell
# 1. Record post-harden state
$postHarden = Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    auditpol /get /category:* | Out-String
    Get-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\EventLog\Security" -ErrorAction SilentlyContinue
}

# 2. Restore pre-harden checkpoint
Restore-VMCheckpoint -VMName "SRV01" -Name "PreHarden_*" -Confirm:$false
Start-VM -Name "SRV01"
Start-Sleep -Seconds 60  # Wait for boot

# 3. Verify state is restored
$preHarden = Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    auditpol /get /category:* | Out-String
}

# 4. Compare - should differ (proving harden actually changed things)
if ($postHarden -eq $preHarden) {
    Write-Warning "Post-harden and pre-harden states are identical - hardening may not have applied"
}
```

### 5.2  -  Idempotency Test

Run harden twice on same VM  -  second run should produce 0 new changes:

```powershell
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # First harden
    & $sf harden --output "C:\STIGForge-Test\output\harden-run1" --machine localhost 2>&1

    # Second harden (idempotent)
    & $sf harden --output "C:\STIGForge-Test\output\harden-run2" --machine localhost 2>&1

    $run1 = Get-Content "C:\STIGForge-Test\output\harden-run1\harden-summary.json" -Raw | ConvertFrom-Json
    $run2 = Get-Content "C:\STIGForge-Test\output\harden-run2\harden-summary.json" -Raw | ConvertFrom-Json

    Write-Host "Run 1 applied: $($run1.appliedCount)"
    Write-Host "Run 2 applied: $($run2.appliedCount)"

    if ($run2.appliedCount -gt 0) {
        Write-Warning "Harden is not idempotent - $($run2.appliedCount) changes on second run"
    }
}
```

### 5.3  -  Compliance Delta Test

Verify compliance improves after hardening:

```powershell
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $baseline = Get-Content "C:\STIGForge-Test\output\scan\scan-summary.json" -Raw | ConvertFrom-Json
    $postHarden = Get-Content "C:\STIGForge-Test\output\verify\verify-summary.json" -Raw | ConvertFrom-Json

    $delta = $postHarden.compliancePercent - $baseline.compliancePercent
    Write-Host "Baseline compliance: $($baseline.compliancePercent)%"
    Write-Host "Post-harden compliance: $($postHarden.compliancePercent)%"
    Write-Host "Delta: +$($delta)%"

    if ($delta -le 0) {
        throw "Compliance did not improve after hardening (delta: $delta%)"
    }
}
```

---

## Phase 6: Domain Controller Tests (DC01)

**Prereq**: DC01 started, domain services healthy (~15 min boot)

### 6.1  -  DC-Specific STIG Validation

```powershell
# Start DC01 and wait for AD DS
Start-VM -Name "DC01"
# DC01 takes ~15 min after snapshot restore

Invoke-Command -VMName "DC01" -Credential $dcCred -ScriptBlock {
    # Verify AD DS is healthy
    $dc = Get-ADDomainController -Identity $env:COMPUTERNAME
    if (-not $dc) { throw "AD DS not ready" }

    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Scan with DC-specific STIG profile
    & $sf scan `
        --evaluate-stig-path "C:\EvaluateSTIG\Evaluate-STIG.ps1" `
        --output "C:\STIGForge-Test\output\dc-scan" `
        --select-stig "Windows_Server_2022_DC" `
        --machine localhost 2>&1

    if ($LASTEXITCODE -ne 0) { throw "DC scan failed" }
}
```

### 6.2  -  GPO Import/Export Validation

```powershell
Invoke-Command -VMName "DC01" -Credential $dcCred -ScriptBlock {
    $sf = "C:\STIGForge-Test\Cli\STIGForge.Cli.exe"

    # Import GPO-based STIG content
    & $sf import --input "C:\STIGForge-Test\fixtures\gpo-backup" --output "C:\STIGForge-Test\output\gpo-import" 2>&1

    # Verify GPO content was parsed
    & $sf list-packs --format json 2>&1
}
```

---

## Phase 7: Edge Case & Negative Tests

### 7.1  -  Missing Tool Paths

```powershell
# Should fail gracefully with clear error, not crash
& $sf scan --evaluate-stig-path "C:\NonExistent\Tool.ps1" --output "C:\STIGForge-Test\output\neg1" 2>&1
# Expected: Non-zero exit code, descriptive error message
```

### 7.2  -  Invalid STIG Content

```powershell
# Feed malformed XML
& $sf import --input "C:\STIGForge-Test\fixtures\malformed" --output "C:\STIGForge-Test\output\neg2" 2>&1
# Expected: Warning/skip, no crash, non-zero if all invalid
```

### 7.3  -  Non-Admin Execution

```powershell
# Run scan without elevation
$nonAdminCred = Get-Credential -UserName "TestUser"
Invoke-Command -VMName "SRV01" -Credential $nonAdminCred -ScriptBlock {
    & "C:\STIGForge-Test\Cli\STIGForge.Cli.exe" scan --output "C:\temp\test" 2>&1
}
# Expected: Elevation required error with clear message
```

### 7.4  -  Concurrent Execution

```powershell
# Run two instances simultaneously  -  verify no DB corruption
$job1 = Start-Job -ScriptBlock {
    Invoke-Command -VMName "SRV01" -Credential $using:cred -ScriptBlock {
        & "C:\STIGForge-Test\Cli\STIGForge.Cli.exe" import --input "C:\STIGForge-Test\fixtures" --output "C:\STIGForge-Test\output\concurrent1" 2>&1
    }
}
$job2 = Start-Job -ScriptBlock {
    Invoke-Command -VMName "SRV01" -Credential $using:cred -ScriptBlock {
        & "C:\STIGForge-Test\Cli\STIGForge.Cli.exe" import --input "C:\STIGForge-Test\fixtures" --output "C:\STIGForge-Test\output\concurrent2" 2>&1
    }
}
Wait-Job $job1, $job2
# Expected: Both complete without SQLite lock errors
```

### 7.5  -  Large Content Pack Stress

```powershell
# Import the full DISA STIG library (all benchmarks)
& $sf import --input "C:\STIGForge-Test\fixtures\full-disa-library" --output "C:\STIGForge-Test\output\stress" 2>&1
# Expected: Completes within 5 minutes, memory stays under 500 MB
```

---

## Phase 8: WPF App E2E (SRV01  -  RDP Session)

### 8.1  -  Full Workflow via GUI

| Step | Action | Verification |
|------|--------|-------------|
| 1 | Launch STIGForge.App.exe | Window appears, dark theme |
| 2 | Settings → Set Import Folder | Path persists after save |
| 3 | Settings → Set Evaluate-STIG path | Path validates on save |
| 4 | Settings → Set Output Folder | Path validates on save |
| 5 | Import tab → Run Import | Progress bar, status updates, items count increases |
| 6 | Workflow tab → Run Scan | Scan completes, compliance text appears |
| 7 | Workflow tab → Run Harden | Harden completes, applied count > 0 |
| 8 | Workflow tab → Run Verify | Verify completes, compliance improves |
| 9 | Results tab → Open Output Folder | Explorer opens to correct path |
| 10 | Compliance Summary tab | Donut chart shows, stats match, icons visible |
| 11 | Run Auto (Ctrl+R) | All 4 steps run sequentially to completion |
| 12 | Restart Workflow (Ctrl+N) | Confirmation dialog → Yes → all states reset |

### 8.2  -  Wizard Mode E2E

| Step | Action | Verification |
|------|--------|-------------|
| 1 | Toggle wizard mode | Step indicator shows 6 steps |
| 2 | Fill Setup fields, click Next | Advances to Import, auto-runs |
| 3 | Import completes, click Next | Advances to Scan, auto-runs |
| 4 | Scan completes, click Next | Advances to Harden |
| 5 | Harden completes, click Next | Advances to Verify |
| 6 | Verify completes, click Next | Shows Done step with checkmark |
| 7 | Click step 1 circle | Navigates back to Setup |
| 8 | Click Back button | Steps backward correctly |

---

## Phase 9: Accessibility Validation (SRV01)

### 9.1  -  Windows Narrator Test

| Area | Test | Expected |
|------|------|----------|
| Tab headers | Navigate with arrow keys | Tab name + glyph state announced |
| Step indicator | Tab through wizard steps | AutomationProperties.Name read |
| Donut chart | Focus chart area | "Compliance donut chart" + help text read |
| Buttons | Tab to each button | Name and tooltip read |
| Status banner | Step completes | LiveSetting="Polite" announces update |
| Failure card | Error occurs | LiveSetting="Assertive" announces recovery guidance |

### 9.2  -  High Contrast Mode

1. Enable Windows High Contrast (Settings → Accessibility → High Contrast → On)
2. Verify all semantic colors are distinguishable:
   - Success (HighlightColor/cyan) vs normal text
   - Warning (HotTrackColor/yellow) vs accent
   - Danger (MenuHighlightColor/red) vs warning
3. Verify shadow effects are transparent (no dark blobs)
4. Tab through all views  -  verify all text is readable

---

## Phase 10: Performance Benchmarks

### 10.1  -  Import Performance

```powershell
# Measure import of varying content sizes
@(1, 5, 10, 50) | ForEach-Object {
    $packCount = $_
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    # Import $packCount STIG packs
    $sw.Stop()
    [PSCustomObject]@{ Packs = $packCount; DurationMs = $sw.ElapsedMilliseconds }
} | Format-Table -AutoSize
```

### 10.2  -  Memory Profiling

```powershell
Invoke-Command -VMName "SRV01" -Credential $cred -ScriptBlock {
    $proc = Start-Process "C:\STIGForge-Test\App\STIGForge.App.exe" -PassThru
    Start-Sleep -Seconds 10
    $mem = $proc.WorkingSet64 / 1MB
    Write-Host "App memory at idle: $([math]::Round($mem, 1)) MB"

    # After full workflow, check again
    Start-Sleep -Seconds 120  # Allow workflow to complete
    $proc.Refresh()
    $memPost = $proc.WorkingSet64 / 1MB
    Write-Host "App memory after workflow: $([math]::Round($memPost, 1)) MB"

    $proc.Kill()
}
```

---

## Execution Order & Dependencies

```
Phase 0 (Provisioning) ──┐
                         │
Phase 1 (Unit/Contract)  │  ← Can run in parallel on host
                         │
Phase 2 (UI Smoke) ──────┤  ← Requires SRV01 RDP
                         │
Phase 3 (CLI E2E) ───────┤  ← SRV01 + SRV02 in parallel
                         │
Phase 4 (Full Pipeline) ─┤  ← SRV01 + SRV02 sequential
                         │
Phase 5 (Regression) ────┤  ← After Phase 4
                         │
Phase 6 (DC Tests) ──────┤  ← DC01 (independent, slow boot)
                         │
Phase 7 (Edge Cases) ────┤  ← Any VM
                         │
Phase 8 (WPF E2E) ───────┤  ← SRV01 RDP
                         │
Phase 9 (Accessibility) ─┤  ← SRV01 RDP
                         │
Phase 10 (Performance) ──┘  ← SRV01
```

## Artifacts Collected

| Artifact | Location | Format |
|----------|----------|--------|
| Unit test results | `.artifacts/test-results/unit-tests.trx` | TRX |
| Integration test results | `.artifacts/test-results/integration-tests.trx` | TRX |
| UI smoke screenshots | `.artifacts/ui-smoke/<vm>/` | PNG |
| E2E pipeline results | `.artifacts/e2e/<vm>/` | JSON + CKL + CSV |
| Release gate report | `.artifacts/release-gate/<vm>/` | JSON + MD |
| Stability budget | `.artifacts/stability-budget/<vm>/` | JSON + MD |
| Performance benchmarks | `.artifacts/perf/<vm>/` | JSON |

## Go/No-Go Criteria

| Gate | Requirement |
|------|------------|
| Unit tests | 927/927 pass |
| Integration tests | 102/102 pass |
| UI smoke | All 6 FlaUI tests pass (interactive session) |
| Full E2E pipeline | Pass on at least 2 VMs (SRV01 + SRV02) |
| Compliance delta | Post-harden > baseline on all VMs |
| Idempotency | Second harden produces 0 new changes |
| Accessibility | Narrator + HighContrast pass manual checks |
| Performance | Import < 60s, App memory < 300 MB |
| Release gate | All 5 validation stages pass |
