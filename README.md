# STIGForge

Offline-first Windows hardening platform: Import → Build → Apply → Verify → Prove.

Version: **1.0.3** · .NET 8 · Windows 10/11 · WPF + CLI

---

## Quick start

1. Install .NET 8 SDK
2. Run the WPF app:
```powershell
dotnet run --project .\src\STIGForge.App\STIGForge.App.csproj
```
3. Import a content pack, build a bundle, and run the full hardening pipeline (see CLI reference below).

---

## CLI reference

All CLI commands use the same entry point. Commands are grouped by workflow stage.

```powershell
$CLI = "dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj --"
```

---

### Import & content management

```powershell
# Import a DISA content pack (STIG XCCDF, SCAP bundle, or GPO package)
$CLI import-pack C:\path\to\pack.zip --name Q1_2026

# List all imported content packs
$CLI list-packs

# List all overlays
$CLI list-overlays

# Import PowerSTIG overrides from CSV
$CLI overlay-import-powerstig --csv C:\path\to\powerstig_overrides.csv --name "PowerSTIG Overrides"

# Export PowerSTIG mapping template CSV from a pack
$CLI powerstig-map-export --pack-id <PACK_ID> --output C:\path\to\powerstig_map.csv

# Edit overlay (add/remove rule overrides)
$CLI overlay-edit --overlay <OVERLAY_ID> --add-rule SV-12345 --status NotApplicable --reason "Org policy"

# Import ACAS/Nessus XML and correlate findings to bundle controls
$CLI acas-import --file C:\path\to\acas.xml --bundle C:\path\to\bundle --json

# Import Nessus .nessus XML findings
$CLI nessus-import --file C:\path\to\scan.nessus --json

# Import STIG Viewer CKL checklist
$CLI ckl-import --file C:\path\to\checklist.ckl --json
```

---

### Build

```powershell
# Build offline bundle (Apply/Verify/Manual/Evidence/Reports)
$CLI build-bundle --pack-id <PACK_ID> --profile-json .\docs\samples\Profile-Classified-Win11.json --save-profile --force-auto-apply

# Seamless autopilot mission: NIWC enhanced SCAP import + build + apply + verify
$CLI mission-autopilot `
    --niwc-source-url https://github.com/niwc-atlantic/scap-content-library `
    --disa-stig-url https://www.cyber.mil/stigs/downloads `
    --powerstig-source-url https://github.com/microsoft/PowerStig `
    --evaluate-stig C:\path\to\Evaluate-STIG `
    --scap-cmd "C:\path\to\cscc.exe"
# Remote source archives are cached in .stigforge/airgap-transfer for air-gap transfer.
```

---

### Apply

```powershell
# Apply using a custom script
$CLI apply-run --bundle C:\path\to\bundle --mode Safe --script C:\path\to\apply.ps1 --script-args "-Example 1"

# Apply using PowerSTIG DSC
$CLI apply-run --bundle C:\path\to\bundle `
    --powerstig-module C:\path\to\PowerStig `
    --powerstig-data C:\path\to\your.psd1 `
    --powerstig-verbose `
    --dsc-path C:\path\to\bundle\Apply\Dsc --dsc-verbose

# Orchestrate full pipeline: build + apply + verify
$CLI orchestrate --bundle C:\path\to\bundle `
    --powerstig-module C:\path\to\PowerStig `
    --powerstig-data C:\path\to\your.psd1 `
    --evaluate-stig C:\path\to\Evaluate-STIG `
    --evaluate-args "-AnswerFile .\AnswerFile.xml" `
    --scap-cmd "C:\path\to\cscc.exe" --scap-args "-u -s -r -f" --scap-label "DISA SCAP"

# List supported per-rule remediation rules
$CLI remediate-list

# Run per-rule remediation
$CLI remediate --bundle C:\path\to\bundle --mode Safe --rule-id SV-12345 --dry-run
```

---

### Verify

```powershell
# Verify with SCAP tool (use cscc.exe/cscc-remote.exe for automation; scc.exe is the GUI launcher)
$CLI verify-scap --cmd "C:\path\to\cscc.exe" --args "-u -s -r -f" --output-root C:\path\to\scap\output --tool "DISA SCAP"

# Verify with Evaluate-STIG
$CLI verify-evaluate-stig --tool-root C:\path\to\Evaluate-STIG --args "-AnswerFile .\AnswerFile.xml" --output-root C:\path\to\output

# Build coverage overlap summary from multiple tool results
$CLI coverage-overlap --inputs C:\path\to\ckl1 C:\path\to\ckl2 --output C:\path\to\overlap.json
```

---

### Rollback

```powershell
# Create a pre-hardening rollback snapshot
$CLI rollback-create --bundle C:\path\to\bundle --description "Before Q1 hardening"

# List rollback snapshots
$CLI rollback-list --bundle C:\path\to\bundle

# Apply a rollback snapshot
$CLI rollback-apply --snapshot-id <SNAPSHOT_ID>
```

---

### Manual controls & evidence

```powershell
# List manual controls and answer status
$CLI list-manual-controls --bundle C:\path\to\bundle --status Open

# Save a single manual answer
$CLI manual-answer --bundle C:\path\to\bundle --rule-id SV-12345 --status NotApplicable --reason "Not in scope"

# Save manual answers in bulk from CSV
$CLI manual-answer --bundle C:\path\to\bundle --csv C:\path\to\answers.csv

# Save an evidence artifact
$CLI evidence-save --bundle C:\path\to\bundle --rule-id SV-12345 --type File --source-file C:\path\to\evidence.txt
```

---

### Compliance & drift

```powershell
# Record a compliance snapshot (after a SCAP or Evaluate-STIG run)
$CLI compliance-score --bundle C:\path\to\bundle --pass 245 --fail 38 --not-applicable 22 --tool "DISA SCAP" --pack-id <PACK_ID>

# Show compliance trend over time
$CLI compliance-trend --bundle C:\path\to\bundle --limit 10 --json

# Compare two compliance snapshots (detect regressions and remediations)
$CLI compliance-diff --baseline <RUN_ID_1> --target <RUN_ID_2> --format console

# Check for baseline drift and optionally auto-remediate
$CLI drift-check --bundle C:\path\to\bundle --auto-remediate

# Show drift detection history
$CLI drift-history --bundle C:\path\to\bundle --limit 20 --json
```

---

### Exceptions

```powershell
# Create a control exception
$CLI exception-create --bundle C:\path\to\bundle --rule-id SV-12345 --type Operational --risk Low --approved-by "John Smith" --justification "Compensating control in place" --expires 2026-12-31

# List active/expired exceptions
$CLI exception-list --bundle C:\path\to\bundle --json

# Audit exception health (flags expired, unapproved, or near-expiry exceptions)
$CLI exception-audit --bundle C:\path\to\bundle --json

# Revoke an exception
$CLI exception-revoke --exception-id <EXCEPTION_ID> --revoked-by "John Smith"
```

---

### Export & reporting

```powershell
# Bundle summary dashboard
$CLI bundle-summary --bundle C:\path\to\bundle --json

# Export standalone HTML/JSON compliance report
$CLI export-report --bundle C:\path\to\bundle --output C:\path\to\report --format html --audience admin --system-name "MySystem"

# Export eMASS submission package
$CLI export-emass --bundle C:\path\to\bundle --output C:\path\to\emass

# Generate full packaged eMASS submission
$CLI emass-package --bundle C:\path\to\bundle --output C:\path\to\emass-package

# Export standalone POA&M
$CLI export-poam --bundle C:\path\to\bundle --output C:\path\to\poam --system-name "MySystem"

# Export STIG Viewer CKL checklist
$CLI export-ckl --bundle C:\path\to\bundle --host-name MYHOST --stig-id "Win11_STIG" --format ckl

# Export bundle controls into CKL format
$CLI ckl-export --bundle C:\path\to\bundle --json

# Merge an imported CKL with bundle verification results and detect conflicts
$CLI ckl-merge --bundle C:\path\to\bundle --ckl-file C:\path\to\checklist.ckl --json
```

---

### Pack diff & overlay rebase

```powershell
# Diff two packs (quarterly update review)
$CLI diff-packs --baseline <OLD_PACK_ID> --target <NEW_PACK_ID> --output diff-report.md

# Rebase an overlay to a new pack
$CLI rebase-overlay --overlay <OVERLAY_ID> --baseline <OLD_PACK_ID> --target <NEW_PACK_ID> --apply

# Check for new STIG releases for a pack
$CLI check-release --pack-id <PACK_ID>

# Generate release notes between two pack versions
$CLI release-notes --baseline <OLD_PACK_ID> --target <NEW_PACK_ID>
```

---

### GPO conflict detection

```powershell
# Detect GPO conflicts with local STIG settings
$CLI gpo-conflicts --bundle C:\path\to\bundle --json
```

---

### Security features

```powershell
# Check security feature status (WDAC, BitLocker, Firewall)
$CLI security-status --json

# Apply security features
$CLI security-apply --bundle C:\path\to\bundle --mode Safe --dry-run --json
```

---

### Fleet (multi-machine via WinRM)

```powershell
# Save encrypted credentials for a fleet target (DPAPI)
$CLI fleet-credential-save --host SRV01 --user domain\admin --password "P@ssw0rd"

# List stored fleet credentials
$CLI fleet-credential-list

# Remove stored credential
$CLI fleet-credential-remove --host SRV01

# Check WinRM connectivity
$CLI fleet-status --targets "SRV01,SRV02,SRV03"

# Apply hardening across multiple machines
$CLI fleet-apply --targets "SRV01,SRV02:10.0.0.2,SRV03" --remote-bundle-path "C:\STIGForge\bundle" --mode Safe --concurrency 5

# Verify across multiple machines
$CLI fleet-verify --targets "SRV01,SRV02,SRV03" --scap-cmd "C:\SCC\cscc.exe" --json
```

---

### Continuous compliance agent

```powershell
# Install continuous compliance Windows service
$CLI agent-install --bundle C:\path\to\bundle --service-name STIGForgeAgent

# Query service status
$CLI agent-status --service-name STIGForgeAgent --json

# Manage agent configuration
$CLI agent-config --service-name STIGForgeAgent --set-key VerifyIntervalHours --value 24

# Uninstall service
$CLI agent-uninstall --service-name STIGForgeAgent
```

---

### Scheduled re-verification

```powershell
# Schedule daily re-verification via Windows Task Scheduler
$CLI schedule-verify --name "DailyVerify" --bundle C:\path\to\bundle --frequency DAILY --time 06:00

# List scheduled STIGForge tasks
$CLI schedule-list

# Remove a scheduled task
$CLI schedule-remove --name "DailyVerify"
```

---

### Audit trail

```powershell
# Query the tamper-evident audit trail
$CLI audit-log --action apply --limit 50 --json

# Verify audit trail chain integrity
$CLI audit-verify
```

---

### Diagnostics & support

```powershell
# Collect logs and diagnostics into a support zip
$CLI support-bundle --output .\artifacts\support --bundle C:\path\to\bundle --max-log-files 30
```

---

## Local workflow (`workflow-local`)

Run the v1 local mission path (Setup → Import → Scan) and emit `mission.json`:

```powershell
$CLI workflow-local --import-root .\.stigforge\import --tool-root .\.stigforge\tools\Evaluate-STIG\Evaluate-STIG --output-root .\.stigforge\local-workflow
```

Defaults: `--import-root` → `.stigforge/import`; `--tool-root` → `.stigforge/tools/Evaluate-STIG/Evaluate-STIG`; `--output-root` → `.stigforge/local-workflow`.

Behavior notes:
- Strict setup gate: fails immediately when `Evaluate-STIG.ps1` is not found at `--tool-root`.
- Import gate: fails if import scanning produces no canonical checklist items.
- Unmapped scanner findings are warnings; workflow still writes `mission.json` and records unmapped entries under `Unmapped`.

---

## Ship readiness gate

Run the automated release gate (build + tests + artifact manifest + checksums):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\local
```

Outputs:
- `report/release-gate-report.md` — human-readable gate summary
- `report/release-gate-summary.json` — machine-readable step results
- `report/sha256-checksums.txt` — artifact checksums
- `logs/*.log` — per-step command logs
- `security/reports/security-gate-report.md` — vuln/license/secrets summary
- `security/reports/security-gate-summary.json` — machine-readable security summary
- `sbom/dotnet-packages.json` — dependency inventory (unless `-SkipSbom`)

See `docs/release/ShipReadinessChecklist.md` for go/no-go criteria.
See `docs/release/SecurityGatePolicies.md` for policy file and exception management.

Run security gate only (dependency vuln/license/secrets checks):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\release\Invoke-SecurityGate.ps1 -OutputRoot .\.artifacts\security-gate\local
```

Build release packages (CLI + WPF publish zips, optional signing):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\release\Invoke-PackageBuild.ps1 -Configuration Release -Runtime win-x64 -OutputRoot .\.artifacts\release-package\local
```

---

## CI/CD workflows

- `ci.yml` — build, 2,516-test suite, coverage gate (35% floor), CLI artifact publish on `main`
- `release-package.yml` (manual dispatch) — release gate + package build + upload
- `vm-smoke-matrix.yml` (manual dispatch) — release gate + E2E tests on self-hosted VM runners: `win11`, `server2019`, `server2022`

---

## E2E Compliance Metrics (Server 2019 Lab, 2026-03-10)

Tested on isolated Hyper-V lab VMs (SRV01, SRV02) running Windows Server 2019 (Member Server).
Hardening pipeline: PowerSTIG DSC (3-phase) + LGPO GPO application + DoD PKI certificate import.

### Per-STIG Compliance Delta

| STIG | Controls | Original | After DSC | After DSC+GPO+Certs | Total Delta |
|------|----------|----------|-----------|---------------------|-------------|
| Microsoft Windows Server 2019 | 283 | 50.9% | 68.2% | 71.4% | **+20.5%** |
| Microsoft Windows Defender Firewall | 21 | 4.8% | 100% | 100% | **+95.2%** |
| Microsoft DotNet Framework 4.0 | 16 | 87.5% | 93.8% | 93.8% | **+6.3%** |
| Microsoft Defender Antivirus | 68 | 26.5% | 26.5% | 26.5% | 0% |
| Microsoft Internet Explorer 11 | 137 | 1.5% | 1.5% | 1.5% | 0% |

### Aggregate Results

| Metric | Original Baseline | After DSC Only | After DSC+GPO+Certs |
|--------|-------------------|----------------|---------------------|
| **Weighted Total** | 35.7% (179/502) | 47.4% (249/525) | 49.1% (258/525) |
| **Unweighted Average** | 34.2% | 58.0% | 58.6% |
| **Findings Fixed** | — | 70 | 78 |

### Remaining Findings Analysis

- 248/253 remaining open findings (98%) are **Manual/Interview** checks requiring human review
- IE11: 135 open (all require domain GPO or manual policy)
- Defender: 48 open (all require domain GPO or manual Defender config)
- Server 2019: 62 open (mix of GPO, certificate, and manual checks)
- PKI/Certificate: 2 open (Root CA 3/5/6 import failed due to DER format mismatch)

### Test Infrastructure

- Tests: 2,516/2,516 pass (UnitTests: 1,296 · CrossPlatform: 1,114 · Integration: 102 · UiTests: 6)
- PowerSTIG: 4.29.0, Evaluate-STIG: DISA latest
- Lab: Hyper-V (triton-ajt), VMs: SRV01/SRV02 (Server 2019), DC01 (Domain Controller)

---

## Repo layout

```
src/        STIGForge.* projects (WPF App, CLI, and all modules)
tests/      Unit, integration, cross-platform, and UI tests
tools/      Release gate, security gate, package build, and QA scripts
docs/       Specs (bundle, eMASS export), samples, and release docs
openspec/   JSON schemas (profile, overlay, manifest)
```

## Docker test lane (WSL)

For Docker-on-WSL unit/integration test commands, see `docs/testing/DockerWslTestLane.md`.
