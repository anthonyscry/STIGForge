---
feature: dc-lgpo-gpo
type: technical-spec
status: complete
---

# DC Auto-Detection, LGPO Staging, and GPO Import

## Overview

Enterprise domain controller support for PowerSTIG hardening in Active Directory environments. Automatically detects DC role, stages LGPO.exe for local policy, and imports domain GPO backups.

## Domain Controller Auto-Detection

**Detection Logic:**
```csharp
// Check NTDS service registry key
var isDomainController = Registry.LocalMachine.OpenSubKey(
    @"SYSTEM\CurrentControlSet\Services\NTDS") != null;
```

**Impact on PowerSTIG:**
- Sets `OsRole='DC'` in compiled DSC configuration
- Uses Domain Controller STIG baseline instead of Member Server
- Affects applicable rules and settings

**Implementation:**
- `ApplyRunner.DetectDomainControllerRole()`
- Called before PowerSTIG compilation
- Logged to audit trail

---

## LGPO.exe Staging

**Purpose:** Make Local Group Policy Object utility available for policy application

**Source:** Import ZIP files (`import/` directory)

**Staging Process:**
1. Scan import directory for LGPO.exe
2. Extract to `tools/lgpo/` directory
3. Verify executable signature
4. Set executable permissions

**Resolution:**
```csharp
// Runtime resolution via AppContext.BaseDirectory
var lgpoPath = Path.Combine(
    AppContext.BaseDirectory, 
    "tools", "lgpo", "LGPO.exe");
```

**Implementation:**
- `LocalSetupValidator.StageLgpoFromImport()`
- Called during `workflow-local` setup
- Integrated into `apply-run` validation

---

## GPO Import for Domain Controllers

**Purpose:** Import domain GPO backups into local policy for hardening

**When:** Only on detected Domain Controllers

**Process:**
1. `apply_gpo_import` step added to DC apply workflow
2. Scans `GPO/` subdirectory in bundle
3. For each GPO backup folder:
   - Parse `manifest.xml` for GPO metadata
   - Run `Import-GPO` with target domain
   - Log import results

**PowerShell:**
```powershell
Import-GPO -BackupId $gpoGuid -Path $backupPath -TargetName $gpoName
```

**Implementation:**
- `ApplyRunner.ApplyGpoImport()` (lines in ApplyRunner.cs)
- Added to apply models: `ApplyGpoImportOptions`
- Integrated into `apply-run` CLI command

---

## Workflow Integration

### apply-run Command Changes

```powershell
# Standard member server
dotnet run -- apply-run --bundle C:\bundle --powerstig-module C:\PowerStig

# Domain controller (auto-detected)
dotnet run -- apply-run --bundle C:\bundle --powerstig-module C:\PowerStig
# Automatically uses OsRole='DC' and runs GPO import
```

### Options

| Option | Description |
|--------|-------------|
| `--gpo-import` | Force GPO import (even on non-DC) |
| `--gpo-path` | Custom GPO backup directory |
| `--lgpo-path` | Custom LGPO.exe path |

---

## File Changes

**Commit:** 88236cd

| File | Changes |
|------|---------|
| `ApplyRunner.cs` | +101 lines: DC detection, LGPO resolution, GPO import |
| `ApplyModels.cs` | +3: GPO import options |
| `LgpoRunner.cs` | +5: Path resolution logic |
| `WorkflowViewModel.cs` | +43: WPF UI updates for DC status |
| `ImportInboxScanner.cs` | +12: LGPO detection in imports |

---

## Testing

**Scenarios:**
1. Member Server: No DC detection, no GPO import
2. Domain Controller: Auto-detects, stages LGPO, imports GPOs
3. Custom LGPO path: Uses provided path
4. Missing LGPO: Error with remediation guidance

**Environment Requirements:**
- Windows Server with AD DS role for full testing
- Import ZIP containing LGPO.exe for staging tests
- GPO backup files for import tests

---

## Security Considerations

- GPO import requires Domain Admin or delegated permissions
- LGPO.exe must be signed Microsoft executable
- GPO backups validated before import (checksum verification)
- Audit logging for all GPO import operations

---

## Future Enhancements

- Read-only Domain Controller (RODC) detection
- Multi-domain forest support
- GPO backup creation (export current policy)
- Group Policy Results (RSoP) integration
