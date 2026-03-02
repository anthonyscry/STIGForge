---
feature: powerstig-bundling
type: technical-spec
status: complete
---

# PowerSTIG Dependency Bundling (Air-Gapped Support)

## Overview

Self-healing dependency management for PowerSTIG in air-gapped environments. Automatically stages required PowerShell modules from bundled resources or local cache, eliminating internet dependency during hardening.

## Problem

**Air-Gapped Constraints:**
- No internet access to PowerShell Gallery
- Missing DSC resource modules (AuditPolicyDsc, SecurityPolicyDsc, etc.)
- `Import-DscResource` fails at parse time if modules not available
- PowerSTIG compilation requires 6+ dependent modules

## Solution

**Self-Healing Module Staging:**
1. Detect missing dependencies at harden time
2. Stage from bundled ZIP resources
3. Add to PSModulePath for discovery
4. Retry compilation

---

## Module Inventory

**PowerSTIG Dependencies:**

| Module | Purpose | Bundled |
|--------|---------|---------|
| PowerSTIG | STIG automation engine | ✅ Yes |
| AuditPolicyDsc | Advanced audit policy | ✅ Yes |
| SecurityPolicyDsc | Local security policy | ✅ Yes |
| AccessControlDsc | ACL management | ✅ Yes |
| FileContentDsc | File content rules | ✅ Yes |
| XmlContentDsc | XML content rules | ✅ Yes |
| WmiDsc | WMI configuration | ✅ Yes |
| nx | Linux DSC resources | ✅ Yes |

---

## Staging Process

### Detection

```csharp
// Check if Import-DscResource would succeed
var missingModules = DetectMissingDscResources(requiredModules);
```

### Extraction

```powershell
# From bundled resources or local cache
Expand-Archive -Path $bundledModulesZip -DestinationPath $psModulePath
```

### Path Configuration

```csharp
// Add staged modules to process PSModulePath
Environment.SetEnvironmentVariable(
    "PSModulePath", 
    $"{stagedModulePath};{currentPsModulePath}",
    EnvironmentVariableTarget.Process);
```

### Version Pinning

```powershell
# Use -ModuleVersion to avoid multiple-version conflicts
Import-DscResource -ModuleName AuditPolicyDsc -ModuleVersion 1.4.0
```

---

## Implementation

### ApplyRunner Integration

**Method:** `StagePowerStigDependencies()`

**Flow:**
1. Check if PowerSTIG module is available
2. If missing, extract from bundled `PSModules.zip`
3. Verify all required dependencies present
4. Unblock files (Remove-Item -Path *Zone.Identifier)
5. Set PSModulePath
6. Return staging result

### Air-Gapped Validation

```csharp
public class DependencyStagingResult
{
    public bool Success { get; set; }
    public List<string> StagedModules { get; set; }
    public List<string> MissingModules { get; set; }
    public string StagingPath { get; set; }
}
```

---

## Error Handling

### Partial DSC Failures

**Issue:** DSC LCM may report failure even when configuration was applied  
**Fix:** Treat as success when LCM shows `Applied` status

```csharp
// In ApplyRunner.cs
if (lcmStatus == "Applied" && dscResult.Failed)
{
    _logger.LogWarning("DSC reported failure but LCM shows Applied; treating as success");
    return ApplyResult.Success;
}
```

### Execution Policy Rejection

**Issue:** Downloaded modules blocked by Windows execution policy  
**Fix:** Unblock files during staging

```powershell
Get-ChildItem -Path $modulePath -Recurse | Unblock-File
```

---

## CLI Integration

### apply-run Command

```powershell
# Auto-stages dependencies if missing
dotnet run -- apply-run --bundle C:\bundle --powerstig-module C:\PowerStig

# With bundled modules
dotnet run -- apply-run --bundle C:\bundle --powerstig-bundled-modules C:\bundle\PSModules.zip
```

### Options

| Option | Description |
|--------|-------------|
| `--powerstig-bundled-modules` | Path to bundled modules ZIP |
| `--skip-dependency-check` | Skip staging (fail fast if missing) |
| `--dependency-staging-path` | Custom path for staging |

---

## Bundle Structure

**PSModules.zip Contents:**
```
PSModules/
├── PowerSTIG/
│   └── 4.20.0/
├── AuditPolicyDsc/
│   └── 1.4.0/
├── SecurityPolicyDsc/
│   └── 2.10.0/
└── ...
```

**Generated During:**
- Build with `-BundlePowerStig` flag
- Import with `import-pack --include-modules`
- Manual download for air-gap transfer

---

## Testing

**Unit Tests:**
- `DependencyStagingTests` — staging logic
- `ModuleDiscoveryTests` — PSModulePath resolution
- `VersionPinningTests` — Import-DscResource syntax

**Integration Tests:**
- Air-gapped VM with no internet
- Missing modules scenario
- Partial failure handling

**Manual Validation:**
1. Disconnect VM from internet
2. Run `apply-run` without pre-staged modules
3. Verify auto-staging succeeds
4. Verify hardening completes

---

## Version History

| Commit | Change |
|--------|--------|
| d99fb0e | Auto-install missing dependencies |
| 3dca22f | Stage all dependencies for air-gapped |
| 8964fc0 | Diagnostic logging for staging |
| ce1ad408 | Self-heal missing dependencies |
| 1c11d9e | Add nx module + OS detection fallback |
| 6f6b6a8 | Set PSModulePath on process environment |
| 289d69a | Fix PSModulePath resolution |
| 5e35e3d | Bundle PowerSTIG dependencies |
| cf105ac | Auto-install with air-gapped fallback |
| 4653137 | Treat partial DSC failures as success |
| 9acf34a | Unblock DSC module files |

---

## Air-Gapped Deployment Guide

1. **Download bundles** on internet-connected machine:
   ```powershell
   dotnet run -- mission-autopilot --niwc-source-url ...
   ```

2. **Transfer to air-gapped environment:**
   - `.stigforge/airgap-transfer/` contains cached modules

3. **Import on target:**
   ```powershell
   dotnet run -- import-pack --include-modules C:\transfer\pack-with-modules.zip
   ```

4. **Apply:**
   ```powershell
   dotnet run -- apply-run --bundle C:\bundle
   # Dependencies auto-stage from bundled resources
   ```
