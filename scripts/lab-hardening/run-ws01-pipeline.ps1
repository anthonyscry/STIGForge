# STIGForge Lab - WS01 Hardening Pipeline
# Run on: WS01 as Administrator (.\Install or domain admin)
# Prerequisites: import/ folder contents copied to C:\temp\

$ErrorActionPreference = 'Continue'
$scriptDir = 'C:\temp\scripts'
$resultsDir = 'C:\StigResults\pipeline-ws'
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
$allSteps += Invoke-StigScan -StepName '00-baseline' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile

# STEP 2: DSC hardening
Invoke-PipelineStep -StepName '2' -Description 'DSC hardening' -LogFile $logFile -Action {
    & "$scriptDir\03-ws01-dsc-hardening.ps1"
}
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '02-after-dsc' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile

# STEP 3: Domain policy refresh
Write-PipelineLog "STEP 3: Domain policy baseline refresh" $logFile
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '03-after-policy-refresh' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile

# STEP 4: Certificate install
Invoke-PipelineStep -StepName '4' -Description 'Certificate install' -LogFile $logFile -Action {
    & "$scriptDir\05-ws01-install-certs.ps1"
}
$allSteps += Invoke-StigScan -StepName '04-after-certs' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile

# STEP 5: Custom GPO remediation
Write-PipelineLog "STEP 5: Applying workstation custom GPO links via gpupdate" $logFile
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '05-after-custom-gpo' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile

# STEP 6: Script fallback
Invoke-PipelineStep -StepName '6' -Description 'Script fallback remediation' -LogFile $logFile -Action {
    & "$scriptDir\04-ws01-local-hardening.ps1"
}
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '06-after-script-fallback' -EvaluateStigPath $esPath -ResultsDir $resultsDir -LogFile $logFile

# FINAL: Delta summary
Write-DeltaSummary -Steps $allSteps -LogFile $logFile

Set-Content "$resultsDir\PIPELINE-DONE.txt" "Completed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
