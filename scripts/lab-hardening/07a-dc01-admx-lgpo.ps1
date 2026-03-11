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
$productType = (Get-CimInstance Win32_OperatingSystem).ProductType
$isDC = ($productType -eq 2)
$roleName = if ($isDC) { 'Domain Controller' } else { 'Member Server' }

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

    $dcOU = 'OU=Domain Controllers,DC=lab,DC=local'

    # GPO backups to import as AD domain GPOs
    $gpoImports = @(
        @{
            Name     = 'DoD-WinSvr2019-DC'
            BackupId = '{FAF6982B-26E3-4CBB-976C-6B623D8E530B}'
            Path     = "$gpoDir\DoD WinSvr 2019 MS and DC v3r7\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD-Defender'
            BackupId = $null
            Path     = "$gpoDir\DoD Microsoft Defender Antivirus STIG v2r7\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD-Firewall'
            BackupId = $null
            Path     = "$gpoDir\DoD Windows Defender Firewall v2r2\GPOs"
            Target   = $dcOU
        }
        @{
            Name     = 'DoD-IE11'
            BackupId = $null
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

        $existingLink = (Get-GPInheritance -Target $import.Target).GpoLinks | Where-Object { $_.DisplayName -eq $import.Name }
        if (-not $existingLink) {
            try {
                $gpo | New-GPLink -Target $import.Target -LinkEnabled Yes -ErrorAction Stop | Out-Null
                Write-Host "  Linked: $($import.Name) -> $($import.Target)"
            } catch {
                Write-Host "  WARN: Could not link $($import.Name): $($_.Exception.Message)"
            }
        } else {
            Write-Host "  Already linked: $($import.Name)"
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
