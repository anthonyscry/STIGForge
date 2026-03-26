# StigForge-WinRM.psm1 — WinRM state management for pipeline execution
# Import: Import-Module "$PSScriptRoot\..\lib\StigForge-WinRM.psm1" -Force
#
# Evaluate-STIG cannot run inside WinRM sessions (detects wsmprovhost).
# These functions capture, disable, and restore WinRM state safely.

function Assert-NotWinRmHosted {
    param([string]$LogFile)

    $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction SilentlyContinue).ParentProcessId
    if ($parentId) {
        $parent = Get-Process -Id $parentId -ErrorAction SilentlyContinue
        if ($parent -and $parent.Name -ieq 'wsmprovhost') {
            $msg = "ERROR: Pipeline is running inside WinRM host (wsmprovhost). Run locally or via scheduled task."
            if ($LogFile) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $msg" | Add-Content $LogFile }
            Write-Host $msg
            exit 1
        }
    }
}

function Get-WinRmStartType {
    $svc = Get-CimInstance Win32_Service -Filter "Name='WinRM'" -ErrorAction SilentlyContinue
    if ($svc) { return $svc.StartMode }
    return $null
}

function Save-WinRmState {
    param([string]$LogFile)

    $service = Get-Service -Name WinRM -ErrorAction SilentlyContinue
    if (-not $service) {
        if ($LogFile) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') WinRM service not found" | Add-Content $LogFile }
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
    param(
        [object]$State,
        [string]$LogFile
    )

    if (-not $State -or -not $State.Exists) {
        Write-Host "WinRM state unavailable; skipping temporary disable"
        return $State
    }

    $service = Get-Service -Name WinRM -ErrorAction SilentlyContinue
    if (-not $service) { return $State }

    if (-not $State.WasRunning -and $State.WasDisabled) {
        Write-Host "WinRM already disabled and stopped"
        return $State
    }

    try {
        Disable-PSRemoting -Force -ErrorAction Stop
        if ($service.Status -eq 'Running') {
            Stop-Service -Name WinRM -Force -ErrorAction SilentlyContinue
        }
        Set-Service -Name WinRM -StartupType Disabled -ErrorAction SilentlyContinue
        $State.Changed = $true
        Write-Host "WinRM remoting endpoints disabled temporarily"
    } catch {
        Write-Host "WARN: Disable-PSRemoting failed, using service fallback: $($_.Exception.Message)"
        try {
            if ($service.Status -eq 'Running') {
                Stop-Service -Name WinRM -Force -ErrorAction Stop
            }
            Set-Service -Name WinRM -StartupType Disabled -ErrorAction Stop
            $State.Changed = $true
        } catch {
            Write-Host "WARN: Could not fully disable WinRM: $($_.Exception.Message)"
        }
    }

    return $State
}

function Restore-WinRmState {
    param(
        [object]$State,
        [string]$LogFile
    )

    if (-not $State -or -not $State.Exists) { return }
    if (-not $State.Changed) {
        Write-Host "WinRM restore skipped (no temporary change made)"
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

        Write-Host "WinRM restored (StartMode=$($State.StartMode), WasRunning=$($State.WasRunning))"
    } catch {
        Write-Host "WARN: Failed to restore WinRM state: $($_.Exception.Message)"
    }
}

Export-ModuleMember -Function @(
    'Assert-NotWinRmHosted', 'Get-WinRmStartType',
    'Save-WinRmState', 'Disable-WinRmTemporarily', 'Restore-WinRmState'
)
