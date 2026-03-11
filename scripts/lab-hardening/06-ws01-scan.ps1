# STIGForge Lab - Step 5: Run Evaluate-STIG Scan and Report
# Run on: WS01 (.\Install or domain admin)
# Prerequisites: Evaluate-STIG installed at C:\STIGForge-Test\Evaluate-STIG

$esScript = Get-ChildItem 'C:\STIGForge-Test\Evaluate-STIG' -Filter 'Evaluate-STIG.ps1' -Recurse | Select-Object -First 1
if (-not $esScript) {
    Write-Host "ERROR: Evaluate-STIG not found"
    return
}

Set-Location (Split-Path $esScript.FullName -Parent)
Write-Host "Running Evaluate-STIG scan..."
& $esScript.FullName -ScanType Unclassified -Output CKL 2>&1 | Out-Null

$resultsBase = 'C:\Users\Public\Documents\STIG_Compliance\WS01'
$dirs = Get-ChildItem $resultsBase -Directory | Sort-Object LastWriteTime -Descending
$latestDir = $dirs | Select-Object -First 1
$cklFiles = Get-ChildItem $latestDir.FullName -Filter '*.ckl' -Recurse

Write-Host ""
Write-Host "=== STIG Compliance Report ==="
Write-Host "Scan Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host "Results: $($latestDir.FullName)"
Write-Host ""

$totalOpen = 0; $totalNaf = 0; $totalNr = 0; $totalNa = 0

foreach ($ckl in $cklFiles) {
    [xml]$xml = Get-Content $ckl.FullName
    $stigName = $xml.CHECKLIST.STIGS.iSTIG.STIG_INFO.SI_DATA |
        Where-Object { $_.SID_NAME -eq 'title' } |
        Select-Object -ExpandProperty SID_DATA -ErrorAction SilentlyContinue
    $open = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Open' }).Count
    $naf  = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'NotAFinding' }).Count
    $nr   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Reviewed' }).Count
    $na   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Applicable' }).Count
    $applicable = $open + $naf + $nr
    $pctApp = if ($applicable -gt 0) { [math]::Round(($naf / $applicable) * 100, 1) } else { 0 }
    $totalOpen += $open; $totalNaf += $naf; $totalNr += $nr; $totalNa += $na

    $status = if ($open -eq 0 -and $nr -eq 0) { '[PASS]' }
              elseif ($open -eq 0) { '[WARN]' }
              else { '[FAIL]' }

    Write-Host "  $status $stigName"
    Write-Host "         Open=$open  NaF=$naf  NR=$nr  N/A=$na  Compliant=${pctApp}%"
}

$grandApplicable = $totalOpen + $totalNaf + $totalNr
$grandPct = if ($grandApplicable -gt 0) { [math]::Round(($totalNaf / $grandApplicable) * 100, 1) } else { 0 }

Write-Host ""
Write-Host "  ================================================"
Write-Host "  OVERALL COMPLIANCE (excl N/A)"
Write-Host "  $totalNaf / $grandApplicable ($grandPct%)"
Write-Host "  Open=$totalOpen  Not_Reviewed=$totalNr  N/A=$totalNa"
Write-Host "  ================================================"

# List remaining Open findings
if ($totalOpen -gt 0) {
    Write-Host ""
    Write-Host "  REMAINING OPEN FINDINGS:"
    foreach ($ckl in $cklFiles) {
        [xml]$xml = Get-Content $ckl.FullName
        $openVulns = $xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Open' }
        foreach ($v in $openVulns) {
            $vid = ($v.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Vuln_Num' }).ATTRIBUTE_DATA
            $sev = ($v.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Severity' }).ATTRIBUTE_DATA
            $title = ($v.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Rule_Title' }).ATTRIBUTE_DATA
            if ($title.Length -gt 80) { $title = $title.Substring(0,80) + '...' }
            Write-Host "    $vid ($sev): $title"
        }
    }
}
