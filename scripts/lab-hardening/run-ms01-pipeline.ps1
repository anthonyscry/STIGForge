# STIGForge Lab - MS01 Member Server Hardening Pipeline
# Run on: MS01 as Administrator (lab\Administrator or lab\Install)
# Prerequisites: import/ folder contents copied to C:\temp\
# Uses DC01 scripts with auto-detect (DC vs MS role)

$ErrorActionPreference = 'Continue'
$scriptDir = 'C:\temp\scripts'
$resultsDir = 'C:\StigResults\pipeline-ms'
$logFile = "$resultsDir\pipeline-log.txt"

# Load shared modules
Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force

Confirm-Directory $resultsDir

# Find Evaluate-STIG
$esPath = Find-EvaluateStig

if (-not $esPath) {
    Write-Host "NOTE: Evaluate-STIG.ps1 not yet found - will be extracted by module install step"
}

if (-not (Test-Path $scriptDir)) {
    Write-Host "ERROR: Scripts not found at $scriptDir"
    exit 1
}

"=== Pipeline Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Set-Content $logFile
"Host: $env:COMPUTERNAME" | Add-Content $logFile
"" | Add-Content $logFile

$allSteps = @()

# STEP 0: Install modules
Invoke-PipelineStep -StepName '0' -Description 'Install modules (E-STIG + PowerSTIG + DSC)' -LogFile $logFile -Action {
    & "$scriptDir\00-install-modules.ps1"
}

# Re-resolve Evaluate-STIG path
$esPath = Find-EvaluateStig
if (-not $esPath) {
    Write-PipelineLog "ERROR: Evaluate-STIG still not found after module install" $logFile
    exit 1
}
Write-PipelineLog "Evaluate-STIG resolved to: $esPath" $logFile

# STEP 1: Baseline scan
Write-PipelineLog "STEP 1: Baseline scan" $logFile
$allSteps += Invoke-StigScan -StepName '00-baseline' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# STEP 2: DSC hardening (auto-detects MS role)
Invoke-PipelineStep -StepName '2' -Description 'DSC hardening' -LogFile $logFile -Action {
    & "$scriptDir\07-dc01-dsc-hardening.ps1"
}
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '02-after-dsc' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# STEP 3: DISA ADMX/LGPO import
Invoke-PipelineStep -StepName '3' -Description 'DISA ADMX/LGPO import' -LogFile $logFile -Action {
    & "$scriptDir\07a-dc01-admx-lgpo.ps1"
}
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '03-after-admx-lgpo' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# STEP 4: Certificate install
Invoke-PipelineStep -StepName '4' -Description 'Certificate install' -LogFile $logFile -Action {
    & "$scriptDir\10-dc01-install-certs.ps1"
}
$allSteps += Invoke-StigScan -StepName '04-after-certs' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# STEP 5: Custom GPO remediation
Write-PipelineLog "STEP 5: Member Server custom domain GPO stage; applying gpupdate" $logFile
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '05-after-custom-gpo' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# STEP 6: Script fallback (auto-detects MS role)
Invoke-PipelineStep -StepName '6' -Description 'Script fallback remediation' -LogFile $logFile -Action {
    & "$scriptDir\09-dc01-local-hardening.ps1"
    if (Test-Path "$scriptDir\09a-dc01-ie11-hardening.ps1") {
        & "$scriptDir\09a-dc01-ie11-hardening.ps1"
    }
}
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '06-after-script-fallback' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile -Detailed

# FINAL: Delta summary
Write-DeltaSummary -Steps $allSteps -LogFile $logFile

Set-Content "$resultsDir\PIPELINE-DONE.txt" "Completed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
