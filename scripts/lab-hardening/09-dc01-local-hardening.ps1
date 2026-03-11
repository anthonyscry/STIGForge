# STIGForge Lab - Step 9: Server Local-Only Fixes (Non-GPO)
# Run on: Any Windows Server (DC or Member Server) as Administrator
# Auto-detects: DC vs MS role
#
# This script handles ONLY settings that CANNOT be managed via GPO:
#   - AD operations: Protected Users, delegation audit, Schema Admins check
#   - DNS hardening: recursion, RRL, connection limits
#   - LDAP idle timeout: AD object manipulation
#   - NTP configuration: w32tm service
#
# All other settings (user rights, audit policy, account rename, registry)
# are now in GPOs (08-dc01-stig-gpos.ps1) for easy enable/disable.

# Suppress all confirmation prompts (for non-interactive scheduled task execution)
$ConfirmPreference = 'None'

$productType = (Get-CimInstance Win32_OperatingSystem).ProductType
$isDC = ($productType -eq 2)
$roleName = if ($isDC) { 'Domain Controller' } else { 'Member Server' }

Write-Host "=== Local-Only Fixes: $roleName ($env:COMPUTERNAME) ==="
Write-Host ""

$fixed = 0

# ============================================
# DNS Hardening (if DNS role installed)
# Cannot be GPO: requires DNS cmdlets / dnscmd
# ============================================
Write-Host "--- DNS Hardening ---"

$dnsInstalled = (Get-WindowsFeature -Name DNS -ErrorAction SilentlyContinue).Installed
if ($dnsInstalled) {
    # V-259341: Disable recursion on authoritative server
    try {
        Set-DnsServerRecursion -Enable $false -Confirm:$false -ErrorAction Stop
        Write-Host "  Recursion disabled"
        $fixed++
    } catch {
        Write-Host "  WARN: Could not disable recursion: $($_.Exception.Message)"
    }

    # V-259417: Enable Response Rate Limiting
    try {
        Set-DnsServerResponseRateLimiting -Mode Enable -Force -Confirm:$false -ErrorAction Stop
        Write-Host "  Response Rate Limiting enabled"
        $fixed++
    } catch {
        Write-Host "  WARN: RRL not available: $($_.Exception.Message)"
    }

    # V-259395: Limit connections per IP
    try {
        dnscmd /config /maxconnectionsperip 10 2>&1 | Out-Null
        Write-Host "  MaxConnectionsPerIP = 10"
        $fixed++
    } catch {
        Write-Host "  WARN: Could not set MaxConnectionsPerIP"
    }
} else {
    Write-Host "  DNS role not installed - skipping"
}

# ============================================
# AD Checks & Fixes (DC only)
# Cannot be GPO: requires AD PowerShell module operations
# ============================================
if ($isDC) {
Write-Host ""
Write-Host "--- AD Checks (DC only) ---"

# V-243477: Admin accounts in Protected Users group
$domainAdmins = Get-ADGroupMember 'Domain Admins' -ErrorAction SilentlyContinue
$protectedUsers = Get-ADGroupMember 'Protected Users' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty SamAccountName
$protectedCount = 0
foreach ($admin in $domainAdmins) {
    if ($admin.SamAccountName -notin $protectedUsers) {
        try {
            Add-ADGroupMember -Identity 'Protected Users' -Members $admin.SamAccountName -ErrorAction Stop
            Write-Host "  Added $($admin.SamAccountName) to Protected Users"
            $protectedCount++
        } catch {
            Write-Host "  WARN: Could not add $($admin.SamAccountName) to Protected Users: $($_.Exception.Message)"
        }
    }
}
if ($protectedCount -eq 0) { Write-Host "  All Domain Admins already in Protected Users" }
$fixed += $protectedCount

# V-243470: Check for delegated accounts
$delegated = Get-ADUser -Filter { TrustedForDelegation -eq $true -and ObjectClass -eq 'user' } -ErrorAction SilentlyContinue
if ($delegated) {
    Write-Host "  WARNING: Users trusted for delegation:"
    foreach ($u in $delegated) { Write-Host "    - $($u.SamAccountName)" }
} else {
    Write-Host "  No user accounts trusted for delegation (good)"
}

# V-243487: Schema Admins / Group Policy Creator Owners membership
foreach ($grp in @('Schema Admins','Group Policy Creator Owners')) {
    $members = Get-ADGroupMember $grp -ErrorAction SilentlyContinue
    $count = ($members | Measure-Object).Count
    Write-Host "  $grp members: $count"
    if ($count -gt 0) {
        foreach ($m in $members) { Write-Host "    - $($m.SamAccountName)" }
    }
}

# V-205658: Check for non-expiring passwords
$nonExpiring = Get-ADUser -Filter { PasswordNeverExpires -eq $true -and Enabled -eq $true } -ErrorAction SilentlyContinue
if ($nonExpiring) {
    Write-Host "  WARNING: Users with non-expiring passwords:"
    foreach ($u in $nonExpiring) { Write-Host "    - $($u.SamAccountName)" }
} else {
    Write-Host "  All enabled users have expiring passwords (good)"
}

} # end if ($isDC) for AD Checks

# ============================================
# LDAP Idle Timeout (V-205726) - DC only
# Cannot be GPO: requires AD object manipulation on Directory Service config
# ============================================
if ($isDC) {
Write-Host ""
Write-Host "--- LDAP Idle Timeout ---"

try {
    $configDN = (Get-ADRootDSE).configurationNamingContext
    $ldapPolicy = Get-ADObject -SearchBase "CN=Default Query Policy,CN=Query-Policies,CN=Directory Service,CN=Windows NT,CN=Services,$configDN" -Filter * -Properties lDAPAdminLimits -ErrorAction Stop
    $currentLimits = $ldapPolicy.lDAPAdminLimits
    $hasIdleTime = $false
    $newLimits = @()
    foreach ($limit in $currentLimits) {
        if ($limit -match '^MaxConnIdleTime=') {
            $newLimits += 'MaxConnIdleTime=300'
            $hasIdleTime = $true
        } else {
            $newLimits += $limit
        }
    }
    if (-not $hasIdleTime) { $newLimits += 'MaxConnIdleTime=300' }
    Set-ADObject $ldapPolicy -Replace @{ lDAPAdminLimits = $newLimits } -ErrorAction Stop
    Write-Host "  MaxConnIdleTime = 300 seconds"
    $fixed++
} catch {
    Write-Host "  WARN: Could not set LDAP idle timeout: $($_.Exception.Message)"
}

} # end if ($isDC) for LDAP

# ============================================
# NTP Configuration (V-243504 / V-205800) - DC only (PDC emulator)
# Cannot be GPO: w32tm service configuration via command-line
# ============================================
if ($isDC) {
Write-Host ""
Write-Host "--- NTP Configuration ---"

$ntpPeers = 'pool.ntp.org,0x9'
w32tm /config /manualpeerlist:"$ntpPeers" /syncfromflags:manual /reliable:yes /update 2>&1 | Out-Null
Restart-Service w32time -Force -ErrorAction SilentlyContinue
# /nowait prevents hanging on isolated networks that can't reach NTP peer
w32tm /resync /rediscover /nowait 2>&1 | Out-Null
Write-Host "  NTP peer: pool.ntp.org (lab environment)"
Write-Host "  Production: replace with DoD-approved time source"
$fixed++
} # end if ($isDC) for NTP

# ============================================
# Summary
# ============================================
Write-Host ""
Write-Host "========================================="
Write-Host "  Total local-only fixes applied: $fixed"
Write-Host "========================================="
Write-Host ""
Write-Host "  Settings handled by GPO (disable/enable via GPO link):"
Write-Host "    - User rights, account rename, audit policy -> STIG-Server2019 GPO"
Write-Host "    - Registry hardening -> STIG-Server2019, STIG-Defender, STIG-Firewall GPOs"
Write-Host "    - IE11 registry -> STIG-IE11 GPO"
Write-Host ""
Write-Host "  REMAINING (not automated):"
Write-Host "    - DoD/ECA certs: run 10-dc01-install-certs.ps1"
Write-Host "    - Secure Boot (V-205857): enable in Hyper-V VM settings"
Write-Host "    - TPM (V-205848): enable vTPM in Hyper-V VM settings"
Write-Host "    - VBS (V-205864): requires Gen2 VM + Secure Boot"
Write-Host "    - Data partition (V-205723): requires disk layout change"
Write-Host "    - LAPS (V-243471): see README TODO section"
Write-Host "    - Smart card/MFA: hardware-dependent"
Write-Host ""
Write-Host "  Next: gpupdate /force, then reboot"
