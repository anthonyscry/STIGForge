$ErrorActionPreference = 'Continue'
$scriptDir = 'C:\temp\scripts'
$resultsDir = 'C:\StigResults\pipeline-ws'
$logFile = "$resultsDir\pipeline-log.txt"

if (-not (Test-Path $resultsDir)) {
    New-Item -Path $resultsDir -ItemType Directory -Force | Out-Null
}

$esPath = $null
$esZip = 'C:\temp\Evaluate-STIG.zip'
$esSearchDirs = @('C:\temp\Evaluate-STIG', 'C:\Evaluate-STIG', 'C:\EvaluateSTIG', 'C:\STIGForge-Test\Evaluate-STIG')

foreach ($dir in $esSearchDirs) {
    $found = Get-ChildItem $dir -Filter 'Evaluate-STIG.ps1' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $esPath = $found.FullName; break }
}

if (-not $esPath -and (Test-Path $esZip)) {
    Expand-Archive -Path $esZip -DestinationPath 'C:\temp\Evaluate-STIG' -Force
    $found = Get-ChildItem 'C:\temp\Evaluate-STIG' -Filter 'Evaluate-STIG.ps1' -Recurse | Select-Object -First 1
    if ($found) { $esPath = $found.FullName }
}

if (-not $esPath) {
    Write-Host "NOTE: Evaluate-STIG.ps1 not yet found - will be extracted by module install step"
    $esDir = $null
} else {
    $esDir = Split-Path $esPath -Parent
}

function Invoke-StigScan {
    param([string]$StepName)

    $stepDir = "$resultsDir\scan-$StepName"
    if (Test-Path $stepDir) { Remove-Item $stepDir -Recurse -Force }
    New-Item -Path $stepDir -ItemType Directory -Force | Out-Null

    $scanCmd = "Set-Location '$esDir'; & '$esPath' -ScanType Unclassified -Output CKL -OutputPath '$stepDir'"
    $proc = Start-Process -FilePath 'powershell.exe' -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command `"$scanCmd`"" -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Host "SCAN WARNING: E-STIG exited with code $($proc.ExitCode)"
    }

    $cklFiles = Get-ChildItem $stepDir -Filter '*.ckl' -Recurse -ErrorAction SilentlyContinue
    if (-not $cklFiles) {
        return [PSCustomObject]@{ Step = $StepName; TotalNaf = 0; TotalApp = 0; TotalPct = 0; TotalOpen = 0; TotalNR = 0; TotalNA = 0; TotalChecks = 0; ClosedTotal = 0; ClosedPct = 0; Details = @() }
    }

    $totalOpen = 0; $totalNaf = 0; $totalNr = 0; $totalNa = 0
    foreach ($ckl in $cklFiles) {
        [xml]$xml = Get-Content $ckl.FullName
        $open = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Open' }).Count
        $naf  = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'NotAFinding' }).Count
        $nr   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Reviewed' }).Count
        $na   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Applicable' }).Count
        $totalOpen += $open; $totalNaf += $naf; $totalNr += $nr; $totalNa += $na
    }

    $grandApplicable = $totalOpen + $totalNaf + $totalNr
    $grandPct = if ($grandApplicable -gt 0) { [math]::Round(($totalNaf / $grandApplicable) * 100, 1) } else { 0 }
    $totalChecks = $grandApplicable + $totalNa
    $closedTotal = $totalNaf + $totalNa
    $closedPct = if ($totalChecks -gt 0) { [math]::Round(($closedTotal / $totalChecks) * 100, 1) } else { 0 }
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $StepName - Overall: $totalNaf/$grandApplicable ($grandPct%) Open=$totalOpen NR=$totalNr NA=$totalNa Closed=$closedTotal/$totalChecks ($closedPct%)" | Add-Content $logFile
    return [PSCustomObject]@{ Step = $StepName; TotalNaf = $totalNaf; TotalApp = $grandApplicable; TotalPct = $grandPct; TotalOpen = $totalOpen; TotalNR = $totalNr; TotalNA = $totalNa; TotalChecks = $totalChecks; ClosedTotal = $closedTotal; ClosedPct = $closedPct; Details = @() }
}

function Log-Step { param([string]$Msg)
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    "[$ts] $Msg" | Add-Content $logFile
    Write-Host "[$ts] $Msg"
}

if (-not (Test-Path $scriptDir)) {
    Write-Host "ERROR: Scripts not found at $scriptDir"
    exit 1
}

"=== Pipeline Start: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Set-Content $logFile
"Host: $env:COMPUTERNAME" | Add-Content $logFile
"" | Add-Content $logFile

$allSteps = @()

Log-Step "STEP 0: Install modules (E-STIG + PowerSTIG + DSC)"
try { & "$scriptDir\00-install-modules.ps1" } catch { Log-Step "STEP 0 ERROR: $($_.Exception.Message)" }

# Re-resolve Evaluate-STIG path after install (00-install-modules extracts to C:\Evaluate-STIG)
$esPath = $null
foreach ($dir in $esSearchDirs) {
    $found = Get-ChildItem $dir -Filter 'Evaluate-STIG.ps1' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $esPath = $found.FullName; break }
}
if ($esPath) {
    $esDir = Split-Path $esPath -Parent
    Log-Step "Evaluate-STIG resolved to: $esPath"
} else {
    Log-Step "ERROR: Evaluate-STIG still not found after module install"
    exit 1
}

Log-Step "STEP 1: Baseline scan"
$allSteps += Invoke-StigScan -StepName '00-baseline'

Log-Step "STEP 2: DSC hardening"
try { & "$scriptDir\03-ws01-dsc-hardening.ps1" } catch { Log-Step "STEP 2 ERROR: $($_.Exception.Message)" }
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '02-after-dsc'

Log-Step "STEP 3: DISA/domain policy import stage"
Log-Step "STEP 3: Applying domain policy baseline refresh on workstation"
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '03-after-policy-refresh'

Log-Step "STEP 4: Certificate install"
try { & "$scriptDir\05-ws01-install-certs.ps1" } catch { Log-Step "STEP 4 ERROR: $($_.Exception.Message)" }
$allSteps += Invoke-StigScan -StepName '04-after-certs'

Log-Step "STEP 5: Custom GPO remediation stage"
Log-Step "STEP 5: Applying workstation custom GPO links via gpupdate"
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '05-after-custom-gpo'

Log-Step "STEP 6: Script fallback remediation"
try { & "$scriptDir\04-ws01-local-hardening.ps1" } catch { Log-Step "STEP 6 ERROR: $($_.Exception.Message)" }
gpupdate /force 2>&1 | Out-Null
$allSteps += Invoke-StigScan -StepName '06-after-script-fallback'

Write-Host ""
Write-Host "Step                      NaF/Applicable    %Compl   NaF+NA/Total    %Closed  Open   NR   NA   Delta"
Write-Host "----                      --------------    ------   ------------     -------  ----   --   --   -----"
$prevPct = 0
foreach ($step in $allSteps) {
    $delta = if ($step.Step -eq '00-baseline') { '  ---' } else { $d = $step.TotalPct - $prevPct; '{0:+0.0;-0.0; 0.0}' -f $d }
    Write-Host ("{0,-25} {1,4}/{2,-4}          {3,5}%   {4,4}/{5,-4}      {6,5}%  {7,4}  {8,4} {9,4}   {10}" -f $step.Step, $step.TotalNaf, $step.TotalApp, $step.TotalPct, $step.ClosedTotal, $step.TotalChecks, $step.ClosedPct, $step.TotalOpen, $step.TotalNR, $step.TotalNA, $delta)
    $prevPct = $step.TotalPct
}

Set-Content "$resultsDir\PIPELINE-DONE.txt" "Completed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
