# STIGForge Lab - Step 10: Install DoD & ECA Certificates on DC01
# Run on: DC01 (lab.local\Administrator)
# Prerequisites: Copy cert files to C:\temp\ on DC01
#   - dod_certs.zip (DoD Root CA 3/4/5/6 PKCS7 bundles)
#   - eca_certs.zip (ECA Root CA 4/5 PKCS7 bundles)
#   - fbca_crosscert_remover_v118.zip (FBCA Cross-Certificate Remover)
#   - crosscert_irca2_rootca3_49CBE933.cer
#   - crosscert_cceb2_rootca3_9B749645.cer
#   - crosscert_cceb2_rootca6_D471CA32.cer
#
# Same approach as WS01 cert install (05-ws01-install-certs.ps1)

$tempDir = 'C:\temp'
$fixed = 0

# ============================================
# DoD Root CA Certificates
# ============================================
Write-Host "--- DoD Root CA Certificates ---"

$dodZip = "$tempDir\dod_certs.zip"
if (Test-Path $dodZip) {
    $dodExtract = "$tempDir\dod_certs"
    if (-not (Test-Path $dodExtract)) {
        Expand-Archive -Path $dodZip -DestinationPath $dodExtract -Force
    }

    # Import all .p7b files to Trusted Root
    $p7bFiles = Get-ChildItem -Path $dodExtract -Filter '*.p7b' -Recurse
    foreach ($p7b in $p7bFiles) {
        certutil -addstore Root "$($p7b.FullName)" 2>&1 | Out-Null
        Write-Host "  Imported: $($p7b.Name)"
        $fixed++
    }
} else {
    Write-Host "  SKIP: $dodZip not found"
}

# ============================================
# ECA Root CA Certificates
# ============================================
Write-Host ""
Write-Host "--- ECA Root CA Certificates ---"

$ecaZip = "$tempDir\eca_certs.zip"
if (Test-Path $ecaZip) {
    $ecaExtract = "$tempDir\eca_certs"
    if (-not (Test-Path $ecaExtract)) {
        Expand-Archive -Path $ecaZip -DestinationPath $ecaExtract -Force
    }

    $p7bFiles = Get-ChildItem -Path $ecaExtract -Filter '*.p7b' -Recurse
    foreach ($p7b in $p7bFiles) {
        certutil -addstore Root "$($p7b.FullName)" 2>&1 | Out-Null
        Write-Host "  Imported: $($p7b.Name)"
        $fixed++
    }
} else {
    Write-Host "  SKIP: $ecaZip not found"
}

# ============================================
# FBCA Cross-Certificate Remover
# ============================================
Write-Host ""
Write-Host "--- FBCA Cross-Certificate Remover ---"

$fbcaZip = "$tempDir\fbca_crosscert_remover_v118.zip"
if (Test-Path $fbcaZip) {
    $fbcaExtract = "$tempDir\fbca_remover"
    if (-not (Test-Path $fbcaExtract)) {
        Expand-Archive -Path $fbcaZip -DestinationPath $fbcaExtract -Force
    }

    $fbcaExe = Get-ChildItem -Path $fbcaExtract -Filter '*.exe' -Recurse | Select-Object -First 1
    if ($fbcaExe) {
        $result = Start-Process -FilePath $fbcaExe.FullName -ArgumentList '/silent' -Wait -PassThru
        Write-Host "  FBCA remover exit code: $($result.ExitCode)"
        $fixed++
    }
} else {
    Write-Host "  SKIP: $fbcaZip not found"
}

# ============================================
# Cross-Certificates (Untrusted/Disallowed Store)
# V-205649: DoD IRCA cross-certs
# V-205650: CCEB cross-certs
# ============================================
Write-Host ""
Write-Host "--- Cross-Certificates (Untrusted Store) ---"

$crossCerts = @(
    @{ file = "$tempDir\crosscert_irca2_rootca3_49CBE933.cer"; thumb = '49CBE933151872E17C8EAE7F0ABA97FB610F6477'; desc = 'IRCA2>DoD Root CA 3 (exp 11/2024)' }
    @{ file = "$tempDir\crosscert_cceb2_rootca3_9B749645.cer"; thumb = '9B74964506C7ED9138070D08D5F8B969866560C8'; desc = 'CCEB2>DoD Root CA 3 (exp 7/2025)' }
    @{ file = "$tempDir\crosscert_cceb2_rootca6_D471CA32.cer"; thumb = 'D471CA32F7A692CE6CBB6196BD3377FE4DBCD106'; desc = 'CCEB2>DoD Root CA 6 (exp 7/2026)' }
)

foreach ($cc in $crossCerts) {
    if (Test-Path $cc.file) {
        # Check if already in Disallowed store
        $existing = Get-ChildItem Cert:\LocalMachine\Disallowed | Where-Object { $_.Thumbprint -eq $cc.thumb }
        if ($existing) {
            Write-Host "  Already present: $($cc.desc)"
        } else {
            certutil -addstore Disallowed "$($cc.file)" 2>&1 | Out-Null
            Write-Host "  Imported to Disallowed: $($cc.desc)"
            $fixed++
        }
    } else {
        Write-Host "  SKIP: $($cc.file) not found"
    }
}

# ============================================
# Verification
# ============================================
Write-Host ""
Write-Host "--- Verification ---"

# Check DoD Root CAs (V-205648)
$dodRoots = @('DoD Root CA 3','DoD Root CA 4','DoD Root CA 5','DoD Root CA 6')
foreach ($ca in $dodRoots) {
    $cert = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like "*$ca*" }
    if ($cert) {
        Write-Host "  [OK] $ca in Trusted Root"
    } else {
        Write-Host "  [MISSING] $ca not in Trusted Root"
    }
}

# Check cross-certs in Disallowed (V-205649, V-205650)
$requiredThumbs = @(
    @{ thumb = '49CBE933151872E17C8EAE7F0ABA97FB610F6477'; desc = 'IRCA2>DoD Root CA 3' }
    @{ thumb = '9B74964506C7ED9138070D08D5F8B969866560C8'; desc = 'CCEB2>DoD Root CA 3' }
    @{ thumb = 'D471CA32F7A692CE6CBB6196BD3377FE4DBCD106'; desc = 'CCEB2>DoD Root CA 6' }
)
foreach ($rt in $requiredThumbs) {
    $cert = Get-ChildItem Cert:\LocalMachine\Disallowed | Where-Object { $_.Thumbprint -eq $rt.thumb }
    if ($cert) {
        Write-Host "  [OK] $($rt.desc) in Disallowed ($($rt.thumb.Substring(0,8)))"
    } else {
        Write-Host "  [MISSING] $($rt.desc) not in Disallowed"
    }
}

Write-Host ""
Write-Host "========================================="
Write-Host "  Certificate fixes applied: $fixed"
Write-Host "========================================="
