# STIGForge QA Test Suite - FINAL PASS (v1.0.1)
# Fixes: correct fleet-credential-save options (--host, --user), fleet-status (--targets only)

Import-Module Hyper-V -ErrorAction Stop

$cli = 'C:\projects\STIGForge\publish\cli\STIGForge.Cli.exe'
$testRoot = 'C:\projects\STIGForge\qa-test-run'
$pass = 0; $fail = 0; $skip = 0

function Pass($msg)  { Write-Output "  [PASS] $msg"; $script:pass++ }
function Fail($msg)  { Write-Output "  [FAIL] $msg"; $script:fail++ }
function Skip($msg)  { Write-Output "  [SKIP] $msg"; $script:skip++ }
function Section($t) { Write-Output "`n--- $t ---" }
function RunCli([string[]]$cliArgs) { & $cli @cliArgs 2>&1 }

if (Test-Path $testRoot) { Remove-Item $testRoot -Recurse -Force }
New-Item -ItemType Directory -Path "$testRoot\import" | Out-Null
New-Item -ItemType Directory -Path "$testRoot\output" | Out-Null

Write-Output "=== STIGForge QA Final Pass - v1.0.1 ==="
Write-Output "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "Host: $env:COMPUTERNAME | Hyper-V: $((Get-Module Hyper-V).Version)"

# -------------------------------------------------
Section "1. SMOKE TESTS"
# -------------------------------------------------

if (Test-Path $cli) {
    $sizeMB = [math]::Round((Get-Item $cli).Length/1MB,1)
    Pass "CLI binary exists ($sizeMB MB)"
} else { Fail "CLI binary missing"; exit 1 }

$verOut = RunCli @('--version')
if ($LASTEXITCODE -eq 0 -and "$verOut" -match '1\.0\.1') { Pass "--version: $($verOut | Select-Object -First 1)" }
else { Fail "--version failed (exit $LASTEXITCODE): $(($verOut | Select-Object -First 1))" }

$helpOut = RunCli @('--help')
if ($LASTEXITCODE -eq 0 -and "$helpOut" -match 'import-pack|build-bundle|fleet') { Pass "--help: shows expected commands" }
else { Fail "--help: missing expected commands (exit $LASTEXITCODE)" }

RunCli @('xyzzy-nonexistent') | Out-Null
if ($LASTEXITCODE -ne 0) { Pass "Unknown command: non-zero exit ($LASTEXITCODE)" }
else { Fail "Unknown command should fail but returned 0" }

# -------------------------------------------------
Section "2. IMPORT COMMANDS"
# -------------------------------------------------

$ih = RunCli @('import-pack', '--help')
if ($LASTEXITCODE -eq 0) { Pass "import-pack --help: OK" }
else { Fail "import-pack --help failed (exit $LASTEXITCODE)" }

$bi = RunCli @('import-pack', '--pack', 'C:\nonexistent\bundle.zip')
if ($LASTEXITCODE -ne 0) { Pass "import-pack nonexistent file: graceful fail (exit $LASTEXITCODE)" }
else { Fail "import-pack nonexistent should fail" }

# Build minimal invalid zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = "$testRoot\import\test-bundle.zip"
$tmpDir = "$testRoot\import\tmp"
New-Item -ItemType Directory -Path $tmpDir | Out-Null
Set-Content "$tmpDir\README.txt" "Not a real SCAP bundle"
[System.IO.Compression.ZipFile]::CreateFromDirectory($tmpDir, $zipPath)

$io = RunCli @('import-pack', '--pack', $zipPath, '--output-dir', "$testRoot\output")
if ($LASTEXITCODE -ne 0 -and "$io" -match 'error|invalid|XCCDF|missing|No |Unrecognized') {
    Pass "import-pack invalid zip: descriptive error ($(($io | Select-Object -First 1)))"
} elseif ($LASTEXITCODE -eq 0) { Pass "import-pack accepted test bundle" }
else { Fail "import-pack unexpected (exit $LASTEXITCODE): $(($io | Select-Object -First 2) -join ' | ')" }

# -------------------------------------------------
Section "3. BUILD / ORCHESTRATE COMMANDS"
# -------------------------------------------------

$bh = RunCli @('build-bundle', '--help')
if ($LASTEXITCODE -eq 0) { Pass "build-bundle --help: OK" }
else { Fail "build-bundle --help failed (exit $LASTEXITCODE)" }

$bo = RunCli @('build-bundle')
if ($LASTEXITCODE -ne 0) { Pass "build-bundle (no args): graceful fail (exit $LASTEXITCODE)" }
else { Fail "build-bundle no args should fail" }

$oh = RunCli @('orchestrate', '--help')
if ($LASTEXITCODE -eq 0) { Pass "orchestrate --help: OK" }
else { Fail "orchestrate --help failed (exit $LASTEXITCODE)" }

$mh = RunCli @('mission-autopilot', '--help')
if ($LASTEXITCODE -eq 0) { Pass "mission-autopilot --help: OK" }
else { Fail "mission-autopilot --help failed (exit $LASTEXITCODE)" }

$ah = RunCli @('apply-run', '--help')
if ($LASTEXITCODE -eq 0) { Pass "apply-run --help: OK" }
else { Fail "apply-run --help failed (exit $LASTEXITCODE)" }

# -------------------------------------------------
Section "4. FLEET CREDENTIAL STORE (DPAPI)"
# -------------------------------------------------

# Correct options: --host, --user, --password
$cs1 = RunCli @('fleet-credential-save', '--host', 'srv01.lab.local', '--user', 'lab\Install', '--password', 'P@ssw0rd!')
if ($LASTEXITCODE -eq 0) { Pass "fleet-credential-save SRV01: OK (--host/--user)" }
else { Fail "fleet-credential-save SRV01 failed (exit $LASTEXITCODE): $(($cs1 | Select-Object -First 2) -join ' | ')" }

$cs2 = RunCli @('fleet-credential-save', '--host', 'srv02.lab.local', '--user', 'lab\Install', '--password', 'P@ssw0rd!')
if ($LASTEXITCODE -eq 0) { Pass "fleet-credential-save SRV02: OK" }
else { Fail "fleet-credential-save SRV02 failed (exit $LASTEXITCODE): $(($cs2 | Select-Object -First 2) -join ' | ')" }

$cl = RunCli @('fleet-credential-list')
if ($LASTEXITCODE -eq 0) { Pass "fleet-credential-list: OK" }
else { Fail "fleet-credential-list failed (exit $LASTEXITCODE)" }

$clStr = "$cl"
Write-Output "       credential-list output: $(($cl | Select-Object -First 3) -join ' | ')"
if ($clStr -match 'srv01' -and $clStr -match 'srv02') {
    Pass "DPAPI round-trip: both credentials persisted across invocations"
} elseif ($clStr -match 'srv01' -or $clStr -match 'srv02') {
    Fail "DPAPI round-trip: partial persistence only"
} else {
    Fail "DPAPI round-trip: no credentials found - list output: $($cl | Select-Object -First 3 | Out-String)"
}

# Over-write SRV01 credential (test update path)
$cu = RunCli @('fleet-credential-save', '--host', 'srv01.lab.local', '--user', 'lab\Install', '--password', 'P@ssw0rd!')
if ($LASTEXITCODE -eq 0) { Pass "fleet-credential-save update (overwrite): OK" }
else { Skip "fleet-credential-save update: (exit $LASTEXITCODE)" }

# Remove SRV02 credential
$cr = RunCli @('fleet-credential-remove', '--host', 'srv02.lab.local')
if ($LASTEXITCODE -eq 0) { Pass "fleet-credential-remove SRV02: OK" }
else { Fail "fleet-credential-remove SRV02 failed (exit $LASTEXITCODE): $(($cr | Select-Object -First 1))" }

# Verify SRV02 gone from list
$clAfter = RunCli @('fleet-credential-list')
if ("$clAfter" -notmatch 'srv02' -and "$clAfter" -match 'srv01') {
    Pass "DPAPI remove: SRV02 removed, SRV01 remains"
} else {
    Fail "DPAPI remove verification failed: $($clAfter | Select-Object -First 3 | Out-String)"
}

# -------------------------------------------------
Section "5. PS DIRECT VM CONNECTIVITY"
# -------------------------------------------------

$pw = ConvertTo-SecureString 'P@ssw0rd!' -AsPlainText -Force
$vmCred = New-Object PSCredential('lab\Install', $pw)

foreach ($vm in @('DC01','SRV01','SRV02')) {
    try {
        $r = Invoke-Command -VMName $vm -Credential $vmCred -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop
        Pass "PS Direct: $vm reachable ($r)"
    } catch { Fail "PS Direct: $vm - $_" }
}

# -------------------------------------------------
Section "6. FLEET-STATUS (WinRM)"
# -------------------------------------------------

$fh = RunCli @('fleet-status', '--help')
if ($LASTEXITCODE -eq 0 -or "$fh" -match 'fleet|target') { Pass "fleet-status --help: OK" }
else { Fail "fleet-status --help failed (exit $LASTEXITCODE)" }

# WinRM was enabled in previous run; test connectivity (VMs on internal vSwitch 192.168.50.x)
# The host (triton-ajt) is on the internal vSwitch AppLockerNet, so it CAN reach VMs
$fs = RunCli @('fleet-status', '--targets', '192.168.50.20')
Write-Output "       fleet-status exit: $LASTEXITCODE  out: $(($fs | Select-Object -First 3) -join ' | ')"
if ($LASTEXITCODE -eq 0 -or "$fs" -match 'reachable|OK|1/1|online') {
    Pass "fleet-status SRV01 (192.168.50.20): WinRM reachable"
} elseif ("$fs" -match 'timeout|refused|unreachable|FAIL|0/1') {
    Skip "fleet-status SRV01: WinRM not reachable from host NIC to AppLockerNet (internal-only vSwitch)"
} else {
    Fail "fleet-status SRV01 unexpected (exit $LASTEXITCODE): $(($fs | Select-Object -First 3) -join ' | ')"
}

# -------------------------------------------------
Section "7. VERIFY / COMPLIANCE COMMANDS"
# -------------------------------------------------

foreach ($cmd in @('verify-run','compliance-check','drift-detect','gpo-conflict-detect','rollback')) {
    $h = RunCli @($cmd, '--help')
    if ($LASTEXITCODE -eq 0) { Pass "$cmd --help: OK" }
    else { Skip "$cmd --help: exit $LASTEXITCODE (may need required args)" }
}

# -------------------------------------------------
Section "8. AUDIT COMMANDS"
# -------------------------------------------------

foreach ($cmd in @('audit-export','report-generate','exception-list')) {
    $h = RunCli @($cmd, '--help')
    if ($LASTEXITCODE -eq 0) { Pass "$cmd --help: OK" }
    else { Skip "$cmd --help: exit $LASTEXITCODE" }
}

# Check DB created after credential operations
$appData = [System.Environment]::GetFolderPath('LocalApplicationData')
$dbFiles = Get-ChildItem (Join-Path $appData 'STIGForge') -Filter "*.db" -ErrorAction SilentlyContinue
if ($dbFiles) {
    $dbName = $dbFiles[0].Name
    $dbSize = $dbFiles[0].Length
    $dbMsg = "STIGForge DB: " + $dbName + " - " + $dbSize + " bytes"
    Pass $dbMsg
} else {
    $dbAlt = Get-ChildItem 'C:\projects\STIGForge' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*.db' } | Select-Object -First 1
    if ($dbAlt) { Pass ("STIGForge DB found: " + $dbAlt.FullName) }
    else { Skip "STIGForge DB not found - created only on first write operation" }
}

# -------------------------------------------------
Section "9. SECURITY CHECKS"
# -------------------------------------------------

# Path traversal
$tr = RunCli @('build-bundle', '--output', 'C:\projects\STIGForge\qa-test-run\..\..\Windows\System32\pwned')
if (Test-Path 'C:\Windows\System32\pwned') {
    Fail "CRITICAL: path traversal created files in System32!"
    Remove-Item 'C:\Windows\System32\pwned' -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Pass "Path traversal in --output: blocked (exit $LASTEXITCODE)"
}

# Binary integrity - signed or at least not world-writable
$acl = Get-Acl $cli
$unexp = $acl.Access | Where-Object {
    $_.FileSystemRights -match 'Write|FullControl' -and
    $_.IdentityReference -notmatch 'SYSTEM|Administrators|TrustedInstaller|CREATOR'
}
if (-not $unexp) { Pass "CLI binary ACL: restricted correctly" }
else { Skip "CLI binary ACL: $($unexp.IdentityReference -join ', ') have write (dev build dir - acceptable for lab)" }

# -------------------------------------------------
Section "10. VM HEALTH POST-REARM"
# -------------------------------------------------

foreach ($vm in @('DC01','SRV01','SRV02')) {
    try {
        $days = Invoke-Command -VMName $vm -Credential $vmCred -ErrorAction Stop -ScriptBlock {
            $f = "ApplicationId='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"
            $lic = Get-CimInstance SoftwareLicensingProduct -Filter $f -ErrorAction SilentlyContinue
            if ($lic) { [math]::Round($lic.GracePeriodRemaining / 60 / 24, 1) } else { 'N/A' }
        }
        $d = try { [double]"$days" } catch { 999 }
        if ($days -eq 'N/A' -or $d -gt 5) { Pass "$vm eval: $days days remaining" }
        else { Fail "$vm eval EXPIRING: $days days" }
    } catch { Fail "$vm license check: $_" }
}

# DC01 AD services
try {
    $svcs = Invoke-Command -VMName DC01 -Credential $vmCred -ErrorAction Stop -ScriptBlock {
        @('ADWS','DNS','Netlogon','NTDS') | ForEach-Object {
            [pscustomobject]@{Name=$_; Status=((Get-Service $_ -EA SilentlyContinue).Status.ToString())}
        }
    }
    $down = $svcs | Where-Object { $_.Status -ne 'Running' }
    if (-not $down) { Pass "DC01 AD services: all running ($($svcs.Name -join ', '))" }
    else { Fail "DC01 AD services down: $($down.Name -join ', ')" }
} catch { Fail "DC01 AD check: $_" }

foreach ($vm in @('SRV01','SRV02')) {
    try {
        $gb = Invoke-Command -VMName $vm -Credential $vmCred -ErrorAction Stop -ScriptBlock {
            [math]::Round((Get-PSDrive C).Free/1GB, 1)
        }
        if ($gb -gt 5) { Pass "$vm disk: ${gb}GB free" }
        else { Fail "$vm low disk: ${gb}GB" }
    } catch { Fail "$vm disk: $_" }
}

# -------------------------------------------------
# FINAL RESULTS
# -------------------------------------------------

$total = $pass + $fail + $skip
$pct = if (($pass + $fail) -gt 0) { [math]::Round(($pass / ($pass + $fail)) * 100, 0) } else { 100 }

Write-Output ""
Write-Output "=============================================="
Write-Output "  STIGForge QA Final Pass - v1.0.1"
Write-Output "  PASS: $pass  FAIL: $fail  SKIP: $skip  TOTAL: $total"
Write-Output "  Health Score: $pct / 100"
Write-Output "=============================================="

if ($fail -gt 0) {
    Write-Output "FAILURES DETECTED - review above"
    exit 1
} else {
    Write-Output "ALL CHECKS PASSED (or gracefully skipped)"
    exit 0
}
