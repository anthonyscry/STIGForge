# STIGForge Lab - Step 7a: Import ADMX Templates + DISA STIG GPOs via LGPO/Import-GPO
# Run on: Any Windows Server (DC or Member Server) as Administrator
# Auto-detects: DC vs MS role, selects correct GPO backup
# Prerequisites: Copy import/ folder contents to C:\temp\ on target
#   C:\temp\U_STIG_GPO_Package_January_2026.zip
#   C:\temp\LGPO.zip
#
# DC: ADMX -> SYSVOL Central Store, Import-GPO into AD, LGPO local policy
# MS: ADMX -> local PolicyDefinitions, LGPO local policy only (no AD GPO import)
#
# Run AFTER 07-dc01-dsc-hardening.ps1, BEFORE 08-dc01-stig-gpos.ps1

$tempDir = 'C:\temp'
$fixed = 0

# ============================================
# Detect Server Role
# ============================================
Import-Module "$PSScriptRoot\lib\StigForge-Common.psm1" -Force
$role = Get-ServerRole
$isDC = $role.IsDC
$roleName = $role.Name

Write-Host "=== Role: $roleName ($env:COMPUTERNAME) ==="
Write-Host ""

# ============================================
# 1. Extract STIG GPO Package
# ============================================
Write-Host "=== Extracting STIG GPO Package ==="

$gpoZip = "$tempDir\U_STIG_GPO_Package_January_2026.zip"
$gpoDir = "$tempDir\STIG_GPO_Package"

if (-not (Test-Path $gpoZip)) {
    Write-Host "ERROR: $gpoZip not found"
    exit 1
}

if (-not (Test-Path $gpoDir)) {
    Expand-Archive -Path $gpoZip -DestinationPath $gpoDir -Force
    Write-Host "  Extracted to $gpoDir"
} else {
    Write-Host "  Already extracted"
}

# ============================================
# 2. Copy ADMX Templates
# DC: SYSVOL Central Store (domain-wide)
# MS: Local PolicyDefinitions
# ============================================
Write-Host ""
Write-Host "=== ADMX Templates ==="

if ($isDC) {
    # Use Get-ADDomain for SYSTEM context where $env:USERDNSDOMAIN may be empty
    $dnsDomain = $env:USERDNSDOMAIN
    if (-not $dnsDomain) { $dnsDomain = (Get-ADDomain).DNSRoot }
    $policyDefDir = "\\$dnsDomain\SYSVOL\$dnsDomain\Policies\PolicyDefinitions"
    Write-Host "  Target: SYSVOL Central Store ($dnsDomain)"
} else {
    $policyDefDir = "$env:WINDIR\PolicyDefinitions"
    Write-Host "  Target: Local PolicyDefinitions"
}

$admxSource = "$gpoDir\ADMX Templates"

if (-not (Test-Path $policyDefDir)) {
    New-Item -Path $policyDefDir -ItemType Directory -Force | Out-Null
}
if (-not (Test-Path "$policyDefDir\en-US")) {
    New-Item -Path "$policyDefDir\en-US" -ItemType Directory -Force | Out-Null
}

$admxFolders = Get-ChildItem -Path $admxSource -Directory
foreach ($folder in $admxFolders) {
    $admxFiles = Get-ChildItem -Path $folder.FullName -Filter '*.admx' -ErrorAction SilentlyContinue
    foreach ($admx in $admxFiles) {
        Copy-Item -Path $admx.FullName -Destination $policyDefDir -Force
        $fixed++
    }

    $admlDirs = Get-ChildItem -Path $folder.FullName -Directory | Where-Object { $_.Name -match '^en' }
    foreach ($admlDir in $admlDirs) {
        $admlFiles = Get-ChildItem -Path $admlDir.FullName -Filter '*.adml' -ErrorAction SilentlyContinue
        foreach ($adml in $admlFiles) {
            Copy-Item -Path $adml.FullName -Destination "$policyDefDir\en-US" -Force
            $fixed++
        }
    }

    Write-Host "  $($folder.Name): $($admxFiles.Count) ADMX files"
}

Write-Host "  ADMX templates installed"

# ============================================
# 3. Extract LGPO
# ============================================
Write-Host ""
Write-Host "=== Extracting LGPO ==="

$lgpoZip = "$tempDir\LGPO.zip"
$lgpoDir = "$tempDir\LGPO"

if (Test-Path $lgpoZip) {
    if (-not (Test-Path $lgpoDir)) {
        Expand-Archive -Path $lgpoZip -DestinationPath $lgpoDir -Force
    }
    $lgpoExe = Get-ChildItem -Path $lgpoDir -Filter 'LGPO.exe' -Recurse | Select-Object -First 1
    if ($lgpoExe) {
        Write-Host "  LGPO.exe: $($lgpoExe.FullName)"
    } else {
        Write-Host "  WARNING: LGPO.exe not found in archive"
    }
} else {
    Write-Host "  WARNING: $lgpoZip not found"
}

# ============================================
# 4. Import DISA STIG GPOs into Active Directory (DC only)
# ============================================
if ($isDC) {
    Write-Host ""
    Write-Host "=== Importing DISA STIG GPOs into AD ==="

    Import-Module GroupPolicy
    Import-Module "$PSScriptRoot\lib\StigForge-GPO.psm1" -Force

    $dcOU = 'OU=Domain Controllers,DC=lab,DC=local'
    $msOU = 'OU=Member Servers,DC=lab,DC=local'

    # Ensure Member Servers OU exists
    if (-not ([ADSI]::Exists("LDAP://$msOU"))) {
        Import-Module ActiveDirectory
        New-ADOrganizationalUnit -Name 'Member Servers' -Path 'DC=lab,DC=local' -ProtectedFromAccidentalDeletion $false
        Write-Host "  Created OU: $msOU"
    }

    # GPO backups to import — using original DISA display names (do NOT rename)
    # DC STIG -> Domain Controllers OU only
    # MS STIG -> Member Servers OU only
    # Defender, Firewall, IE11 -> both OUs (cross-linked below)
    $gpoImports = @(
        @{
            Name     = 'DoD WinSvr 2019 DC STIG Comp v3r7'
            BackupId = '{FAF6982B-26E3-4CBB-976C-6B623D8E530B}'
            Path     = "$gpoDir\DoD WinSvr 2019 MS and DC v3r7\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD WinSvr 2019 MS STIG Comp v3r7'
            BackupId = '{7A965217-8441-42E2-A612-4B8A5D49CD53}'
            Path     = "$gpoDir\DoD WinSvr 2019 MS and DC v3r7\GPOs"
            Target   = $msOU
        }
        @{
            Name     = 'DoD Microsoft Defender Antivirus STIG Computer v2r7'
            BackupId = '{893BF0A9-A4EC-4464-B75A-D597414C5A12}'
            Path     = "$gpoDir\DoD Microsoft Defender Antivirus STIG v2r7\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD Windows Defender Firewall STIG v2r2'
            BackupId = '{EB82B913-90A2-4599-A554-90B3A116B382}'
            Path     = "$gpoDir\DoD Windows Defender Firewall v2r2\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD Internet Explorer 11 STIG Computer v2r6'
            BackupId = '{63A768C8-9905-4871-BF57-422A8D83B6D0}'
            Path     = "$gpoDir\DoD Internet Explorer 11 v2r6\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD Internet Explorer 11 STIG User v2r6'
            BackupId = '{75194269-DE5A-403D-A8C1-3DE83516BBAA}'
            Path     = "$gpoDir\DoD Internet Explorer 11 v2r6\GPOs"
            Target   = $dcOU
        }
    )

    foreach ($import in $gpoImports) {
        if (-not (Test-Path $import.Path)) {
            Write-Host "  SKIP: $($import.Name) - backup path not found"
            continue
        }

        # Find backup ID from manifest if not specified
        $backupId = $import.BackupId
        if (-not $backupId) {
            $manifestPath = "$($import.Path)\manifest.xml"
            if (Test-Path $manifestPath) {
                [xml]$manifest = Get-Content $manifestPath
                $ns = New-Object Xml.XmlNamespaceManager($manifest.NameTable)
                $ns.AddNamespace('m', 'http://www.microsoft.com/GroupPolicy/GPOOperations/Manifest')
                $firstBackup = $manifest.SelectSingleNode('//m:BackupInst/m:ID', $ns)
                if ($firstBackup) { $backupId = $firstBackup.InnerText }
            }
            if (-not $backupId) {
                $backupDir = Get-ChildItem -Path $import.Path -Directory | Where-Object { $_.Name -match '^\{' } | Select-Object -First 1
                if ($backupDir) { $backupId = $backupDir.Name }
            }
        }

        if (-not $backupId) {
            Write-Host "  SKIP: $($import.Name) - could not determine backup ID"
            continue
        }

        $gpo = Get-GPO -Name $import.Name -ErrorAction SilentlyContinue
        if (-not $gpo) {
            $gpo = New-GPO -Name $import.Name
            Write-Host "  Created GPO: $($import.Name)"
        }

        try {
            Import-GPO -BackupId $backupId -Path $import.Path -TargetName $import.Name -ErrorAction Stop | Out-Null
            Write-Host "  Imported: $($import.Name) from backup $backupId"
            $fixed++
        } catch {
            Write-Host "  ERROR importing $($import.Name): $($_.Exception.Message)"
        }

        try {
            Ensure-GPOApplyPermission -GpoName $import.Name -Principal 'Authenticated Users'
            Ensure-GPOApplyPermission -GpoName $import.Name -Principal 'Domain Computers'
        } catch {
            Write-Host "  WARN: Could not update security filtering for $($import.Name): $($_.Exception.Message)"
        }

        try {
            Ensure-GPOLink -GpoName $import.Name -Target $import.Target
        } catch {
            Write-Host "  WARN: Could not enforce link for $($import.Name): $($_.Exception.Message)"
        }
    }

    # Cross-link Defender, Firewall, IE11 to Member Servers OU
    # (Primary target above is DC OU; these apply to both roles)
    Write-Host ""
    Write-Host "=== Cross-linking shared DISA GPOs to Member Servers OU ==="
    $sharedGpos = @(
        'DoD Microsoft Defender Antivirus STIG Computer v2r7'
        'DoD Windows Defender Firewall STIG v2r2'
        'DoD Internet Explorer 11 STIG Computer v2r6'
        'DoD Internet Explorer 11 STIG User v2r6'
    )
    foreach ($name in $sharedGpos) {
        $gpo = Get-GPO -Name $name -ErrorAction SilentlyContinue
        if (-not $gpo) {
            Write-Host "  SKIP: $name not found"
            continue
        }
        try {
            Ensure-GPOLink -GpoName $name -Target $msOU
        } catch {
            Write-Host "  WARN: Could not link $name to Member Servers: $($_.Exception.Message)"
        }
    }
}

# ============================================
# 5. Apply Local Policy via LGPO
# DC: uses DC STIG backup
# MS: uses MS STIG backup from Support Files\Local Policies
# ============================================
Write-Host ""
Write-Host "=== Applying Local Policy via LGPO ==="

if ($lgpoExe) {
    if ($isDC) {
        # DC backup path
        $backupPath = "$gpoDir\DoD WinSvr 2019 MS and DC v3r7\GPOs\{FAF6982B-26E3-4CBB-976C-6B623D8E530B}\DomainSysvol\GPO"
        $backupLabel = 'DC STIG'
    } else {
        # MS local policy backup path (from Support Files)
        $msLocalDir = "$gpoDir\Support Files\Local Policies\DoD Windows Server 2019 MS v3r7\GPOs"
        $msBackupDir = Get-ChildItem -Path $msLocalDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\{' } | Select-Object -First 1
        if ($msBackupDir) {
            $backupPath = "$($msBackupDir.FullName)\DomainSysvol\GPO"
        } else {
            # Fallback: use MS backup from main GPOs folder
            $backupPath = "$gpoDir\DoD WinSvr 2019 MS and DC v3r7\GPOs\{7A965217-8441-42E2-A612-4B8A5D49CD53}\DomainSysvol\GPO"
        }
        $backupLabel = 'MS STIG'
    }

    Write-Host "  Using: $backupLabel backup"

    # Apply machine registry.pol
    if (Test-Path "$backupPath\Machine\registry.pol") {
        & $lgpoExe.FullName /m "$backupPath\Machine\registry.pol" 2>&1 | ForEach-Object { Write-Host "  $_" }
        Write-Host "  Applied machine registry.pol"
        $fixed++
    } else {
        Write-Host "  SKIP: registry.pol not found at $backupPath"
    }

    # Apply GptTmpl.inf (security settings)
    $gptTmpl = "$backupPath\Machine\microsoft\windows nt\SecEdit\GptTmpl.inf"
    if (Test-Path $gptTmpl) {
        & $lgpoExe.FullName /s $gptTmpl 2>&1 | ForEach-Object { Write-Host "  $_" }
        Write-Host "  Applied GptTmpl.inf (security settings)"
        $fixed++
    }

    # Apply audit.csv (advanced audit policy)
    $auditCsv = "$backupPath\Machine\microsoft\windows nt\Audit\audit.csv"
    if (Test-Path $auditCsv) {
        & $lgpoExe.FullName /ac $auditCsv 2>&1 | ForEach-Object { Write-Host "  $_" }
        Write-Host "  Applied audit.csv (advanced audit policy)"
        $fixed++
    }
} else {
    Write-Host "  SKIP: LGPO.exe not available"
}

# ============================================
# Summary
# ============================================
Write-Host ""
Write-Host "========================================="
Write-Host "  ${roleName}: ADMX + LGPO fixes applied: $fixed"
Write-Host "========================================="
Write-Host ""
if ($isDC) {
    Write-Host "  Next: gpupdate /force, then run 08-dc01-stig-gpos.ps1"
} else {
    Write-Host "  Next: gpupdate /force, then run local hardening script"
}
