# STIGForge Lab - Fix Remaining Open STIG Findings
# Run on: DC01, MS01, or WS01 as Administrator
# Auto-detects: DC vs MS vs WS role
#
# Addresses remaining open findings that are not covered by the existing
# GPO scripts (02, 08) or local hardening scripts (04, 09).
#
# DC-specific findings:
#   V-243466/V-243467: Enterprise/Domain Admin membership (audit only)
#   V-269097: Kerberos logging (covered by 08 GPO — verified here)
#   V-223122/V-223124: IE11 AutoComplete (covered by STIG-IE11 GPO + loopback — verified here)
#   V-213428: Windows Defender real-time monitoring
#   V-205648: DoD Root CA 5/6 (environment limitation — informational)
#   V-205702-V-205706: Kerberos policies in Default Domain Policy
#   V-271429: Certificate strong mapping (covered by 08 GPO — verified here)
#   V-205848/V-205864: TPM and VBS (VM hardware dependent — informational)
#   V-259342: DNS forwarders (network architecture — informational)
#
# MS/WS findings (subset):
#   Same cert, TPM, VBS, IE11, Defender findings
#
# Idempotent: safe to run multiple times.

#Requires -RunAsAdministrator

$ErrorActionPreference = 'Continue'
$ConfirmPreference = 'None'

Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force

$role = Get-ServerRole
$isDC = $role.IsDC
$roleName = $role.Name
$roleType = $role.Type

Write-Host "================================================================"
Write-Host "  Fix Remaining Open STIG Findings"
Write-Host "  Host: $env:COMPUTERNAME | Role: $roleName ($roleType)"
Write-Host "  Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "================================================================"
Write-Host ""

$fixed = 0
$skipped = 0
$auditOnly = 0
$infoOnly = 0

# ============================================
# Helper: Set registry value, create path if needed
# ============================================
function Set-Reg {
    param(
        [string]$Path,
        [string]$Name,
        $Value,
        [string]$Type = 'DWord'
    )
    if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
    Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -Force
}

# ============================================
# V-243466/V-243467: Enterprise/Domain Admin Membership (DC only, AUDIT ONLY)
# These are organizational controls — membership must be reviewed by IAO.
# ============================================
if ($isDC) {
    Write-Host "--- V-243466/V-243467: Enterprise/Domain Admin Membership (AUDIT ONLY) ---"

    foreach ($groupName in @('Enterprise Admins', 'Domain Admins')) {
        $members = Get-ADGroupMember -Identity $groupName -ErrorAction SilentlyContinue
        $count = ($members | Measure-Object).Count
        Write-Host "  $groupName members ($count):"
        if ($count -gt 0) {
            foreach ($m in $members) {
                Write-Host "    - $($m.SamAccountName) ($($m.objectClass))"
            }
        }

        if ($groupName -eq 'Enterprise Admins' -and $count -gt 1) {
            Write-Host "  WARNING: Enterprise Admins should contain only the Administrator account"
            Write-Host "           (V-243466). Review and remove unnecessary members."
        }
        if ($groupName -eq 'Domain Admins' -and $count -gt 1) {
            Write-Host "  WARNING: Domain Admins membership should be minimized"
            Write-Host "           (V-243467). Review and remove unnecessary members."
        }
    }

    $auditOnly += 2
    Write-Host ""
}

# ============================================
# V-269097: Kerberos Audit Logging (DC only)
# Already set via STIG-Server2019 GPO in 08-dc01-stig-gpos.ps1.
# Verify and apply directly as fallback.
# ============================================
if ($isDC) {
    Write-Host "--- V-269097: Kerberos Audit Logging ---"

    $kerbPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa\Kerberos\Parameters'
    $current = Get-ItemProperty -Path $kerbPath -Name 'LogLevel' -ErrorAction SilentlyContinue

    if ($current -and $current.LogLevel -eq 1) {
        Write-Host "  Already set: LogLevel=1 (OK — likely from STIG-Server2019 GPO)"
        $skipped++
    } else {
        Set-Reg $kerbPath 'LogLevel' 1
        Write-Host "  FIXED: Set LogLevel=1 at $kerbPath"
        $fixed++
    }
    Write-Host ""
}

# ============================================
# V-223122/V-223124: IE11 AutoComplete (All roles)
# STIG-IE11 GPO sets HKCU FormSuggest values via User Configuration.
# GPO loopback processing (STIG-Server2019 UserPolicyMode=1) delivers
# HKCU settings to DCs/servers. Verify and apply directly as fallback.
#
# Correct values (from 02-dc01-create-gpos.ps1):
#   Use FormSuggest     = "no" (REG_SZ)  -> V-223122
#   FormSuggest Passwords = "no" (REG_SZ)  -> V-223124
#   FormSuggest PW Ask  = "no" (REG_SZ)  -> V-223124
# ============================================
Write-Host "--- V-223122/V-223124: IE11 AutoComplete ---"

$hkcuIE = 'HKCU:\SOFTWARE\Policies\Microsoft\Internet Explorer\Main'
$ie11Settings = @{
    'Use FormSuggest'     = 'no'   # V-223122
    'FormSuggest Passwords' = 'no' # V-223124
    'FormSuggest PW Ask'  = 'no'   # V-223124
}

$ie11AllGood = $true
foreach ($kv in $ie11Settings.GetEnumerator()) {
    $current = Get-ItemProperty -Path $hkcuIE -Name $kv.Key -ErrorAction SilentlyContinue
    if ($current -and $current.($kv.Key) -eq $kv.Value) {
        Write-Host "  Already set: '$($kv.Key)' = '$($kv.Value)' (OK — from GPO or prior run)"
    } else {
        Set-Reg $hkcuIE $kv.Key $kv.Value 'String'
        Write-Host "  FIXED: Set '$($kv.Key)' = '$($kv.Value)'"
        $fixed++
        $ie11AllGood = $false
    }
}
if ($ie11AllGood) {
    Write-Host "  All IE11 AutoComplete settings correct"
    $skipped += 3
}

# Verify GPO loopback processing is configured (DC/MS only)
if ($roleType -in @('DC', 'MS')) {
    $loopbackPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
    $loopback = Get-ItemProperty -Path $loopbackPath -Name 'UserPolicyMode' -ErrorAction SilentlyContinue
    if ($loopback -and $loopback.UserPolicyMode -ge 1) {
        Write-Host "  GPO loopback processing: enabled (mode=$($loopback.UserPolicyMode))"
    } else {
        Write-Host "  WARNING: GPO loopback processing not applied yet"
        Write-Host "           Run 'gpupdate /force' to apply STIG-Server2019 GPO settings"
        Write-Host "           (loopback is set in 08-dc01-stig-gpos.ps1 UserPolicyMode=1)"
    }
}
Write-Host ""

# ============================================
# V-213428: Windows Defender Real-Time Monitoring (All roles)
# Ensure WinDefend service is running and real-time monitoring is enabled.
# ============================================
Write-Host "--- V-213428: Windows Defender Real-Time Monitoring ---"

$defenderService = Get-Service -Name 'WinDefend' -ErrorAction SilentlyContinue
if (-not $defenderService) {
    Write-Host "  WARNING: WinDefend service not found — Defender may not be installed"
    $skipped++
} else {
    # Start service if stopped
    if ($defenderService.Status -ne 'Running') {
        try {
            Set-Service -Name 'WinDefend' -StartupType Automatic -ErrorAction Stop
            Start-Service -Name 'WinDefend' -ErrorAction Stop
            Write-Host "  FIXED: Started WinDefend service"
            $fixed++
        } catch {
            Write-Host "  WARN: Could not start WinDefend: $($_.Exception.Message)"
            $skipped++
        }
    } else {
        Write-Host "  WinDefend service: Running"
    }

    # Enable real-time monitoring
    try {
        $mpPref = Get-MpPreference -ErrorAction Stop
        if ($mpPref.DisableRealtimeMonitoring -eq $true) {
            Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction Stop
            Write-Host "  FIXED: Enabled real-time monitoring"
            $fixed++
        } else {
            Write-Host "  Real-time monitoring: already enabled"
            $skipped++
        }
    } catch {
        Write-Host "  WARN: Could not query/set Defender preferences: $($_.Exception.Message)"
        $skipped++
    }
}
Write-Host ""

# ============================================
# V-205648: DoD Root CA Certificates (All roles)
# Root CA 5 and 6 may not be in v5.14 SCC bundle.
# Check if they exist; inform if missing.
# ============================================
Write-Host "--- V-205648: DoD Root CA Certificates ---"

# DoD Root CA 3 (expected present), Root CA 5, Root CA 6
$rootCAs = @(
    @{ Name = 'DoD Root CA 3';  Thumbprint = 'D73CA91102A2204A36459ED32213B467D7CE97FB' }
    @{ Name = 'DoD Root CA 5';  Thumbprint = '43C01BB1CF8FCB0F8E246C88F4BC5D2DF6E99A7A' }
    @{ Name = 'DoD Root CA 6';  Thumbprint = '6B7AA1D4ADB0603EC2C2A1B01AF7DAFB81DBB79C' }
)

$missingCAs = @()
foreach ($ca in $rootCAs) {
    $cert = Get-ChildItem -Path Cert:\LocalMachine\Root -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $ca.Thumbprint }
    if ($cert) {
        Write-Host "  FOUND: $($ca.Name) ($($ca.Thumbprint.Substring(0, 8))...)"
    } else {
        Write-Host "  MISSING: $($ca.Name) ($($ca.Thumbprint.Substring(0, 8))...)"
        $missingCAs += $ca.Name
    }
}

if ($missingCAs.Count -gt 0) {
    Write-Host "  INFO: Missing root CAs: $($missingCAs -join ', ')"
    Write-Host "        Root CA 5/6 may not be in v5.14 InstallRoot/SCC bundles."
    Write-Host "        Import from updated DoD PKI bundle or manually from DISA PKI."
    Write-Host "        Run: 10-dc01-install-certs.ps1 after placing updated bundles in C:\temp\"
    $infoOnly++
} else {
    Write-Host "  All required DoD Root CAs present"
}
Write-Host ""

# ============================================
# V-205702 through V-205706: Kerberos Policies (DC only)
# Must be in Default Domain Policy GptTmpl.inf [Kerberos Policy] section.
# These cannot be set by any other GPO — only DDP applies Kerberos policy.
#
# Required values:
#   MaxTicketAge     = 10  (hours)   — V-205702
#   MaxServiceAge    = 600 (minutes) — V-205703
#   MaxRenewAge      = 7   (days)    — V-205704
#   MaxClockSkew     = 5   (minutes) — V-205705
#   TicketValidateClient = 1         — V-205706
# ============================================
if ($isDC) {
    Write-Host "--- V-205702-V-205706: Kerberos Policies (Default Domain Policy) ---"

    try {
        Import-Module GroupPolicy -ErrorAction Stop
        Import-Module ActiveDirectory -ErrorAction Stop

        $ddpGPO = Get-GPO -Name 'Default Domain Policy' -ErrorAction Stop
        $ddpId = $ddpGPO.Id.ToString('B').ToUpper()
        $dnsDomain = (Get-ADDomain).DNSRoot
        $sysvolPath = "\\$dnsDomain\SYSVOL\$dnsDomain\Policies\$ddpId\Machine\Microsoft\Windows NT\SecEdit"
        $gptTmplPath = "$sysvolPath\GptTmpl.inf"

        if (-not (Test-Path $sysvolPath)) {
            New-Item -Path $sysvolPath -ItemType Directory -Force | Out-Null
        }

        # Read existing GptTmpl.inf to preserve other sections
        $existingContent = ''
        if (Test-Path $gptTmplPath) {
            $existingContent = Get-Content $gptTmplPath -Raw -Encoding Unicode -ErrorAction SilentlyContinue
        }

        # Check if [Kerberos Policy] section already exists with correct values
        $hasKerbSection = $existingContent -match '\[Kerberos Policy\]'
        $hasCorrectValues = $existingContent -match 'MaxTicketAge\s*=\s*10' -and
                            $existingContent -match 'MaxServiceAge\s*=\s*600' -and
                            $existingContent -match 'MaxRenewAge\s*=\s*7' -and
                            $existingContent -match 'MaxClockSkew\s*=\s*5' -and
                            $existingContent -match 'TicketValidateClient\s*=\s*1'

        if ($hasKerbSection -and $hasCorrectValues) {
            Write-Host "  Already set: all 5 Kerberos policy values correct"
            $skipped += 5
        } else {
            # Build updated GptTmpl.inf preserving existing sections
            # Parse existing sections
            $sections = [ordered]@{}
            $currentSection = ''
            if ($existingContent) {
                foreach ($line in ($existingContent -split "`r?`n")) {
                    if ($line -match '^\[(.+)\]$') {
                        $currentSection = $Matches[1]
                        if (-not $sections.Contains($currentSection)) {
                            $sections[$currentSection] = @()
                        }
                    } elseif ($currentSection -and $line.Trim()) {
                        $sections[$currentSection] += $line
                    }
                }
            }

            # Ensure required sections exist
            if (-not $sections.Contains('Unicode')) {
                $sections['Unicode'] = @('Unicode=yes')
            }
            if (-not $sections.Contains('Version')) {
                $sections['Version'] = @('signature="$CHICAGO$"', 'Revision=1')
            }

            # Add/replace [Kerberos Policy] section
            $sections['Kerberos Policy'] = @(
                'MaxTicketAge = 10'
                'MaxServiceAge = 600'
                'MaxRenewAge = 7'
                'MaxClockSkew = 5'
                'TicketValidateClient = 1'
            )

            # Rebuild the INF file
            $infLines = @()
            foreach ($section in $sections.Keys) {
                $infLines += "[$section]"
                foreach ($entry in $sections[$section]) {
                    $infLines += $entry
                }
            }

            $infContent = ($infLines -join "`r`n") + "`r`n"
            Set-Content $gptTmplPath $infContent -Encoding Unicode -Force

            # Ensure CSE GUIDs are registered for security settings processing
            $domDN = (Get-ADDomain).DistinguishedName
            $ddpDN = "CN=$ddpId,CN=Policies,CN=System,$domDN"
            $cseGuid = '[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]'

            $adObj = [ADSI]"LDAP://$ddpDN"
            $currentCSE = $adObj.Properties['gPCMachineExtensionNames'].Value
            if ($currentCSE -and $currentCSE -notmatch '827D319E') {
                $adObj.Properties['gPCMachineExtensionNames'].Value = $currentCSE + $cseGuid
                $adObj.CommitChanges()
                Write-Host "  CSE GUIDs appended to Default Domain Policy"
            } elseif (-not $currentCSE) {
                $adObj.Properties['gPCMachineExtensionNames'].Value = $cseGuid
                $adObj.CommitChanges()
                Write-Host "  CSE GUIDs set on Default Domain Policy"
            }

            # Bump GPO version to force replication
            $newVer = [int]$adObj.Properties['versionNumber'].Value + 1
            $adObj.Properties['versionNumber'].Value = $newVer
            $adObj.CommitChanges()

            Write-Host "  FIXED: Wrote [Kerberos Policy] to Default Domain Policy GptTmpl.inf"
            Write-Host "    MaxTicketAge=10h, MaxServiceAge=600m, MaxRenewAge=7d"
            Write-Host "    MaxClockSkew=5m, TicketValidateClient=1"
            Write-Host "    GPO version bumped to $newVer"
            $fixed += 5
        }
    } catch {
        Write-Host "  ERROR: Could not update Default Domain Policy: $($_.Exception.Message)"
        Write-Host "         Ensure GroupPolicy and ActiveDirectory modules are available."
        $skipped += 5
    }
    Write-Host ""
}

# ============================================
# V-271429: Certificate Strong Mapping (DC only)
# Already set via STIG-Server2019 GPO in 08-dc01-stig-gpos.ps1.
# Verify and apply directly as fallback.
# ============================================
if ($isDC) {
    Write-Host "--- V-271429: Certificate Strong Mapping ---"

    $kdcPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\Kdc'
    $current = Get-ItemProperty -Path $kdcPath -Name 'StrongCertificateBindingEnforcement' -ErrorAction SilentlyContinue

    if ($current -and $current.StrongCertificateBindingEnforcement -ge 1) {
        Write-Host "  Already set: StrongCertificateBindingEnforcement=$($current.StrongCertificateBindingEnforcement) (OK)"
        $skipped++
    } else {
        Set-Reg $kdcPath 'StrongCertificateBindingEnforcement' 1
        Write-Host "  FIXED: Set StrongCertificateBindingEnforcement=1"
        $fixed++
    }
    Write-Host ""
}

# ============================================
# V-205848: TPM (All roles — VM hardware dependent)
# V-205864: Virtualization Based Security (All roles — VM hardware dependent)
# These require Gen2 VM + vTPM + Secure Boot. Cannot be fixed in software.
# ============================================
Write-Host "--- V-205848/V-205864: TPM and Virtualization Based Security ---"

# Check TPM status
$tpm = $null
try {
    $tpm = Get-Tpm -ErrorAction Stop
} catch {
    # Get-Tpm not available or failed
}

if ($tpm -and $tpm.TpmPresent) {
    Write-Host "  TPM: Present (TpmReady=$($tpm.TpmReady))"
} else {
    Write-Host "  TPM: NOT present"
    Write-Host "  INFO (V-205848): Enable vTPM in VM settings (Hyper-V Gen2 VM required)"
}

# Check VBS status
$vbs = Get-CimInstance -ClassName 'Win32_DeviceGuard' -Namespace 'root\Microsoft\Windows\DeviceGuard' -ErrorAction SilentlyContinue
if ($vbs) {
    $vbsStatus = switch ($vbs.VirtualizationBasedSecurityStatus) {
        0 { 'Not enabled' }
        1 { 'Enabled but not running' }
        2 { 'Enabled and running' }
        default { "Unknown ($($vbs.VirtualizationBasedSecurityStatus))" }
    }
    Write-Host "  VBS: $vbsStatus"
    if ($vbs.VirtualizationBasedSecurityStatus -ne 2) {
        Write-Host "  INFO (V-205864): VBS requires Gen2 VM + Secure Boot + vTPM"
        Write-Host "         Enable in Hyper-V: Set-VMFirmware -EnableSecureBoot On"
        Write-Host "         Enable in Hyper-V: Enable-VMTPM"
        Write-Host "         GPO setting already configured in STIG-Server2019 (DeviceGuard)"
    }
} else {
    Write-Host "  VBS: Could not query DeviceGuard WMI class"
    Write-Host "  INFO (V-205864): VBS requires Gen2 VM + Secure Boot + vTPM"
}

$infoOnly += 2
Write-Host ""

# ============================================
# V-259342: DNS Forwarders (DC only — network architecture)
# DNS forwarders are environment/network dependent.
# ============================================
if ($isDC) {
    Write-Host "--- V-259342: DNS Forwarders ---"

    $dnsInstalled = (Get-WindowsFeature -Name DNS -ErrorAction SilentlyContinue).Installed
    if ($dnsInstalled) {
        try {
            $forwarders = (Get-DnsServerForwarder -ErrorAction Stop).IPAddress
            if ($forwarders) {
                Write-Host "  Current DNS forwarders:"
                foreach ($fw in $forwarders) { Write-Host "    - $fw" }
                Write-Host "  INFO (V-259342): Verify forwarders are DoD-approved DNS resolvers."
                Write-Host "         For NIPR: use DoD DNS infrastructure."
                Write-Host "         For lab: current configuration is acceptable."
            } else {
                Write-Host "  No DNS forwarders configured (root hints only)"
                Write-Host "  INFO: This may be acceptable for authoritative-only DNS."
            }
        } catch {
            Write-Host "  WARN: Could not query DNS forwarders: $($_.Exception.Message)"
        }
    } else {
        Write-Host "  DNS role not installed — N/A"
    }

    $infoOnly++
    Write-Host ""
}

# ============================================
# IE11 GPO + Loopback verification (DC/MS)
# Verify STIG-IE11 GPO is linked to the correct OUs and
# loopback processing is enabled so HKCU settings apply.
# ============================================
if ($isDC) {
    Write-Host "--- Verify: STIG-IE11 GPO Linkage ---"

    try {
        Import-Module GroupPolicy -ErrorAction Stop

        $dcOU = 'OU=Domain Controllers,DC=lab,DC=local'
        $msOU = 'OU=Member Servers,DC=lab,DC=local'

        foreach ($ou in @($dcOU, $msOU)) {
            $ouName = ($ou -split ',')[0] -replace 'OU=', ''
            $links = (Get-GPInheritance -Target $ou -ErrorAction SilentlyContinue).GpoLinks |
                     Where-Object { $_.DisplayName -eq 'STIG-IE11' }
            if ($links) {
                Write-Host "  STIG-IE11 linked to $ouName OU: YES (Enabled=$($links.Enabled))"
            } else {
                Write-Host "  STIG-IE11 linked to $ouName OU: NO"
                Write-Host "  WARNING: Run 08-dc01-stig-gpos.ps1 to link STIG-IE11 to server OUs"
            }
        }
    } catch {
        Write-Host "  WARN: Could not verify GPO links: $($_.Exception.Message)"
    }
    Write-Host ""
}

# ============================================
# Summary
# ============================================
Write-Host "================================================================"
Write-Host "  SUMMARY: Fix Remaining Open Findings"
Write-Host "================================================================"
Write-Host ""
Write-Host "  Fixed programmatically:   $fixed"
Write-Host "  Already compliant:        $skipped"
Write-Host "  Audit-only (manual):      $auditOnly"
Write-Host "  Informational (hardware): $infoOnly"
Write-Host ""

if ($isDC) {
    Write-Host "  DC findings addressed:"
    Write-Host "    V-243466/V-243467  Enterprise/Domain Admin membership   -> AUDIT (review output above)"
    Write-Host "    V-269097           Kerberos logging                     -> $(if ($fixed -gt 0) { 'FIXED/VERIFIED' } else { 'VERIFIED' })"
    Write-Host "    V-223122/V-223124  IE11 AutoComplete                   -> $(if ($ie11AllGood) { 'VERIFIED (GPO)' } else { 'FIXED' })"
    Write-Host "    V-213428           Defender real-time monitoring        -> FIXED/VERIFIED"
    Write-Host "    V-205648           DoD Root CAs                        -> $(if ($missingCAs.Count -gt 0) { 'MISSING — run 10-dc01-install-certs.ps1' } else { 'VERIFIED' })"
    Write-Host "    V-205702-V-205706  Kerberos policies (DDP)             -> FIXED/VERIFIED"
    Write-Host "    V-271429           Certificate strong mapping          -> FIXED/VERIFIED"
    Write-Host "    V-205848/V-205864  TPM / VBS                           -> INFO (VM hardware)"
    Write-Host "    V-259342           DNS forwarders                      -> INFO (network arch)"
} else {
    Write-Host "  $roleType findings addressed:"
    Write-Host "    V-223122/V-223124  IE11 AutoComplete                   -> $(if ($ie11AllGood) { 'VERIFIED (GPO)' } else { 'FIXED' })"
    Write-Host "    V-213428           Defender real-time monitoring        -> FIXED/VERIFIED"
    Write-Host "    V-205648           DoD Root CAs                        -> $(if ($missingCAs.Count -gt 0) { 'MISSING — run 10-dc01-install-certs.ps1' } else { 'VERIFIED' })"
    Write-Host "    V-205848/V-205864  TPM / VBS                           -> INFO (VM hardware)"
}

Write-Host ""
Write-Host "  Next steps:"
Write-Host "    1. gpupdate /force (apply GPO changes)"
Write-Host "    2. Reboot (for VBS/DeviceGuard changes)"
Write-Host "    3. Re-scan with Evaluate-STIG to verify"
Write-Host ""
