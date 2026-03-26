# STIGForge Lab - Step 6: Run Evaluate-STIG Scan and Report
# Run on: WS01 (.\Install or domain admin)
# Prerequisites: Evaluate-STIG installed

Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force

$esPath = Find-EvaluateStig
if (-not $esPath) {
    Write-Host "ERROR: Evaluate-STIG not found"
    return
}

$resultsDir = 'C:\StigResults\ws01-standalone'
Confirm-Directory $resultsDir

$result = Invoke-StigScan -StepName 'ws01-scan' -EvaluateStigPath $esPath -ResultsDir $resultsDir -Detailed

Write-Host ""
Write-Host "  ================================================"
Write-Host "  OVERALL COMPLIANCE (excl N/A)"
Write-Host "  $($result.TotalNaf) / $($result.TotalApp) ($($result.TotalPct)%)"
Write-Host "  Open=$($result.TotalOpen)  Not_Reviewed=$($result.TotalNR)  N/A=$($result.TotalNA)"
Write-Host "  ================================================"

# List remaining Open findings
if ($result.TotalOpen -gt 0) {
    Write-Host ""
    Write-Host "  REMAINING OPEN FINDINGS:"
    $cklFiles = Get-ChildItem "$resultsDir\scan-ws01-scan" -Filter '*.ckl' -Recurse -ErrorAction SilentlyContinue
    foreach ($ckl in $cklFiles) {
        [xml]$xml = Get-Content $ckl.FullName
        $openVulns = $xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Open' }
        foreach ($v in $openVulns) {
            $vid = ($v.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Vuln_Num' }).ATTRIBUTE_DATA
            $sev = ($v.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Severity' }).ATTRIBUTE_DATA
            $title = ($v.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Rule_Title' }).ATTRIBUTE_DATA
            if ($title.Length -gt 80) { $title = $title.Substring(0, 80) + '...' }
            Write-Host "    $vid ($sev): $title"
        }
    }
}
