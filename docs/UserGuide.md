# STIGForge User Guide

## What is STIGForge?

STIGForge is an offline-first Windows STIG hardening platform that automates the full compliance lifecycle: importing DISA content, building hardening bundles, applying configurations, verifying compliance, and generating export packages for eMASS submission. It works entirely offline with no network dependencies.

## Prerequisites

- Windows 10/11 or Windows Server 2019/2022
- .NET 8 SDK (for development/CLI)
- Administrator privileges (required for Dashboard Scan/Verify, Apply, and scheduled runs)

## Getting Started

### Installation

1. Clone or download the repository
2. Build:
   ```powershell
   dotnet build STIGForge.sln
   ```
3. Launch the WPF app:
   ```powershell
   dotnet run --project src\STIGForge.App\STIGForge.App.csproj
   ```
   Or use the CLI:
   ```powershell
   dotnet run --project src\STIGForge.Cli\STIGForge.Cli.csproj -- <command>
   ```

### Data Storage

STIGForge stores all data locally:
- **Database**: `%ProgramData%\STIGForge\data\stigforge.db`
- **Bundles**: `%ProgramData%\STIGForge\bundles\`
- **Content Packs**: `%ProgramData%\STIGForge\packs\`
- **Logs**: `%ProgramData%\STIGForge\logs\`
- **Credentials**: `%ProgramData%\STIGForge\credentials\`

---

## Workflow Overview

### 1. Import Content

Download DISA STIG content packs (ZIP files containing XCCDF/SCAP benchmarks) from [public.cyber.mil](https://public.cyber.mil).

**CLI**:
```powershell
STIGForge.Cli.exe import-pack C:\Downloads\U_MS_Windows_11_V1R4_STIG.zip --name "Win11_Q1_2026"
```

**WPF**: Navigate to the **Content Packs** tab and click **Import ZIP...**.

STIGForge parses XCCDF XML, extracts controls, maps severity levels, and stores everything in the local database.

### 2. Configure Profile

A profile defines hardening behavior:

- **Hardening Mode**: `AuditOnly` (no changes), `Safe` (reversible only), `Full` (all changes)
- **Classification**: `Classified`, `Unclassified`, or `Mixed`
- **NA Policy**: Auto-mark out-of-scope controls as Not Applicable
- **Overlays**: Optional rule overrides (e.g., PowerSTIG settings, organizational exceptions)

**CLI**: Create a JSON file (see `docs/samples/` for templates) and pass via `--profile-json`.

**WPF**: Use the **Profiles** tab to configure and save profiles. Select overlays from the checkbox list.

### 3. Build Bundle

A bundle is a self-contained directory with everything needed for hardening:

```powershell
STIGForge.Cli.exe build-bundle --pack-id <PACK_ID> --profile-json .\myprofile.json --save-profile
```

**WPF**: On the **Build** tab, select a pack and profile, then click **Build Bundle**.

The bundle contains:
- `Manifest/` — manifest.json, pack_controls.json
- `Apply/` — RunApply.ps1, rollback scripts
- `Verify/` — placeholder for scan results
- `Manual/` — manual_answers.json
- `Evidence/` — evidence artifacts
- `Reports/` — automation_gate.json

### 4. Apply Hardening

Apply the bundle to the local system:

```powershell
STIGForge.Cli.exe apply-run --bundle C:\bundles\my-bundle --mode Safe
```

Options:
- Custom PowerShell scripts (`--script`)
- DSC MOFs (`--dsc-path`)
- PowerSTIG compilation (`--powerstig-module`, `--powerstig-data`)

A system snapshot is captured before changes for rollback capability.

**WPF**: Use the **Apply** tab to browse to a bundle and click **Run Apply**.

### 5. Verify Compliance

Run verification tools against the hardened system:

```powershell
# Using DISA SCAP Compliance Checker
STIGForge.Cli.exe verify-scap --cmd "C:\SCC\cscc.exe" --args "-u -s -r -f" --output-root C:\results

# Using Evaluate-STIG
STIGForge.Cli.exe verify-evaluate-stig --tool-root "C:\Evaluate-STIG" --args "-AnswerFile .\AnswerFile.xml"
```

Use `cscc.exe` or `cscc-remote.exe` for SCAP automation. `scc.exe` opens the interactive GUI.

STIGForge parses CKL results and generates consolidated-results.json.

**WPF**: Use the **Verify** tab to configure tool paths and click **Run Verify**.

For Dashboard **Scan** and **Verify** workflow runs:
- Start STIGForge as Administrator before running **Scan** or **Verify**.
- Confirm the Evaluate-STIG path resolves to `Evaluate-STIG.ps1`.
- Configure advanced settings in **Settings -> Evaluate-STIG Advanced** when the default command needs customization.
- Minimal example: set `AFPath` to `C:\Evaluate-STIG\AnswerFile.xml` and `SelectSTIG` to `U_MS_Windows_11_V1R4_STIG`, then run **Verify**.
- If Scan/Verify fails, use the dashboard failure card to see **What happened** and the exact **Next step**.
- Use failure-card actions (`Open Settings`, `Open Output Folder`, `Rerun Scan`, `Rerun Verify`) for one-click recovery.
- Detailed diagnostics remain in `mission.json` under the configured output folder.

### 6. Handle Manual Controls

Some STIG controls require manual review. STIGForge tracks these separately:

```powershell
# List manual controls
STIGForge.Cli.exe list-manual-controls --bundle C:\bundles\my-bundle --status Open

# Answer individually
STIGForge.Cli.exe manual-answer --bundle C:\bundles\my-bundle --rule-id SV-12345 --status NotApplicable --reason "Not in scope"

# Batch from CSV
STIGForge.Cli.exe manual-answer --bundle C:\bundles\my-bundle --csv C:\answers.csv
```

**WPF**: The **Manual** tab provides a review wizard with filtering, bulk answers, and progress tracking.

### 7. Collect Evidence

Attach evidence artifacts to specific controls:

```powershell
STIGForge.Cli.exe evidence-save --bundle C:\bundles\my-bundle --rule-id SV-12345 --type File --source-file C:\evidence\screenshot.png
```

**WPF**: Use the **Evidence** tab to attach files, text, or command output.

### 8. Export

#### eMASS Package
```powershell
STIGForge.Cli.exe export-emass --bundle C:\bundles\my-bundle
```
Generates a complete submission package with manifests, scans, checklists, POA&M, evidence, and attestations.

#### Standalone POA&M
```powershell
STIGForge.Cli.exe export-poam --bundle C:\bundles\my-bundle --system-name "MySystem"
```

#### CKL Checklist
```powershell
STIGForge.Cli.exe export-ckl --bundle C:\bundles\my-bundle --host-name MYHOST --stig-id "Win11_STIG"
```

**WPF**: Use the **Export** tab for POA&M and CKL, or the **Reports** tab for full eMASS export.

---

## Advanced Features

### Quarterly STIG Updates (Diff & Rebase)

When DISA releases new STIG content:

1. Import the new pack:
   ```powershell
   STIGForge.Cli.exe import-pack C:\Downloads\new_stig.zip --name "Win11_Q2_2026"
   ```

2. Diff the packs:
   ```powershell
   STIGForge.Cli.exe diff-packs --baseline <OLD_PACK_ID> --target <NEW_PACK_ID> --output diff-report.md
   ```

3. Rebase overlays to the new pack:
   ```powershell
   STIGForge.Cli.exe rebase-overlay --overlay <OVERLAY_ID> --baseline <OLD_PACK_ID> --target <NEW_PACK_ID> --apply
   ```

**WPF equivalent**:
- Use **Content Packs -> Compare Packs** to run baseline/target diff and export Markdown/JSON artifacts.
- Use **Profiles -> Rebase Overlay** to run rebase analysis, review blocking conflicts, export Markdown/JSON artifacts, and apply only when conflicts are resolved.
- Use Dashboard mission severity + recovery guidance to track `blocking`, `warnings`, and `optional-skips` before release promotion.

### Orchestration (One-Click Pipeline)

Run the complete Apply -> Verify -> Export pipeline:

```powershell
STIGForge.Cli.exe orchestrate --bundle C:\bundles\my-bundle --evaluate-stig "C:\Evaluate-STIG" --scap-cmd "C:\SCC\cscc.exe"
```

**WPF**: The **Orchestrate** tab provides a one-click workflow with real-time log output.

Import selection is deterministic and STIG-driven:
- STIG selection is manual, while SCAP/GPO/ADMX are auto-included and locked when required.
- A missing SCAP dependency is warning-only and does not block mission setup.
- Selection summary counts are computed with STIG selections as the source-of-truth semantics.

### Audit Trail

Every compliance-relevant action is recorded in a tamper-evident audit log with chained SHA-256 hashes:

```powershell
# Query recent actions
STIGForge.Cli.exe audit-log --action apply --limit 50 --json

# Verify chain integrity
STIGForge.Cli.exe audit-verify
```

### Scheduled Re-Verification

Schedule automated re-verification via Windows Task Scheduler:

```powershell
# Register daily verification at 6 AM
STIGForge.Cli.exe schedule-verify --name "DailyVerify" --bundle C:\bundles\my-bundle --frequency DAILY --time 06:00

# List scheduled tasks
STIGForge.Cli.exe schedule-list

# Remove a task
STIGForge.Cli.exe schedule-remove --name "DailyVerify"
```

**WPF**: Use the **Schedule** tab to register, remove, and monitor scheduled tasks.

### Fleet Operations (Multi-Machine)

Manage STIG hardening across multiple Windows machines via WinRM/PSRemoting:

```powershell
# Check connectivity
STIGForge.Cli.exe fleet-status --targets "SRV01,SRV02:10.0.0.2,SRV03"

# Apply across fleet
STIGForge.Cli.exe fleet-apply --targets "SRV01,SRV02,SRV03" --remote-bundle-path "C:\STIGForge\bundle" --mode Safe

# Verify across fleet
STIGForge.Cli.exe fleet-verify --targets "SRV01,SRV02,SRV03" --scap-cmd "C:\SCC\cscc.exe"
```

#### Fleet Credential Management

Store credentials securely with DPAPI encryption (per-user):

```powershell
# Save credentials
STIGForge.Cli.exe fleet-credential-save --host SRV01 --user admin --password "s3cret"

# List stored hosts
STIGForge.Cli.exe fleet-credential-list

# Remove credentials
STIGForge.Cli.exe fleet-credential-remove --host SRV01
```

Stored credentials are automatically used by fleet operations when no explicit credentials are provided.

**WPF**: Use the **Fleet** tab to configure targets, select operations, and execute fleet-wide actions.

### Dashboard

The **Dashboard** tab (WPF) provides at-a-glance compliance status:
- Total controls (automated vs. manual)
- Automated verification compliance rate
- Manual review progress
- Quick action buttons for common operations

### Support Bundle

Collect diagnostics for troubleshooting:

```powershell
STIGForge.Cli.exe support-bundle --output .\artifacts\support --bundle C:\bundles\my-bundle --max-log-files 30
```

---

## Common Workflows

### Fresh Hardening
1. `import-pack` -> `build-bundle` -> `apply-run` -> `verify-scap` -> `export-emass`

### Quarterly Update
1. `import-pack` (new quarter) -> `diff-packs` -> `rebase-overlay` -> `build-bundle` -> `apply-run` -> `verify-scap` -> `export-emass`

### Continuous Monitoring
1. `schedule-verify` -> `audit-log` -> `audit-verify`

### Fleet Deployment
1. `fleet-credential-save` -> `fleet-apply` -> `fleet-verify` -> `fleet-status`
