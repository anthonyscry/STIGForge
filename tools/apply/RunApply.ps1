param(
  [Parameter(Mandatory)] [string]$BundleRoot,
  [string]$ModulesPath = "",
  [string]$PreflightScript = "",
  [string]$DscMofPath = "",
  [switch]$SkipPreflight,
  [switch]$SkipLcm,
  [switch]$AutoReboot,
  [switch]$VerboseDsc
)

$ErrorActionPreference = "Stop"

function Write-Log {
  param([string]$Message)
  $line = "[APPLY] " + (Get-Date).ToString('o') + " " + $Message
  Write-Host $line
  if ($script:LogPath) { Add-Content -Path $script:LogPath -Value $line }
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

function Set-LcmApplyOnly {
  param([string]$OutPath)
  Configuration SetStigForgeLcm {
    Node localhost {
      Settings {
        ConfigurationMode = 'ApplyOnly'
        RebootNodeIfNeeded = $true
        ActionAfterReboot = 'ContinueConfiguration'
        RefreshMode = 'Push'
      }
    }
  }

  SetStigForgeLcm -OutputPath $OutPath | Out-Null
  Set-DscLocalConfigurationManager -Path $OutPath -Force
}

if (-not (Test-Path $BundleRoot)) { throw "BundleRoot not found" }

$applyRoot = Join-Path $BundleRoot "Apply"
$logsDir = Join-Path $applyRoot "Logs"
$snapDir = Join-Path $applyRoot "Snapshots"
if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir | Out-Null }
if (-not (Test-Path $snapDir)) { New-Item -ItemType Directory -Path $snapDir | Out-Null }
$script:LogPath = Join-Path $logsDir "runapply.log"

if ([string]::IsNullOrWhiteSpace($ModulesPath)) { $ModulesPath = Join-Path $applyRoot "Modules" }
if ([string]::IsNullOrWhiteSpace($PreflightScript)) { $PreflightScript = Join-Path $applyRoot "Preflight\Preflight.ps1" }
if ([string]::IsNullOrWhiteSpace($DscMofPath)) { $DscMofPath = Join-Path $applyRoot "Dsc" }

Write-Log "Starting apply"

if (-not $SkipPreflight) {
  if (-not (Test-Path $PreflightScript)) { throw "Preflight script not found" }
  Write-Log "Running preflight"
  $pre = & $PreflightScript -BundleRoot $BundleRoot -ModulesPath $ModulesPath
  if (-not $pre.Ok) {
    Write-Log "Preflight failed"
    $pre.Issues | ForEach-Object { Write-Log ("Issue: " + $_) }
    exit 2
  }
}

if (Test-Path $ModulesPath) {
  Write-Log "Staging modules: $ModulesPath"
  $env:PSModulePath = $ModulesPath + ";" + $env:PSModulePath
}

if (-not $SkipLcm) {
  Write-Log "Recording LCM state"
  try {
    $lcm = Get-DscLocalConfigurationManager
    $lcm | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $snapDir "lcm_before.json")
  } catch { }

  Write-Log "Setting LCM ApplyOnly"
  $lcmOut = Join-Path $snapDir "lcm"
  if (-not (Test-Path $lcmOut)) { New-Item -ItemType Directory -Path $lcmOut | Out-Null }
  Set-LcmApplyOnly -OutPath $lcmOut
}

if (Test-Path $DscMofPath) {
  Write-Log "Applying DSC configuration"
  $v = $false
  if ($VerboseDsc) { $v = $true }
  Start-DscConfiguration -Path $DscMofPath -Wait -Force -Verbose:$v
}

if (Test-PendingReboot) {
  Write-Log "Pending reboot detected"
  $flag = Join-Path $applyRoot "reboot_required.txt"
  "Pending reboot detected" | Set-Content -Path $flag
  if ($AutoReboot) {
    Write-Log "Rebooting in 15 seconds"
    shutdown /r /t 15 /c "STIGForge apply requested reboot"
    exit 3010
  }
}

Write-Log "Apply complete"
