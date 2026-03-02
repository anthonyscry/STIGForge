---
feature: gap-program
type: technical-spec
status: complete
---

# GAP (Gap Analysis Program) Features

## Overview

Comprehensive compliance workflow enhancements addressing 8 GAP areas for production deployment. Provides per-rule remediation, dry-run preview, exception management, and advanced security integration.

## GAP Feature Matrix

| ID | Feature | Status | Service | CLI Commands |
|----|---------|--------|---------|--------------|
| GAP-1 | Per-rule Remediation | ✅ | `RemediationRunner` | `remediate`, `remediate-list` |
| GAP-2 | Dry-Run Preview | ✅ | `DryRunCollector` | `--dry-run` flag |
| GAP-3 | Granular Filtering | ✅ | `ControlFilterService` | `--filter-*` options |
| GAP-4 | Compliance Scoring | ✅ | `ComplianceTrendService` | `compliance-score`, `compliance-trend` |
| GAP-6 | Exception Lifecycle | ✅ | `ExceptionWorkflowService` | `exception-*` |
| GAP-7 | Advanced Security | ✅ | `SecurityFeatureRunner` | `security-*` |
| GAP-8 | STIG Release Monitor | ✅ | `StigReleaseMonitorService` | `check-release`, `release-notes` |

---

## GAP-1: Per-Rule Remediation

**Purpose:** Targeted remediation for individual controls without full bundle re-apply

**Handlers:**

| Handler | Registry Path | Supports |
|---------|---------------|----------|
| `RegistryRemediationHandler` | HKLM/HKCU keys | Set values, delete keys |
| `AuditPolicyRemediationHandler` | Auditpol.exe | Advanced audit policies |
| `ServiceRemediationHandler` | Services registry | Start/stop, change startup |

**Usage:**
```powershell
dotnet run --project src/STIGForge.Cli -- remediate --bundle C:\bundle --rule-id SV-12345
```

---

## GAP-2: Dry-Run Preview

**Purpose:** Preview changes before applying them

**Components:**
- `DryRunCollector`: Records what would change
- `DscWhatIfParser`: Parses PowerShell DSC What-If output

**Output:** `DryRunReport` with:
- Rules that would be modified
- Current vs proposed values
- Risk assessment

**Usage:**
```powershell
dotnet run --project src/STIGForge.Cli -- apply-run --bundle C:\bundle --dry-run
```

---

## GAP-3: Granular Control Filtering

**Purpose:** Apply only specific controls based on criteria

**Filters:**
- Rule ID (exact match or pattern)
- Severity (Critical, High, Medium, Low)
- Category (Registry, Audit, Service, etc.)
- Vuln ID

**Usage:**
```powershell
dotnet run --project src/STIGForge.Cli -- apply-run --bundle C:\bundle --filter-severity Critical,High --filter-category Registry
```

---

## GAP-4: Compliance Scoring and Trends

**Purpose:** Track compliance over time with trend analysis

**Models:**
```csharp
public class ComplianceSnapshot
{
    public DateTime Timestamp { get; set; }
    public double CompliancePercentage { get; set; }
    public int TotalControls { get; set; }
    public int PassedControls { get; set; }
    public int FailedControls { get; set; }
    public int NotReviewedControls { get; set; }
}
```

**Storage:** SQLite with `IComplianceTrendRepository`

**Commands:**
```powershell
# Current score
dotnet run --project src/STIGForge.Cli -- compliance-score --bundle C:\bundle

# Historical trends
dotnet run --project src/STIGForge.Cli -- compliance-trend --days 30 --json
```

---

## GAP-6: Exception and Waiver Lifecycle

**Purpose:** Formal exception management for non-applicable controls

**States:**
- `Draft` → `Submitted` → `Approved` → `Active` → `Expired`/`Revoked`

**Model:**
```csharp
public class ControlException
{
    public string Id { get; set; }
    public string RuleId { get; set; }
    public string RequestedBy { get; set; }
    public string ApprovedBy { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public string Justification { get; set; }
    public ExceptionStatus Status { get; set; }
}
```

**Commands:**
```powershell
dotnet run -- exception-create --rule-id SV-12345 --justification "Not in scope" --valid-until 2026-12-31
dotnet run -- exception-list --status Active
dotnet run -- exception-revoke --exception-id EXC-001
```

---

## GAP-7: Advanced Security Features

### WDAC (Windows Defender Application Control)

**Service:** `WdacPolicyService`
- Deploy signed policies
- Audit vs Enforced modes
- Policy refresh

```powershell
dotnet run -- security-wdac-deploy --policy-file C:\policies\baseline.xml --mode Enforced
```

### BitLocker

**Service:** `BitLockerService`
- Check encryption status
- Initiate encryption
- Recovery key escrow

```powershell
dotnet run -- security-bitlocker-status --drive C:
dotnet run -- security-bitlocker-encrypt --drive D: --recovery-location AD
```

### Firewall

**Service:** `FirewallRuleService`
- Add/remove rules
- Enable/disable profiles
- Block/allow by port/program

```powershell
dotnet run -- security-firewall-add --name "Block-RDP" --direction Inbound --protocol TCP --local-port 3389 --action Block
```

---

## GAP-8: STIG Release Monitoring

**Purpose:** Track DISA STIG releases and notify of updates

**Features:**
- Check DISA download site for new releases
- Compare local pack versions
- Generate release notes diff
- Email/webhook notifications

**Storage:** `ReleaseCheck` entity with SQLite repository

**Commands:**
```powershell
# Check for updates
dotnet run -- check-release --stig-id Windows_11_STIG

# View release notes
dotnet run -- release-notes --stig-id Windows_11_STIG --since 2026-01-01
```

---

## Integration Points

- **Apply Runner:** Uses `DryRunCollector` for `--dry-run` mode
- **Bundle Orchestrator:** Uses `ControlFilterService` for filtered applies
- **CLI:** All services exposed via commands
- **WPF:** Future integration for GUI exception management

**Tests:** 15 unit test files, 5 integration test files  
**Coverage:** All GAP services have unit and integration tests
