param(
  [Parameter(Mandatory)] [string]$BundleRoot,
  [Parameter(Mandatory)] [string]$ModulesPath,
  [int]$MinFreeGb = 5
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

$result = [PSCustomObject]@{
  Ok = ($issues.Count -eq 0)
  Issues = $issues
  Timestamp = (Get-Date).ToString('o')
}

$result
