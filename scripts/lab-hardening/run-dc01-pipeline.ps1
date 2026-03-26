# STIGForge Lab - DC01 Full Hardening Pipeline with Per-Step Compliance Tracking
# Run on: DC01 as Administrator (lab\Install or lab\Administrator)
# Prerequisites: import/ folder contents copied to C:\temp\
#
# Priority order:
#   0. Module install (E-STIG + PowerSTIG)
#   0b. AD setup (OUs, accounts, STIG-IE11/DotNet GPOs via 01+02)
#   1. Baseline scan
#   2. DSC (PowerSTIG) - bulk baseline settings
#   3. DISA ADMX/GPO import + LGPO - official DISA baselines
#   4. Certificates - DoD/ECA certs
#   5. Custom GPOs - fill gaps (registry, security policy, audit) + link to DC+MS OUs
#   6. Script fallback remediation - non-GPO items (AD ops, DNS, LDAP, NTP, optional IE11)

$ErrorActionPreference = 'Continue'
$scriptDir = 'C:\temp\scripts'
$resultsDir = 'C:\StigResults\pipeline'
$logFile = "$resultsDir\pipeline-log.txt"

# Load shared modules
Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force
Import-Module "$PSScriptRoot\lib\StigForge-WinRM.psm1" -Force

Confirm-Directory $resultsDir

# Find Evaluate-STIG
$esPath = Find-EvaluateStig

if (-not $esPath) {
    Write-Host "NOTE: Evaluate-STIG.ps1 not yet found - will be extracted by module install step"
} else {
    Write-Host "Evaluate-STIG: $esPath"
}

# ============================================
# Pipeline Start
# ============================================
Write-Host "=== STIGForge DC01 Hardening Pipeline ==="
Write-Host "  Host: $env:COMPUTERNAME"
Write-Host "  Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host "  Priority: DSC > DISA GPO > Certs > Custom GPO > Script fallback"
Write-Host ""

if (-not (Test-Path $scriptDir)) {
    Write-Host "ERROR: Scripts not found at $scriptDir"
    exit 1
}

"=== Pipeline Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Set-Content $logFile
"Host: $env:COMPUTERNAME" | Add-Content $logFile
"" | Add-Content $logFile

$allSteps = @()
$winRmOriginal = $null

# ============================================
# STEP 0: Install E-STIG + DSC modules
# ============================================
Assert-NotWinRmHosted -LogFile $logFile

Invoke-PipelineStep -StepName '0' -Description 'Module install (E-STIG + PowerSTIG + DSC)' -LogFile $logFile -Action {
    & "$scriptDir\00-install-modules.ps1"
}

# Re-resolve Evaluate-STIG path after install
$esPath = Find-EvaluateStig
if (-not $esPath) {
    Write-PipelineLog "ERROR: Evaluate-STIG still not found after module install" $logFile
    exit 1
}
Write-PipelineLog "Evaluate-STIG resolved to: $esPath" $logFile

# ============================================
# STEP 0b: AD Setup (OUs, accounts, WS/common GPOs)
# ============================================
Invoke-PipelineStep -StepName '0b' -Description 'AD setup (OUs, accounts, GPOs)' -LogFile $logFile -Action {
    & "$scriptDir\01-dc01-create-accounts.ps1"
    & "$scriptDir\02-dc01-create-gpos.ps1"
}

# ============================================
# STEP 1: Baseline scan
# ============================================
Write-PipelineLog "STEP 1: Starting baseline scan" $logFile
$allSteps += Invoke-StigScan -StepName '00-baseline' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# ============================================
# STEP 1a: Capture WinRM state
# ============================================
Write-PipelineLog "STEP 1a: Capturing WinRM state" $logFile
$winRmOriginal = Save-WinRmState -LogFile $logFile

try {
    # ============================================
    # STEP 2: DSC Hardening
    # ============================================
    Invoke-PipelineStep -StepName '2' -Description 'DSC hardening' -LogFile $logFile -Action {
        & "$scriptDir\07-dc01-dsc-hardening.ps1"
    }
    gpupdate /force 2>&1 | Out-Null
    $allSteps += Invoke-StigScan -StepName '02-after-dsc' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

    # ============================================
    # STEP 2a: Temporarily disable WinRM
    # ============================================
    Write-PipelineLog "STEP 2a: Temporarily disabling WinRM" $logFile
    $winRmOriginal = Disable-WinRmTemporarily -State $winRmOriginal -LogFile $logFile

    # ============================================
    # STEP 3: ADMX + LGPO + DISA GPO Import
    # ============================================
    Invoke-PipelineStep -StepName '3' -Description 'ADMX + LGPO' -LogFile $logFile -Action {
        & "$scriptDir\07a-dc01-admx-lgpo.ps1"
    }
    gpupdate /force 2>&1 | Out-Null
    $allSteps += Invoke-StigScan -StepName '03-after-admx-lgpo' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

    # ============================================
    # STEP 4: Certificate Installation
    # ============================================
    Invoke-PipelineStep -StepName '4' -Description 'Certificate install' -LogFile $logFile -Action {
        & "$scriptDir\10-dc01-install-certs.ps1"
    }
    $allSteps += Invoke-StigScan -StepName '04-after-certs' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

    # ============================================
    # STEP 5: Custom STIG GPOs
    # ============================================
    Invoke-PipelineStep -StepName '5' -Description 'Custom GPOs' -LogFile $logFile -Action {
        & "$scriptDir\08-dc01-stig-gpos.ps1"
    }
    gpupdate /force 2>&1 | Out-Null
    $allSteps += Invoke-StigScan -StepName '05-after-gpos' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

    # ============================================
    # STEP 6: Script Fallback Remediation
    # ============================================
    Invoke-PipelineStep -StepName '6' -Description 'Local-only fallback remediation' -LogFile $logFile -Action {
        & "$scriptDir\09-dc01-local-hardening.ps1"
        if (Test-Path "$scriptDir\09a-dc01-ie11-hardening.ps1") {
            & "$scriptDir\09a-dc01-ie11-hardening.ps1"
        }
    }
    gpupdate /force 2>&1 | Out-Null
    $allSteps += Invoke-StigScan -StepName '06-after-script-fallback' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed
}
finally {
    Write-PipelineLog "Finalizing: restoring WinRM state" $logFile
    Restore-WinRmState -State $winRmOriginal -LogFile $logFile
}

# ============================================
# FINAL: Delta Summary
# ============================================
Write-DeltaSummary -Steps $allSteps -LogFile $logFile
Write-StigBreakdown -Steps $allSteps

Write-Host "================================================================"
Write-Host "  Pipeline complete. Log: $logFile"
Write-Host "  CKL results: $resultsDir"
Write-Host "================================================================"

Set-Content "$resultsDir\PIPELINE-DONE.txt" "Completed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
