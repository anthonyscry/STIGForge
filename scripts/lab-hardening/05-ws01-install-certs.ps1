# STIGForge Lab - Step 5: Install DoD & ECA Root Certificates
# Run on: WS01 (.\Install or domain admin)
# Prerequisites: dod_certs.zip and eca_certs.zip copied to C:\temp\
#   (from scripts/lab-hardening/import/ on build host)
#
# Covers STIG findings:
#   V-253425: DoD Root CA 3 in Trusted Root Store
#   V-253426: DoD Root CA 4 in Trusted Root Store
#   V-253427: DoD Root CA 5 in Trusted Root Store
#   V-253428: DoD Root CA 6 in Trusted Root Store
#   V-253431: ECA Root CA 4 in Trusted Root Store
#   V-253432: ECA Root CA 5 in Trusted Root Store
#
# Also covers (via FBCA Cross-Certificate Remover):
#   V-253429: DoD Interoperability cross-certs in Untrusted Store
#   V-253430: CCEB cross-certs in Untrusted Store
#
# Prerequisites: Copy from scripts/lab-hardening/import/ to C:\temp\ on WS01:
#   - dod_certs.zip, eca_certs.zip, fbca_crosscert_remover_v118.zip
#   - crosscert_irca2_rootca3_49CBE933.cer
#   - crosscert_cceb2_rootca3_9B749645.cer
#   - crosscert_cceb2_rootca6_D471CA32.cer

$fixed = 0

# ============================================
# Import DoD Root CA Certificates
# ============================================
Write-Host "=== DoD Root CA Certificates ==="

$dodZip = 'C:\temp\dod_certs.zip'
$dodExtract = 'C:\temp\dod_certs'
if (Test-Path $dodZip) {
    if (Test-Path $dodExtract) { Remove-Item $dodExtract -Recurse -Force }
    Expand-Archive -Path $dodZip -DestinationPath $dodExtract -Force

    # Import full chain bundles to CA (intermediate) store first
    $fullP7b = Get-ChildItem $dodExtract -Filter '*.der.p7b' -Recurse |
        Where-Object { $_.Name -notmatch 'Root_CA_\d' }
    foreach ($p7b in $fullP7b) {
        Write-Host "  Importing $($p7b.Name) to CA store..."
        certutil -addstore CA $p7b.FullName 2>&1 | Select-String 'added|already' | ForEach-Object { Write-Host "    $_" }
    }

    # Import individual root CA P7B files
    $rootP7bs = Get-ChildItem $dodExtract -Filter '*Root_CA_*.der.p7b' -Recurse
    foreach ($p7b in $rootP7bs) {
        Write-Host "  Importing $($p7b.Name) to Root store..."
        certutil -addstore Root $p7b.FullName 2>&1 | Select-String 'added|already|error' | ForEach-Object { Write-Host "    $_" }
    }

    # Extract any root CAs that ended up in CA store and move to Root store
    $dodRoots = Get-ChildItem Cert:\LocalMachine\CA | Where-Object {
        $_.Subject -eq $_.Issuer -and $_.Subject -match 'DoD Root CA'
    }
    foreach ($cert in $dodRoots) {
        Write-Host "  Moving root from CA to Root store: $($cert.Subject)"
        $certPath = "$env:TEMP\dod_root_$($cert.Thumbprint).cer"
        [IO.File]::WriteAllBytes($certPath, $cert.RawData)
        certutil -addstore Root $certPath 2>&1 | Select-String 'added|already' | ForEach-Object { Write-Host "    $_" }
        Remove-Item $certPath -ErrorAction SilentlyContinue
        $fixed++
    }
} else {
    Write-Host "  SKIP: $dodZip not found"
    Write-Host "  Copy dod_certs.zip from scripts/lab-hardening/import/ to C:\temp\"
}

# ============================================
# Import ECA Root CA Certificates
# ============================================
Write-Host ""
Write-Host "=== ECA Root CA Certificates ==="

$ecaZip = 'C:\temp\eca_certs.zip'
$ecaExtract = 'C:\temp\eca_certs'
if (Test-Path $ecaZip) {
    if (Test-Path $ecaExtract) { Remove-Item $ecaExtract -Recurse -Force }
    Expand-Archive -Path $ecaZip -DestinationPath $ecaExtract -Force

    # Import full chain bundles to CA store
    $fullP7b = Get-ChildItem $ecaExtract -Filter '*_der.p7b' -Recurse |
        Where-Object { $_.Name -notmatch 'Root_CA_\d' }
    foreach ($p7b in $fullP7b) {
        Write-Host "  Importing $($p7b.Name) to CA store..."
        certutil -addstore CA $p7b.FullName 2>&1 | Select-String 'added|already' | ForEach-Object { Write-Host "    $_" }
    }

    # Import individual root CA P7B files
    $rootP7bs = Get-ChildItem $ecaExtract -Filter '*Root_CA_*_der.p7b' -Recurse
    foreach ($p7b in $rootP7bs) {
        Write-Host "  Importing $($p7b.Name) to Root store..."
        certutil -addstore Root $p7b.FullName 2>&1 | Select-String 'added|already|error' | ForEach-Object { Write-Host "    $_" }
    }

    # Extract any ECA root CAs from CA store and move to Root store
    $ecaRoots = Get-ChildItem Cert:\LocalMachine\CA | Where-Object {
        $_.Subject -eq $_.Issuer -and $_.Subject -match 'ECA Root CA'
    }
    foreach ($cert in $ecaRoots) {
        Write-Host "  Moving root from CA to Root store: $($cert.Subject)"
        $certPath = "$env:TEMP\eca_root_$($cert.Thumbprint).cer"
        [IO.File]::WriteAllBytes($certPath, $cert.RawData)
        certutil -addstore Root $certPath 2>&1 | Select-String 'added|already' | ForEach-Object { Write-Host "    $_" }
        Remove-Item $certPath -ErrorAction SilentlyContinue
        $fixed++
    }
} else {
    Write-Host "  SKIP: $ecaZip not found"
    Write-Host "  Copy eca_certs.zip from scripts/lab-hardening/import/ to C:\temp\"
}

# ============================================
# Verification
# ============================================
Write-Host ""
Write-Host "=== Verification ==="

Write-Host "--- DoD Root CAs in Trusted Root Store ---"
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match 'DoD Root CA' } |
    Sort-Object Subject | ForEach-Object {
        Write-Host "  $($_.Subject) (Exp: $($_.NotAfter.ToString('yyyy-MM-dd')))"
    }

Write-Host ""
Write-Host "--- ECA Root CAs in Trusted Root Store ---"
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match 'ECA Root CA' } |
    Sort-Object Subject | ForEach-Object {
        Write-Host "  $($_.Subject) (Exp: $($_.NotAfter.ToString('yyyy-MM-dd')))"
    }

Write-Host ""
Write-Host "--- DoD Intermediate CAs (count) ---"
$intCerts = Get-ChildItem Cert:\LocalMachine\CA | Where-Object { $_.Subject -match 'DoD' }
Write-Host "  Count: $($intCerts.Count)"

# ============================================
# FBCA Cross-Certificate Remover (V-253429/V-253430)
# Moves DoD Interoperability & CCEB cross-certs to Untrusted store
# ============================================
Write-Host ""
Write-Host "=== FBCA Cross-Certificate Remover ==="

$fbcaZip = 'C:\temp\fbca_crosscert_remover_v118.zip'
$fbcaExtract = 'C:\temp\fbca_crosscert_remover'
if (Test-Path $fbcaZip) {
    if (Test-Path $fbcaExtract) { Remove-Item $fbcaExtract -Recurse -Force }
    Expand-Archive -Path $fbcaZip -DestinationPath $fbcaExtract -Force

    $exe = Get-ChildItem $fbcaExtract -Filter 'FBCA_crosscert_remover.exe' -Recurse | Select-Object -First 1
    if ($exe) {
        # Run with /eca to also handle ECA cross-certs, /silent for unattended
        Write-Host "  Running $($exe.Name) /silent /eca ..."
        $proc = Start-Process $exe.FullName -ArgumentList '/silent','/eca' -Wait -PassThru -NoNewWindow
        Write-Host "  Exit code: $($proc.ExitCode)"

        # Verify Untrusted Certificates store
        Write-Host ""
        Write-Host "--- Untrusted Certificates (Disallowed) Store ---"
        $disallowed = Get-ChildItem Cert:\LocalMachine\Disallowed -ErrorAction SilentlyContinue
        if ($disallowed) {
            foreach ($cert in $disallowed) {
                Write-Host "  $($cert.Subject)"
            }
            Write-Host "  Total: $($disallowed.Count) certificates"
        } else {
            Write-Host "  (empty - cross-certs may not have been present to move)"
        }
        $fixed++
    } else {
        Write-Host "  ERR: FBCA_crosscert_remover.exe not found in zip"
    }
} else {
    Write-Host "  SKIP: $fbcaZip not found"
    Write-Host "  Copy fbca_crosscert_remover_v118.zip from scripts/lab-hardening/import/ to C:\temp\"
}

# ============================================
# Import specific cross-certs required by STIG
# (FBCA tool handles Federal Bridge certs but not
#  these IRCA2/CCEB2 -> DoD Root CA cross-certs)
# ============================================
Write-Host ""
Write-Host "=== Importing STIG-required cross-certs to Disallowed ==="

$crossCerts = @(
    @{ file = 'C:\temp\crosscert_irca2_rootca3_49CBE933.cer'; thumb = '49CBE933151872E17C8EAE7F0ABA97FB610F6477'; desc = 'IRCA2>DoD Root CA 3 (exp 11/2024)' }
    @{ file = 'C:\temp\crosscert_cceb2_rootca3_9B749645.cer'; thumb = '9B74964506C7ED9138070D08D5F8B969866560C8'; desc = 'CCEB2>DoD Root CA 3 (exp 7/2025)' }
    @{ file = 'C:\temp\crosscert_cceb2_rootca6_D471CA32.cer'; thumb = 'D471CA32F7A692CE6CBB6196BD3377FE4DBCD106'; desc = 'CCEB2>DoD Root CA 6 (exp 7/2026)' }
)

foreach ($c in $crossCerts) {
    $existing = Get-ChildItem Cert:\LocalMachine\Disallowed -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $c.thumb }
    if ($existing) {
        Write-Host "  [OK] $($c.desc) - already in Disallowed"
    } elseif (Test-Path $c.file) {
        Write-Host "  Importing $($c.desc)..."
        certutil -addstore Disallowed $c.file 2>&1 | Select-String 'added|already|error' | ForEach-Object { Write-Host "    $_" }
        $fixed++
    } else {
        Write-Host "  [SKIP] $($c.file) not found"
        Write-Host "    Copy from scripts/lab-hardening/import/ to C:\temp\"
    }
}

# ============================================
# Final Verification
# ============================================
Write-Host ""
Write-Host "=== Final Verification ==="

Write-Host "  Certs imported/fixed: $fixed"

# Verify STIG-required cross-cert thumbprints
Write-Host ""
Write-Host "--- STIG Cross-cert Thumbprint Check ---"
$stigCerts = @{
    '49CBE933151872E17C8EAE7F0ABA97FB610F6477' = 'V-253429: IRCA2>DoD Root CA 3'
    'AC06108CA348CC03B53795C64BF84403C1DBD341' = 'V-253429: IRCA2>DoD Root CA 3 (older)'
    '9B74964506C7ED9138070D08D5F8B969866560C8' = 'V-253430: CCEB2>DoD Root CA 3'
    'D471CA32F7A692CE6CBB6196BD3377FE4DBCD106' = 'V-253430: CCEB2>DoD Root CA 6'
}
foreach ($kv in $stigCerts.GetEnumerator()) {
    $cert = Get-ChildItem Cert:\LocalMachine\Disallowed -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $kv.Key }
    if ($cert) {
        Write-Host "  [OK] $($kv.Key) ($($kv.Value))"
    } else {
        Write-Host "  [MISSING] $($kv.Key) ($($kv.Value))"
    }
}
