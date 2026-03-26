# StigForge-GPO.psm1 — GPO helper functions for STIG hardening
# Import: Import-Module "$PSScriptRoot\..\lib\StigForge-GPO.psm1" -Force
# Requires: GroupPolicy, ActiveDirectory modules on DC

# ============================================
# Set-GPReg — Set a single registry value in a GPO
# ============================================
function Set-GPReg {
    param(
        [string]$GPOName,
        [string]$Key,
        [string]$ValueName,
        $Value,
        [string]$Type = 'DWord'
    )
    try {
        Set-GPRegistryValue -Name $GPOName -Key $Key -ValueName $ValueName -Value $Value -Type $Type -ErrorAction Stop | Out-Null
    } catch {
        Write-Host "  ERR: $GPOName/$Key/$ValueName - $($_.Exception.Message)"
    }
}

# ============================================
# Register-GPOExtension — Add a CSE GUID to a GPO's AD object
# ============================================
function Register-GPOExtension {
    param(
        [string]$GpoDN,
        [string]$CseGuid,
        [string]$CsePattern
    )

    $adObj = [ADSI]"LDAP://$GpoDN"
    $currentCSE = $adObj.Properties['gPCMachineExtensionNames'].Value
    if ($currentCSE -and $currentCSE -notmatch $CsePattern) {
        $adObj.Properties['gPCMachineExtensionNames'].Value = $currentCSE + $CseGuid
        $adObj.CommitChanges()
    } elseif (-not $currentCSE) {
        $adObj.Properties['gPCMachineExtensionNames'].Value = $CseGuid
        $adObj.CommitChanges()
    }

    # Bump GPO version to force replication
    $newVer = [int]$adObj.Properties['versionNumber'].Value + 1
    $adObj.Properties['versionNumber'].Value = $newVer
    $adObj.CommitChanges()

    return $newVer
}

# ============================================
# Set-GPOSecurityPolicy — Write GptTmpl.inf into a GPO's SYSVOL
# ============================================
function Set-GPOSecurityPolicy {
    param(
        [string]$GPOName,
        [string]$InfContent
    )

    $gpoObj = Get-GPO -Name $GPOName
    $gpoId = $gpoObj.Id.ToString('B').ToUpper()
    $dnsDomain = (Get-ADDomain).DNSRoot
    $sysvolBase = "\\$dnsDomain\SYSVOL\$dnsDomain\Policies\$gpoId\Machine\Microsoft\Windows NT\SecEdit"

    if (-not (Test-Path $sysvolBase)) {
        New-Item -Path $sysvolBase -ItemType Directory -Force | Out-Null
    }

    Set-Content "$sysvolBase\GptTmpl.inf" $InfContent -Encoding Unicode -Force

    $secCSE = '[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]'
    $domDN = (Get-ADDomain).DistinguishedName
    $gpoDN = "CN=$gpoId,CN=Policies,CN=System,$domDN"

    $newVer = Register-GPOExtension -GpoDN $gpoDN -CseGuid $secCSE -CsePattern '827D319E'
    Write-Host "  GptTmpl.inf written to $GPOName (v$newVer)"
}

# ============================================
# Set-GPOAuditPolicy — Write audit.csv into a GPO's SYSVOL
# ============================================
function Set-GPOAuditPolicy {
    param(
        [string]$GPOName,
        [string]$CsvContent
    )

    $gpoObj = Get-GPO -Name $GPOName
    $gpoId = $gpoObj.Id.ToString('B').ToUpper()
    $dnsDomain = (Get-ADDomain).DNSRoot
    $auditBase = "\\$dnsDomain\SYSVOL\$dnsDomain\Policies\$gpoId\Machine\Microsoft\Windows NT\Audit"

    if (-not (Test-Path $auditBase)) {
        New-Item -Path $auditBase -ItemType Directory -Force | Out-Null
    }

    Set-Content "$auditBase\audit.csv" $CsvContent -Encoding Unicode -Force

    $auditCSE = '[{F3BC9527-C350-480B-A84D-6A23D2597B2F}{D02B1F73-3407-48AE-BA88-E8213C6761F1}]'
    $domDN = (Get-ADDomain).DistinguishedName
    $gpoDN = "CN=$gpoId,CN=Policies,CN=System,$domDN"

    $newVer = Register-GPOExtension -GpoDN $gpoDN -CseGuid $auditCSE -CsePattern 'F3BC9527'
    Write-Host "  audit.csv written to $GPOName (v$newVer)"
}

# ============================================
# Ensure-GPOLink — Idempotently link and enable a GPO on a target OU
# ============================================
function Ensure-GPOLink {
    param(
        [string]$GpoName,
        [string]$Target
    )

    $inheritance = Get-GPInheritance -Target $Target -ErrorAction SilentlyContinue
    $existing = $inheritance.GpoLinks | Where-Object { $_.DisplayName -eq $GpoName } | Select-Object -First 1

    if (-not $existing) {
        Get-GPO -Name $GpoName -ErrorAction Stop | New-GPLink -Target $Target -LinkEnabled Yes -ErrorAction Stop | Out-Null
        Write-Host "  Linked $GpoName -> $Target"
        return
    }

    if (-not $existing.Enabled) {
        Set-GPLink -Name $GpoName -Target $Target -LinkEnabled Yes -ErrorAction Stop | Out-Null
        Write-Host "  Re-enabled $GpoName -> $Target"
        return
    }

    Write-Host "  $GpoName already linked and enabled on $Target"
}

# ============================================
# Ensure-GPOApplyPermission — Set security filtering on a GPO
# ============================================
function Ensure-GPOApplyPermission {
    param(
        [string]$GpoName,
        [string]$Principal
    )

    $perm = $null
    try {
        $perm = Get-GPPermission -Name $GpoName -TargetName $Principal -TargetType Group -ErrorAction Stop
    } catch {
        $perm = $null
    }

    if (-not $perm -or "$($perm.Permission)" -ne 'GpoApply') {
        Set-GPPermission -Name $GpoName -TargetName $Principal -TargetType Group -PermissionLevel GpoApply -Replace -ErrorAction Stop
        Write-Host "  Set security filtering: $Principal = GpoApply on $GpoName"
    }
}

# ============================================
# New-StigGPO — Idempotent GPO creation
# ============================================
function New-StigGPO {
    param([string]$Name)
    $g = Get-GPO -Name $Name -ErrorAction SilentlyContinue
    if (-not $g) { $g = New-GPO -Name $Name }
    Write-Host "GPO: $($g.DisplayName)"
    return $g
}

Export-ModuleMember -Function @(
    'Set-GPReg', 'Register-GPOExtension',
    'Set-GPOSecurityPolicy', 'Set-GPOAuditPolicy',
    'Ensure-GPOLink', 'Ensure-GPOApplyPermission', 'New-StigGPO'
)
