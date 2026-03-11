# STIGForge Lab Hardening Scripts

Repeatable STIG hardening scripts for Windows 11 workstations and Server 2019 domain controllers.

## Usage Order

### WS01 (Windows 11 Workstation)
1. `01-dc01-create-accounts.ps1` - Create admin accounts and OU structure on DC01
2. `02-dc01-create-gpos.ps1` - Create STIG GPOs + set Default Domain Policy account policies
3. `03-ws01-dsc-hardening.ps1` - Apply DSC configurations (Firewall, Defender, Edge, DotNet, Win11)
4. Policy/GPO refresh stage (`gpupdate /force`) - apply DISA/domain policy baseline to workstation
5. `05-ws01-install-certs.ps1` - Install DoD & ECA root CAs + cross-certs to Untrusted store
6. Custom GPO remediation stage (`gpupdate /force`) - apply workstation custom STIG GPO deltas
7. `04-ws01-local-hardening.ps1` - Script fallback remediation (BitLocker/non-GPO gaps)
6. `06-ws01-scan.ps1` - Run Evaluate-STIG and report compliance

Orchestrated WS01 run with per-step compliance tracking:
- `run-ws01-pipeline.ps1`

### DC01 (Server 2019 Domain Controller)
7. `07-dc01-dsc-hardening.ps1` - Apply DSC configurations (Server2019-DC, Firewall, Defender, DotNet, IE11)
7a. `07a-dc01-admx-lgpo.ps1` - Import ADMX templates to Central Store + DISA STIG GPO backups via Import-GPO/LGPO
8. `10-dc01-install-certs.ps1` - Install DoD & ECA root CAs + cross-certs to Untrusted store
9. `08-dc01-stig-gpos.ps1` - Create STIG-Server2019, STIG-Defender, STIG-Firewall GPOs for DC OU (custom complement)
10. `09-dc01-local-hardening.ps1` - AD/DNS/LDAP/NTP script fallback remediation for non-GPO gaps
11. `11-dc01-scan.ps1` - Run Evaluate-STIG and report compliance

Member Server orchestrated run with the same stage model:
- `run-ms01-pipeline.ps1`

When using `run-dc01-pipeline.ps1`, the enforced sequence is:
`DSC -> DISA ADMX/LGPO/GPO import -> certificate install -> custom STIG GPOs -> script fallback remediation`.

## Hyper-V Host Build/Test Shortcut

If your host details are maintained under `/home/anthonyscry/projects/lab-environment`, you can run STIGForge build/test commands on `triton-ajt` with:

```bash
scripts/lab-hardening/run-on-hyperv-host.sh "dotnet build STIGForge.sln -c Release"
scripts/lab-hardening/run-on-hyperv-host.sh "dotnet test STIGForge.sln -c Release --no-build"
```

Override defaults if needed with `HYPERV_HOST`, `HYPERV_USER`, `REMOTE_REPO`, and `LAB_ENV_ROOT`.

## Import Folder Contents
The `import/` directory is the canonical source for all deployment files. Copy its contents to `C:\temp\` on the target machine before running scripts.

### Tools
- `Evaluate-STIG.zip` - DISA Evaluate-STIG scanning tool
- `LGPO.zip` - Microsoft LGPO (Local Group Policy Object) tool
- `U_STIG_GPO_Package_January_2026.zip` - DISA STIG GPO backups + ADMX templates

### Certificates
- `dod_certs.zip` - DoD Root CA 3/4/5/6 PKCS7 bundles (from dl.dod.cyber.mil)
- `eca_certs.zip` - ECA Root CA 4/5 PKCS7 bundles (from dl.dod.cyber.mil)
- `fbca_crosscert_remover_v118.zip` - FBCA Cross-Certificate Remover (from dl.dod.cyber.mil)
- `crosscert_irca2_rootca3_49CBE933.cer` - IRCA2>DoD Root CA 3 cross-cert (from crt.sh CT logs)
- `crosscert_cceb2_rootca3_9B749645.cer` - CCEB2>DoD Root CA 3 cross-cert (from crt.sh CT logs)
- `crosscert_cceb2_rootca6_D471CA32.cer` - CCEB2>DoD Root CA 6 cross-cert (from crl.disa.mil)
- `InstallRoot_5.6x64.msi` - DoD PKE InstallRoot tool (optional, needs internet)

### PowerShell Modules (`modules/` subdirectory)
- `PowerSTIG.4.29.0.nupkg` + 15 DSC dependency modules (offline install, no internet required)
- Install via `00-install-modules.ps1`

## Expected Results

### WS01
- Baseline: ~22.5% (124/550)
- After full hardening: **99.6% (549/551)**
- 6 STIGs at 100%: DotNet, IE11, OneDrive, Edge, Defender, Firewall
- Win11 STIG: 99.2% (246/248)

### DC01
- Baseline: 31.7% (155/489)
- Projected after hardening: **~96%** (~470/489)
- See `DC01-STIG-ANALYSIS.md` for full breakdown of 334 Open + 86 Not_Reviewed findings

## Remaining Open — WS01 (2 findings)
- V-253470: MFA (requires smart card/PIV infrastructure)
- V-253476: LAPS (requires organizational LAPS deployment)

## Remaining Open — DC01 (~19 findings, after hardening)
- V-205723: Data files on separate partition (infrastructure)
- V-205848: TPM enabled (Hyper-V VM setting)
- V-205857: Secure Boot enabled (Hyper-V Gen2 VM)
- V-205864: VBS platform security (requires Gen2 VM + Secure Boot)
- V-259342: DNS forwarders (network architecture decision)
- Plus ~14 organizational/manual checks (see DC01-STIG-ANALYSIS.md)

## Classification-Based Answer File Strategy
See `DC01-STIG-ANALYSIS.md` for the full classification breakdown.

**Classified system (default):** V-205649, V-205650 → N/A (cross-certs are unclassified-only)
**Unclassified system:** V-205818 → N/A (NSA Type 1 crypto is classified-only)

Conditional N/A checkboxes planned for STIGForge OrgSettings:
- AD CS Installed, FTP Installed, Split DNS, Smart Card/PKI, Multiple DCs, SIEM, IDS/IPS, AppLocker/WDAC, Backup, FIM

## Key Lessons
- IE11 Feature Controls: must be REG_SZ "1" (not DWORD 1)
- Account policies: only apply from Default Domain Policy (not custom GPOs)
- Legal notice: single `\r\n` between paragraphs (Evaluate-STIG does exact match)
- BitLocker: use `manage-bde -on C: -UsedSpaceOnly` (Enable-BitLocker has parameter bugs)
- IE11 SecureProtocols=2048 (TLS 1.2 only), Zone4/1A00=196608 (anonymous logon)
- certutil: rejects non-root certs to Root store; import chains to CA first, then extract roots
- Cross-certs: FBCA tool handles Federal Bridge certs; IRCA2/CCEB2 cross-certs must be imported
  separately from .cer files (DISA repos only serve current versions, older issuances from CT logs)
- InstallRoot TAMP: fails with exit code 6 from non-NIPRNet; TAMP signature verification also fails
  on fresh installs; local database is unreadable without valid TAMP signer cert
- DC hardening: PowerSTIG DSC first (bulk settings), then GPO complement, then local hardening gaps
- DC user rights differ from workstations (e.g., SeNetworkLogonRight includes Enterprise Domain Controllers)
- ASR rules: 16 GUIDs for Defender Attack Surface Reduction (see 08-dc01-stig-gpos.ps1)

## TODO: Close Remaining WS01 Findings

### V-253476 — LAPS (Local Administrator Password Solution)
DC01 is on Server 2019 Build 17763.3650 (Nov 2022). Windows LAPS requires April 2023+ (KB5025229).
1. Checkpoint DC01
2. Update DC01 via `sconfig` → option 6 (install all updates, ~30 min + reboots)
3. `Update-LapsADSchema` on DC01 (extends AD schema with msLAPS-* attributes)
4. `Set-LapsADComputerSelfPermission -Identity "OU=Workstations,DC=lab,DC=local"`
5. `Set-LapsADReadPasswordPermission -Identity "OU=Workstations,DC=lab,DC=local" -AllowedPrincipals "lab\Domain Admins"`
6. Create STIG-LAPS GPO (BackupDirectory=2/AD, PasswordAgeDays=60, PasswordLength=14, PasswordComplexity=4)
7. Link GPO to OU=Workstations, `gpupdate /force` on WS01
8. `Invoke-LapsPolicyProcessing` on WS01, verify via `Get-LapsADPassword -Identity WS01` on DC01
- WS01 (Win11 24H2 Build 26200) already has Windows LAPS client built in — no install needed

### V-253470 — MFA
Requires smart card/PIV reader hardware + certificate enrollment infrastructure (AD CS or third-party).
Not feasible in current lab without hardware.

## Prerequisites
- DC01: Active Directory domain controller with lab.local domain
- WS01: Windows 11 domain-joined workstation in OU=Workstations
- PowerSTIG + Evaluate-STIG deployed to WS01 and DC01
- AuditSystemDsc 1.1.0 + CertificateDsc 5.0.0 modules on both machines
