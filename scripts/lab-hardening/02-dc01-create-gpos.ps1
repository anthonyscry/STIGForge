# STIGForge Lab - Step 2: Create and Configure All STIG GPOs (WS + Common)
# Run on: DC01 (lab.local\Administrator)
# Prerequisites: Step 1 complete (OUs created)
#
# Creates GPOs for workstations + domain-wide settings:
#   - STIG-IE11, STIG-OneDrive, STIG-Win11, STIG-DotNet, STIG-Edge (registry)
#   - STIG-Win11: security policy (user rights, audit) for workstations
#   - Default Domain Policy: account policies (GptTmpl.inf)
#   - FGPP: 15-char password policy
#
# DC/MS GPOs are in 08-dc01-stig-gpos.ps1 (STIG-Server2019, Defender, Firewall)

Import-Module GroupPolicy
Import-Module ActiveDirectory

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
# Helper: Write GptTmpl.inf security policy into a GPO
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
    $adObj = [ADSI]"LDAP://$gpoDN"
    $currentCSE = $adObj.Properties['gPCMachineExtensionNames'].Value
    if ($currentCSE -and $currentCSE -notmatch '827D319E') {
        $adObj.Properties['gPCMachineExtensionNames'].Value = $currentCSE + $secCSE
        $adObj.CommitChanges()
    } elseif (-not $currentCSE) {
        $adObj.Properties['gPCMachineExtensionNames'].Value = $secCSE
        $adObj.CommitChanges()
    }

    $newVer = [int]$adObj.Properties['versionNumber'].Value + 1
    $adObj.Properties['versionNumber'].Value = $newVer
    $adObj.CommitChanges()
    Write-Host "  GptTmpl.inf written to $GPOName (v$newVer)"
}

# ============================================
# Helper: Write audit.csv into a GPO
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
    $adObj = [ADSI]"LDAP://$gpoDN"
    $currentCSE = $adObj.Properties['gPCMachineExtensionNames'].Value
    if ($currentCSE -and $currentCSE -notmatch 'F3BC9527') {
        $adObj.Properties['gPCMachineExtensionNames'].Value = $currentCSE + $auditCSE
        $adObj.CommitChanges()
    } elseif (-not $currentCSE) {
        $adObj.Properties['gPCMachineExtensionNames'].Value = $auditCSE
        $adObj.CommitChanges()
    }

    $newVer = [int]$adObj.Properties['versionNumber'].Value + 1
    $adObj.Properties['versionNumber'].Value = $newVer
    $adObj.CommitChanges()
    Write-Host "  audit.csv written to $GPOName (v$newVer)"
}

# ============================================
# Create GPOs
# ============================================
$gpoNames = @('STIG-IE11','STIG-OneDrive','STIG-Win11','STIG-DotNet','STIG-Edge')
foreach ($name in $gpoNames) {
    $g = Get-GPO -Name $name -ErrorAction SilentlyContinue
    if (-not $g) { $g = New-GPO -Name $name }
    Write-Host "GPO: $($g.DisplayName)"
}

# ============================================
# STIG-IE11 (137 checks)
# ============================================
Write-Host "`n=== STIG-IE11 ==="
$gpo = 'STIG-IE11'

# Feature Controls - MUST be REG_SZ "1" (not DWORD)
$fcBase = 'HKLM\SOFTWARE\Policies\Microsoft\Internet Explorer\Main\FeatureControl'
$fcKeys = @(
    'FEATURE_RESTRICT_ACTIVEXINSTALL','FEATURE_MIME_HANDLING','FEATURE_MIME_SNIFFING',
    'FEATURE_DISABLE_MK_PROTOCOL','FEATURE_ZONE_ELEVATION','FEATURE_RESTRICT_FILEDOWNLOAD',
    'FEATURE_WINDOW_RESTRICTIONS','FEATURE_SECURITYBAND'
)
foreach ($fc in $fcKeys) {
    foreach ($name in @('(Reserved)','explorer.exe','iexplore.exe')) {
        Set-GPReg $gpo "$fcBase\$fc" $name '1' 'String'
    }
}

# Zone 3 (Internet)
$z3 = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\3'
@{
    '1001'=3;'1004'=3;'1201'=3;'1206'=3;'1209'=3;'120b'=3;'120C'=3;'140C'=3;
    '1406'=3;'1407'=3;'1409'=0;'160A'=3;'1606'=3;'1607'=3;'1802'=3;'1804'=3;
    '1806'=1;'1809'=0;'1A00'=65536;'1C00'=0;'2001'=3;'2004'=3;'2101'=3;'2102'=3;
    '2103'=3;'2200'=3;'2301'=0;'2402'=3;'2500'=0;'2708'=3;'2709'=3;'270C'=0
}.GetEnumerator() | ForEach-Object { Set-GPReg $gpo $z3 $_.Key $_.Value }
Write-Host "  32 Zone 3 values"

# Zone 4 (Restricted Sites)
$z4 = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\4'
@{
    '1001'=3;'1004'=3;'1200'=3;'1201'=3;'1206'=3;'1209'=3;'120b'=3;'120C'=3;
    '140C'=3;'1400'=3;'1402'=3;'1405'=3;'1406'=3;'1407'=3;'1409'=0;'160A'=3;
    '1606'=3;'1607'=3;'1608'=3;'1802'=3;'1803'=3;'1804'=3;'1806'=3;'1809'=0;
    '1A00'=196608;'1C00'=0;'2000'=3;'2001'=3;'2004'=3;'2101'=3;'2102'=3;'2103'=3;
    '2200'=3;'2301'=0;'2402'=3;'2500'=0;'2708'=3;'2709'=3;'270C'=0
}.GetEnumerator() | ForEach-Object { Set-GPReg $gpo $z4 $_.Key $_.Value }
Write-Host "  39 Zone 4 values"

# Zones 0,1,2
$zBase = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones'
Set-GPReg $gpo "$zBase\0" '1C00' 0; Set-GPReg $gpo "$zBase\0" '270C' 0
Set-GPReg $gpo "$zBase\1" '1C00' 65536; Set-GPReg $gpo "$zBase\1" '270C' 0; Set-GPReg $gpo "$zBase\1" '1201' 3
Set-GPReg $gpo "$zBase\2" '1C00' 65536; Set-GPReg $gpo "$zBase\2" '270C' 0; Set-GPReg $gpo "$zBase\2" '1201' 3

# Lockdown Zones
$lz = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Lockdown_Zones'
foreach ($z in @('0','1','2','4')) { Set-GPReg $gpo "$lz\$z" '1C00' 0 }

# General IE Settings
$is = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings'
Set-GPReg $gpo $is 'WarnOnBadCertRecving' 1
Set-GPReg $gpo $is 'PreventIgnoreCertErrors' 1
Set-GPReg $gpo $is 'CertificateRevocation' 1
Set-GPReg $gpo $is 'Security_zones_map_edit' 1
Set-GPReg $gpo $is 'Security_options_edit' 1
Set-GPReg $gpo $is 'Security_HKLM_only' 1
Set-GPReg $gpo $is 'SecureProtocols' 2048
Set-GPReg $gpo $is 'EnableSSL3Fallback' 0
Set-GPReg $gpo "$is\ZoneMap" 'UNCAsIntranet' 0
Set-GPReg $gpo "$is\Url History" 'DaysToKeep' 40

# IE Policies
$ie = 'HKLM\SOFTWARE\Policies\Microsoft\Internet Explorer'
Set-GPReg $gpo "$ie\PhishingFilter" 'PreventOverride' 1
Set-GPReg $gpo "$ie\PhishingFilter" 'PreventOverrideAppRepUnknown' 1
Set-GPReg $gpo "$ie\PhishingFilter" 'EnabledV9' 1
Set-GPReg $gpo "$ie\Security\ActiveX" 'BlockNonAdminActiveXInstall' 1
Set-GPReg $gpo "$ie\Security" 'DisableSecuritySettingsCheck' 0
Set-GPReg $gpo "$ie\Download" 'RunInvalidSignatures' 0
Set-GPReg $gpo "$ie\Download" 'CheckExeSignatures' 'yes' 'String'
Set-GPReg $gpo "$ie\Main" 'Isolation64Bit' 1
Set-GPReg $gpo "$ie\Main" 'DisableEPMCompat' 1
Set-GPReg $gpo "$ie\Main" 'NotifyDisableIEOptions' 0
Set-GPReg $gpo "$ie\IEDevTools" 'Disabled' 1
Set-GPReg $gpo "$ie\Restrictions" 'NoCrashDetection' 1
Set-GPReg $gpo "$ie\Privacy" 'ClearBrowsingHistoryOnExit' 0
Set-GPReg $gpo "$ie\Privacy" 'CleanHistory' 0
Set-GPReg $gpo "$ie\Privacy" 'EnableInPrivateBrowsing' 0

# ActiveX ext policies
Set-GPReg $gpo 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Ext' 'RunThisTimeEnabled' 0
Set-GPReg $gpo 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Ext' 'VersionCheckEnabled' 1

# HKCU (User Configuration)
$hkcuIE = 'HKCU\SOFTWARE\Policies\Microsoft\Internet Explorer\Main'
Set-GPReg $gpo $hkcuIE 'Use FormSuggest' 'no' 'String'
Set-GPReg $gpo $hkcuIE 'FormSuggest Passwords' 'no' 'String'
Set-GPReg $gpo $hkcuIE 'FormSuggest PW Ask' 'no' 'String'

Write-Host "  IE11 GPO complete (~137 settings)"

# ============================================
# STIG-Win11 (registry settings)
# ============================================
Write-Host "`n=== STIG-Win11 (Registry) ==="
$gpo = 'STIG-Win11'

# Event Log Sizes
$el = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\EventLog'
Set-GPReg $gpo "$el\Application" 'MaxSize' 32768
Set-GPReg $gpo "$el\Security" 'MaxSize' 1024000
Set-GPReg $gpo "$el\System" 'MaxSize' 32768

# UAC
$sys = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
Set-GPReg $gpo $sys 'LocalAccountTokenFilterPolicy' 0

# Legal Notice
$legalText = "You are accessing a U.S. Government (USG) Information System (IS) that is provided for USG-authorized use only.`r`nBy using this IS (which includes any device attached to this IS), you consent to the following conditions:`r`n-The USG routinely intercepts and monitors communications on this IS for purposes including, but not limited to, penetration testing, COMSEC monitoring, network operations and defense, personnel misconduct (PM), law enforcement (LE), and counterintelligence (CI) investigations.`r`n-At any time, the USG may inspect and seize data stored on this IS.`r`n-Communications using, or data stored on, this IS are not private, are subject to routine monitoring, interception, and search, and may be disclosed or used for any USG-authorized purpose.`r`n-This IS includes security measures (e.g., authentication and access controls) to protect USG interests--not for your personal benefit or privacy.`r`n-Notwithstanding the above, using this IS does not constitute consent to PM, LE or CI investigative searching or monitoring of the content of privileged communications, or work product, related to personal representation or services by attorneys, psychotherapists, or clergy, and their assistants. Such communications and work product are private and confidential. See User Agreement for details."
Set-GPReg $gpo $sys 'LegalNoticeText' $legalText 'String'
Set-GPReg $gpo $sys 'LegalNoticeCaption' 'DoD Notice and Consent Banner' 'String'

# VBS / DeviceGuard
$dg = 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard'
Set-GPReg $gpo $dg 'EnableVirtualizationBasedSecurity' 1
Set-GPReg $gpo $dg 'RequirePlatformSecurityFeatures' 1
Set-GPReg $gpo $dg 'HypervisorEnforcedCodeIntegrity' 1
Set-GPReg $gpo $dg 'HVCIMATRequired' 0
Set-GPReg $gpo $dg 'LsaCfgFlags' 1
Set-GPReg $gpo $dg 'ConfigureSystemGuardLaunch' 1

# BitLocker FVE (policy, not encryption start)
$fve = 'HKLM\SOFTWARE\Policies\Microsoft\FVE'
Set-GPReg $gpo $fve 'UseAdvancedStartup' 1
Set-GPReg $gpo $fve 'UseTPM' 2
Set-GPReg $gpo $fve 'UseTPMPIN' 1
Set-GPReg $gpo $fve 'UseTPMKey' 0
Set-GPReg $gpo $fve 'UseTPMKeyPIN' 0

# IE11 disable on Win11
Set-GPReg $gpo 'HKLM\SOFTWARE\Policies\Microsoft\Internet Explorer\Main' 'NotifyDisableIEOptions' 0

Write-Host "  Win11 GPO registry complete"

# ============================================
# STIG-Win11: Security Policy (user rights + deny logon)
# Previously applied via secedit in 04-ws01-local-hardening.ps1
# Now in GPO for easy enable/disable
# ============================================
Write-Host "`n=== STIG-Win11 (Security Policy) ==="

$wsSecurityInf = @"
[Unicode]
Unicode=yes
[Privilege Rights]
SeDenyNetworkLogonRight = *S-1-5-32-546,*S-1-5-113
SeDenyRemoteInteractiveLogonRight = *S-1-5-32-546,*S-1-5-113
SeDenyInteractiveLogonRight = *S-1-5-32-546
[Version]
signature="`$CHICAGO`$"
Revision=1
"@

Set-GPOSecurityPolicy -GPOName 'STIG-Win11' -InfContent $wsSecurityInf
Write-Host "  WS: 3 user rights assignments (deny network/RDP/logon)"

# ============================================
# STIG-Win11: Advanced Audit Policy
# Previously applied via auditpol in 04-ws01-local-hardening.ps1
# Now in GPO for easy enable/disable
# ============================================
Write-Host "`n=== STIG-Win11 (Audit Policy) ==="

$wsAuditCsv = @(
    'Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value'
    ',System,Process Creation,{0CCE922B-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,File System,{0CCE921D-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,Handle Manipulation,{0CCE9223-69AE-11D9-BED3-505054503030},Success and Failure,,3'
    ',System,Registry,{0CCE921E-69AE-11D9-BED3-505054503030},Success and Failure,,3'
) -join "`r`n"

Set-GPOAuditPolicy -GPOName 'STIG-Win11' -CsvContent $wsAuditCsv
Write-Host "  WS: 4 audit subcategories"

# ============================================
# STIG-OneDrive
# ============================================
Write-Host "`n=== STIG-OneDrive ==="
$gpo = 'STIG-OneDrive'

$od = 'HKLM\SOFTWARE\Policies\Microsoft\OneDrive'
Set-GPReg $gpo $od 'GPOEnabled' 1
Set-GPReg $gpo $od 'PreventNetworkTrafficPreUserSignIn' 1
Set-GPReg $gpo $od 'FilesOnDemandEnabled' 1
Set-GPReg $gpo $od 'DehydrateSyncedTeamSites' 1

# AllowTenantList - set org tenant GUID here
Set-GPReg $gpo "$od\AllowTenantList" '00000000-0000-0000-0000-000000000001' 'lab.local' 'String'

# groove.exe Feature Controls
$fc2 = 'HKLM\SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl'
$grooveFeatures = @(
    'FEATURE_DISABLE_MK_PROTOCOL','FEATURE_MIME_HANDLING','FEATURE_MIME_SNIFFING',
    'FEATURE_OBJECT_CACHING','FEATURE_RESTRICT_ACTIVEXINSTALL','FEATURE_RESTRICT_FILEDOWNLOAD',
    'FEATURE_SAFE_BINDTOOBJECT','FEATURE_SCRIPT_PASTE','FEATURE_UNC_SAVEDFILECHECK',
    'FEATURE_VALIDATE_NAVIGATE_URL','FEATURE_WEBOC_POPUPMANAGEMENT','FEATURE_WINDOW_RESTRICTIONS',
    'FEATURE_ZONE_ELEVATION'
)
foreach ($feat in $grooveFeatures) {
    Set-GPReg $gpo "$fc2\$feat" 'groove.exe' 1
}

# HKCU
Set-GPReg $gpo 'HKCU\SOFTWARE\Policies\Microsoft\OneDrive' 'DisablePersonalSync' 1

Write-Host "  OneDrive GPO complete"

# ============================================
# STIG-DotNet
# ============================================
Write-Host "`n=== STIG-DotNet ==="
$gpo = 'STIG-DotNet'

$dn32 = 'HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319'
$dn64 = 'HKLM\SOFTWARE\Wow6432Node\Microsoft\.NETFramework\v4.0.30319'
Set-GPReg $gpo $dn32 'SchUseStrongCrypto' 1
Set-GPReg $gpo $dn32 'SystemDefaultTlsVersions' 1
Set-GPReg $gpo $dn64 'SchUseStrongCrypto' 1
Set-GPReg $gpo $dn64 'SystemDefaultTlsVersions' 1

Write-Host "  DotNet GPO complete"

# ============================================
# STIG-Edge
# ============================================
Write-Host "`n=== STIG-Edge ==="
$gpo = 'STIG-Edge'

Set-GPReg $gpo 'HKLM\SOFTWARE\Policies\Microsoft\Edge' 'DefaultCookiesSetting' 4
Set-GPReg $gpo 'HKLM\SOFTWARE\Policies\Microsoft\Edge' 'ProxySettings' '{"ProxyMode": "system"}' 'String'

Write-Host "  Edge GPO complete"

# ============================================
# Link GPOs to OUs
# ============================================
Write-Host "`n=== Linking GPOs ==="

$wkOU = 'OU=Workstations,DC=lab,DC=local'
$domRoot = 'DC=lab,DC=local'

foreach ($name in @('STIG-IE11','STIG-OneDrive','STIG-Win11','STIG-Edge')) {
    $existing = (Get-GPInheritance -Target $wkOU).GpoLinks | Where-Object { $_.DisplayName -eq $name }
    if (-not $existing) {
        Get-GPO $name | New-GPLink -Target $wkOU -LinkEnabled Yes -ErrorAction SilentlyContinue
        Write-Host "  Linked $name -> Workstations OU"
    } else {
        Write-Host "  $name already linked"
    }
}

$existing = (Get-GPInheritance -Target $domRoot).GpoLinks | Where-Object { $_.DisplayName -eq 'STIG-DotNet' }
if (-not $existing) {
    Get-GPO 'STIG-DotNet' | New-GPLink -Target $domRoot -LinkEnabled Yes -ErrorAction SilentlyContinue
    Write-Host "  Linked STIG-DotNet -> Domain Root"
} else {
    Write-Host "  STIG-DotNet already linked"
}

# ============================================
# Default Domain Policy - Account Policies
# Account policies ONLY apply from domain root GPOs.
# Default Domain Policy takes precedence over custom GPOs.
# ============================================
Write-Host "`n=== Default Domain Policy (Account Policies) ==="

$ddpGPO = Get-GPO -Name 'Default Domain Policy'
$ddpId = $ddpGPO.Id.ToString('B').ToUpper()
$sysvolPath = "\\lab.local\SYSVOL\lab.local\Policies\$ddpId\Machine\Microsoft\Windows NT\SecEdit"

if (-not (Test-Path $sysvolPath)) {
    New-Item -Path $sysvolPath -ItemType Directory -Force | Out-Null
}

$gptTmplPath = "$sysvolPath\GptTmpl.inf"

$gptTmpl = @"
[Unicode]
Unicode=yes
[System Access]
MinimumPasswordAge = 1
MaximumPasswordAge = 60
MinimumPasswordLength = 14
PasswordComplexity = 1
PasswordHistorySize = 24
LockoutBadCount = 3
ResetLockoutCount = 15
LockoutDuration = 15
ClearTextPassword = 0
[Version]
signature="`$CHICAGO`$"
Revision=1
"@

Set-Content $gptTmplPath $gptTmpl -Encoding Unicode -Force
Write-Host "  GptTmpl.inf written: MinPwLen=14, Lockout=3/15/15"

# Ensure CSE GUIDs are registered so GPO engine processes security settings
$ddpDN = "CN=$ddpId,CN=Policies,CN=System,DC=lab,DC=local"
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
} else {
    Write-Host "  CSE GUIDs already present"
}

# Bump GPO version to force replication
$ddpGPO2 = Get-GPO -Name 'Default Domain Policy'
$newVer = $ddpGPO2.Computer.DSVersion + 1
$adObj.Properties['versionNumber'].Value = $newVer
$adObj.CommitChanges()
Write-Host "  GPO version bumped to $newVer"

# ============================================
# Fine-Grained Password Policy (15-char minimum)
# Overrides Default Domain Policy for all domain users
# ============================================
Write-Host "`n=== Fine-Grained Password Policy ==="

$fgppName = 'STIG-PasswordPolicy'
$existing = Get-ADFineGrainedPasswordPolicy -Filter { Name -eq 'STIG-PasswordPolicy' } -ErrorAction SilentlyContinue

if ($existing) {
    Write-Host "  $fgppName already exists, updating..."
    Set-ADFineGrainedPasswordPolicy -Identity $fgppName `
        -MinPasswordLength 15 `
        -ComplexityEnabled $true `
        -MaxPasswordAge '60.00:00:00' `
        -MinPasswordAge '1.00:00:00' `
        -PasswordHistoryCount 24 `
        -LockoutThreshold 3 `
        -LockoutDuration '00:15:00' `
        -LockoutObservationWindow '00:15:00' `
        -ReversibleEncryptionEnabled $false
} else {
    New-ADFineGrainedPasswordPolicy -Name $fgppName `
        -DisplayName 'STIG Password Policy - 15 char minimum' `
        -Description 'STIG-hardened: 15-char min, complexity, 60-day max age, lockout after 3 attempts' `
        -Precedence 10 `
        -MinPasswordLength 15 `
        -ComplexityEnabled $true `
        -MaxPasswordAge '60.00:00:00' `
        -MinPasswordAge '1.00:00:00' `
        -PasswordHistoryCount 24 `
        -LockoutThreshold 3 `
        -LockoutDuration '00:15:00' `
        -LockoutObservationWindow '00:15:00' `
        -ReversibleEncryptionEnabled $false `
        -ProtectedFromAccidentalDeletion $true
    Write-Host "  Created $fgppName"
}

# Apply to Domain Users
$subjects = (Get-ADFineGrainedPasswordPolicy -Identity $fgppName).AppliesTo
if ('CN=Domain Users,CN=Users,DC=lab,DC=local' -notin $subjects) {
    Add-ADFineGrainedPasswordPolicySubject -Identity $fgppName -Subjects 'Domain Users'
    Write-Host "  Applied to Domain Users"
} else {
    Write-Host "  Already applied to Domain Users"
}

$fgpp = Get-ADFineGrainedPasswordPolicy -Identity $fgppName
Write-Host "  MinPasswordLength: $($fgpp.MinPasswordLength) | MaxAge: $($fgpp.MaxPasswordAge) | Lockout: $($fgpp.LockoutThreshold)"

Write-Host "`n=== GPO Setup Complete ==="
Write-Host "  WS GPOs: STIG-IE11, STIG-OneDrive, STIG-Win11 (registry + security + audit), STIG-Edge"
Write-Host "  Common: STIG-DotNet, Default Domain Policy (account policies), FGPP"
Write-Host "  DC GPOs: created separately by 08-dc01-stig-gpos.ps1"
Write-Host ""
Write-Host "  To DISABLE WS hardening: disable GPO links on Workstations OU"
Write-Host "  To RE-ENABLE: re-enable GPO links"
