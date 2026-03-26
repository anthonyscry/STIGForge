# STIGForge Lab - Step 11: Run Evaluate-STIG on DC01
# Run on: DC01 (lab.local\Administrator)
# Prerequisites: Evaluate-STIG installed

Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force

$esPath = Find-EvaluateStig
if (-not $esPath) {
    Write-Host "ERROR: Evaluate-STIG not found"
    return
}

$resultsDir = 'C:\StigResults\dc01-standalone'
Confirm-Directory $resultsDir

$result = Invoke-StigScan -StepName 'dc01-scan' -EvaluateStigPath $esPath -ResultsDir $resultsDir -Detailed

Write-Host ""
Write-Host "=== Scan Complete ==="
Write-Host "  Results in: $resultsDir"
