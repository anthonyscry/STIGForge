# StigForge-Certificates.psm1 — Certificate installation for STIG compliance
# Import: Import-Module "$PSScriptRoot\..\lib\StigForge-Certificates.psm1" -Force

$script:CrossCerts = @(
    @{ file = 'crosscert_irca2_rootca3_49CBE933.cer'; thumb = '49CBE933151872E17C8EAE7F0ABA97FB610F6477'; desc = 'IRCA2>DoD Root CA 3 (exp 11/2024)' }
    @{ file = 'crosscert_cceb2_rootca3_9B749645.cer'; thumb = '9B74964506C7ED9138070D08D5F8B969866560C8'; desc = 'CCEB2>DoD Root CA 3 (exp 7/2025)' }
    @{ file = 'crosscert_cceb2_rootca6_D471CA32.cer'; thumb = 'D471CA32F7A692CE6CBB6196BD3377FE4DBCD106'; desc = 'CCEB2>DoD Root CA 6 (exp 7/2026)' }
)

$script:StigThumbprints = @{
    '49CBE933151872E17C8EAE7F0ABA97FB610F6477' = 'V-253429: IRCA2>DoD Root CA 3'
    'AC06108CA348CC03B53795C64BF84403C1DBD341' = 'V-253429: IRCA2>DoD Root CA 3 (older)'
    '9B74964506C7ED9138070D08D5F8B969866560C8' = 'V-253430: CCEB2>DoD Root CA 3'
    'D471CA32F7A692CE6CBB6196BD3377FE4DBCD106' = 'V-253430: CCEB2>DoD Root CA 6'
}

# ============================================
# Install-DoDCertificates — Import DoD Root CA from zip
# ============================================
function Install-DoDCertificates {
    param(
        [string]$TempDir = 'C:\temp',
        [switch]$MoveRootsFromCA   # WS01 needs this; DC01 does not
    )

    $fixed = 0
    Write-Host "=== DoD Root CA Certificates ==="

    $dodZip = "$TempDir\dod_certs.zip"
    $dodExtract = "$TempDir\dod_certs"
    if (-not (Test-Path $dodZip)) {
        Write-Host "  SKIP: $dodZip not found"
        return $fixed
    }

    if (Test-Path $dodExtract) { Remove-Item $dodExtract -Recurse -Force }
    Expand-Archive -Path $dodZip -DestinationPath $dodExtract -Force

    if ($MoveRootsFromCA) {
        # Full chain bundles to CA (intermediate) store first
        $fullP7b = Get-ChildItem $dodExtract -Filter '*.der.p7b' -Recurse |
            Where-Object { $_.Name -notmatch 'Root_CA_\d' }
        foreach ($p7b in $fullP7b) {
            Write-Host "  Importing $($p7b.Name) to CA store..."
            certutil -addstore CA $p7b.FullName 2>&1 | Select-String 'added|already' | ForEach-Object { Write-Host "    $_" }
        }

        # Individual root CA files
        $rootP7bs = Get-ChildItem $dodExtract -Filter '*Root_CA_*.der.p7b' -Recurse
        foreach ($p7b in $rootP7bs) {
            Write-Host "  Importing $($p7b.Name) to Root store..."
            certutil -addstore Root $p7b.FullName 2>&1 | Select-String 'added|already|error' | ForEach-Object { Write-Host "    $_" }
        }

        # Move any root CAs that ended up in CA store
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
        # DC/MS: import all .p7b files directly to Root
        $p7bFiles = Get-ChildItem -Path $dodExtract -Filter '*.p7b' -Recurse
        foreach ($p7b in $p7bFiles) {
            certutil -addstore Root "$($p7b.FullName)" 2>&1 | Out-Null
            Write-Host "  Imported: $($p7b.Name)"
            $fixed++
        }
    }

    return $fixed
}

# ============================================
# Install-ECACertificates — Import ECA Root CA from zip
# ============================================
function Install-ECACertificates {
    param(
        [string]$TempDir = 'C:\temp',
        [switch]$MoveRootsFromCA
    )

    $fixed = 0
    Write-Host ""
    Write-Host "=== ECA Root CA Certificates ==="

    $ecaZip = "$TempDir\eca_certs.zip"
    $ecaExtract = "$TempDir\eca_certs"
    if (-not (Test-Path $ecaZip)) {
        Write-Host "  SKIP: $ecaZip not found"
        return $fixed
    }

    if (Test-Path $ecaExtract) { Remove-Item $ecaExtract -Recurse -Force }
    Expand-Archive -Path $ecaZip -DestinationPath $ecaExtract -Force

    if ($MoveRootsFromCA) {
        # Chain bundles to CA store
        $fullP7b = Get-ChildItem $ecaExtract -Filter '*_der.p7b' -Recurse |
            Where-Object { $_.Name -notmatch 'Root_CA_\d' }
        foreach ($p7b in $fullP7b) {
            Write-Host "  Importing $($p7b.Name) to CA store..."
            certutil -addstore CA $p7b.FullName 2>&1 | Select-String 'added|already' | ForEach-Object { Write-Host "    $_" }
        }

        $rootP7bs = Get-ChildItem $ecaExtract -Filter '*Root_CA_*_der.p7b' -Recurse
        foreach ($p7b in $rootP7bs) {
            Write-Host "  Importing $($p7b.Name) to Root store..."
            certutil -addstore Root $p7b.FullName 2>&1 | Select-String 'added|already|error' | ForEach-Object { Write-Host "    $_" }
        }

        # Move ECA roots from CA store
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
        $p7bFiles = Get-ChildItem -Path $ecaExtract -Filter '*.p7b' -Recurse
        foreach ($p7b in $p7bFiles) {
            certutil -addstore Root "$($p7b.FullName)" 2>&1 | Out-Null
            Write-Host "  Imported: $($p7b.Name)"
            $fixed++
        }
    }

    return $fixed
}

# ============================================
# Install-FBCACrossCertRemover — Run FBCA tool
# ============================================
function Install-FBCACrossCertRemover {
    param([string]$TempDir = 'C:\temp')

    $fixed = 0
    Write-Host ""
    Write-Host "=== FBCA Cross-Certificate Remover ==="

    $fbcaZip = "$TempDir\fbca_crosscert_remover_v118.zip"
    if (-not (Test-Path $fbcaZip)) {
        Write-Host "  SKIP: $fbcaZip not found"
        return $fixed
    }

    $fbcaExtract = "$TempDir\fbca_crosscert_remover"
    if (Test-Path $fbcaExtract) { Remove-Item $fbcaExtract -Recurse -Force }
    Expand-Archive -Path $fbcaZip -DestinationPath $fbcaExtract -Force

    $exe = Get-ChildItem $fbcaExtract -Filter 'FBCA_crosscert_remover*.exe' -Recurse | Select-Object -First 1
    if ($exe) {
        Write-Host "  Running $($exe.Name) /silent /eca ..."
        $proc = Start-Process $exe.FullName -ArgumentList '/silent', '/eca' -Wait -PassThru -NoNewWindow
        Write-Host "  Exit code: $($proc.ExitCode)"
        $fixed++

        # Show Disallowed store
        $disallowed = Get-ChildItem Cert:\LocalMachine\Disallowed -ErrorAction SilentlyContinue
        if ($disallowed) {
            Write-Host "  Disallowed store: $($disallowed.Count) certificates"
        }
    } else {
        Write-Host "  ERR: FBCA exe not found in zip"
    }

    return $fixed
}

# ============================================
# Import-CrossCertificates — Import IRCA2/CCEB2 cross-certs to Disallowed
# ============================================
function Import-CrossCertificates {
    param([string]$TempDir = 'C:\temp')

    $fixed = 0
    Write-Host ""
    Write-Host "=== Importing STIG-required cross-certs to Disallowed ==="

    foreach ($c in $script:CrossCerts) {
        $filePath = "$TempDir\$($c.file)"
        $existing = Get-ChildItem Cert:\LocalMachine\Disallowed -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $c.thumb }
        if ($existing) {
            Write-Host "  [OK] $($c.desc) - already in Disallowed"
        } elseif (Test-Path $filePath) {
            Write-Host "  Importing $($c.desc)..."
            certutil -addstore Disallowed $filePath 2>&1 | Select-String 'added|already|error' | ForEach-Object { Write-Host "    $_" }
            $fixed++
        } else {
            Write-Host "  [SKIP] $filePath not found"
        }
    }

    return $fixed
}

# ============================================
# Install-InstallRootMSI — Install DoD InstallRoot PKI tool
# ============================================
function Install-InstallRootMSI {
    param([string]$TempDir = 'C:\temp')

    $fixed = 0
    Write-Host ""
    Write-Host "=== InstallRoot PKI Tool ==="

    $msiPath = Get-ChildItem "$TempDir" -Filter 'InstallRoot_*.msi' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $msiPath) {
        Write-Host "  SKIP: InstallRoot MSI not found in $TempDir"
        return $fixed
    }

    # Check if already installed
    $installed = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match 'InstallRoot' }
    if ($installed) {
        Write-Host "  [OK] InstallRoot already installed: $($installed.DisplayName)"
        return $fixed
    }

    Write-Host "  Installing $($msiPath.Name)..."
    $proc = Start-Process 'msiexec.exe' -ArgumentList "/i `"$($msiPath.FullName)`" /quiet /norestart" -Wait -PassThru -NoNewWindow
    if ($proc.ExitCode -eq 0) {
        Write-Host "  InstallRoot installed successfully"
        $fixed++
    } else {
        Write-Host "  InstallRoot install exit code: $($proc.ExitCode)"
    }

    return $fixed
}

# ============================================
# Test-CertCompliance — Verify all STIG-required certs are present
# ============================================
function Test-CertCompliance {
    Write-Host ""
    Write-Host "=== Certificate Compliance Verification ==="

    # DoD Root CAs
    Write-Host "--- DoD Root CAs ---"
    foreach ($ca in @('DoD Root CA 3', 'DoD Root CA 4', 'DoD Root CA 5', 'DoD Root CA 6')) {
        $cert = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like "*$ca*" }
        if ($cert) {
            Write-Host "  [OK] $ca (Exp: $($cert.NotAfter.ToString('yyyy-MM-dd')))"
        } else {
            Write-Host "  [MISSING] $ca"
        }
    }

    # ECA Root CAs
    Write-Host "--- ECA Root CAs ---"
    Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -match 'ECA Root CA' } |
        Sort-Object Subject | ForEach-Object {
            Write-Host "  [OK] $($_.Subject) (Exp: $($_.NotAfter.ToString('yyyy-MM-dd')))"
        }

    # Cross-certs in Disallowed
    Write-Host "--- STIG Cross-cert Thumbprint Check ---"
    foreach ($kv in $script:StigThumbprints.GetEnumerator()) {
        $cert = Get-ChildItem Cert:\LocalMachine\Disallowed -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $kv.Key }
        if ($cert) {
            Write-Host "  [OK] $($kv.Key.Substring(0,8))... ($($kv.Value))"
        } else {
            Write-Host "  [MISSING] $($kv.Key.Substring(0,8))... ($($kv.Value))"
        }
    }
}

# ============================================
# Install-AllCertificates — One-call wrapper for full cert installation
# ============================================
function Install-AllCertificates {
    param(
        [string]$TempDir = 'C:\temp',
        [switch]$IsWorkstation   # WS01 needs MoveRootsFromCA logic
    )

    $total = 0
    $total += Install-DoDCertificates -TempDir $TempDir -MoveRootsFromCA:$IsWorkstation
    $total += Install-ECACertificates -TempDir $TempDir -MoveRootsFromCA:$IsWorkstation
    $total += Install-InstallRootMSI -TempDir $TempDir
    $total += Install-FBCACrossCertRemover -TempDir $TempDir
    $total += Import-CrossCertificates -TempDir $TempDir
    Test-CertCompliance

    Write-Host ""
    Write-Host "  Total certificate fixes applied: $total"
    return $total
}

Export-ModuleMember -Function @(
    'Install-DoDCertificates', 'Install-ECACertificates',
    'Install-FBCACrossCertRemover', 'Import-CrossCertificates',
    'Install-InstallRootMSI', 'Test-CertCompliance', 'Install-AllCertificates'
)
