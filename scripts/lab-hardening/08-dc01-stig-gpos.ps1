# STIGForge Lab - Step 8: Create DC/MS STIG GPOs (Registry + Security Policy + Audit)
# Run on: DC01 (lab.local\Administrator)
# Prerequisites: Steps 1-2 complete (OUs, WS01 GPOs created)
# Creates: STIG-Server2019, STIG-Defender, STIG-Firewall GPOs
# Links all STIG GPOs to BOTH Domain Controllers OU and Member Servers OU
#
# This script consolidates ALL GPO-able DC/MS hardening:
#   - Registry settings (Set-GPRegistryValue)
#   - Security policy: user rights, account rename (GptTmpl.inf)
#   - Advanced audit policy (audit.csv)
#   - GPO loopback processing (for HKCU settings on DC)
#
# Settings that were previously in 09-dc01-local-hardening.ps1 via secedit/auditpol
# are now here as GPO settings, making them easy to disable/re-enable.

Import-Module GroupPolicy
Import-Module ActiveDirectory
Import-Module "$PSScriptRoot\lib\StigForge-GPO.psm1" -Force
Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force

# ============================================
# Detect Server Role
# ============================================
$role = Get-ServerRole
$isDC = $role.IsDC
$roleName = $role.Name

Write-Host "=== Creating STIG GPOs for $roleName ==="
Write-Host ""

$dcOU = 'OU=Domain Controllers,DC=lab,DC=local'
$msOU = 'OU=Member Servers,DC=lab,DC=local'
$wsOU = 'OU=Workstations,DC=lab,DC=local'

# ============================================
# Ensure Member Servers OU exists
# ============================================
if (-not ([ADSI]::Exists("LDAP://$msOU"))) {
    New-ADOrganizationalUnit -Name 'Member Servers' -Path 'DC=lab,DC=local' -ProtectedFromAccidentalDeletion $false
    Write-Host "Created OU: $msOU"
}

# ============================================
# Create GPOs
# ============================================
$gpoNames = @('STIG-Server2019','STIG-Defender','STIG-Firewall')
foreach ($name in $gpoNames) {
    $g = Get-GPO -Name $name -ErrorAction SilentlyContinue
    if (-not $g) { $g = New-GPO -Name $name }
    Write-Host "GPO: $($g.DisplayName)"
}

# ============================================
# STIG-Server2019 (registry settings)
# ============================================
Write-Host "`n=== STIG-Server2019 (Registry) ==="
$gpo = 'STIG-Server2019'
$p = 'HKLM\SOFTWARE\Policies\Microsoft'
$s = 'HKLM\SYSTEM\CurrentControlSet'

# --- WinRM ---
Set-GPReg $gpo "$p\Windows\WinRM\Client" 'AllowBasic' 0
Set-GPReg $gpo "$p\Windows\WinRM\Client" 'AllowDigest' 0
Set-GPReg $gpo "$p\Windows\WinRM\Client" 'AllowUnencryptedTraffic' 0
Set-GPReg $gpo "$p\Windows\WinRM\Service" 'AllowBasic' 0
Set-GPReg $gpo "$p\Windows\WinRM\Service" 'AllowUnencryptedTraffic' 0
Set-GPReg $gpo "$p\Windows\WinRM\Service" 'DisableRunAs' 1
Write-Host "  WinRM: 6 settings"

# --- AutoPlay/AutoRun ---
Set-GPReg $gpo "$p\Windows\Explorer" 'NoAutoplayfornonVolume' 1
Set-GPReg $gpo 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoAutorun' 1
Set-GPReg $gpo 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoDriveTypeAutoRun' 255
Write-Host "  AutoPlay: 3 settings"

# --- UAC ---
$sys = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
Set-GPReg $gpo "$p\Windows\CurrentVersion\Policies\CredUI" 'EnumerateAdministrators' 0
Set-GPReg $gpo $sys 'ConsentPromptBehaviorAdmin' 2
Set-GPReg $gpo $sys 'ConsentPromptBehaviorUser' 0
Set-GPReg $gpo $sys 'FilterAdministratorToken' 1
Set-GPReg $gpo $sys 'EnableLUA' 1
Write-Host "  UAC: 5 settings"

# --- Legal Notice ---
$legalText = "You are accessing a U.S. Government (USG) Information System (IS) that is provided for USG-authorized use only.`r`nBy using this IS (which includes any device attached to this IS), you consent to the following conditions:`r`n-The USG routinely intercepts and monitors communications on this IS for purposes including, but not limited to, penetration testing, COMSEC monitoring, network operations and defense, personnel misconduct (PM), law enforcement (LE), and counterintelligence (CI) investigations.`r`n-At any time, the USG may inspect and seize data stored on this IS.`r`n-Communications using, or data stored on, this IS are not private, are subject to routine monitoring, interception, and search, and may be disclosed or used for any USG-authorized purpose.`r`n-This IS includes security measures (e.g., authentication and access controls) to protect USG interests--not for your personal benefit or privacy.`r`n-Notwithstanding the above, using this IS does not constitute consent to PM, LE or CI investigative searching or monitoring of the content of privileged communications, or work product, related to personal representation or services by attorneys, psychotherapists, or clergy, and their assistants. Such communications and work product are private and confidential. See User Agreement for details."
Set-GPReg $gpo $sys 'LegalNoticeText' $legalText 'String'
Set-GPReg $gpo $sys 'LegalNoticeCaption' 'DoD Notice and Consent Banner' 'String'
Set-GPReg $gpo $sys 'InactivityTimeoutSecs' 900
Write-Host "  Legal notice + inactivity timeout"

# --- RDS ---
$ts = 'HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services'
Set-GPReg $gpo $ts 'fEncryptRPCTraffic' 1
Set-GPReg $gpo $ts 'MinEncryptionLevel' 3
Set-GPReg $gpo $ts 'fDisableCdm' 1
Set-GPReg $gpo $ts 'DisablePasswordSaving' 1
Set-GPReg $gpo $ts 'fPromptForPassword' 1
Write-Host "  RDS: 5 settings"

# --- SMB/NTLM/Kerberos ---
Set-GPReg $gpo "$s\Control\Lsa" 'RestrictAnonymous' 1
Set-GPReg $gpo "$s\Control\Lsa" 'UseMachineId' 1
Set-GPReg $gpo "$s\Control\Lsa" 'LmCompatibilityLevel' 5
Set-GPReg $gpo "$s\Control\Lsa" 'SCENoApplyLegacyAuditPolicy' 1
Set-GPReg $gpo "$s\Control\Lsa\MSV1_0" 'allownullsessionfallback' 0
Set-GPReg $gpo "$s\Control\Lsa\MSV1_0" 'NTLMMinClientSec' 537395200
Set-GPReg $gpo "$s\Control\Lsa\MSV1_0" 'NTLMMinServerSec' 537395200
Set-GPReg $gpo "$s\Control\Lsa\pku2u" 'AllowOnlineID' 0
Set-GPReg $gpo "$s\Services\LanmanWorkstation\Parameters" 'RequireSecuritySignature' 1
Set-GPReg $gpo "$p\Windows\LanmanWorkstation" 'AllowInsecureGuestAuth' 0
Set-GPReg $gpo "$s\Services\NTDS\Parameters" 'LDAPServerIntegrity' 2
Set-GPReg $gpo 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Kerberos\Parameters' 'SupportedEncryptionTypes' 2147483640
Set-GPReg $gpo "$s\Services\Kdc" 'StrongCertificateBindingEnforcement' 1
Write-Host "  SMB/NTLM/Kerberos/LDAP: 13 settings"

# --- Network ---
Set-GPReg $gpo "$s\Services\Netbt\Parameters" 'NoNameReleaseOnDemand' 1
Set-GPReg $gpo "$s\Services\Tcpip6\Parameters" 'DisableIPSourceRouting' 2
Set-GPReg $gpo "$s\Services\Tcpip\Parameters" 'DisableIPSourceRouting' 2
Set-GPReg $gpo "$s\Services\Tcpip\Parameters" 'EnableICMPRedirect' 0
Set-GPReg $gpo "$p\Windows\NetworkProvider\HardenedPaths" '\\*\NETLOGON' 'RequireMutualAuthentication=1, RequireIntegrity=1' 'String'
Set-GPReg $gpo "$p\Windows\NetworkProvider\HardenedPaths" '\\*\SYSVOL' 'RequireMutualAuthentication=1, RequireIntegrity=1' 'String'
Set-GPReg $gpo "$p\Windows\CredentialsDelegation" 'AllowProtectedCreds' 1
Write-Host "  Network: 7 settings"

# --- Misc Policies ---
Set-GPReg $gpo "$sys\Audit" 'ProcessCreationIncludeCmdLine_Enabled' 1
Set-GPReg $gpo "$p\Windows\PowerShell\ScriptBlockLogging" 'EnableScriptBlockLogging' 1
Set-GPReg $gpo "$p\Windows\PowerShell\Transcription" 'EnableTranscripting' 1
Set-GPReg $gpo "$p\Cryptography" 'ForceKeyProtection' 2
Set-GPReg $gpo "$p\Windows\Personalization" 'NoLockScreenSlideshow' 1
Set-GPReg $gpo "$s\Control\SecurityProviders\WDigest" 'UseLogonCredential' 0
Set-GPReg $gpo 'HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Printers' 'DisableWebPnPDownload' 1
Set-GPReg $gpo 'HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Printers' 'DisableHTTPPrinting' 1
Set-GPReg $gpo "$p\Windows\System" 'DontDisplayNetworkSelectionUI' 1
Set-GPReg $gpo "$p\Windows\System" 'EnableSmartScreen' 1
Set-GPReg $gpo "$p\Windows\AppCompat" 'DisableInventory' 1
Set-GPReg $gpo "$p\Windows\Windows Search" 'AllowIndexingEncryptedStoresOrItems' 0
Set-GPReg $gpo "$p\Windows\Installer" 'EnableUserControl' 0
Set-GPReg $gpo "$p\Windows\Installer" 'AlwaysInstallElevated' 0
Set-GPReg $gpo "$s\Control\Lsa\FIPSAlgorithmPolicy" 'Enabled' 1
Set-GPReg $gpo "$p\Windows\DataCollection" 'AllowTelemetry' 1
Set-GPReg $gpo "$p\Windows\DeliveryOptimization" 'DODownloadMode' 1
Set-GPReg $gpo "$p\Internet Explorer\Feeds" 'DisableEnclosureDownload' 1
Set-GPReg $gpo "$s\Services\Netlogon\Parameters" 'RefusePasswordChange' 0
Set-GPReg $gpo 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' 'ScRemoveOption' '1' 'String'
Write-Host "  Misc policies: 20 settings"

# --- Wake prompt ---
$powerGuid = '0e796bdb-100d-47d6-a2d5-f7d2daa51f51'
Set-GPReg $gpo "$p\Windows\Power\PowerSettings\$powerGuid" 'ACSettingIndex' 1
Set-GPReg $gpo "$p\Windows\Power\PowerSettings\$powerGuid" 'DCSettingIndex' 1
Write-Host "  Wake prompt: 2 settings"

# --- Event Log Sizes ---
$el = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\EventLog'
Set-GPReg $gpo "$el\Application" 'MaxSize' 32768
Set-GPReg $gpo "$el\Security" 'MaxSize' 196608
Set-GPReg $gpo "$el\System" 'MaxSize' 32768
Write-Host "  Event logs: 3 settings"

# --- VBS / Device Guard ---
$dg = "$p\Windows\DeviceGuard"
Set-GPReg $gpo $dg 'EnableVirtualizationBasedSecurity' 1
Set-GPReg $gpo $dg 'RequirePlatformSecurityFeatures' 1
Set-GPReg $gpo $dg 'HypervisorEnforcedCodeIntegrity' 1
Set-GPReg $gpo $dg 'LsaCfgFlags' 1
Write-Host "  VBS/DeviceGuard: 4 settings"

# --- Kerberos Audit Logging (V-269097) ---
# Previously in 09-dc01-local-hardening.ps1 via direct registry
Set-GPReg $gpo "$s\Control\Lsa\Kerberos\Parameters" 'LogLevel' 1
Write-Host "  Kerberos audit: 1 setting"

# --- GPO Loopback Processing (enables HKCU settings from computer-linked GPOs) ---
# Without this, STIG-IE11 HKCU settings won't apply on DCs
Set-GPReg $gpo "$p\Windows\System" 'UserPolicyMode' 1  # 1=Merge
Write-Host "  Loopback processing: Merge mode"

Write-Host "  Server2019 GPO complete (~77 registry settings)"

# ============================================
# STIG-Server2019: Security Policy (GptTmpl.inf)
# Previously applied via secedit in 09-dc01-local-hardening.ps1
# Now in GPO for easy enable/disable
# ============================================
Write-Host "`n=== STIG-Server2019 (Security Policy) ==="

if ($isDC) {
    # DC user rights: includes Enterprise Domain Controllers (S-1-5-9)
    $securityInf = @"
[Unicode]
Unicode=yes
[System Access]
NewAdministratorName = "StigAdmin"
NewGuestName = "StigGuest"
[Privilege Rights]
SeNetworkLogonRight = *S-1-5-32-544,*S-1-5-11,*S-1-5-9
SeDenyNetworkLogonRight = *S-1-5-32-546
SeDenyBatchLogonRight = *S-1-5-32-546
SeDenyInteractiveLogonRight = *S-1-5-32-546
SeInteractiveLogonRight = *S-1-5-32-544
SeDenyRemoteInteractiveLogonRight = *S-1-5-32-546
SeMachineAccountPrivilege = *S-1-5-32-544
SeBackupPrivilege = *S-1-5-32-544
SeRemoteShutdownPrivilege = *S-1-5-32-544
SeIncreaseBasePriorityPrivilege = *S-1-5-32-544
SeLoadDriverPrivilege = *S-1-5-32-544
SeRestorePrivilege = *S-1-5-32-544
[Version]
signature="`$CHICAGO`$"
Revision=1
"@
    Write-Host "  DC: account rename + 12 user rights assignments"
} else {
    # Member Server user rights: no Enterprise DC, adds Local account deny
    $securityInf = @"
[Unicode]
Unicode=yes
[System Access]
NewAdministratorName = "StigAdmin"
NewGuestName = "StigGuest"
[Privilege Rights]
SeNetworkLogonRight = *S-1-5-32-544,*S-1-5-11
SeDenyNetworkLogonRight = *S-1-5-32-546
SeDenyBatchLogonRight = *S-1-5-32-546
SeDenyInteractiveLogonRight = *S-1-5-32-546
SeInteractiveLogonRight = *S-1-5-32-544
SeDenyRemoteInteractiveLogonRight = *S-1-5-32-546,*S-1-5-113
SeBackupPrivilege = *S-1-5-32-544
SeRemoteShutdownPrivilege = *S-1-5-32-544
SeIncreaseBasePriorityPrivilege = *S-1-5-32-544
SeLoadDriverPrivilege = *S-1-5-32-544
SeRestorePrivilege = *S-1-5-32-544
[Version]
signature="`$CHICAGO`$"
Revision=1
"@
    Write-Host "  MS: account rename + 11 user rights assignments"
}

Set-GPOSecurityPolicy -GPOName 'STIG-Server2019' -InfContent $securityInf

# ============================================
# STIG-Server2019: Advanced Audit Policy (audit.csv)
# Previously applied via auditpol in 09-dc01-local-hardening.ps1
# Now in GPO for easy enable/disable
# ============================================
Write-Host "`n=== STIG-Server2019 (Audit Policy) ==="

# audit.csv format: Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value
# Setting Value: 1=Success, 2=Failure, 3=Success+Failure, 0=No Auditing
$auditHeader = 'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value'
$auditLines = @(
    $auditHeader
    # Account Management
    ',System,User Account Management,{0CCE9235-69AE-11D9-BED3-505054503030},Failure,,2'
    ',System,Other Account Management Events,{0CCE923A-69AE-11D9-BED3-505054503030},Success,,1'
    # Logon/Logoff
    ',System,Account Lockout,{0CCE9217-69AE-11D9-BED3-505054503030},Failure,,2'
    ',System,Group Membership,{0CCE9249-69AE-11D9-BED3-505054503030},Success,,1'
    # Detailed Tracking
    ',System,Process Creation,{0CCE922B-69AE-11D9-BED3-505054503030},Success,,1'
    ',System,Plug and Play Events,{0CCE9248-69AE-11D9-BED3-505054503030},Success,,1'
    # Policy Change
    ',System,Audit Policy Change,{0CCE922F-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,Authorization Policy Change,{0CCE9231-69AE-11D9-BED3-505054503030},Success,,1'
    # Privilege Use
    ',System,Sensitive Privilege Use,{0CCE9228-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    # System
    ',System,IPsec Driver,{0CCE9213-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,Security System Extension,{0CCE9211-69AE-11D9-BED3-505054503030},Success,,1'
    # Account Logon
    ',System,Credential Validation,{0CCE923F-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    # Object Access
    ',System,Other Object Access Events,{0CCE9227-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,File System,{0CCE921D-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,Handle Manipulation,{0CCE9223-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,Registry,{0CCE921E-69AE-11D9-BED3-505054503030},Success and Failure,,3'
)

# DC-only: Directory Service Access auditing
if ($isDC) {
    $auditLines += @(
        ',System,Directory Service Access,{0CCE923B-69AE-11D9-BED3-505054503030},Success and Failure,,3'
        ',System,Directory Service Changes,{0CCE923C-69AE-11D9-BED3-505054503030},Success,,1'
    )
    Write-Host "  DC: 18 audit subcategories (includes DS Access)"
} else {
    Write-Host "  MS: 16 audit subcategories"
}

$auditCsv = $auditLines -join "`r`n"
Set-GPOAuditPolicy -GPOName 'STIG-Server2019' -CsvContent $auditCsv

# ============================================
# STIG-Defender (48 settings)
# ============================================
Write-Host "`n=== STIG-Defender ==="
$gpo = 'STIG-Defender'
$wd = 'HKLM\SOFTWARE\Policies\Microsoft\Windows Defender'

# Core settings
Set-GPReg $gpo "$wd\MpEngine" 'MpEnablePus' 1
Set-GPReg $gpo "$wd\MpEngine" 'MpCloudBlockLevel' 2
Set-GPReg $gpo "$wd\MpEngine" 'MpBafsExtendedTimeout' 50
Set-GPReg $gpo "$wd\MpEngine" 'EnableFileHashComputation' 1
Set-GPReg $gpo "$wd\MpEngine" 'EnableConvertWarnToBlock' 1

# MAPS / Cloud
Set-GPReg $gpo "$wd\SpyNet" 'SpynetReporting' 2
Set-GPReg $gpo "$wd\SpyNet" 'SubmitSamplesConsent' 1

# Scanning
Set-GPReg $gpo "$wd\Scan" 'DisableRemovableDriveScanning' 0
Set-GPReg $gpo "$wd\Scan" 'ScheduleDay' 1
Set-GPReg $gpo "$wd\Scan" 'DisableEmailScanning' 0
Set-GPReg $gpo "$wd\Scan" 'DisablePackedExeScanning' 0
Set-GPReg $gpo "$wd\Scan" 'DisableHeuristics' 0
Set-GPReg $gpo "$wd\Scan" 'QuickScanIncludeExclusions' 1

# Signature Updates
Set-GPReg $gpo "$wd\Signature Updates" 'ASSignatureDue' 7
Set-GPReg $gpo "$wd\Signature Updates" 'AVSignatureDue' 7
Set-GPReg $gpo "$wd\Signature Updates" 'ScheduleDay' 0

# Threat Severity Remediation (2=Quarantine)
Set-GPReg $gpo "$wd\Threats" 'Threats_ThreatSeverityDefaultAction' 1
Set-GPReg $gpo "$wd\Threats\ThreatSeverityDefaultAction" '1' '2' 'String'
Set-GPReg $gpo "$wd\Threats\ThreatSeverityDefaultAction" '2' '2' 'String'
Set-GPReg $gpo "$wd\Threats\ThreatSeverityDefaultAction" '4' '2' 'String'
Set-GPReg $gpo "$wd\Threats\ThreatSeverityDefaultAction" '5' '2' 'String'

# Real-Time Protection
Set-GPReg $gpo "$wd\Real-Time Protection" 'DisableScriptScanning' 0
Set-GPReg $gpo "$wd\Real-Time Protection" 'OobeEnableRtpAndSigUpdate' 1
Set-GPReg $gpo "$wd\Real-Time Protection" 'DisableAsyncScanOnOpen' 0

# Network Protection
Set-GPReg $gpo "$wd\Windows Defender Exploit Guard\Network Protection" 'EnableNetworkProtection' 1

# Misc
Set-GPReg $gpo $wd 'DisableLocalAdminMerge' 1
Set-GPReg $gpo $wd 'HideExclusionsFromLocalAdmins' 1
Set-GPReg $gpo $wd 'RandomizeScheduleTaskTimes' 1
Set-GPReg $gpo "$wd\Configuration" 'ForceDefenderPassiveMode' 0
Set-GPReg $gpo "$wd\Reporting" 'DisableGenericReports' 0
Write-Host "  Defender core: 30 settings"

# ASR Rules (Attack Surface Reduction)
$asrPath = "$wd\Windows Defender Exploit Guard\ASR"
Set-GPReg $gpo $asrPath 'ExploitGuard_ASR_Rules' 1

$asrRules = @{
    'BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550' = '1'  # Block executable email content
    'D4F940AB-401B-4EFC-AADC-AD5F3C50688A' = '1'  # Block Office child processes
    '3B576869-A4EC-4529-8536-B80A7769E899' = '1'  # Block Office executable content
    '75668C1F-73B5-4CF0-BB93-3ECF5CB7CC84' = '1'  # Block Office inject into processes
    'D3E037E1-3EB8-44C8-A917-57927947596D' = '1'  # Block JS/VBS launching executables
    '5BEB7EFE-FD9A-4556-801D-275E5FFC04CC' = '1'  # Block obfuscated scripts
    '92E97FA1-2EDF-4476-BDD6-9DD0B4DDDC7B' = '1'  # Block Win32 imports from macros
    '7674BA52-37EB-4A4F-A9A1-F0F9A1619A2C' = '1'  # Block Adobe Reader child processes
    '9E6C4E1F-7D60-472F-BA1A-A39EF669E4B2' = '1'  # Block credential stealing from LSASS
    'B2B3F03D-6A65-4F7B-A9C7-1C7EF74A9BA4' = '1'  # Block untrusted USB processes
    'C1DB55AB-C21A-4637-BB3F-A12568109D35' = '1'  # Advanced ransomware protection
    'D1E49AAC-8F56-4280-B9BA-993A6D77406C' = '1'  # Block PSExec/WMI process creation
    'E6DB77E5-3DF2-4CF1-B95A-636979351E5B' = '1'  # Block WMI event subscription persistence
    '01443614-CD74-433A-B99E-2ECDC07BFC25' = '1'  # Block executables unless prevalence/age/trusted
    '26190899-1602-49E8-8B27-EB1D0A1CE869' = '1'  # Block Office comms app child processes
    '56A863A9-875E-4185-98A7-B882C64B5CE5' = '1'  # Block exploited vulnerable signed drivers
}
foreach ($rule in $asrRules.GetEnumerator()) {
    Set-GPReg $gpo "$asrPath\Rules" $rule.Key $rule.Value 'String'
}
Write-Host "  ASR rules: 16 rules"

# Windows Security Center UI
Set-GPReg $gpo 'HKLM\SOFTWARE\Policies\Microsoft\Windows Defender Security Center\Family options' 'UILockdown' 1
Write-Host "  Defender GPO complete (~48 settings)"

# ============================================
# STIG-Firewall (20 settings)
# ============================================
Write-Host "`n=== STIG-Firewall ==="
$gpo = 'STIG-Firewall'
$fw = 'HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall'

foreach ($profile in @('DomainProfile','PrivateProfile','PublicProfile')) {
    Set-GPReg $gpo "$fw\$profile" 'EnableFirewall' 1
    Set-GPReg $gpo "$fw\$profile" 'DefaultInboundAction' 1
    Set-GPReg $gpo "$fw\$profile" 'DefaultOutboundAction' 0
    Set-GPReg $gpo "$fw\$profile\Logging" 'LogFileSize' 16384
    Set-GPReg $gpo "$fw\$profile\Logging" 'LogDroppedPackets' 1
    Set-GPReg $gpo "$fw\$profile\Logging" 'LogSuccessfulConnections' 1
}

# Public profile extra restrictions
Set-GPReg $gpo "$fw\PublicProfile" 'AllowLocalPolicyMerge' 0
Set-GPReg $gpo "$fw\PublicProfile" 'AllowLocalIPsecPolicyMerge' 0
Write-Host "  Firewall GPO complete (20 settings)"

# ============================================
# Link GPOs to Domain Controllers OU + Member Servers OU
# ============================================
Write-Host "`n=== Linking GPOs ==="

# GPOs that apply to BOTH DC and Member Servers
$bothOUs = @($dcOU, $msOU)
foreach ($name in @('STIG-Server2019','STIG-Defender','STIG-Firewall','STIG-IE11')) {
    $gpoObj = Get-GPO -Name $name -ErrorAction SilentlyContinue
    if (-not $gpoObj) {
        Write-Host "  SKIP: $name GPO not found (will be created by 02-dc01-create-gpos.ps1)"
        continue
    }
    foreach ($ou in $bothOUs) {
        try {
            Ensure-GPOLink -GpoName $name -Target $ou
        } catch {
            Write-Host "  WARN: Could not enforce $name link on ${ou}: $($_.Exception.Message)"
        }
    }
}

# STIG-DotNet applies domain-wide (already linked to domain root by 02-dc01-create-gpos.ps1)
$domRoot = 'DC=lab,DC=local'
$dotnetGpo = Get-GPO -Name 'STIG-DotNet' -ErrorAction SilentlyContinue
if ($dotnetGpo) {
    try {
        Ensure-GPOLink -GpoName 'STIG-DotNet' -Target $domRoot
    } catch {
        Write-Host "  WARN: Could not enforce STIG-DotNet link on Domain Root: $($_.Exception.Message)"
    }
}

Write-Host "`n=== GPO Setup Complete ==="
Write-Host "  Registry settings: ~145"
Write-Host "  Security policy: user rights + account rename (GptTmpl.inf)"
Write-Host "  Audit policy: 16-18 subcategories (audit.csv)"
Write-Host "  GPOs linked to: Domain Controllers OU + Member Servers OU"
Write-Host "  Run 'gpupdate /force' to apply, then reboot for VBS/DeviceGuard"
