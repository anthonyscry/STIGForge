# StigForge-Common.psm1 — Shared helpers for all lab-hardening pipelines
# Import: Import-Module "$PSScriptRoot\..\lib\StigForge-Common.psm1" -Force

# ============================================
# Logging
# ============================================
function Write-PipelineLog {
    param(
        [string]$Message,
        [string]$LogFile
    )
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$ts] $Message"
    Write-Host $line
    if ($LogFile) { $line | Add-Content $LogFile }
}

# ============================================
# Directory helpers
# ============================================
function Confirm-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
    }
}

function Expand-ArchiveClean {
    param(
        [string]$ZipPath,
        [string]$DestinationPath
    )
    if (Test-Path $DestinationPath) { Remove-Item $DestinationPath -Recurse -Force }
    Expand-Archive -Path $ZipPath -DestinationPath $DestinationPath -Force
}

# ============================================
# Evaluate-STIG discovery
# ============================================
function Find-EvaluateStig {
    param(
        [string]$ZipPath = 'C:\temp\Evaluate-STIG.zip',
        [string[]]$SearchDirs = @('C:\temp\Evaluate-STIG', 'C:\Evaluate-STIG', 'C:\EvaluateSTIG', 'C:\STIGForge-Test\Evaluate-STIG')
    )

    foreach ($dir in $SearchDirs) {
        $found = Get-ChildItem $dir -Filter 'Evaluate-STIG.ps1' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    if (Test-Path $ZipPath) {
        Write-Host "Extracting Evaluate-STIG..."
        Expand-Archive -Path $ZipPath -DestinationPath 'C:\temp\Evaluate-STIG' -Force
        $found = Get-ChildItem 'C:\temp\Evaluate-STIG' -Filter 'Evaluate-STIG.ps1' -Recurse | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    return $null
}

# ============================================
# CKL parsing
# ============================================
function Read-CklFile {
    param([string]$CklPath)

    [xml]$xml = Get-Content $CklPath
    $stigName = $xml.CHECKLIST.STIGS.iSTIG.STIG_INFO.SI_DATA |
        Where-Object { $_.SID_NAME -eq 'title' } |
        Select-Object -ExpandProperty SID_DATA -ErrorAction SilentlyContinue

    $shortName = ($stigName -replace 'Microsoft |Windows |Security Technical Implementation Guide', '').Trim()
    if ($shortName.Length -gt 40) { $shortName = $shortName.Substring(0, 40) }

    $open = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Open' }).Count
    $naf  = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'NotAFinding' }).Count
    $nr   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Reviewed' }).Count
    $na   = @($xml.CHECKLIST.STIGS.iSTIG.VULN | Where-Object { $_.STATUS -eq 'Not_Applicable' }).Count
    $applicable = $open + $naf + $nr
    $pct = if ($applicable -gt 0) { [math]::Round(($naf / $applicable) * 100, 1) } else { 0 }

    return [PSCustomObject]@{
        STIG       = $shortName
        FullName   = $stigName
        Open       = $open
        NaF        = $naf
        NR         = $nr
        NA         = $na
        Applicable = $applicable
        Pct        = $pct
    }
}

# ============================================
# STIG scan (runs Evaluate-STIG, parses all CKLs)
# ============================================
function Invoke-StigScan {
    param(
        [Parameter(Mandatory)][string]$StepName,
        [Parameter(Mandatory)][string]$EvaluateStigPath,
        [Parameter(Mandatory)][string]$ResultsDir,
        [string]$LogFile,
        [switch]$Detailed
    )

    $esDir = Split-Path $EvaluateStigPath -Parent
    $stepDir = "$ResultsDir\scan-$StepName"
    if (Test-Path $stepDir) { Remove-Item $stepDir -Recurse -Force }
    New-Item -Path $stepDir -ItemType Directory -Force | Out-Null

    Write-Host ""
    Write-Host "================================================================"
    Write-Host "  SCANNING: $StepName ($(Get-Date -Format 'HH:mm:ss'))"
    Write-Host "================================================================"

    $scanStart = Get-Date

    # Run in separate process to avoid concurrency lock
    $scanCmd = "Set-Location '$esDir'; & '$EvaluateStigPath' -ScanType Unclassified -Output CKL -OutputPath '$stepDir'"
    $proc = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command `"$scanCmd`"" `
        -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Host "  SCAN WARNING: E-STIG exited with code $($proc.ExitCode)"
    }

    $scanDuration = (Get-Date) - $scanStart

    # Parse CKL files
    $cklFiles = Get-ChildItem $stepDir -Filter '*.ckl' -Recurse -ErrorAction SilentlyContinue
    if (-not $cklFiles) {
        Write-Host "  WARNING: No CKL files generated for $StepName"
        return [PSCustomObject]@{
            Step = $StepName; TotalNaf = 0; TotalApp = 0; TotalPct = 0
            TotalOpen = 0; TotalNR = 0; TotalNA = 0
            TotalChecks = 0; ClosedTotal = 0; ClosedPct = 0
            Details = @(); ScanSeconds = [math]::Round($scanDuration.TotalSeconds, 1)
        }
    }

    $details = @()
    $totalOpen = 0; $totalNaf = 0; $totalNr = 0; $totalNa = 0

    foreach ($ckl in $cklFiles) {
        $parsed = Read-CklFile -CklPath $ckl.FullName
        $totalOpen += $parsed.Open; $totalNaf += $parsed.NaF
        $totalNr += $parsed.NR; $totalNa += $parsed.NA
        $details += $parsed
    }

    $grandApplicable = $totalOpen + $totalNaf + $totalNr
    $grandPct = if ($grandApplicable -gt 0) { [math]::Round(($totalNaf / $grandApplicable) * 100, 1) } else { 0 }
    $totalChecks = $grandApplicable + $totalNa
    $closedTotal = $totalNaf + $totalNa
    $closedPct = if ($totalChecks -gt 0) { [math]::Round(($closedTotal / $totalChecks) * 100, 1) } else { 0 }

    # Display results
    if ($Detailed) {
        Write-Host ""
        Write-Host "  STIG                                     Open   NaF    NR   N/A   %Compl"
        Write-Host "  ----                                     ----   ---    --   ---   ------"
        foreach ($r in $details | Sort-Object STIG) {
            Write-Host ("  {0,-40} {1,4}  {2,4}  {3,4}  {4,4}   {5,5}%" -f $r.STIG, $r.Open, $r.NaF, $r.NR, $r.NA, $r.Pct)
        }
        Write-Host "  ----                                     ----   ---    --   ---   ------"
        Write-Host ("  {0,-40} {1,4}  {2,4}  {3,4}  {4,4}   {5,5}%" -f 'TOTAL', $totalOpen, $totalNaf, $totalNr, $totalNa, $grandPct)
        Write-Host ""
    } else {
        Write-Host "  Result: $totalNaf/$grandApplicable ($grandPct%) | Open=$totalOpen NR=$totalNr NA=$totalNa"
    }

    # Log
    if ($LogFile) {
        $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        "[$ts] $StepName - $totalNaf/$grandApplicable ($grandPct%) Open=$totalOpen NR=$totalNr NA=$totalNa Closed=$closedTotal/$totalChecks ($closedPct%) [$([math]::Round($scanDuration.TotalSeconds,1))s]" | Add-Content $LogFile
        if ($Detailed) {
            foreach ($r in $details | Sort-Object STIG) {
                "  $($r.STIG): $($r.NaF)/$($r.Applicable) ($($r.Pct)%) Open=$($r.Open) NR=$($r.NR)" | Add-Content $LogFile
            }
            "" | Add-Content $LogFile
        }
    }

    return [PSCustomObject]@{
        Step = $StepName; TotalNaf = $totalNaf; TotalApp = $grandApplicable
        TotalPct = $grandPct; TotalOpen = $totalOpen; TotalNR = $totalNr; TotalNA = $totalNa
        TotalChecks = $totalChecks; ClosedTotal = $closedTotal; ClosedPct = $closedPct
        Details = $details; ScanSeconds = [math]::Round($scanDuration.TotalSeconds, 1)
    }
}

# ============================================
# Delta summary table
# ============================================
function Write-DeltaSummary {
    param(
        [array]$Steps,
        [string]$LogFile
    )

    Write-Host ""
    Write-Host "================================================================"
    Write-Host "  COMPLIANCE DELTA SUMMARY"
    Write-Host "================================================================"
    Write-Host ""
    Write-Host "  Step                      NaF/Applicable    %Compl   NaF+NA/Total    %Closed  Open   NR   NA   Delta"
    Write-Host "  ----                      --------------    ------   ------------     -------  ----   --   --   -----"

    $prevPct = 0
    foreach ($step in $Steps) {
        $delta = if ($step.Step -eq '00-baseline') { '  ---' }
                 else { $d = $step.TotalPct - $prevPct; '{0:+0.0;-0.0; 0.0}' -f $d }
        Write-Host ("  {0,-25} {1,4}/{2,-4}          {3,5}%   {4,4}/{5,-4}      {6,5}%  {7,4}  {8,4} {9,4}   {10}" -f `
            $step.Step, $step.TotalNaf, $step.TotalApp, $step.TotalPct,
            $step.ClosedTotal, $step.TotalChecks, $step.ClosedPct,
            $step.TotalOpen, $step.TotalNR, $step.TotalNA, $delta)
        $prevPct = $step.TotalPct
    }

    if ($LogFile) {
        "" | Add-Content $LogFile
        "=== DELTA SUMMARY ===" | Add-Content $LogFile
        $prevPct = 0
        foreach ($step in $Steps) {
            $deltaLog = if ($step.Step -eq '00-baseline') { '---' }
                        else { "$([math]::Round($step.TotalPct - $prevPct, 1))%" }
            "$($step.Step): $($step.TotalNaf)/$($step.TotalApp) ($($step.TotalPct)%) Open=$($step.TotalOpen) NR=$($step.TotalNR) NA=$($step.TotalNA) Closed=$($step.ClosedTotal)/$($step.TotalChecks) ($($step.ClosedPct)%) Delta=$deltaLog" | Add-Content $LogFile
            $prevPct = $step.TotalPct
        }
    }
}

# ============================================
# Per-STIG family breakdown
# ============================================
function Write-StigBreakdown {
    param([array]$Steps)

    Write-Host ""
    Write-Host "  PER-STIG FAMILY BREAKDOWN:"
    Write-Host ""

    $allStigNames = $Steps | ForEach-Object { $_.Details } | Select-Object -ExpandProperty STIG -Unique | Sort-Object

    foreach ($stigName in $allStigNames) {
        Write-Host "  $stigName"
        Write-Host "    Step                        Open   NaF/App     %"
        foreach ($step in $Steps) {
            $detail = $step.Details | Where-Object { $_.STIG -eq $stigName }
            if ($detail) {
                Write-Host ("    {0,-27} {1,4}   {2,4}/{3,-4}  {4,5}%" -f $step.Step, $detail.Open, $detail.NaF, $detail.Applicable, $detail.Pct)
            }
        }
        Write-Host ""
    }
}

# ============================================
# Server role detection
# ============================================
function Get-ServerRole {
    $productType = (Get-CimInstance Win32_OperatingSystem).ProductType
    switch ($productType) {
        1 { return [PSCustomObject]@{ Type = 'WS';  Name = 'Workstation';        IsDC = $false; ProductType = 1 } }
        2 { return [PSCustomObject]@{ Type = 'DC';  Name = 'Domain Controller';  IsDC = $true;  ProductType = 2 } }
        3 { return [PSCustomObject]@{ Type = 'MS';  Name = 'Member Server';      IsDC = $false; ProductType = 3 } }
        default { return [PSCustomObject]@{ Type = 'Unknown'; Name = "Unknown ($productType)"; IsDC = $false; ProductType = $productType } }
    }
}

# ============================================
# Step runner with timing
# ============================================
function Invoke-PipelineStep {
    param(
        [string]$StepName,
        [string]$Description,
        [scriptblock]$Action,
        [string]$LogFile
    )

    Write-PipelineLog -Message "STEP $StepName`: $Description" -LogFile $LogFile
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
    } catch {
        Write-PipelineLog -Message "STEP $StepName ERROR: $($_.Exception.Message)" -LogFile $LogFile
    }
    $sw.Stop()
    $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    Write-PipelineLog -Message "STEP $StepName complete (${elapsed}s)" -LogFile $LogFile
    return $elapsed
}

# ============================================
# SCC (SCAP Compliance Checker) discovery and execution
# ============================================
function Find-SCC {
    param(
        [string]$TempDir = 'C:\temp',
        [string[]]$SearchDirs = @('C:\temp\SCC', 'C:\temp\scc_5.14', 'C:\SCC')
    )

    # Check if already extracted
    foreach ($dir in $SearchDirs) {
        $found = Get-ChildItem $dir -Filter 'cscc.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    # SCC 5.14 ships as a nested bundle:
    #   scc-5.14_Windows_bundle.zip
    #     └── scc-5.14_Windows/scc-5.14_Windows.zip   (portable standalone)
    #           └── scc_5.14/cscc.exe
    #
    # We extract the inner portable zip — no installer needed.

    $sccDest = "$TempDir\SCC"

    # Look for the bundle zip (scc-*_bundle.zip) or the inner portable zip (scc-*_Windows.zip)
    $bundleZip = Get-ChildItem $TempDir -Filter 'scc-*_bundle.zip' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $bundleZip) {
        # Also match SCC_* pattern
        $bundleZip = Get-ChildItem $TempDir -Filter 'SCC_*_bundle.zip' -ErrorAction SilentlyContinue | Select-Object -First 1
    }

    if ($bundleZip) {
        Write-Host "  Extracting SCC bundle: $($bundleZip.Name)..."

        # Step 1: Extract the outer bundle to get the inner portable zip
        $outerDir = "$TempDir\scc_bundle_tmp"
        if (Test-Path $outerDir) { Remove-Item $outerDir -Recurse -Force }
        Expand-Archive -Path $bundleZip.FullName -DestinationPath $outerDir -Force

        # Step 2: Find the inner portable zip (not the Setup.exe)
        $innerZip = Get-ChildItem $outerDir -Filter 'scc-*_Windows.zip' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $innerZip) {
            $innerZip = Get-ChildItem $outerDir -Filter 'SCC_*_Windows.zip' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        }

        if ($innerZip) {
            Write-Host "  Extracting portable SCC: $($innerZip.Name)..."
            if (Test-Path $sccDest) { Remove-Item $sccDest -Recurse -Force }
            Expand-Archive -Path $innerZip.FullName -DestinationPath $sccDest -Force
        }

        # Clean up outer extraction
        Remove-Item $outerDir -Recurse -Force -ErrorAction SilentlyContinue

        $found = Get-ChildItem $sccDest -Filter 'cscc.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    # Fallback: look for a direct portable zip (already the inner zip)
    $portableZip = Get-ChildItem $TempDir -Filter 'scc-*_Windows.zip' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch 'bundle' } | Select-Object -First 1
    if (-not $portableZip) {
        $portableZip = Get-ChildItem $TempDir -Filter 'SCC_*_Windows.zip' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notmatch 'bundle' } | Select-Object -First 1
    }

    if ($portableZip) {
        Write-Host "  Extracting portable SCC: $($portableZip.Name)..."
        if (Test-Path $sccDest) { Remove-Item $sccDest -Recurse -Force }
        Expand-Archive -Path $portableZip.FullName -DestinationPath $sccDest -Force

        $found = Get-ChildItem $sccDest -Filter 'cscc.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    return $null
}

function Invoke-SCCScan {
    param(
        [Parameter(Mandatory)][string]$SCCPath,
        [string]$ResultsDir = 'C:\SCC_Results',
        [string]$LogFile
    )

    Confirm-Directory $ResultsDir
    $sccDir = Split-Path $SCCPath -Parent
    $scanStart = Get-Date

    Write-Host ""
    Write-Host "================================================================"
    Write-Host "  SCC SCAN: $(Get-Date -Format 'HH:mm:ss')"
    Write-Host "================================================================"

    # Do NOT modify options.xml — SCC validates it strictly against XSD schema.
    # SCC writes results to the user profile (or SYSTEM profile if run as SYSTEM).
    # We'll find the results after the scan completes.

    # Run cscc.exe (command-line SCC) — headless local scan
    $proc = Start-Process -FilePath $SCCPath `
        -Wait -NoNewWindow -PassThru -WorkingDirectory $sccDir
    if ($proc.ExitCode -ne 0) {
        Write-Host "  SCC exited with code $($proc.ExitCode)"
    }

    $scanDuration = (Get-Date) - $scanStart
    $elapsed = [math]::Round($scanDuration.TotalSeconds, 1)

    # Find XCCDF results — SCC writes to user/SYSTEM profile, not a configurable path
    $searchPaths = @(
        $ResultsDir,
        "$env:USERPROFILE\SCC",
        'C:\Windows\system32\config\systemprofile\SCC',
        "$sccDir\Results"
    )
    $xccdfFiles = @()
    $sccResultsPath = $null
    foreach ($sp in $searchPaths) {
        $found = Get-ChildItem $sp -Filter '*XCCDF-Results*.xml' -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -gt $scanStart }
        if ($found) {
            $xccdfFiles = $found
            $sccResultsPath = $sp
            break
        }
    }
    # Copy results to our results dir for easy collection
    if ($xccdfFiles -and $sccResultsPath -ne $ResultsDir) {
        $xccdfFiles | ForEach-Object { Copy-Item $_.FullName -Destination $ResultsDir -Force -ErrorAction SilentlyContinue }
        Write-Host "  Copied $($xccdfFiles.Count) XCCDF results to $ResultsDir"
    }
    $summaryFiles = if ($sccResultsPath) { Get-ChildItem $sccResultsPath -Filter '*Summary*.html' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $scanStart } } else { @() }
    $allResults = if ($sccResultsPath) { Get-ChildItem $sccResultsPath -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $scanStart } } else { @() }

    Write-Host "  SCC scan complete (${elapsed}s)"
    Write-Host "  XCCDF result files: $($xccdfFiles.Count)"
    Write-Host "  Summary files: $($summaryFiles.Count)"
    Write-Host "  Total output files: $($allResults.Count)"
    Write-Host "  Results: $ResultsDir"

    # Parse XCCDF results if available
    $sccResults = @()
    foreach ($xccdf in $xccdfFiles) {
        try {
            [xml]$xml = Get-Content $xccdf.FullName
            $ns = @{ x = 'http://checklists.nist.gov/xccdf/1.2' }
            $benchmarkTitle = $xml.Benchmark.title
            if (-not $benchmarkTitle) {
                $benchmarkTitle = $xml.SelectSingleNode('//x:Benchmark/x:title', (New-Object Xml.XmlNamespaceManager($xml.NameTable)))
                if ($benchmarkTitle) { $benchmarkTitle = $benchmarkTitle.InnerText }
            }

            $ruleResults = $xml.SelectNodes('//x:rule-result', (New-Object Xml.XmlNamespaceManager($xml.NameTable)))
            if (-not $ruleResults -or $ruleResults.Count -eq 0) {
                # Try without namespace
                $ruleResults = $xml.SelectNodes('//rule-result')
            }

            $pass = 0; $fail = 0; $other = 0
            foreach ($rr in $ruleResults) {
                $result = $rr.result
                if (-not $result) {
                    $node = $rr.SelectSingleNode('result')
                    if ($node) { $result = $node.InnerText }
                }
                switch ($result) {
                    'pass'          { $pass++ }
                    'fail'          { $fail++ }
                    'notapplicable' { $other++ }
                    'notchecked'    { $other++ }
                    default         { $other++ }
                }
            }

            $total = $pass + $fail
            $pct = if ($total -gt 0) { [math]::Round(($pass / $total) * 100, 1) } else { 0 }

            $sccResults += [PSCustomObject]@{
                Benchmark = $benchmarkTitle
                Pass      = $pass
                Fail      = $fail
                Other     = $other
                Total     = $total
                Pct       = $pct
                File      = $xccdf.Name
            }

            Write-Host "  $benchmarkTitle`: $pass/$total ($pct%) Pass=$pass Fail=$fail"
        } catch {
            Write-Host "  WARN: Could not parse $($xccdf.Name): $($_.Exception.Message)"
        }
    }

    if ($LogFile) {
        $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        "[$ts] SCC scan complete (${elapsed}s) - $($xccdfFiles.Count) XCCDF results" | Add-Content $LogFile
        foreach ($r in $sccResults) {
            "  $($r.Benchmark): $($r.Pass)/$($r.Total) ($($r.Pct)%) Fail=$($r.Fail)" | Add-Content $LogFile
        }
    }

    return [PSCustomObject]@{
        ScanSeconds = $elapsed
        XccdfCount  = $xccdfFiles.Count
        Results     = $sccResults
        ResultsDir  = $ResultsDir
    }
}

Export-ModuleMember -Function @(
    'Write-PipelineLog', 'Confirm-Directory', 'Expand-ArchiveClean',
    'Find-EvaluateStig', 'Read-CklFile', 'Invoke-StigScan',
    'Write-DeltaSummary', 'Write-StigBreakdown', 'Get-ServerRole',
    'Invoke-PipelineStep', 'Find-SCC', 'Invoke-SCCScan'
)
