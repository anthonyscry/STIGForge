# STIGForge Lab - Step 0: Install PowerSTIG + DSC Modules + Evaluate-STIG (Offline)
# Run on: Target machine (WS01 or DC01) as Administrator
# Prerequisites: Copy import/ folder contents to C:\temp\ on target machine
#
# Expected layout on target:
#   C:\temp\Evaluate-STIG.zip
#   C:\temp\modules\*.nupkg   (PowerSTIG + 15 DSC dependency packages)
#   C:\temp\dod_certs.zip     (for cert install scripts)
#   C:\temp\eca_certs.zip
#   C:\temp\fbca_crosscert_remover_v118.zip
#   C:\temp\crosscert_*.cer
#
# The import/ folder in the repo is the canonical source for all tools.
# Users update it with latest versions; scripts always install from C:\temp\.
# No internet required.

$tempDir = 'C:\temp'
$modulesDir = 'C:\Program Files\WindowsPowerShell\Modules'

# ============================================
# 1. Install Evaluate-STIG
# ============================================
Write-Host "=== Evaluate-STIG ==="

$esZip = "$tempDir\Evaluate-STIG.zip"
$esTarget = 'C:\Evaluate-STIG'

if (Test-Path $esZip) {
    $esExists = (Test-Path "$esTarget\Evaluate-STIG.ps1") -or (Get-ChildItem $esTarget -Filter 'Evaluate-STIG.ps1' -Recurse -ErrorAction SilentlyContinue)
    if ($esExists) {
        Write-Host "  OK: Evaluate-STIG already installed at $esTarget"
    } else {
        Write-Host "  Installing Evaluate-STIG..."
        if (-not (Test-Path $esTarget)) { New-Item -Path $esTarget -ItemType Directory -Force | Out-Null }
        Expand-Archive -Path $esZip -DestinationPath $esTarget -Force
        $esScript = Get-ChildItem $esTarget -Filter 'Evaluate-STIG.ps1' -Recurse | Select-Object -First 1
        if ($esScript) {
            Write-Host "  -> $($esScript.FullName)"
        } else {
            Write-Host "  WARNING: Evaluate-STIG.ps1 not found after extraction"
        }
    }
} else {
    Write-Host "  SKIP: $esZip not found"
}

# ============================================
# 2. Install PowerSTIG + DSC Module Dependencies
# ============================================
Write-Host ""
Write-Host "=== PowerSTIG + DSC Modules ==="

$nupkgDir = "$tempDir\modules"
if (-not (Test-Path $nupkgDir)) {
    # Fallback: check if nupkgs were placed directly in C:\temp
    $fallbackPkgs = Get-ChildItem -Path $tempDir -Filter '*.nupkg' -ErrorAction SilentlyContinue
    if ($fallbackPkgs.Count -gt 0) {
        Write-Host "NOTE: $nupkgDir not found, but found $($fallbackPkgs.Count) nupkg files in $tempDir - creating modules dir"
        New-Item -Path $nupkgDir -ItemType Directory -Force | Out-Null
        $fallbackPkgs | ForEach-Object { Move-Item $_.FullName $nupkgDir -Force }
    } else {
        Write-Host "ERROR: $nupkgDir not found. Copy import/modules/*.nupkg to C:\temp\modules\ first."
        Write-Host "Skipping module installation."
    }
}
if (Test-Path $nupkgDir) {
    $nupkgs = Get-ChildItem -Path $nupkgDir -Filter '*.nupkg'
    if ($nupkgs.Count -eq 0) {
        Write-Host "ERROR: No .nupkg files found in $nupkgDir"
    } else {
        Write-Host "Found $($nupkgs.Count) module packages"
        Write-Host ""

        foreach ($pkg in $nupkgs) {
            # Parse module name and version from filename (e.g., PowerSTIG.4.29.0.nupkg)
            $baseName = $pkg.BaseName

            # Split on first digit group that looks like a version
            if ($baseName -match '^(.+?)\.(\d+\.\d+.*)$') {
                $moduleName = $Matches[1]
                $moduleVersion = $Matches[2]
            } else {
                Write-Host "  SKIP: Cannot parse $($pkg.Name)"
                continue
            }

            $targetDir = "$modulesDir\$moduleName\$moduleVersion"

            # Check both 3-part and 4-part version directories (psd1 may use 4-part)
            if (Test-Path $targetDir) {
                Write-Host "  OK: $moduleName $moduleVersion (already installed)"
                continue
            }
            $targetDir4 = "$modulesDir\$moduleName\${moduleVersion}.0"
            if (Test-Path $targetDir4) {
                Write-Host "  OK: $moduleName ${moduleVersion}.0 (already installed)"
                continue
            }

            Write-Host "  Installing $moduleName $moduleVersion..."

            # Create temp extraction directory
            $extractDir = "$env:TEMP\nupkg_$moduleName"
            if (Test-Path $extractDir) { Remove-Item -Path $extractDir -Recurse -Force }

            # nupkg is a zip file — rename to .zip for Expand-Archive compatibility
            $zipCopy = "$env:TEMP\$($pkg.BaseName).zip"
            Copy-Item $pkg.FullName $zipCopy -Force
            try {
                Expand-Archive -Path $zipCopy -DestinationPath $extractDir -Force
            } catch {
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                [System.IO.Compression.ZipFile]::ExtractToDirectory($zipCopy, $extractDir)
            }
            Remove-Item $zipCopy -Force -ErrorAction SilentlyContinue

            # Create target module directory
            New-Item -Path $targetDir -ItemType Directory -Force | Out-Null

            # Copy module contents (skip NuGet metadata files)
            Get-ChildItem -Path $extractDir | Where-Object {
                $_.Name -notin @('_rels', 'package') -and
                $_.Name -ne '[Content_Types].xml' -and
                $_.Extension -ne '.nuspec'
            } | ForEach-Object {
                Copy-Item -Path $_.FullName -Destination $targetDir -Recurse -Force
            }

            # Clean up temp
            Remove-Item -Path $extractDir -Recurse -Force

            # Fix version mismatch: psd1 may declare 4-part version (e.g., 1.4.0.0)
            # but nupkg filename only has 3-part (e.g., 1.4.0). Directory must match psd1.
            $psd1File = Get-ChildItem $targetDir -Filter '*.psd1' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($psd1File) {
                try {
                    $manifest = Import-PowerShellDataFile $psd1File.FullName -ErrorAction Stop
                    $psd1Version = $manifest.ModuleVersion
                    if ($psd1Version -and $psd1Version -ne $moduleVersion) {
                        $correctDir = "$modulesDir\$moduleName\$psd1Version"
                        if (-not (Test-Path $correctDir)) {
                            Rename-Item $targetDir $psd1Version
                            $targetDir = $correctDir
                            Write-Host "    (dir renamed $moduleVersion -> $psd1Version)"
                        }
                    }
                } catch {
                    # Non-fatal: module may still work
                }
            }

            if (Test-Path "$targetDir\*.psd1") {
                Write-Host "    -> $targetDir"
            } else {
                Write-Host "    -> $targetDir (verify manifest)"
            }
        }
    }
}

# ============================================
# 3. Verify Installation
# ============================================
Write-Host ""
Write-Host "=== Verification ==="

$requiredModules = @(
    'PowerSTIG', 'SecurityPolicyDsc', 'AuditPolicyDsc', 'AuditSystemDsc',
    'CertificateDsc', 'WindowsDefenderDsc', 'GPRegistryPolicyDsc',
    'ComputerManagementDsc', 'AccessControlDsc', 'FileContentDsc',
    'PSDscResources', 'xDnsServer'
)

$allGood = $true
foreach ($mod in $requiredModules) {
    # Check module directory directly (more reliable than Get-Module in remote sessions)
    $modDirs = Get-ChildItem "$modulesDir\$mod" -Directory -ErrorAction SilentlyContinue
    if ($modDirs) {
        $versions = ($modDirs | Select-Object -ExpandProperty Name) -join ', '
        Write-Host "  [OK] $mod ($versions)"
    } else {
        Write-Host "  [MISSING] $mod"
        $allGood = $false
    }
}

# Check Evaluate-STIG
$esScript = Get-ChildItem 'C:\Evaluate-STIG' -Filter 'Evaluate-STIG.ps1' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($esScript) {
    Write-Host "  [OK] Evaluate-STIG at $($esScript.FullName)"
} else {
    Write-Host "  [MISSING] Evaluate-STIG"
    $allGood = $false
}

Write-Host ""
if ($allGood) {
    Write-Host "All tools installed. Ready for STIG hardening."
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  WS01: Run 03-ws01-dsc-hardening.ps1"
    Write-Host "  DC01: Run 07-dc01-dsc-hardening.ps1"
} else {
    Write-Host "WARNING: Some components missing. Check output above."
}
