# STIGForge Lab - Step 11: Run Evaluate-STIG on DC01
# Run on: DC01 (lab.local\Administrator)
# Prerequisites: Evaluate-STIG installed at C:\Evaluate-STIG\Evaluate-STIG\

$esPath = 'C:\Evaluate-STIG\Evaluate-STIG\Evaluate-STIG.ps1'
$outputDir = 'C:\StigResults\DC01'

if (-not (Test-Path $outputDir)) {
    New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
}

Write-Host "Running Evaluate-STIG on DC01..."
Write-Host "Output: $outputDir"
Write-Host ""

& $esPath -ScanType Unclassified -Output CKL,Summary -OutputPath $outputDir

Write-Host ""
Write-Host "=== Scan Complete ==="
Write-Host "Results in: $outputDir"
Write-Host ""
Write-Host "To view summary:"
Write-Host "  Get-ChildItem '$outputDir\*Summary*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content"
