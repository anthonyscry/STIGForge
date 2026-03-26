# STIGForge Lab - Master Pipeline: DC + MS + WS with Full Metrics
# Run on: Each target machine as Administrator
# OR: Remotely via run-on-hyperv-host.sh / scheduled task
#
# Auto-detects machine role and runs the appropriate pipeline.
# Records: before/after compliance, per-step timing, SCC results,
#          total wall clock time, and estimated human time saved.
#
# Prerequisites: import/ folder contents copied to C:\temp\ on each target
#   Including: scc-5.14_Windows_bundle.zip (portable, no install needed)
#
# Usage:
#   .\run-all-pipeline.ps1                   # auto-detect role
#   .\run-all-pipeline.ps1 -Role DC          # force DC pipeline
#   .\run-all-pipeline.ps1 -Role MS          # force MS pipeline
#   .\run-all-pipeline.ps1 -Role WS          # force WS pipeline
#   .\run-all-pipeline.ps1 -SkipSCC          # skip SCC scan
#   .\run-all-pipeline.ps1 -BaselineOnly     # just scan, no hardening

param(
    [ValidateSet('DC','MS','WS','Auto')]
    [string]$Role = 'Auto',
    [ValidateSet('classified','unclassified')]
    [string]$Classification = 'unclassified',
    [switch]$SkipSCC,
    [switch]$BaselineOnly
)

$ErrorActionPreference = 'Continue'
$pipelineStart = Get-Date
$scriptDir = 'C:\temp\scripts'
$tempDir = 'C:\temp'

# Load shared modules
Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force
Import-Module "$PSScriptRoot\lib\StigForge-OrgSettings.psm1" -Force

# ============================================
# Detect role
# ============================================
if ($Role -eq 'Auto') {
    $detected = Get-ServerRole
    $Role = $detected.Type
}

$hostName = $env:COMPUTERNAME
$resultsDir = "C:\StigResults\pipeline-$($Role.ToLower())"
$logFile = "$resultsDir\pipeline-log.txt"
$metricsFile = "$resultsDir\metrics.json"
Confirm-Directory $resultsDir

Write-Host "================================================================"
Write-Host "  STIGForge Master Pipeline"
Write-Host "  Host:    $hostName"
Write-Host "  Role:    $Role"
Write-Host "  Date:    $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host "  Results: $resultsDir"
Write-Host "================================================================"
Write-Host ""

"=== Pipeline Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Set-Content $logFile
"Host: $hostName | Role: $Role" | Add-Content $logFile
"" | Add-Content $logFile

# ============================================
# Metrics tracking
# ============================================
$metrics = [ordered]@{
    host          = $hostName
    role          = $Role
    startTime     = $pipelineStart.ToString('yyyy-MM-dd HH:mm:ss')
    endTime       = $null
    totalSeconds  = 0
    stepTimings   = @()
    baseline      = $null
    final         = $null
    delta         = $null
    sccResults    = $null
    humanEstimate = $null
}

$stepTimings = @()

function Add-StepTiming {
    param([string]$Step, [double]$Seconds)
    $script:stepTimings += [PSCustomObject]@{ Step = $Step; Seconds = $Seconds }
}

# ============================================
# Find tools
# ============================================
$esPath = Find-EvaluateStig
$sccPath = if (-not $SkipSCC) { Find-SCC } else { $null }

if (-not $esPath) {
    Write-Host "NOTE: Evaluate-STIG not yet found - will be extracted by module install"
}
if (-not $SkipSCC -and -not $sccPath) {
    Write-Host "NOTE: SCC not found - will look again after module install"
    Write-Host "  Place scc-5.14_Windows_bundle.zip in C:\temp\"
}

if (-not (Test-Path $scriptDir)) {
    Write-Host "ERROR: Scripts not found at $scriptDir"
    exit 1
}

$allSteps = @()

# ============================================
# STEP 0: Install modules
# ============================================
$t = Invoke-PipelineStep -StepName '0' -Description 'Module install (E-STIG + PowerSTIG + DSC)' -LogFile $logFile -Action {
    & "$scriptDir\00-install-modules.ps1"
}
Add-StepTiming 'Module Install' $t

# Re-resolve tools after install
$esPath = Find-EvaluateStig
if (-not $esPath) {
    Write-PipelineLog "ERROR: Evaluate-STIG still not found after module install" $logFile
    exit 1
}
Write-PipelineLog "Evaluate-STIG: $esPath" $logFile

if (-not $SkipSCC -and -not $sccPath) {
    $sccPath = Find-SCC
    if ($sccPath) { Write-PipelineLog "SCC: $sccPath" $logFile }
    else { Write-PipelineLog "WARN: SCC not available. Place scc-5.14_Windows_bundle.zip in C:\temp\" $logFile }
}

# ============================================
# STEP 0b: AD Setup (DC only)
# ============================================
if ($Role -eq 'DC') {
    $t = Invoke-PipelineStep -StepName '0b' -Description 'AD setup (OUs, accounts, GPOs)' -LogFile $logFile -Action {
        & "$scriptDir\01-dc01-create-accounts.ps1"
        & "$scriptDir\02-dc01-create-gpos.ps1"
    }
    Add-StepTiming 'AD Setup' $t
}

# ============================================
# STEP 1: Baseline scan (BEFORE hardening)
# ============================================
Write-PipelineLog "STEP 1: Baseline scan" $logFile
$baselineScan = Invoke-StigScan -StepName '00-baseline' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed
$allSteps += $baselineScan
$metrics.baseline = [ordered]@{
    compliance = $baselineScan.TotalPct
    naf        = $baselineScan.TotalNaf
    applicable = $baselineScan.TotalApp
    open       = $baselineScan.TotalOpen
    nr         = $baselineScan.TotalNR
    na         = $baselineScan.TotalNA
}

# SCC baseline
$sccBaseline = $null
if ($sccPath) {
    $sccBaseline = Invoke-SCCScan -SCCPath $sccPath -ResultsDir "$resultsDir\scc-baseline" -LogFile $logFile
    Add-StepTiming 'SCC Baseline Scan' $sccBaseline.ScanSeconds
}

if ($BaselineOnly) {
    Write-Host ""
    Write-Host "  Baseline-only mode. Skipping hardening."
    $metrics.endTime = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $metrics.totalSeconds = [math]::Round(((Get-Date) - $pipelineStart).TotalSeconds, 1)
    $metrics.stepTimings = $stepTimings
    $metrics | ConvertTo-Json -Depth 5 | Set-Content $metricsFile
    exit 0
}

# ============================================
# WinRM management (DC only)
# ============================================
$winRmOriginal = $null
if ($Role -eq 'DC') {
    Import-Module "$PSScriptRoot\lib\StigForge-WinRM.psm1" -Force
    Assert-NotWinRmHosted -LogFile $logFile
    $winRmOriginal = Save-WinRmState -LogFile $logFile
}

# Outer try/finally guarantees WinRM restore even if a step throws a terminating error.
# Each step also has its own inner try/catch for non-fatal error handling.
try {

    # ============================================
    # STEP 2: DSC Hardening
    # ============================================
    switch ($Role) {
        'WS' { $dscScript = '03-ws01-dsc-hardening.ps1' }
        default { $dscScript = '07-dc01-dsc-hardening.ps1' }
    }
    $t = Invoke-PipelineStep -StepName '2' -Description 'DSC hardening' -LogFile $logFile -Action {
        & "$scriptDir\$dscScript"
    }
    Add-StepTiming 'DSC Hardening' $t

    # DSC and its child scripts may set ErrorActionPreference=Stop or leave
    # terminating-error traps. Reset everything defensively.
    $ErrorActionPreference = 'Continue'
    $global:ErrorActionPreference = 'Continue'

    Write-PipelineLog "Post-DSC: running gpupdate + scan" $logFile
    try { gpupdate /force 2>&1 | Out-Null } catch { Write-PipelineLog "WARN: gpupdate failed after DSC: $_" $logFile }
    try {
        $postDscScan = Invoke-StigScan -StepName '02-after-dsc' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed
        if ($postDscScan) { $allSteps += $postDscScan }
    } catch { Write-PipelineLog "WARN: Post-DSC scan failed: $_" $logFile }

    # ============================================
    # STEP 2a: Disable WinRM temporarily (DC only)
    # ============================================
    $ErrorActionPreference = 'Continue'
    if ($Role -eq 'DC' -and $winRmOriginal) {
        Write-PipelineLog "STEP 2a: Temporarily disabling WinRM" $logFile
        try { $winRmOriginal = Disable-WinRmTemporarily -State $winRmOriginal -LogFile $logFile } catch { Write-PipelineLog "WARN: WinRM disable failed: $_" $logFile }
    }

    # ============================================
    # STEP 3: ADMX + LGPO (DC/MS only)
    # ============================================
    $ErrorActionPreference = 'Continue'
    if ($Role -in @('DC','MS')) {
        $t = Invoke-PipelineStep -StepName '3' -Description 'ADMX + LGPO import' -LogFile $logFile -Action {
            & "$scriptDir\07a-dc01-admx-lgpo.ps1"
        }
        Add-StepTiming 'ADMX/LGPO Import' $t
    } else {
        Write-PipelineLog "STEP 3: Domain policy baseline refresh" $logFile
    }
    try { gpupdate /force 2>&1 | Out-Null } catch { Write-PipelineLog "WARN: gpupdate failed: $_" $logFile }
    try { $allSteps += Invoke-StigScan -StepName '03-after-admx-lgpo' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed } catch { Write-PipelineLog "WARN: Scan failed after ADMX: $_" $logFile }

    # ============================================
    # STEP 4: Certificate Installation
    # ============================================
    switch ($Role) {
        'WS' { $certScript = '05-ws01-install-certs.ps1' }
        default { $certScript = '10-dc01-install-certs.ps1' }
    }
    $t = Invoke-PipelineStep -StepName '4' -Description 'Certificate install' -LogFile $logFile -Action {
        & "$scriptDir\$certScript"
    }
    Add-StepTiming 'Certificate Install' $t
    try { $allSteps += Invoke-StigScan -StepName '04-after-certs' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed } catch { Write-PipelineLog "WARN: Scan failed after certs: $_" $logFile }

    # ============================================
    # STEP 5: Custom STIG GPOs (DC/MS: full GPOs; WS: gpupdate only)
    # ============================================
    if ($Role -in @('DC','MS')) {
        $t = Invoke-PipelineStep -StepName '5' -Description 'Custom STIG GPOs' -LogFile $logFile -Action {
            & "$scriptDir\08-dc01-stig-gpos.ps1"
        }
        Add-StepTiming 'Custom GPOs' $t
    } else {
        Write-PipelineLog "STEP 5: Applying workstation GPO links via gpupdate" $logFile
    }
    try { gpupdate /force 2>&1 | Out-Null } catch { Write-PipelineLog "WARN: gpupdate failed: $_" $logFile }
    try { $allSteps += Invoke-StigScan -StepName '05-after-gpos' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed } catch { Write-PipelineLog "WARN: Scan failed after GPOs: $_" $logFile }

    # ============================================
    # STEP 6: Script Fallback Remediation
    # ============================================
    switch ($Role) {
        'WS' {
            $t = Invoke-PipelineStep -StepName '6' -Description 'Script fallback (BitLocker)' -LogFile $logFile -Action {
                & "$scriptDir\04-ws01-local-hardening.ps1"
            }
        }
        default {
            $t = Invoke-PipelineStep -StepName '6' -Description 'Local-only fallback remediation' -LogFile $logFile -Action {
                & "$scriptDir\09-dc01-local-hardening.ps1"
                if (Test-Path "$scriptDir\09a-dc01-ie11-hardening.ps1") {
                    & "$scriptDir\09a-dc01-ie11-hardening.ps1"
                }
            }
        }
    }
    Add-StepTiming 'Script Fallback' $t
    try { gpupdate /force 2>&1 | Out-Null } catch { Write-PipelineLog "WARN: gpupdate failed: $_" $logFile }
    $finalScan = [PSCustomObject]@{
        Step = '06-after-script-fallback'; TotalNaf = 0; TotalApp = 0; TotalPct = 0
        TotalOpen = 0; TotalNR = 0; TotalNA = 0
        TotalChecks = 0; ClosedTotal = 0; ClosedPct = 0; Details = @()
    }
    try {
        $finalScan = Invoke-StigScan -StepName '06-after-script-fallback' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed
        $allSteps += $finalScan
    } catch { Write-PipelineLog "WARN: Final scan failed: $_" $logFile }

    # ============================================
    # STEP 7: SCC Final Scan
    # ============================================
    $sccFinal = $null
    if ($sccPath) {
        $sccFinal = Invoke-SCCScan -SCCPath $sccPath -ResultsDir "$resultsDir\scc-final" -LogFile $logFile
        Add-StepTiming 'SCC Final Scan' $sccFinal.ScanSeconds
    }

# ============================================
# STEP 8: Apply Org Settings (classification + environment N/A markings)
# ============================================
$ErrorActionPreference = 'Continue'
try {
    Write-PipelineLog "STEP 8: Applying OrgSettings ($Classification)" $logFile

    # Detect environment conditions
    $envConditions = @{
        hasSmartCard   = $false   # No smart card hardware in lab
        hasLAPS        = $false   # LAPS not deployed
        hasSIEM        = $false   # No SIEM in lab
        hasIDS         = $false   # No IDS/IPS in lab
        hasAppLocker   = $false   # No AppLocker/WDAC
        hasBackup      = $false   # No enterprise backup
        hasFIM         = $false   # No FIM
        isVirtualMachine = $true  # Lab VMs
        hasADCS        = $false   # No AD CS
        hasFTP         = $false   # No FTP
        hasSplitDNS    = $false   # No split DNS
        hasMultipleDCs = $true    # DC01 + DC02
    }

    # Apply to the final scan CKL directory
    $finalCklDir = "$resultsDir\scan-06-after-script-fallback"
    if (Test-Path $finalCklDir) {
        $orgChanged = Apply-OrgSettings -CklDirectory $finalCklDir -Classification $Classification `
            -EnvironmentConditions $envConditions -OrgSettingsDir "$PSScriptRoot\org-settings"
        Write-PipelineLog "OrgSettings: $orgChanged findings marked N/A" $logFile
    }
} catch {
    Write-PipelineLog "WARN: OrgSettings failed: $_" $logFile
}

} finally {
    # ============================================
    # Restore WinRM (DC only) — guaranteed even on terminating errors
    # ============================================
    if ($Role -eq 'DC' -and $winRmOriginal) {
        Write-PipelineLog "Finalizing: restoring WinRM state" $logFile
        try { Restore-WinRmState -State $winRmOriginal -LogFile $logFile } catch { Write-PipelineLog "WARN: WinRM restore failed: $_" $logFile }
    }
}

# ============================================
# FINAL: Metrics calculation
# ============================================
$pipelineEnd = Get-Date
$totalSeconds = [math]::Round(($pipelineEnd - $pipelineStart).TotalSeconds, 1)
$totalMinutes = [math]::Round($totalSeconds / 60, 1)

$metrics.endTime = $pipelineEnd.ToString('yyyy-MM-dd HH:mm:ss')
$metrics.totalSeconds = $totalSeconds
$metrics.stepTimings = $stepTimings
$metrics.final = [ordered]@{
    compliance = $finalScan.TotalPct
    naf        = $finalScan.TotalNaf
    applicable = $finalScan.TotalApp
    open       = $finalScan.TotalOpen
    nr         = $finalScan.TotalNR
    na         = $finalScan.TotalNA
}
$metrics.delta = [ordered]@{
    complianceGain = [math]::Round($finalScan.TotalPct - $baselineScan.TotalPct, 1)
    findingsFixed  = $baselineScan.TotalOpen - $finalScan.TotalOpen
    nrResolved     = $baselineScan.TotalNR - $finalScan.TotalNR
}

# SCC metrics
if ($sccBaseline -or $sccFinal) {
    $metrics.sccResults = [ordered]@{
        baseline = if ($sccBaseline) { $sccBaseline.Results } else { $null }
        final    = if ($sccFinal) { $sccFinal.Results } else { $null }
    }
}

# ============================================
# Human time estimate
# DISA manual STIG process benchmarks:
#   - Evaluate/scan: 30-60 min per system (manual STIG Viewer)
#   - Research each finding: 5-15 min per open finding
#   - Remediate each finding: 10-30 min per fix (GPO, registry, policy)
#   - Verify each fix: 5-10 min per finding re-check
#   - Documentation: 30-60 min per system (CKL annotation)
#   - SCC scan + review: 45-60 min per system
# Conservative estimate: 20 min/finding average (research + fix + verify)
# ============================================
$findingsFixed = $metrics.delta.findingsFixed
$nrResolved = $metrics.delta.nrResolved
$totalResolved = $findingsFixed + $nrResolved

# Minimum 4 hours for base work (scans, GPO setup, cert install, documentation)
$humanBaseHours = 4.0
$humanPerFinding = 20.0 / 60.0  # 20 min per finding in hours
$humanScanHours = 1.0           # manual scan + SCC review
$humanTotalHours = $humanBaseHours + $humanScanHours + ($totalResolved * $humanPerFinding)
$humanTotalHours = [math]::Round($humanTotalHours, 1)

$automatedHours = [math]::Round($totalSeconds / 3600, 2)
$timeSaved = [math]::Round($humanTotalHours - $automatedHours, 1)
$speedup = if ($automatedHours -gt 0) { [math]::Round($humanTotalHours / $automatedHours, 1) } else { 0 }

$metrics.humanEstimate = [ordered]@{
    manualHours    = $humanTotalHours
    automatedHours = $automatedHours
    hoursSaved     = $timeSaved
    speedupFactor  = "${speedup}x"
    findingsFixed  = $findingsFixed
    nrResolved     = $nrResolved
}

# Save metrics JSON
$metrics | ConvertTo-Json -Depth 5 | Set-Content $metricsFile

# ============================================
# Display Results
# ============================================
Write-DeltaSummary -Steps $allSteps -LogFile $logFile

if ($Role -eq 'DC') {
    Write-StigBreakdown -Steps $allSteps
}

Write-Host ""
Write-Host "================================================================"
Write-Host "  PIPELINE METRICS: $hostName ($Role)"
Write-Host "================================================================"
Write-Host ""
Write-Host "  Compliance:"
Write-Host "    Baseline:     $($baselineScan.TotalNaf)/$($baselineScan.TotalApp) ($($baselineScan.TotalPct)%)"
Write-Host "    Final:        $($finalScan.TotalNaf)/$($finalScan.TotalApp) ($($finalScan.TotalPct)%)"
Write-Host "    Gain:         +$($metrics.delta.complianceGain)%"
Write-Host "    Findings fixed: $findingsFixed open, $nrResolved not-reviewed"
Write-Host ""

if ($sccBaseline -and $sccFinal) {
    Write-Host "  SCC Results:"
    Write-Host "    Baseline benchmarks: $($sccBaseline.XccdfCount)"
    Write-Host "    Final benchmarks:    $($sccFinal.XccdfCount)"
    foreach ($r in $sccFinal.Results) {
        Write-Host "      $($r.Benchmark): $($r.Pass)/$($r.Total) ($($r.Pct)%)"
    }
    Write-Host ""
}

Write-Host "  Timing:"
Write-Host "    Total:        $totalMinutes min ($totalSeconds sec)"
foreach ($st in $stepTimings) {
    Write-Host ("    {0,-20} {1,6}s" -f $st.Step, $st.Seconds)
}
Write-Host ""
Write-Host "  Time Saved (vs manual):"
Write-Host "    Estimated manual: $humanTotalHours hours"
Write-Host "    Automated:        $automatedHours hours"
Write-Host "    Time saved:       $timeSaved hours"
Write-Host "    Speedup:          ${speedup}x faster"
Write-Host ""
Write-Host "  Output:"
Write-Host "    CKL results:  $resultsDir"
Write-Host "    Metrics JSON: $metricsFile"
Write-Host "    Pipeline log: $logFile"
if ($sccFinal) {
    Write-Host "    SCC results:  $resultsDir\scc-final"
}
Write-Host "================================================================"

Set-Content "$resultsDir\PIPELINE-DONE.txt" "Completed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | $($finalScan.TotalPct)% compliance | ${totalMinutes}m"
