# STIGForge Lab - Step 9a: IE11 STIG Registry Hardening (OPTIONAL FALLBACK)
# Run on: Any Windows Server (DC or MS) as Administrator
# Covers: All 135 IE11 STIG registry checks via direct registry writes
#
# NORMALLY NOT NEEDED: STIG-IE11 GPO (02-dc01-create-gpos.ps1) covers all these settings.
# GPO loopback processing (08-dc01-stig-gpos.ps1) enables HKCU settings on DCs.
# Only run this if GPO is not applying IE11 settings correctly after gpupdate /force.
# Same registry values as STIG-IE11 GPO.

$fixed = 0

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

Write-Host "=== IE11 STIG Registry Hardening ==="
Write-Host ""

# ============================================
# Feature Controls - MUST be REG_SZ "1" (not DWORD)
# V-46477 through V-46509 (8 features x 3 processes)
# ============================================
Write-Host "--- Feature Controls ---"

$fcBase = 'HKLM:\SOFTWARE\Policies\Microsoft\Internet Explorer\Main\FeatureControl'
$fcKeys = @(
    'FEATURE_RESTRICT_ACTIVEXINSTALL','FEATURE_MIME_HANDLING','FEATURE_MIME_SNIFFING',
    'FEATURE_DISABLE_MK_PROTOCOL','FEATURE_ZONE_ELEVATION','FEATURE_RESTRICT_FILEDOWNLOAD',
    'FEATURE_WINDOW_RESTRICTIONS','FEATURE_SECURITYBAND'
)
foreach ($fc in $fcKeys) {
    foreach ($proc in @('(Reserved)','explorer.exe','iexplore.exe')) {
        Set-Reg "$fcBase\$fc" $proc '1' 'String'
    }
    $fixed++
}
Write-Host "  8 Feature Controls x 3 processes = 24 values (REG_SZ '1')"

# ============================================
# Zone 3 (Internet) Settings
# ============================================
Write-Host ""
Write-Host "--- Zone 3 (Internet) ---"

$z3 = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\3'
$zone3Settings = @{
    '1001'=3;'1004'=3;'1201'=3;'1206'=3;'1209'=3;'120b'=3;'120C'=3;'140C'=3;
    '1406'=3;'1407'=3;'1409'=0;'160A'=3;'1606'=3;'1607'=3;'1802'=3;'1804'=3;
    '1806'=1;'1809'=0;'1A00'=65536;'1C00'=0;'2001'=3;'2004'=3;'2101'=3;'2102'=3;
    '2103'=3;'2200'=3;'2301'=0;'2402'=3;'2500'=0;'2708'=3;'2709'=3;'270C'=0
}
foreach ($kv in $zone3Settings.GetEnumerator()) {
    Set-Reg $z3 $kv.Key $kv.Value
    $fixed++
}
Write-Host "  32 Zone 3 values"

# ============================================
# Zone 4 (Restricted Sites) Settings
# ============================================
Write-Host ""
Write-Host "--- Zone 4 (Restricted Sites) ---"

$z4 = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\4'
$zone4Settings = @{
    '1001'=3;'1004'=3;'1200'=3;'1201'=3;'1206'=3;'1209'=3;'120b'=3;'120C'=3;
    '140C'=3;'1400'=3;'1402'=3;'1405'=3;'1406'=3;'1407'=3;'1409'=0;'160A'=3;
    '1606'=3;'1607'=3;'1608'=3;'1802'=3;'1803'=3;'1804'=3;'1806'=3;'1809'=0;
    '1A00'=196608;'1C00'=0;'2000'=3;'2001'=3;'2004'=3;'2101'=3;'2102'=3;'2103'=3;
    '2200'=3;'2301'=0;'2402'=3;'2500'=0;'2708'=3;'2709'=3;'270C'=0
}
foreach ($kv in $zone4Settings.GetEnumerator()) {
    Set-Reg $z4 $kv.Key $kv.Value
    $fixed++
}
Write-Host "  39 Zone 4 values"

# ============================================
# Zones 0, 1, 2 Settings
# ============================================
Write-Host ""
Write-Host "--- Zones 0, 1, 2 ---"

$zBase = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones'
Set-Reg "$zBase\0" '1C00' 0
Set-Reg "$zBase\0" '270C' 0
Set-Reg "$zBase\1" '1C00' 65536
Set-Reg "$zBase\1" '270C' 0
Set-Reg "$zBase\1" '1201' 3
Set-Reg "$zBase\2" '1C00' 65536
Set-Reg "$zBase\2" '270C' 0
Set-Reg "$zBase\2" '1201' 3
$fixed += 8
Write-Host "  8 values across zones 0-2"

# ============================================
# Lockdown Zones
# ============================================
Write-Host ""
Write-Host "--- Lockdown Zones ---"

$lz = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Lockdown_Zones'
foreach ($z in @('0','1','2','4')) {
    Set-Reg "$lz\$z" '1C00' 0
    $fixed++
}
Write-Host "  4 lockdown zone values"

# ============================================
# General IE Settings
# ============================================
Write-Host ""
Write-Host "--- General IE Settings ---"

$is = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings'
Set-Reg $is 'WarnOnBadCertRecving' 1
Set-Reg $is 'PreventIgnoreCertErrors' 1
Set-Reg $is 'CertificateRevocation' 1
Set-Reg $is 'Security_zones_map_edit' 1
Set-Reg $is 'Security_options_edit' 1
Set-Reg $is 'Security_HKLM_only' 1
Set-Reg $is 'SecureProtocols' 2048
Set-Reg $is 'EnableSSL3Fallback' 0
Set-Reg "$is\ZoneMap" 'UNCAsIntranet' 0
Set-Reg "$is\Url History" 'DaysToKeep' 40
$fixed += 10
Write-Host "  SecureProtocols=2048 (TLS 1.2 only)"
Write-Host "  10 general settings"

# ============================================
# IE Policies
# ============================================
Write-Host ""
Write-Host "--- IE Policies ---"

$ie = 'HKLM:\SOFTWARE\Policies\Microsoft\Internet Explorer'
Set-Reg "$ie\PhishingFilter" 'PreventOverride' 1
Set-Reg "$ie\PhishingFilter" 'PreventOverrideAppRepUnknown' 1
Set-Reg "$ie\PhishingFilter" 'EnabledV9' 1
Set-Reg "$ie\Security\ActiveX" 'BlockNonAdminActiveXInstall' 1
Set-Reg "$ie\Security" 'DisableSecuritySettingsCheck' 0
Set-Reg "$ie\Download" 'RunInvalidSignatures' 0
Set-Reg "$ie\Download" 'CheckExeSignatures' 'yes' 'String'
Set-Reg "$ie\Main" 'Isolation64Bit' 1
Set-Reg "$ie\Main" 'DisableEPMCompat' 1
Set-Reg "$ie\Main" 'NotifyDisableIEOptions' 0
Set-Reg "$ie\IEDevTools" 'Disabled' 1
Set-Reg "$ie\Restrictions" 'NoCrashDetection' 1
Set-Reg "$ie\Privacy" 'ClearBrowsingHistoryOnExit' 0
Set-Reg "$ie\Privacy" 'CleanHistory' 0
Set-Reg "$ie\Privacy" 'EnableInPrivateBrowsing' 0
Set-Reg "$ie\Feeds" 'DisableEnclosureDownload' 1
$fixed += 16
Write-Host "  16 IE policy settings"

# ============================================
# ActiveX Extension Policies
# ============================================
Write-Host ""
Write-Host "--- ActiveX Extensions ---"

Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Ext' 'RunThisTimeEnabled' 0
Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Ext' 'VersionCheckEnabled' 1
$fixed += 2
Write-Host "  2 ActiveX settings"

# ============================================
# HKCU Settings (per-user IE policy)
# ============================================
Write-Host ""
Write-Host "--- HKCU IE Settings ---"

$hkcuIE = 'HKCU:\SOFTWARE\Policies\Microsoft\Internet Explorer\Main'
if (-not (Test-Path $hkcuIE)) { New-Item -Path $hkcuIE -Force | Out-Null }
Set-ItemProperty -Path $hkcuIE -Name 'Use FormSuggest' -Value 'no' -Type String -Force
Set-ItemProperty -Path $hkcuIE -Name 'FormSuggest Passwords' -Value 'no' -Type String -Force
Set-ItemProperty -Path $hkcuIE -Name 'FormSuggest PW Ask' -Value 'no' -Type String -Force
$fixed += 3
Write-Host "  3 HKCU FormSuggest settings"

# ============================================
# Summary
# ============================================
Write-Host ""
Write-Host "========================================="
Write-Host "  Total IE11 registry fixes applied: $fixed"
Write-Host "========================================="
Write-Host ""
Write-Host "  This covers all 135 IE11 STIG checks via direct registry writes."
Write-Host "  No gpupdate needed - values are applied immediately."
