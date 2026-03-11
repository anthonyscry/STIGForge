# STIGForge Lab - DC01 Full Hardening Pipeline with Per-Step Compliance Tracking
# Run on: DC01 as Administrator (lab\Install or lab\Administrator)
# Prerequisites: import/ folder contents copied to C:\temp\
#
# Priority order (DSC-first + DISA-first):
#   1. DSC (PowerSTIG) - bulk baseline settings
#   2. DISA ADMX/GPO import + LGPO - official DISA baselines
#   3. Certificates - DoD/ECA certs
#   4. Custom GPOs - fill gaps (registry, security policy, audit)
#   5. Script fallback remediation - non-GPO items (AD ops, DNS, LDAP, NTP, optional IE11)
#
# IE11 is handled by STIG-IE11 GPO (no separate script step needed).
# User rights, audit policy, account rename all in GPOs (easy on/off).
#
# NOTE: Evaluate-STIG cannot run inside WinRM sessions (detects wsmprovhost
# as concurrent process). This script must run locally or via scheduled task.

$ErrorActionPreference = 'Continue'
$scriptDir = 'C:\temp\scripts'
$resultsDir = 'C:\StigResults\pipeline'
$logFile = "$resultsDir\pipeline-log.txt"

if (-not (Test-Path $resultsDir)) {
    New-Item -Path $resultsDir -ItemType Directory -Force | Out-Null
}

# ============================================
# Find Evaluate-STIG
# ============================================
$esPath = $null
$esZip = 'C:\temp\Evaluate-STIG.zip'
$esSearchDirs = @('C:\temp\Evaluate-STIG', 'C:\Evaluate-STIG', 'C:\EvaluateSTIG')

foreach ($dir in $esSearchDirs) {
    $found = Get-ChildItem $dir -Filter 'Evaluate-STIG.ps1' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $esPath = $found.FullName; break }
}

if (-not $esPath -and (Test-Path $esZip)) {
    Write-Host "Extracting Evaluate-STIG..."
    Expand-Archive -Path $esZip -DestinationPath 'C:\temp\Evaluate-STIG' -Force
    $found = Get-ChildItem 'C:\temp\Evaluate-STIG' -Filter 'Evaluate-STIG.ps1' -Recurse | Select-Object -First 1
    if ($found) { $esPath = $found.FullName }
}

if (-not $esPath) {
    Write-Host "ERROR: Evaluate-STIG.ps1 not found"
    exit 1
}

$esDir = Split-Path $esPath -Parent
Write-Host "Evaluate-STIG: $esPath"

# ============================================
# Helper: Run Evaluate-STIG and parse CKL results
# ============================================
function Invoke-StigScan {
    param([string]$StepName)

    $stepDir = "$resultsDir\scan-$StepName"
    if (Test-Path $stepDir) { Remove-Item $stepDir -Recurse -Force }
    New-Item -Path $stepDir -ItemType Directory -Force | Out-Null

    Write-Host ""
    Write-Host "================================================================"
    Write-Host "  SCANNING: $StepName ($(Get-Date -Format 'HH:mm:ss'))"
    Write-Host "================================================================"

    # Run Evaluate-STIG in a SEPARATE process to avoid concurrency lock
    $scanCmd = "Set-Location '$esDir'; & '$esPath' -ScanType Unclassified -Output CKL -OutputPath '$stepDir'"
    $proc = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command `"$scanCmd`"" `
        -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Host "  SCAN WARNING: E-STIG exited with code $($proc.ExitCode)"
    }

    # Find CKL files (nested under hostname\Checklist\)
    $cklFiles = Get-ChildItem $stepDir -Filter '*.ckl' -Recurse -ErrorAction SilentlyContinue
    if (-not $cklFiles) {
        Write-Host "  WARNING: No CKL files generated for $StepName"
        return [PSCustomObject]@{
            Step = $StepName; TotalNaf = 0; TotalApp = 0; TotalPct = 0
            TotalOpen = 0; TotalNR = 0; Details = @()
        }
    }

    $results = @()
    $totalOpen = 0; $totalNaf = 0; $totalNr = 0; $totalNa = 0

    foreach ($ckl in $cklFiles) {
        [xml]$xml = Get-Content $ckl.FullName
        $stigName = $xml.CHECKLIST.STIGS.iSTIG.STIG_INFO.SI_DATA |
            Where-Object { $_.SID_NAME -eq 'title' } |
            Select-Object -ExpandProperty SID_DATA -ErrorAction SilentlyContinue

        $shortName = $stigName -replace 'Microsoft |Windows |Security Technical Implementation Guide', ''
        $shortName = $shortName.Trim()
        if ($shortName.Length -gt 40) { $shortName = $shortName.Substring(0,40) }

        $open = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Open' }).Count
        $naf  = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'NotAFinding' }).Count
        $nr   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Reviewed' }).Count
        $na   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Applicable' }).Count
        $applicable = $open + $naf + $nr
        $pct = if ($applicable -gt 0) { [math]::Round(($naf / $applicable) * 100, 1) } else { 0 }

        $totalOpen += $open; $totalNaf += $naf; $totalNr += $nr; $totalNa += $na

        $results += [PSCustomObject]@{
            STIG = $shortName; Open = $open; NaF = $naf; NR = $nr
            NA = $na; Applicable = $applicable; Pct = $pct
        }
    }

    $grandApplicable = $totalOpen + $totalNaf + $totalNr
    $grandPct = if ($grandApplicable -gt 0) { [math]::Round(($totalNaf / $grandApplicable) * 100, 1) } else { 0 }

    # Display results table
    Write-Host ""
    Write-Host "  STIG                                     Open   NaF    NR   N/A   %Compl"
    Write-Host "  ----                                     ----   ---    --   ---   ------"
    foreach ($r in $results | Sort-Object STIG) {
        Write-Host ("  {0,-40} {1,4}  {2,4}  {3,4}  {4,4}   {5,5}%" -f $r.STIG, $r.Open, $r.NaF, $r.NR, $r.NA, $r.Pct)
    }
    Write-Host "  ----                                     ----   ---    --   ---   ------"
    Write-Host ("  {0,-40} {1,4}  {2,4}  {3,4}  {4,4}   {5,5}%" -f 'TOTAL', $totalOpen, $totalNaf, $totalNr, $totalNa, $grandPct)
    Write-Host ""

    # Log
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    "[$ts] $StepName - Overall: $totalNaf/$grandApplicable ($grandPct%) Open=$totalOpen NR=$totalNr" | Add-Content $logFile
    foreach ($r in $results | Sort-Object STIG) {
        "  $($r.STIG): $($r.NaF)/$($r.Applicable) ($($r.Pct)%) Open=$($r.Open) NR=$($r.NR)" | Add-Content $logFile
    }
    "" | Add-Content $logFile

    return [PSCustomObject]@{
        Step = $StepName; TotalNaf = $totalNaf; TotalApp = $grandApplicable
        TotalPct = $grandPct; TotalOpen = $totalOpen; TotalNR = $totalNr; Details = $results
    }
}

# ============================================
# Pipeline Start
# ============================================
Write-Host "=== STIGForge DC01 Hardening Pipeline ==="
Write-Host "  Host: $env:COMPUTERNAME"
Write-Host "  Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Host "  Evaluate-STIG: $esPath"
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

# Helper: log step progress (visible to remote monitor)
function Log-Step { param([string]$Msg)
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    "[$ts] $Msg" | Add-Content $logFile
    Write-Host "[$ts] $Msg"
}

function Assert-NotWinRmHosted {
    $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction SilentlyContinue).ParentProcessId
    if ($parentId) {
        $parent = Get-Process -Id $parentId -ErrorAction SilentlyContinue
        if ($parent -and $parent.Name -ieq 'wsmprovhost') {
            Log-Step "ERROR: Pipeline is running inside WinRM host (wsmprovhost). Run locally or via scheduled task."
            exit 1
        }
    }
}

function Get-WinRmStartType {
    $svc = Get-CimInstance Win32_Service -Filter "Name='WinRM'" -ErrorAction SilentlyContinue
    if ($svc) { return $svc.StartMode }
    return $null
}

function Capture-WinRmState {
    $service = Get-Service -Name WinRM -ErrorAction SilentlyContinue
    if (-not $service) {
        Log-Step "WinRM service not found; cannot capture state"
        return $null
    }

    $startMode = Get-WinRmStartType
    return [PSCustomObject]@{
        Exists      = $true
        StartMode   = $startMode
        WasRunning  = ($service.Status -eq 'Running')
        WasDisabled = ($startMode -eq 'Disabled')
        Changed     = $false
    }
}

function Disable-WinRmTemporarily {
    param([object]$State)

    if (-not $State -or -not $State.Exists) {
        Log-Step "WinRM state unavailable; skipping temporary disable"
        return $null
    }

    $service = Get-Service -Name WinRM -ErrorAction SilentlyContinue
    if (-not $service) {
        Log-Step "WinRM service not found; skipping temporary disable"
        return $State
    }

    if (-not $State.WasRunning -and $State.WasDisabled) {
        Log-Step "WinRM already disabled and stopped"
        return $State
    }

    try {
        Disable-PSRemoting -Force -ErrorAction Stop
        if ($service.Status -eq 'Running') {
            Stop-Service -Name WinRM -Force -ErrorAction SilentlyContinue
        }
        Set-Service -Name WinRM -StartupType Disabled -ErrorAction SilentlyContinue
        $State.Changed = $true
        Log-Step "WinRM remoting endpoints disabled temporarily for local hardening stages"
    } catch {
        Log-Step "WARN: Disable-PSRemoting failed, using service fallback: $($_.Exception.Message)"
        try {
            if ($service.Status -eq 'Running') {
                Stop-Service -Name WinRM -Force -ErrorAction Stop
            }
            Set-Service -Name WinRM -StartupType Disabled -ErrorAction Stop
            $State.Changed = $true
            Log-Step "WinRM temporarily disabled with service fallback"
        } catch {
            Log-Step "WARN: Could not fully disable WinRM temporarily: $($_.Exception.Message)"
        }
    }

    return $State
}

function Restore-WinRmState {
    param([object]$State)

    if (-not $State -or -not $State.Exists) { return }
    if (-not $State.Changed) {
        Log-Step "WinRM restore skipped (no temporary change made)"
        return
    }

    try {
        switch ($State.StartMode) {
            'Auto'     { Set-Service -Name WinRM -StartupType Automatic -ErrorAction Stop }
            'Manual'   { Set-Service -Name WinRM -StartupType Manual -ErrorAction Stop }
            'Disabled' { Set-Service -Name WinRM -StartupType Disabled -ErrorAction Stop }
            default    { Set-Service -Name WinRM -StartupType Manual -ErrorAction Stop }
        }

        if ($State.StartMode -ne 'Disabled') {
            Enable-PSRemoting -Force -SkipNetworkProfileCheck -ErrorAction SilentlyContinue | Out-Null
            switch ($State.StartMode) {
                'Auto'    { Set-Service -Name WinRM -StartupType Automatic -ErrorAction Stop }
                'Manual'  { Set-Service -Name WinRM -StartupType Manual -ErrorAction Stop }
                default   { Set-Service -Name WinRM -StartupType Manual -ErrorAction Stop }
            }
        }

        if ($State.WasRunning) {
            Start-Service -Name WinRM -ErrorAction Stop
        }

        Log-Step "WinRM restored to original state (StartMode=$($State.StartMode), WasRunning=$($State.WasRunning))"
    } catch {
        Log-Step "WARN: Failed to restore WinRM state: $($_.Exception.Message)"
    }
}

# ============================================
# STEP 0: Baseline scan (before any hardening)
# ============================================
Assert-NotWinRmHosted
Log-Step "STEP 0: Starting baseline scan"
$allSteps += Invoke-StigScan -StepName '00-baseline'
Log-Step "STEP 0: Baseline scan complete"

# ============================================
# STEP 1: Install E-STIG + DSC modules
# ============================================
Log-Step "STEP 1: Starting module install"
try { & "$scriptDir\00-install-modules.ps1" } catch { Log-Step "STEP 1 ERROR: $($_.Exception.Message)" }
Log-Step "STEP 1: Module install complete"

# ============================================
# STEP 1a: Capture WinRM state
# ============================================
Log-Step "STEP 1a: Capturing WinRM state"
$winRmOriginal = Capture-WinRmState

try {
    # ============================================
    # STEP 2: DSC Hardening (PowerSTIG) — PRIORITY 1
    # Applies bulk baseline: WindowsServer, Firewall, Defender, DotNet
    # ============================================
    Log-Step "STEP 2: Starting DSC hardening"
    try { & "$scriptDir\07-dc01-dsc-hardening.ps1" } catch { Log-Step "STEP 2 ERROR: $($_.Exception.Message)" }
    Log-Step "STEP 2: DSC complete, running gpupdate"
    gpupdate /force 2>&1 | Out-Null
    Log-Step "STEP 2: gpupdate done, starting scan"
    $allSteps += Invoke-StigScan -StepName '02-after-dsc'
    Log-Step "STEP 2: Scan complete"

    # ============================================
    # STEP 2a: Temporarily disable WinRM (if enabled)
    # Keep WSMan available for DSC, then disable for subsequent local hardening stages
    # ============================================
    Log-Step "STEP 2a: Temporarily disabling WinRM for remaining stages"
    $winRmOriginal = Disable-WinRmTemporarily -State $winRmOriginal

    # ============================================
    # STEP 3: ADMX + LGPO + DISA GPO Import — PRIORITY 2
    # Official DISA STIG GPO baselines
    # ============================================
    Log-Step "STEP 3: Starting ADMX + LGPO"
    try { & "$scriptDir\07a-dc01-admx-lgpo.ps1" } catch { Log-Step "STEP 3 ERROR: $($_.Exception.Message)" }
    Log-Step "STEP 3: ADMX + LGPO complete, running gpupdate"
    gpupdate /force 2>&1 | Out-Null
    Log-Step "STEP 3: gpupdate done, starting scan"
    $allSteps += Invoke-StigScan -StepName '03-after-admx-lgpo'
    Log-Step "STEP 3: Scan complete"

    # ============================================
    # STEP 4: Certificate Installation — PRIORITY 3
    # ============================================
    Log-Step "STEP 4: Starting certificate install"
    try { & "$scriptDir\10-dc01-install-certs.ps1" } catch { Log-Step "STEP 4 ERROR: $($_.Exception.Message)" }
    Log-Step "STEP 4: Certs complete, starting scan"
    $allSteps += Invoke-StigScan -StepName '04-after-certs'
    Log-Step "STEP 4: Scan complete"

    # ============================================
    # STEP 5: Custom STIG GPOs — PRIORITY 4
    # Registry, security policy (user rights, account rename), audit policy
    # All GPO-based for easy on/off
    # ============================================
    Log-Step "STEP 5: Starting custom GPOs"
    try { & "$scriptDir\08-dc01-stig-gpos.ps1" } catch { Log-Step "STEP 5 ERROR: $($_.Exception.Message)" }
    Log-Step "STEP 5: GPOs complete, running gpupdate"
    gpupdate /force 2>&1 | Out-Null
    Log-Step "STEP 5: gpupdate done, starting scan"
    $allSteps += Invoke-StigScan -StepName '05-after-gpos'
    Log-Step "STEP 5: Scan complete"

    # ============================================
    # STEP 6: Script Fallback Remediation — PRIORITY 5
    # AD ops, DNS, LDAP, NTP, plus optional IE11 fallback if needed
    # ============================================
    Log-Step "STEP 6: Starting local-only fallback remediation"
    try { & "$scriptDir\09-dc01-local-hardening.ps1" } catch { Log-Step "STEP 6 ERROR: $($_.Exception.Message)" }

    if (Test-Path "$scriptDir\09a-dc01-ie11-hardening.ps1") {
        Log-Step "STEP 6: Running optional IE11 fallback remediation"
        try { & "$scriptDir\09a-dc01-ie11-hardening.ps1" } catch { Log-Step "STEP 6 IE11 WARN: $($_.Exception.Message)" }
    }

    Log-Step "STEP 6: Script fallback complete, running gpupdate"
    gpupdate /force 2>&1 | Out-Null
    Log-Step "STEP 6: gpupdate done, starting scan"
    $allSteps += Invoke-StigScan -StepName '06-after-script-fallback'
    Log-Step "STEP 6: Scan complete"
}
finally {
    # Always restore WinRM state before final summary.
    Log-Step "Finalizing: restoring WinRM state"
    Restore-WinRmState -State $winRmOriginal
}

# ============================================
# FINAL: Delta Summary Table
# ============================================
Write-Host ""
Write-Host "================================================================"
Write-Host "  COMPLIANCE DELTA SUMMARY"
Write-Host "================================================================"
Write-Host ""
Write-Host "  Step                      NaF/Applicable    %Compl   Open    NR     Delta"
Write-Host "  ----                      --------------    ------   ----    --     -----"

$prevPct = 0
foreach ($step in $allSteps) {
    $delta = if ($step.Step -eq '00-baseline') { '  ---' }
             else { $d = $step.TotalPct - $prevPct; '{0:+0.0;-0.0; 0.0}' -f $d }
    Write-Host ("  {0,-25} {1,4}/{2,-4}          {3,5}%  {4,4}  {5,4}   {6}" -f $step.Step, $step.TotalNaf, $step.TotalApp, $step.TotalPct, $step.TotalOpen, $step.TotalNR, $delta)
    $prevPct = $step.TotalPct
}

Write-Host ""
Write-Host "  PER-STIG FAMILY BREAKDOWN:"
Write-Host ""

$allStigNames = $allSteps | ForEach-Object { $_.Details } | Select-Object -ExpandProperty STIG -Unique | Sort-Object

foreach ($stigName in $allStigNames) {
    Write-Host "  $stigName"
    Write-Host "    Step                        Open   NaF/App     %"
    foreach ($step in $allSteps) {
        $detail = $step.Details | Where-Object { $_.STIG -eq $stigName }
        if ($detail) {
            Write-Host ("    {0,-27} {1,4}   {2,4}/{3,-4}  {4,5}%" -f $step.Step, $detail.Open, $detail.NaF, $detail.Applicable, $detail.Pct)
        }
    }
    Write-Host ""
}

# Save delta table to log
"" | Add-Content $logFile
"=== DELTA SUMMARY ===" | Add-Content $logFile
$prevPct = 0
foreach ($step in $allSteps) {
    $d = $step.TotalPct - $prevPct
    "$($step.Step): $($step.TotalNaf)/$($step.TotalApp) ($($step.TotalPct)%) Open=$($step.TotalOpen) NR=$($step.TotalNR) Delta=$([math]::Round($d,1))%" | Add-Content $logFile
    foreach ($r in $step.Details | Sort-Object STIG) {
        "  $($r.STIG): $($r.NaF)/$($r.Applicable) ($($r.Pct)%)" | Add-Content $logFile
    }
    $prevPct = $step.TotalPct
}

Write-Host "================================================================"
Write-Host "  Pipeline complete. Log: $logFile"
Write-Host "  CKL results: $resultsDir"
Write-Host "================================================================"

# Signal completion for remote monitoring
Set-Content "$resultsDir\PIPELINE-DONE.txt" "Completed: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
