param(
  [Parameter(Mandatory)] [string]$BundleRoot,
  [Parameter(Mandatory)] [string]$ModulesPath,
  [int]$MinFreeGb = 5,
  [string]$PowerStigModulePath = '',
  [switch]$CheckLgpoConflict,
  [string]$BundleManifestPath = ''
)

$ErrorActionPreference = "Stop"

function Write-Info {
  param([string]$Message)
  Write-Host "[PRECHECK] $Message"
}

function New-IssueList {
  return (New-Object 'System.Collections.Generic.List[string]')
}

function Add-Issue {
  param([System.Collections.Generic.List[string]]$List, [string]$Message)
  [void]$List.Add($Message)
}

function Test-IsAdmin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-PendingReboot {
  $pending = $false
  if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $pending = $true }
  if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $pending = $true }
  try {
    $p = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue
    if ($null -ne $p) { $pending = $true }
  } catch { }
  return $pending
}

function Test-PowerStigAvailable {
  param([System.Collections.Generic.List[string]]$IssueList, [string]$ModulePath)
  Write-Info "Checking PowerSTIG module availability"
  try {
    if ($ModulePath -and (Test-Path $ModulePath)) {
      Import-Module $ModulePath -ErrorAction Stop -Force
    } else {
      Import-Module PowerSTIG -ErrorAction Stop -Force
    }
  } catch {
    Add-Issue -List $IssueList -Message "PowerSTIG module not available: $($_.Exception.Message)"
  }
}

function Test-DscResources {
  param([System.Collections.Generic.List[string]]$IssueList, [string]$ModulePath)
  Write-Info "Checking required DSC resources"
  try {
    $manifestPath = $null
    if ($ModulePath -and (Test-Path $ModulePath)) {
      if (Test-Path (Join-Path $ModulePath 'PowerSTIG.psd1')) {
        $manifestPath = Join-Path $ModulePath 'PowerSTIG.psd1'
      }
    } else {
      $mod = Get-Module PowerSTIG -ListAvailable -ErrorAction SilentlyContinue | Select-Object -First 1
      if ($mod) { $manifestPath = $mod.Path }
    }

    if (-not $manifestPath -or -not (Test-Path $manifestPath)) {
      Add-Issue -List $IssueList -Message "Cannot locate PowerSTIG module manifest for DSC resource check"
      return
    }

    $manifest = Import-PowerShellDataFile $manifestPath -ErrorAction Stop
    $requiredModules = $manifest.RequiredModules
    if (-not $requiredModules) { return }

    foreach ($reqMod in $requiredModules) {
      $modName = if ($reqMod -is [string]) { $reqMod } else { $reqMod.ModuleName }
      $minVersion = if ($reqMod -is [hashtable] -and $reqMod.ModuleVersion) { $reqMod.ModuleVersion } else { $null }

      if (-not $modName) { continue }

      $installed = Get-Module $modName -ListAvailable -ErrorAction SilentlyContinue | Select-Object -First 1
      if (-not $installed) {
        Add-Issue -List $IssueList -Message "DSC resource module missing: $modName"
      } elseif ($minVersion -and ([version]$installed.Version -lt [version]$minVersion)) {
        Add-Issue -List $IssueList -Message "DSC resource module $modName version $($installed.Version) < required $minVersion"
      }
    }
  } catch {
    Add-Issue -List $IssueList -Message "DSC resource check failed: $($_.Exception.Message)"
  }
}

function Test-MutualExclusion {
  param([System.Collections.Generic.List[string]]$IssueList, [string]$ManifestPath)
  Write-Info "Checking DSC/LGPO mutual exclusion"
  try {
    if (-not (Test-Path $ManifestPath)) {
      Add-Issue -List $IssueList -Message "Bundle manifest not found for mutual-exclusion check: $ManifestPath"
      return
    }

    $manifestJson = Get-Content $ManifestPath -Raw | ConvertFrom-Json -ErrorAction Stop

    # Look for controls that have both DSC and LGPO remediation targets
    $controlsPath = Join-Path (Split-Path $ManifestPath -Parent) 'pack_controls.json'
    if (-not (Test-Path $controlsPath)) { return }

    $controls = Get-Content $controlsPath -Raw | ConvertFrom-Json -ErrorAction Stop
    $conflictIds = @()

    foreach ($ctrl in $controls) {
      $hasDsc = $false
      $hasLgpo = $false
      if ($ctrl.FixText) {
        if ($ctrl.FixText -match 'DSC|Desired State Configuration') { $hasDsc = $true }
        if ($ctrl.FixText -match 'LGPO|Local Group Policy') { $hasLgpo = $true }
      }
      if ($hasDsc -and $hasLgpo) {
        $conflictIds += $ctrl.ControlId
      }
    }

    if ($conflictIds.Count -gt 0) {
      $idList = $conflictIds -join ', '
      Add-Issue -List $IssueList -Message "Mutual exclusion conflict: controls $idList have both DSC and LGPO targets"
    }
  } catch {
    Add-Issue -List $IssueList -Message "Mutual exclusion check failed: $($_.Exception.Message)"
  }
}

# ===== Core Checks (existing) =====

$issues = New-IssueList

Write-Info "Checking admin rights"
if (-not (Test-IsAdmin)) { Add-Issue -List $issues -Message "Admin rights required" }

Write-Info "Checking OS version"
$os = Get-CimInstance Win32_OperatingSystem
if ($os -and $os.Caption -notmatch 'Windows') { Add-Issue -List $issues -Message "Unsupported OS: $($os.Caption)" }

Write-Info "Checking disk free space"
$drive = Get-PSDrive -Name (Split-Path $BundleRoot -Qualifier).TrimEnd(':') -ErrorAction SilentlyContinue
if ($drive -and $drive.Free -lt ($MinFreeGb * 1GB)) { Add-Issue -List $issues -Message "Low disk space (< ${MinFreeGb}GB)" }

Write-Info "Checking PowerShell host"
if ($PSVersionTable.PSVersion.Major -lt 5) { Add-Issue -List $issues -Message "PowerShell 5.1 required for DSC" }

Write-Info "Checking constrained language mode"
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { Add-Issue -List $issues -Message "Constrained language mode detected" }

Write-Info "Checking pending reboot"
if (Test-PendingReboot) { Add-Issue -List $issues -Message "Pending reboot detected" }

Write-Info "Checking module path"
if (-not (Test-Path $ModulesPath)) { Add-Issue -List $issues -Message "Modules path not found: $ModulesPath" }

Write-Info "Checking execution policy (process scope)"
try { Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force } catch { Add-Issue -List $issues -Message "ExecutionPolicy set failed" }

# ===== Extended Checks (new) =====

if ($PowerStigModulePath -ne '') {
  Test-PowerStigAvailable -IssueList $issues -ModulePath $PowerStigModulePath
  Test-DscResources -IssueList $issues -ModulePath $PowerStigModulePath
}

if ($CheckLgpoConflict -and $BundleManifestPath -ne '') {
  Test-MutualExclusion -IssueList $issues -ManifestPath $BundleManifestPath
}

# ===== Output =====

$exitCode = if ($issues.Count -eq 0) { 0 } else { 1 }

$result = [PSCustomObject]@{
  Ok = ($issues.Count -eq 0)
  Issues = $issues
  Timestamp = (Get-Date).ToString('o')
  ExitCode = $exitCode
}

$result | ConvertTo-Json -Depth 5

exit $exitCode
