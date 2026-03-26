# STIGForge Lab Hardening Scripts

Repeatable STIG hardening scripts for Windows 11 workstations, Server 2019 domain controllers, and member servers.

## Architecture

```
scripts/lab-hardening/
├── lib/                          # Shared modules (no duplication)
│   ├── StigForge-Common.psm1    #   E-STIG/SCC discovery, scan, CKL parse, logging, timing
│   ├── StigForge-GPO.psm1       #   Set-GPReg, SecurityPolicy, AuditPolicy, GPO linking
│   ├── StigForge-Certificates.psm1  #   DoD/ECA/FBCA/cross-cert/InstallRoot install
│   └── StigForge-WinRM.psm1     #   WinRM state capture/disable/restore
│
├── run-all-pipeline.ps1          # Master pipeline: auto-detect role, full metrics + SCC
├── run-dc01-pipeline.ps1         # DC01-specific pipeline
├── run-ws01-pipeline.ps1         # WS01-specific pipeline
├── run-ms01-pipeline.ps1         # MS01-specific pipeline
│
├── 00-install-modules.ps1        # PowerSTIG + DSC + E-STIG offline install
├── 01-dc01-create-accounts.ps1   # AD OUs, accounts, security groups
├── 02-dc01-create-gpos.ps1       # WS + common STIG GPOs (IE11, Win11, DotNet, Edge, OneDrive)
├── 03-ws01-dsc-hardening.ps1     # WS DSC: Firewall, Defender, DotNet, Edge, Win11
├── 04-ws01-local-hardening.ps1   # WS non-GPO: BitLocker encryption start
├── 05-ws01-install-certs.ps1     # WS certificate installation (uses lib module)
├── 06-ws01-scan.ps1              # WS standalone scan
├── 07-dc01-dsc-hardening.ps1     # Server DSC: auto-detects DC vs MS role
├── 07a-dc01-admx-lgpo.ps1       # ADMX templates + DISA GPO import via LGPO
├── 08-dc01-stig-gpos.ps1        # DC/MS STIG GPOs (Server2019, Defender, Firewall)
├── 09-dc01-local-hardening.ps1   # Server non-GPO: AD, DNS, LDAP, NTP
├── 09a-dc01-ie11-hardening.ps1   # Optional IE11 registry fallback
├── 10-dc01-install-certs.ps1     # DC/MS certificate installation (uses lib module)
├── 11-dc01-scan.ps1              # DC standalone scan
│
├── import/                       # Canonical source for all offline tools
│   ├── modules/*.nupkg           #   PowerSTIG + 15 DSC dependencies
│   ├── Evaluate-STIG.zip         #   DISA E-STIG scanning tool
│   ├── scc-5.14_Windows_bundle.zip      #   DISA SCAP Compliance Checker (required)
│   ├── LGPO.zip                  #   Microsoft LGPO tool
│   ├── U_STIG_GPO_Package_January_2026.zip  #   DISA STIG GPO backups + ADMX
│   ├── dod_certs.zip             #   DoD Root CA 3/4/5/6
│   ├── eca_certs.zip             #   ECA Root CA 4/5
│   ├── fbca_crosscert_remover_v118.zip  #   FBCA cross-cert remover
│   ├── crosscert_*.cer           #   3 IRCA2/CCEB2 cross-certs
│   └── InstallRoot_5.6x64.msi   #   DoD PKE InstallRoot tool
│
└── run-on-hyperv-host.sh         # Build/test on Hyper-V host
```

## Quick Start

### Master Pipeline (recommended)
```powershell
# Auto-detects DC/MS/WS role, runs full hardening + SCC, records metrics
.\run-all-pipeline.ps1

# Force a specific role
.\run-all-pipeline.ps1 -Role DC
.\run-all-pipeline.ps1 -Role WS

# Baseline scan only (no hardening)
.\run-all-pipeline.ps1 -BaselineOnly
```

### Individual Pipelines
```powershell
.\run-dc01-pipeline.ps1    # DC01 with per-STIG breakdown
.\run-ms01-pipeline.ps1    # MS01 (reuses DC scripts, auto-detects MS)
.\run-ws01-pipeline.ps1    # WS01
```

## Pipeline Stages

```
0. Module install → 0b. AD setup (DC only) → 1. Baseline scan
→ 2. DSC hardening → 3. ADMX/LGPO import → 4. Certificate install
→ 5. Custom GPOs → 6. Script fallback → 7. SCC final scan
```

Each stage runs a compliance scan afterward. Results include:
- Before/after compliance percentages
- Per-step delta tracking
- Per-STIG family breakdown (DC pipeline)
- SCC XCCDF results (baseline + final)
- Per-step wall clock timing
- Estimated human time saved vs manual

## Import Folder Contents

Copy everything in `import/` to `C:\temp\` on the target machine before running.

| File | Used By | Purpose |
|------|---------|---------|
| `modules/*.nupkg` | `00-install-modules.ps1` | PowerSTIG + DSC deps |
| `Evaluate-STIG.zip` | All pipelines | DISA compliance scanner |
| `scc-5.14_Windows_bundle.zip` | `run-all-pipeline.ps1` | SCAP Compliance Checker (portable, no install) |
| `LGPO.zip` | `07a-dc01-admx-lgpo.ps1` | Local policy tool |
| `U_STIG_GPO_Package_*.zip` | `07a-dc01-admx-lgpo.ps1` | DISA GPO backups + ADMX |
| `dod_certs.zip` | `05/10-*-install-certs.ps1` | DoD Root CA bundles |
| `eca_certs.zip` | `05/10-*-install-certs.ps1` | ECA Root CA bundles |
| `fbca_crosscert_remover_*.zip` | `05/10-*-install-certs.ps1` | FBCA cross-cert tool |
| `crosscert_*.cer` | `05/10-*-install-certs.ps1` | IRCA2/CCEB2 cross-certs |
| `InstallRoot_5.6x64.msi` | `05/10-*-install-certs.ps1` | DoD PKE InstallRoot |

## Metrics Output

The master pipeline writes `metrics.json` with:
```json
{
  "host": "DC01",
  "role": "DC",
  "totalSeconds": 1847.3,
  "baseline": { "compliance": 31.7, "open": 334, ... },
  "final": { "compliance": 96.2, "open": 19, ... },
  "delta": { "complianceGain": 64.5, "findingsFixed": 315 },
  "sccResults": { "baseline": [...], "final": [...] },
  "humanEstimate": {
    "manualHours": 15.8,
    "automatedHours": 0.51,
    "hoursSaved": 15.3,
    "speedupFactor": "31x"
  }
}
```

## Expected Results

### WS01
- Baseline: ~22.5% (124/550)
- After full hardening: **99.6% (549/551)**
- 6 STIGs at 100%: DotNet, IE11, OneDrive, Edge, Defender, Firewall

### DC01
- Baseline: 31.7% (155/489)
- Projected after hardening: **~96%** (~470/489)

## Hyper-V Host Build/Test

```bash
scripts/lab-hardening/run-on-hyperv-host.sh "dotnet build STIGForge.sln -c Release"
scripts/lab-hardening/run-on-hyperv-host.sh "dotnet test STIGForge.sln -c Release --no-build"
```

## Key Lessons
- IE11 Feature Controls: must be REG_SZ "1" (not DWORD 1)
- Account policies: only apply from Default Domain Policy (not custom GPOs)
- Legal notice: single `\r\n` between paragraphs (Evaluate-STIG does exact match)
- BitLocker: use `manage-bde -on C: -UsedSpaceOnly` (Enable-BitLocker has parameter bugs)
- Cross-certs: FBCA tool handles Federal Bridge certs; IRCA2/CCEB2 require separate .cer import
- DC user rights differ from workstations (SeNetworkLogonRight includes Enterprise Domain Controllers)
- Evaluate-STIG cannot run inside WinRM sessions (detects wsmprovhost as concurrent process)
