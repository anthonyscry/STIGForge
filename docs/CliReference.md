# STIGForge CLI Reference

38 commands organized by workflow phase.

**Usage**: `dotnet run --project src\STIGForge.Cli\STIGForge.Cli.csproj -- <command> [options]`

Or if published: `STIGForge.Cli.exe <command> [options]`

---

## Import Commands

### `import-pack`
Import DISA content packs (STIG XCCDF, SCAP bundles, or GPO packages).

```
import-pack <zip> [--name <name>]
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `<zip>` | Yes | Path to ZIP file |
| `--name` | No | Pack name (default: `Imported_YYYYMMDD_HHmm`) |

### `overlay-import-powerstig`
Import PowerSTIG overrides from CSV.

```
overlay-import-powerstig --csv <path> [--name <name>] [--overlay-id <id>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--csv` | Yes | CSV path with columns: RuleId, SettingName, Value |
| `--name` | No | Overlay name (default: "PowerSTIG Overrides") |
| `--overlay-id` | No | Update existing overlay by ID |

### `powerstig-map-export`
Export PowerSTIG mapping template CSV from a pack.

```
powerstig-map-export --pack-id <id> --output <path>
```

| Option | Required | Description |
|--------|----------|-------------|
| `--pack-id` | Yes | Pack ID to export from |
| `--output` | Yes | Output CSV path |

---

## Build Commands

### `build-bundle`
Build offline bundle (Apply/Verify/Manual/Evidence/Reports).

```
build-bundle --pack-id <id> (--profile-id <id> | --profile-json <path>) [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--pack-id` | Yes | Pack ID to build from |
| `--profile-id` | One of | Profile ID from repository |
| `--profile-json` | One of | Path to profile JSON file |
| `--bundle-id` | No | Custom bundle ID |
| `--output` | No | Output path override |
| `--save-profile` | No | Save profile to repo when using `--profile-json` |
| `--force-auto-apply` | No | Override release-age gate |

### `orchestrate`
Run full pipeline: Build -> Apply -> Verify using a bundle.

```
orchestrate --bundle <path> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--dsc-path` | No | DSC MOF path |
| `--dsc-verbose` | No | Verbose DSC output |
| `--powerstig-module` | No | PowerSTIG module path |
| `--powerstig-data` | No | PowerSTIG data file |
| `--powerstig-out` | No | PowerSTIG output folder |
| `--powerstig-verbose` | No | Verbose PowerSTIG compile |
| `--evaluate-stig` | No | Evaluate-STIG root |
| `--evaluate-args` | No | Evaluate-STIG arguments |
| `--scap-cmd` | No | SCAP/SCC command path |
| `--scap-args` | No | SCAP arguments |
| `--scap-label` | No | Label for SCAP tool |

### `mission-autopilot`
Run low-intervention import -> build -> apply -> verify with optional NIWC enhanced SCAP ingestion.

```
mission-autopilot [--niwc-enhanced-zip <zip> | --pack-id <id>] [options]
```

Remote downloads are automatically cached under `.stigforge/airgap-transfer` so they can be moved to air-gapped systems.

| Option | Required | Description |
|--------|----------|-------------|
| `--niwc-enhanced-zip` | One of | NIWC enhanced SCAP ZIP to import before build |
| `--niwc-source-url` | No | NIWC source URL (default: `https://github.com/niwc-atlantic/scap-content-library`) |
| `--disa-stig-url` | No | DISA STIG source URL (direct ZIP or downloads page) |
| `--allow-remote-downloads` | No | Auto-download remote source ZIPs when local pack input is not provided (default: true) |
| `--pack-id` | One of | Existing pack ID (skip import) |
| `--source-label` | No | Source label for NIWC import (default: `niwc_atlantic_enhanced_scap`) |
| `--profile-id` | No | Existing profile ID |
| `--profile-json` | No | Profile JSON path |
| `--save-profile` | No | Save generated/JSON profile (default: true) |
| `--profile-name` | No | Generated profile name |
| `--mode` | No | Generated profile mode: AuditOnly, Safe, Full |
| `--classification` | No | Generated profile classification: Classified, Unclassified, Mixed |
| `--os-target` | No | Generated profile OS target: Win11, Server2019 |
| `--role-template` | No | Generated profile role template |
| `--auto-na` | No | Enable generated-profile auto-NA out-of-scope controls |
| `--na-confidence` | No | Auto-NA threshold: High, Medium, Low |
| `--na-comment` | No | Default generated-profile NA comment |
| `--bundle-id` | No | Bundle ID override |
| `--output` | No | Bundle output root override |
| `--auto-detect-tools` | No | Auto-detect Evaluate-STIG/SCC/PowerSTIG paths (default: true) |
| `--powerstig-module` | No | PowerSTIG module path |
| `--powerstig-source-url` | No | PowerStig source URL (default: `https://github.com/microsoft/PowerStig`) |
| `--powerstig-data` | No | PowerSTIG data file |
| `--powerstig-out` | No | PowerSTIG output folder |
| `--powerstig-verbose` | No | Verbose PowerSTIG compile |
| `--evaluate-stig` | No | Evaluate-STIG root |
| `--evaluate-args` | No | Evaluate-STIG arguments |
| `--scap-cmd` | No | SCC/SCAP executable path |
| `--scap-args` | No | SCC/SCAP arguments (default: `-u -s -r -f`) |
| `--scap-label` | No | SCAP label (default: `DISA SCAP`) |
| `--skip-snapshot` | No | Skip snapshot generation (requires break-glass flags) |
| `--break-glass-ack` | With skip-snapshot | Acknowledge high-risk bypass |
| `--break-glass-reason` | With skip-snapshot | Specific bypass reason |

### `apply-run`
Run apply phase using a bundle and optional script.

```
apply-run --bundle <path> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--mode` | No | Override mode: AuditOnly, Safe, Full |
| `--script` | No | Custom PowerShell script path |
| `--script-args` | No | Script arguments |
| `--dsc-path` | No | DSC MOF directory |
| `--dsc-verbose` | No | Verbose DSC output |
| `--skip-snapshot` | No | Skip snapshot generation |
| `--powerstig-module` | No | PowerSTIG module folder |
| `--powerstig-data` | No | PowerSTIG data file |
| `--powerstig-out` | No | PowerSTIG MOF output folder |
| `--powerstig-verbose` | No | Verbose PowerSTIG compile |

---

## Verify Commands

### `verify-evaluate-stig`
Run Evaluate-STIG.ps1 with provided arguments.

```
verify-evaluate-stig [--tool-root <path>] [--args <args>] [--workdir <path>] [--log <path>] [--output-root <path>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--tool-root` | No | Root folder containing Evaluate-STIG.ps1 |
| `--args` | No | Arguments passed to Evaluate-STIG.ps1 |
| `--workdir` | No | Working directory for the script |
| `--log` | No | Log file path |
| `--output-root` | No | Folder to scan for generated CKL files |

### `verify-scap`
Run a SCAP tool and consolidate CKL results.

```
verify-scap --cmd <path> [--args <args>] [--workdir <path>] [--output-root <path>] [--tool <name>] [--log <path>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--cmd` | Yes | Path to SCAP/SCC executable |
| `--args` | No | Arguments passed to SCAP tool |
| `--workdir` | No | Working directory |
| `--output-root` | No | Folder to scan for generated CKL files |
| `--tool` | No | Tool label (default: "SCAP") |
| `--log` | No | Log file path |

### `coverage-overlap`
Build coverage overlap summary from consolidated results.

```
coverage-overlap --inputs <inputs> [--output <path>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--inputs` | Yes | Semicolon-delimited inputs: `Label\|Path` or `Path` |
| `--output` | No | Output folder for summary files |

### `export-emass`
Export eMASS submission package from a bundle.

```
export-emass --bundle <path> [--output <path>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--output` | No | Export root override |

---

## Diff & Rebase Commands

### `list-packs`
List all imported content packs.

```
list-packs
```

### `list-overlays`
List all overlays in the repository.

```
list-overlays
```

### `diff-packs`
Compare two content packs and show what changed.

```
diff-packs --baseline <id> --target <id> [--output <path>] [--json]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--baseline` | Yes | Baseline (old) pack ID |
| `--target` | Yes | Target (new) pack ID |
| `--output` | No | Write Markdown report to file |
| `--json` | No | Output full diff as JSON |

### `rebase-overlay`
Rebase an overlay from baseline pack to target pack.

```
rebase-overlay --overlay <id> --baseline <id> --target <id> [--apply] [--output <path>] [--json]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--overlay` | Yes | Overlay ID |
| `--baseline` | Yes | Current baseline pack ID |
| `--target` | Yes | New target pack ID |
| `--apply` | No | Apply the rebase |
| `--output` | No | Write report to file |
| `--json` | No | Output as JSON |

---

## Bundle Commands

### `list-manual-controls`
List manual controls and their answer status from a bundle.

```
list-manual-controls --bundle <path> [--status <filter>] [--search <text>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--status` | No | Filter by status: Pass, Fail, NA, Open |
| `--search` | No | Text search filter |

### `manual-answer`
Save manual control answers (single or batch CSV).

```
manual-answer --bundle <path> (--csv <path> | --rule-id <id> --status <status>) [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--csv` | One of | CSV file: RuleId, Status, Reason, Comment |
| `--rule-id` | One of | Single rule ID |
| `--status` | With rule-id | Pass, Fail, NotApplicable, Open |
| `--reason` | No | Reason text |
| `--comment` | No | Comment text |

### `evidence-save`
Save evidence artifact to a bundle.

```
evidence-save --bundle <path> --rule-id <id> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--rule-id` | Yes | Rule ID |
| `--type` | No | Evidence type (default: Other) |
| `--source-file` | No | Source file path |
| `--command` | No | Command that produced evidence |
| `--text` | No | Inline text content |

### `bundle-summary`
Show dashboard summary of a bundle.

```
bundle-summary --bundle <path> [--json]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--json` | No | Output as JSON |

### `overlay-edit`
Add or remove rule overrides from an overlay.

```
overlay-edit --overlay <id> (--add-rule <id> | --remove-rule <id>) [--status <status>] [--reason <text>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--overlay` | Yes | Overlay ID |
| `--add-rule` | One of | Add override for rule ID |
| `--remove-rule` | One of | Remove override for rule ID |
| `--status` | No | Override status (default: NotApplicable) |
| `--reason` | No | NA reason or notes |

### `support-bundle`
Collect logs and diagnostics into a support ZIP package.

```
support-bundle [--output <path>] [--bundle <path>] [--include-db] [--max-log-files <n>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--output` | No | Output directory (default: current directory) |
| `--bundle` | No | Bundle root to include diagnostics from |
| `--include-db` | No | Include stigforge.db in support bundle |
| `--max-log-files` | No | Max recent log files to include (default: 20) |

---

## Audit Commands

### `audit-log`
Query and export the tamper-evident audit trail.

```
audit-log [--action <type>] [--target <text>] [--from <date>] [--to <date>] [--limit <n>] [--json] [--output <path>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--action` | No | Filter by action type (e.g., apply, verify, export-emass) |
| `--target` | No | Filter by target substring |
| `--from` | No | Start date (ISO 8601) |
| `--to` | No | End date (ISO 8601) |
| `--limit` | No | Max entries (default: 50) |
| `--json` | No | Output as JSON |
| `--output` | No | Write results to file (CSV or JSON) |

### `audit-verify`
Verify integrity of the audit trail chain.

```
audit-verify
```

Returns exit code 0 if chain is valid, 1 if tampered.

---

## Export Commands

### `export-poam`
Export standalone POA&M (Plan of Action & Milestones) from a bundle.

```
export-poam --bundle <path> [--output <path>] [--system-name <name>]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--output` | No | Output directory override |
| `--system-name` | No | System name override |

**Output files**: `poam.json`, `poam.csv`, `poam_summary.txt`

### `export-ckl`
Export STIG Viewer-compatible CKL (Checklist) from a bundle.

```
export-ckl --bundle <path> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--bundle` | Yes | Bundle root path |
| `--output` | No | Output directory override |
| `--file-name` | No | Output file name (default: stigforge_checklist.ckl) |
| `--host-name` | No | Host name for CKL ASSET section |
| `--host-ip` | No | Host IP for CKL ASSET section |
| `--host-mac` | No | Host MAC for CKL ASSET section |
| `--stig-id` | No | STIG ID for CKL header |

---

## Schedule Commands

### `schedule-verify`
Register a scheduled re-verification task in Windows Task Scheduler.

```
schedule-verify --name <name> --bundle <path> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--name` | Yes | Task name |
| `--bundle` | Yes | Bundle root path |
| `--frequency` | No | DAILY, WEEKLY, MONTHLY, ONCE (default: DAILY) |
| `--time` | No | Start time HH:mm (default: 06:00) |
| `--days` | No | Days of week for WEEKLY (e.g., MON,WED,FRI) |
| `--interval` | No | Interval in days for DAILY (default: 1) |
| `--verify-type` | No | scap, evaluate-stig, orchestrate (default: orchestrate) |
| `--scap-cmd` | No | SCAP/SCC executable path |
| `--scap-args` | No | SCAP arguments |
| `--evaluate-stig-root` | No | Evaluate-STIG root folder |
| `--evaluate-stig-args` | No | Evaluate-STIG arguments |
| `--output-root` | No | Output root for scan results |
| `--cli-path` | No | CLI executable path override |

### `schedule-remove`
Remove a scheduled re-verification task.

```
schedule-remove --name <name>
```

### `schedule-list`
List scheduled STIGForge verification tasks.

```
schedule-list
```

---

## Fleet Commands

### `fleet-apply`
Apply STIG hardening across multiple machines via WinRM/PSRemoting.

```
fleet-apply --targets <list> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--targets` | Yes | Comma-separated: host1,host2:ip,host3 |
| `--remote-cli-path` | No | STIGForge CLI path on remote machines |
| `--remote-bundle-path` | No | Bundle path on remote machines |
| `--mode` | No | Apply mode: AuditOnly, Safe, Full |
| `--concurrency` | No | Max concurrent machines (default: 5) |
| `--timeout` | No | Timeout per machine in seconds (default: 600) |
| `--json` | No | Output as JSON |

### `fleet-verify`
Run verification across multiple machines via WinRM/PSRemoting.

```
fleet-verify --targets <list> [options]
```

| Option | Required | Description |
|--------|----------|-------------|
| `--targets` | Yes | Comma-separated target list |
| `--remote-cli-path` | No | Remote CLI path |
| `--remote-bundle-path` | No | Remote bundle path |
| `--scap-cmd` | No | SCAP/SCC executable on remote |
| `--scap-args` | No | SCAP arguments |
| `--evaluate-stig-root` | No | Evaluate-STIG root on remote |
| `--concurrency` | No | Max concurrent (default: 5) |
| `--timeout` | No | Timeout seconds (default: 600) |
| `--json` | No | Output as JSON |

### `fleet-status`
Check WinRM connectivity for fleet targets.

```
fleet-status --targets <list> [--json]
```

### `fleet-credential-save`
Save encrypted credentials for a fleet target using DPAPI.

```
fleet-credential-save --host <hostname> --user <username> --password <password>
```

### `fleet-credential-list`
List all stored fleet credentials.

```
fleet-credential-list
```

### `fleet-credential-remove`
Remove stored credential for a fleet target.

```
fleet-credential-remove --host <hostname>
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error |
| 2 | Argument/validation error |
| 3 | File/directory not found |
