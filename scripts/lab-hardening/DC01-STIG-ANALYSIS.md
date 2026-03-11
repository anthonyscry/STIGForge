# DC01 STIG Analysis — Automation & Classification Breakdown

**Baseline Scan**: 155/489 (31.7%) | Open=334 | Not_Reviewed=86 | NA=76

## Summary by STIG

| STIG | NotAFinding | Open | NR | NA | Automatable |
|------|-----------|------|----|----|-------------|
| ADDomain | 10/13 (76.9%) | 3 | 16 | 7 | 1 of 3 |
| ADForest | 2/3 (66.7%) | 1 | 4 | 0 | 1 of 1 |
| DotNET4 | 9/10 (90%) | 1 | 0 | 6 | 1 of 1 |
| IE11 | 2/137 (1.5%) | 135 | 0 | 0 | 135 of 135 |
| MSDefender | 18/66 (27.3%) | 48 | 2 | 0 | 48 of 48 |
| WinFirewall | 0/20 (0%) | 20 | 0 | 1 | 20 of 20 |
| WinServer2019 | 104/224 (46.4%) | 120 | 39 | 20 | ~105 of 120 |
| WinServerDNS | 10/16 (62.5%) | 6 | 25 | 42 | 4 of 6 |
| **TOTAL** | **155/489** | **334** | **86** | **76** | **~315 of 334** |

**Projected after hardening**: ~470/489 (**96.1%**) — 19 remaining manual/infrastructure/hardware

---

## AUTOMATABLE OPEN FINDINGS (~315)

### IE11 — 135 Open (ALL automatable via GPO)

Same registry keys as WS01. Link existing STIG-IE11 GPO to Domain Controllers OU.

### MSDefender — 48 Open (ALL automatable via GPO)

Create STIG-Defender GPO with these registry settings:

| VulnID | Setting | Registry Path | Value |
|--------|---------|--------------|-------|
| V-213426 | PUA blocking | `Windows Defender\MpEngine\MpEnablePus` | 1 |
| V-213433 | MAPS real-time check | `Windows Defender\SpyNet\SpynetReporting` | 2 |
| V-213434 | Join MAPS | `Windows Defender\SpyNet\SpynetReporting` | 2 |
| V-213435 | Safe samples only | `Windows Defender\SpyNet\SubmitSamplesConsent` | 1 |
| V-213449 | Scan removable drives | `Windows Defender\Scan\DisableRemovableDriveScanning` | 0 |
| V-213450 | Weekly scheduled scan | `Windows Defender\Scan\ScheduleDay` | 1 |
| V-213451 | Email scanning | `Windows Defender\Scan\DisableEmailScanning` | 0 |
| V-213452 | Spyware def age ≤7 days | `Windows Defender\Signature Updates\ASSignatureDue` | 7 |
| V-213453 | Virus def age ≤7 days | `Windows Defender\Signature Updates\AVSignatureDue` | 7 |
| V-213454 | Check defs daily | `Windows Defender\Signature Updates\ScheduleDay` | 0 |
| V-213455 | Remediation: Severe | `Windows Defender\Threats\ThreatSeverityDefaultAction\5` | 2 |
| V-213464 | Remediation: High | `Windows Defender\Threats\ThreatSeverityDefaultAction\4` | 2 |
| V-213465 | Remediation: Medium | `Windows Defender\Threats\ThreatSeverityDefaultAction\2` | 2 |
| V-213466 | Remediation: Low | `Windows Defender\Threats\ThreatSeverityDefaultAction\1` | 2 |
| V-213463 | Network protection | `Windows Defender Exploit Guard\Network Protection\EnableNetworkProtection` | 1 |
| V-278672 | Network protection (server) | (same as above) | 1 |

**ASR Rules** (V-213456 through V-213462, V-278647 through V-278655):
All under `Windows Defender\Windows Defender Exploit Guard\ASR\Rules\{GUID}` = `1`

| VulnID | Rule | GUID |
|--------|------|------|
| V-213456 | Block executable email content | BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550 |
| V-213457 | Block Office child processes | D4F940AB-401B-4EFC-AADC-AD5F3C50688A |
| V-213458 | Block Office executable content | 3B576869-A4EC-4529-8536-B80A7769E899 |
| V-213459 | Block Office inject into processes | 75668C1F-73B5-4CF0-BB93-3ECF5CB7CC84 |
| V-213460 | Block JS/VBS launching executables | D3E037E1-3EB8-44C8-A917-57927947596D |
| V-213461 | Block obfuscated scripts | 5BEB7EFE-FD9A-4556-801D-275E5FFC04CC |
| V-213462 | Block Win32 imports from macros | 92E97FA1-2EDF-4476-BDD6-9DD0B4DDDC7B |
| V-278647 | Block Adobe Reader child processes | 7674BA52-37EB-4A4F-A9A1-F0F9A1619A2C |
| V-278648 | Block credential stealing from LSASS | 9E6C4E1F-7D60-472F-BA1A-A39EF669E4B2 |
| V-278649 | Block untrusted USB processes | B2B3F03D-6A65-4F7B-A9C7-1C7EF74A9BA4 |
| V-278650 | Advanced ransomware protection | C1DB55AB-C21A-4637-BB3F-A12568109D35 |
| V-278651 | Block PSExec/WMI process creation | D1E49AAC-8F56-4280-B9BA-993A6D77406C |
| V-278652 | Block WMI event subscription persistence | E6DB77E5-3DF2-4CF1-B95A-636979351E5B |
| V-278653 | Block executables unless prevalence/age/trusted | 01443614-CD74-433A-B99E-2ECDC07BFC25 |
| V-278654 | Block Office comms app child processes | 26190899-1602-49E8-8B27-EB1D0A1CE869 |
| V-278655 | Block exploited vulnerable signed drivers | 56A863A9-875E-4185-98A7-B882C64B5CE5 |

**Additional Defender settings** (V-278656 through V-278680, V-278863):

| VulnID | Setting | Registry Subkey | Value |
|--------|---------|----------------|-------|
| V-278656 | Disable local admin merge | `DisableLocalAdminMerge` | 1 |
| V-278658 | Hide exclusions from local admins | `HideExclusionsFromLocalAdmins` | 1 |
| V-278659 | Randomize scheduled tasks | `RandomizeScheduleTaskTimes` | 1 |
| V-278660 | Hide Family options area | `Windows Security\Family options\UILockdown` | 1 |
| V-278661 | File hash computation | `MpEngine\EnableFileHashComputation` | 1 |
| V-278662 | Extended cloud check (50s) | `MpEngine\MpBafsExtendedTimeout` | 50 |
| V-278668 | Script scanning | `Real-Time Protection\DisableScriptScanning` | 0 |
| V-278669 | OOBE real-time protection | `Real-Time Protection\OobeEnableRtpAndSigUpdate` | 1 |
| V-278674 | EDR block mode | `Configuration\ForceDefenderPassiveMode` | 0 |
| V-278675 | Dynamic Signature dropped events | `Reporting\DisableGenericReports` | 0 |
| V-278676 | Scan excluded files in quick scans | `Scan\QuickScanIncludeExclusions` | 1 |
| V-278677 | Convert warn to block | `MpEngine\EnableConvertWarnToBlock` | 1 |
| V-278678 | Asynchronous inspection | `Real-Time Protection\DisableAsyncScanOnOpen` | 0 |
| V-278679 | Scan packed executables | `Scan\DisablePackedExeScanning` | 0 |
| V-278680 | Enable heuristics | `Scan\DisableHeuristics` | 0 |
| V-278863 | Cloud protection level High | `MpEngine\MpCloudBlockLevel` | 2 |

### WinFirewall — 20 Open (ALL automatable via GPO)

Create STIG-Firewall GPO. All under `HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\`:

| Profile | EnableFirewall | DefaultInbound | DefaultOutbound | LogFileSize | LogDropped | LogSuccessful | LocalPolicyMerge | LocalIPsecMerge |
|---------|---------------|---------------|----------------|-------------|------------|--------------|-----------------|----------------|
| Domain | 1 | 1 (block) | 0 (allow) | 16384 | 1 | 1 | — | — |
| Private | 1 | 1 (block) | 0 (allow) | 16384 | 1 | 1 | — | — |
| Public | 1 | 1 (block) | 0 (allow) | 16384 | 1 | 1 | 0 | 0 |

### WinServer2019 — 120 Open (105 automatable, 15 manual)

#### Registry/GPO (73 settings)

**WinRM:**
| VulnID | Setting | Key | ValueName | Value |
|--------|---------|-----|-----------|-------|
| V-205711 | Client no Basic auth | `WinRM\Client` | AllowBasic | 0 |
| V-205712 | Client no Digest auth | `WinRM\Client` | AllowDigest | 0 |
| V-205713 | Service no Basic auth | `WinRM\Service` | AllowBasic | 0 |
| V-205810 | Service no RunAs creds | `WinRM\Service` | DisableRunAs | 1 |
| V-205816 | Client no unencrypted | `WinRM\Client` | AllowUnencryptedTraffic | 0 |
| V-205817 | Service no unencrypted | `WinRM\Service` | AllowUnencryptedTraffic | 0 |

**AutoPlay/AutoRun:**
| VulnID | Setting | Key | ValueName | Value |
|--------|---------|-----|-----------|-------|
| V-205804 | No autoplay non-volume | `Explorer` | NoAutoplayfornonVolume | 1 |
| V-205805 | No autorun | `Policies\Explorer` | NoAutorun | 1 |
| V-205806 | No autoplay all drives | `Policies\Explorer` | NoDriveTypeAutoRun | 255 |

**UAC:**
| VulnID | Setting | Key | ValueName | Value |
|--------|---------|-----|-----------|-------|
| V-205714 | No admin enum | `Policies\CredUI` | EnumerateAdministrators | 0 |
| V-205717 | Consent on secure desktop | `Policies\System` | ConsentPromptBehaviorAdmin | 2 |
| V-205811 | Built-in admin approval | `Policies\System` | FilterAdministratorToken | 1 |
| V-205812 | Deny standard user elevation | `Policies\System` | ConsentPromptBehaviorUser | 0 |
| V-205813 | Admin approval mode | `Policies\System` | EnableLUA | 1 |

**RDS:**
| VulnID | Setting | Key | ValueName | Value |
|--------|---------|-----|-----------|-------|
| V-205636 | Secure RPC | `Terminal Services` | fEncryptRPCTraffic | 1 |
| V-205637 | Encryption High | `Terminal Services` | MinEncryptionLevel | 3 |
| V-205722 | No drive redirection | `Terminal Services` | fDisableCdm | 1 |
| V-205808 | No save passwords | `Terminal Services` | DisablePasswordSaving | 1 |
| V-205809 | Always prompt password | `Terminal Services` | fPromptForPassword | 1 |

**SMB/NTLM/Kerberos:**
| VulnID | Setting | Key | ValueName | Value |
|--------|---------|-----|-----------|-------|
| V-205724 | No anonymous enum shares | `Lsa` | RestrictAnonymous | 1 |
| V-205825 | SMB signing always | `LanmanWorkstation\Parameters` | RequireSecuritySignature | 1 |
| V-205861 | No insecure SMB logons | `LanmanWorkstation` | AllowInsecureGuestAuth | 0 |
| V-205916 | Negotiate use machine ID | `Lsa` | UseMachineId | 1 |
| V-205917 | No NTLM null session | `Lsa\MSV1_0` | allownullsessionfallback | 0 |
| V-205918 | No PKU2U online ID | `Lsa\pku2u` | AllowOnlineID | 0 |
| V-205919 | LM auth NTLMv2 only | `Lsa` | LmCompatibilityLevel | 5 |
| V-205921 | NTLM SSP client 128-bit | `Lsa\MSV1_0` | NTLMMinClientSec | 537395200 |
| V-205922 | NTLM SSP server 128-bit | `Lsa\MSV1_0` | NTLMMinServerSec | 537395200 |
| V-205708 | Kerberos AES only | `Kerberos\Parameters` | SupportedEncryptionTypes | 2147483640 |
| V-205820 | LDAP signing required | `NTDS\Parameters` | LDAPServerIntegrity | 2 |

**Network:**
| VulnID | Setting | Key | ValueName | Value |
|--------|---------|-----|-----------|-------|
| V-205819 | NetBIOS no name release | `Netbt\Parameters` | NoNameReleaseOnDemand | 1 |
| V-205858 | IPv6 source routing max | `Tcpip6\Parameters` | DisableIPSourceRouting | 2 |
| V-205859 | IP source routing max | `Tcpip\Parameters` | DisableIPSourceRouting | 2 |
| V-205860 | No ICMP redirect OSPF | `Tcpip\Parameters` | EnableICMPRedirect | 0 |
| V-205862 | Hardened UNC paths | `NetworkProvider\HardenedPaths` | (see script) | (see script) |
| V-205863 | Remote host delegation | `CredentialsDelegation` | AllowProtectedCreds | 1 |

**Misc Policies:**
| VulnID | Setting | Registry | Value |
|--------|---------|----------|-------|
| V-205633 | Inactivity 15 min | `Policies\System\InactivityTimeoutSecs` | 900 |
| V-205638 | Cmdline in process creation | `Policies\System\Audit\ProcessCreationIncludeCmdLine_Enabled` | 1 |
| V-205639 | PS script block logging | `PowerShell\ScriptBlockLogging\EnableScriptBlockLogging` | 1 |
| V-205644 | Force audit subcategory | `Lsa\SCENoApplyLegacyAuditPolicy` | 1 |
| V-205651 | Private key access prompt | `Cryptography\ForceKeyProtection` | 2 |
| V-205686 | No lock screen slideshows | `Personalization\NoLockScreenSlideshow` | 1 |
| V-205687 | WDigest disabled | `WDigest\UseLogonCredential` | 0 |
| V-205688 | No HTTP print download | `Printers\DisableWebPnPDownload` | 1 |
| V-205689 | No HTTP printing | `Printers\DisableHTTPPrinting` | 1 |
| V-205690 | No network selection UI | `System\DontDisplayNetworkSelectionUI` | 1 |
| V-205691 | No app compat inventory | `AppCompat\DisableInventory` | 1 |
| V-205692 | SmartScreen enabled | `System\EnableSmartScreen` | 1 |
| V-205694 | No indexing encrypted | `Windows Search\AllowIndexingEncryptedStoresOrItems` | 0 |
| V-205801 | No user install options | `Installer\EnableUserControl` | 0 |
| V-205802 | No elevated install | `Installer\AlwaysInstallElevated` | 0 |
| V-205842 | FIPS enabled | `FIPSAlgorithmPolicy\Enabled` | 1 |
| V-205867 | Wake prompt (battery) | `Power\...\DCSettingIndex` | 1 |
| V-205868 | Wake prompt (plugged in) | `Power\...\ACSettingIndex` | 1 |
| V-205869 | Telemetry Security/Basic | `DataCollection\AllowTelemetry` | 1 |
| V-205870 | No P2P Windows Update | `DeliveryOptimization\DODownloadMode` | 1 |
| V-205873 | No RSS attachments | `Feeds\DisableEnclosureDownload` | 1 |
| V-205876 | Allow machine pwd reset | `Netlogon\Parameters\RefusePasswordChange` | 0 |
| V-205912 | Smart card removal lock | `Winlogon\ScRemoveOption` | 1 |
| V-257503 | PS Transcription | `PowerShell\Transcription\EnableTranscripting` | 1 |
| V-271428 | Cert-based auth for DCs | `Kdc\StrongCertificateBindingEnforcement` | 1 |
| V-271429 | Named-based strong mapping | `Kdc\CertificateMappingMethods` | ... |

**Event Log Sizes:**
| VulnID | Log | Size (KB) |
|--------|-----|-----------|
| V-205796 | Application | 32768 |
| V-205797 | Security | 196608 |
| V-205798 | System | 32768 |

**VBS/Device Guard (hardware-dependent but registry-automatable):**
| VulnID | Setting | Value |
|--------|---------|-------|
| V-205864 | VBS enabled | RequirePlatformSecurityFeatures=1 |

#### Secedit/Security Policy (7 settings)

| VulnID | Setting | Secedit Key | Value |
|--------|---------|-------------|-------|
| V-205629 | Bad logon attempts ≤3 | LockoutBadCount | 3 |
| V-205630 | Bad logon counter reset ≥15 min | ResetLockoutCount | 15 |
| V-205631 | Legal notice text | LegalNoticeText | (DoD banner) |
| V-205632 | Legal notice caption | LegalNoticeCaption | "DoD Notice..." |
| V-205662 | Min password length 14 | MinimumPasswordLength | 14 |
| V-205795 | Lockout duration ≥15 min | LockoutDuration | 15 |
| V-205909 | Rename built-in admin | NewAdministratorName | (any non-default) |
| V-205910 | Rename built-in guest | NewGuestName | (any non-default) |

Note: Account policies are already set via Default Domain Policy (script 02). Legal notice and admin/guest rename need secedit or direct local policy.

#### User Rights Assignment (12 settings)

| VulnID | Right | DC Assignment |
|--------|-------|---------------|
| V-205665 | SeNetworkLogonRight | Administrators, Authenticated Users, Enterprise Domain Controllers |
| V-205667 | SeDenyNetworkLogonRight | Guests |
| V-205668 | SeDenyBatchLogonRight | Guests |
| V-205670 | SeDenyInteractiveLogonRight | Guests |
| V-205676 | SeInteractiveLogonRight | Administrators |
| V-205732 | SeDenyRemoteInteractiveLogonRight | Guests |
| V-205744 | SeMachineAccountPrivilege | Administrators |
| V-205751 | SeBackupPrivilege | Administrators |
| V-205758 | SeRemoteShutdownPrivilege | Administrators |
| V-205761 | SeIncreaseBasePriorityPrivilege | Administrators |
| V-205762 | SeLoadDriverPrivilege | Administrators |
| V-205767 | SeRestorePrivilege | Administrators |

#### Audit Policies (25 subcategories)

| VulnID | Category\Subcategory | Success | Failure |
|--------|---------------------|---------|---------|
| V-205627 | Account Management\User Account Management | — | enable |
| V-205730 | Logon/Logoff\Account Lockout | — | enable |
| V-205769 | Account Management\Other Account Management Events | enable | — |
| V-205770 | Detailed Tracking\Process Creation | enable | — |
| V-205772 | Policy Change\Audit Policy Change | — | enable |
| V-205774 | Policy Change\Authorization Policy Change | enable | — |
| V-205775 | Privilege Use\Sensitive Privilege Use | enable | — |
| V-205776 | Privilege Use\Sensitive Privilege Use | — | enable |
| V-205777 | System\IPsec Driver | enable | — |
| V-205778 | System\IPsec Driver | — | enable |
| V-205782 | System\Security System Extension | enable | — |
| V-205792 | DS Access\Directory Service Access | — | enable |
| V-205793 | DS Access\Directory Service Changes | enable | — |
| V-205833 | Account Logon\Credential Validation | — | enable |
| V-205834 | Logon/Logoff\Group Membership | enable | — |
| V-205836 | Object Access\Other Object Access Events | enable | — |
| V-205837 | Object Access\Other Object Access Events | — | enable |
| V-205839 | Detailed Tracking\Plug and Play Events | enable | — |
| V-278934 | Object Access\File System | — | enable |
| V-278935 | Object Access\File System | enable | — |
| V-278936 | Object Access\Handle Manipulation | — | enable |
| V-278937 | Object Access\Handle Manipulation | enable | — |
| V-278938 | Object Access\Registry | — | enable |
| V-278939 | Object Access\Registry | enable | — |

#### Certificate Install (3 settings)

| VulnID | Requirement | Classification |
|--------|-------------|---------------|
| V-205648 | DoD Root CAs in Trusted Root Store | BOTH |
| V-205649 | DoD IRCA cross-certs in Untrusted | **UNCLASSIFIED ONLY** |
| V-205650 | CCEB cross-certs in Untrusted | **UNCLASSIFIED ONLY** |

#### DNS Hardening (4 of 6 automatable)

| VulnID | Setting | Method |
|--------|---------|--------|
| V-259341 | Disable recursion | `Set-DnsServerRecursion -Enable $false` |
| V-259395 | DoS restrictions | `Set-DnsServerResponseRateLimiting` |
| V-259412 | Event logging for failures | Event log config |
| V-259417 | Enable RRL | `Set-DnsServerResponseRateLimiting -Mode Enable` |

Manual DNS:
- V-259342: Forwarders configuration (depends on network architecture)
- V-259374: Static IP verification (already configured)

#### AD-specific (3 settings)

| VulnID | Setting | Method |
|--------|---------|--------|
| V-243466 | Enterprise Admins membership | PowerShell: restrict to designated accounts |
| V-243467 | Domain Admins membership | PowerShell: restrict to designated accounts |
| V-269097 | Kerberos logging with AD CS | Registry: enable Kerberos audit logging |
| V-243504 | NTP external time source | `w32tm /config /manualpeerlist:...` |

#### DotNET (1 setting)

| VulnID | Setting | Method |
|--------|---------|--------|
| V-225231 | Strong name validation | Registry (already in STIG-DotNet GPO, just link to DC OU) |

### NOT AUTOMATABLE — Manual/Infrastructure (19 Open)

| VulnID | STIG | Why Manual |
|--------|------|-----------|
| V-205648 | WinServer2019 | DoD Root CAs — need cert files (cert install script) |
| V-205649 | WinServer2019 | IRCA cross-certs — need cert files (cert install script, unclass only) |
| V-205650 | WinServer2019 | CCEB cross-certs — need cert files (cert install script, unclass only) |
| V-205723 | WinServer2019 | Data on separate partition — infrastructure/disk layout |
| V-205726 | WinServer2019 | LDAP idle timeout — ntdsutil command (automatable but risky) |
| V-205800 | WinServer2019 | Time service external source — needs NTP server address |
| V-205848 | WinServer2019 | TPM enabled — hardware/Hyper-V setting |
| V-205857 | WinServer2019 | Secure Boot — hardware/Hyper-V setting |
| V-205864 | WinServer2019 | VBS platform security — hardware-dependent (Gen2 VM) |
| V-259342 | WinServerDNS | Forwarders — network architecture decision |
| V-259374 | WinServerDNS | Static IP — verify existing config |

---

## NOT_REVIEWED FINDINGS — Classification Breakdown (86)

### UNCLASSIFIED-ONLY Findings (2)

These apply **only** to unclassified systems. On classified systems, mark as **Not Applicable**.

| VulnID | STIG | Rule |
|--------|------|------|
| V-205649 | WinServer2019 | DoD IRCA cross-certs in Untrusted Certificates Store |
| V-205650 | WinServer2019 | CCEB cross-certs in Untrusted Certificates Store |

Note: These are also in the Open findings list (automatable via cert install). They appear here because Evaluate-STIG gates them with `$ScanType` checks.

### CLASSIFIED-ONLY Findings (1)

These apply **only** to classified systems. On unclassified systems, mark as **Not Applicable**.

| VulnID | STIG | Rule |
|--------|------|------|
| V-205818 | WinServer2019 | NSA Type 1 crypto for classified replication across lower-cleared networks |

### CONDITIONAL — Only If Role/Feature Installed (10)

These are N/A if the specified role/feature is not installed on this server.

| VulnID | STIG | Condition | Rule |
|--------|------|-----------|------|
| V-269098 | ADForest | AD CS installed | CA certificate management approval |
| V-269099 | ADForest | AD CS installed | AD CS managed by PAW tier 0 |
| V-205853 | WinServer2019 | FTP installed | FTP anonymous logon |
| V-205854 | WinServer2019 | FTP installed | FTP access to system drive |
| V-213431 | MSDefender | Server role check | Automatic Exclusions feature |
| V-278673 | MSDefender | Server role check | Disable auto exclusions |
| V-259352 | WinServerDNS | Split DNS config | External/internal RR separation |
| V-259353 | WinServerDNS | Split DNS config | External NS not reachable from inside |
| V-259416 | WinServerDNS | Split DNS config | Internal NS not reachable from outside |
| V-259356 | WinServerDNS | Split DNS config | Internal/external role separation |

### BOTH CLASSIFICATIONS — Organizational/Policy Checks (73)

These require manual verification regardless of classification.

**AD Domain (14 checks):**
| VulnID | Sev | Rule | Can Partially Automate? |
|--------|-----|------|------------------------|
| V-243470 | high | Delegation of privileged accounts prohibited | Check: `Get-ADUser -Filter {TrustedForDelegation -eq $true}` |
| V-243468 | med | Separate admin accounts for member servers | Org policy — verify account naming |
| V-243469 | med | Separate admin accounts for workstations | Org policy — verify account naming |
| V-243471 | med | Local admin passwords not shared | LAPS solves this (see TODO) |
| V-243472 | med | Separate smart cards for EA/DA | Hardware/PKI requirement |
| V-243473 | med | Separate accounts for public-facing servers | Org policy |
| V-243475 | med | DCs blocked from Internet | Network/firewall policy |
| V-243477 | med | Admin accounts in Protected Users group | `Add-ADGroupMember 'Protected Users' ...` |
| V-243479 | med | DSRM password changed annually | Documented procedure |
| V-243487 | med | GP Creator Owners/Incoming Forest Trust limited | Check: `Get-ADGroupMember` |
| V-243488 | low | Delegated authority users removed from builtin admin | Check membership |
| V-243493 | med | AD backup daily (moderate/high Availability) | Backup solution |
| V-243495 | med | VPN for AD traffic spanning enclaves | Network architecture |
| V-243498 | med | IDS for VPN AD traffic | Security infrastructure |
| V-243499 | low | AD in contingency plan | Documentation |
| V-243500 | med | Multiple DCs (moderate/high Availability) | Infrastructure |

**AD Forest (2 checks):**
| VulnID | Sev | Rule |
|--------|-----|------|
| V-243502 | med | Schema Admins group membership limited |
| V-243505 | low | AD schema changes documented |

**WinServer2019 (33 checks):**
| VulnID | Sev | Rule | Can Partially Automate? |
|--------|-----|------|------------------------|
| V-205646 | high | DC PKI certs from DoD PKI/ECA | Check: `Get-ChildItem Cert:\LocalMachine\My` |
| V-205647 | high | User PKI certs from DoD PKI/ECA | Check user certs |
| V-205727 | high | Data at rest encryption | BitLocker status check |
| V-205738 | high | Only designated admins have admin rights | Check: `Get-ADGroupMember Administrators` |
| V-205740 | high | SYSVOL proper ACL | Check: `Get-Acl \\...\SYSVOL` |
| V-205741 | high | GPO proper ACL | Check: `Get-GPO -All | % { Get-GPPermission }` |
| V-205742 | high | DC OU proper ACL | Check: `Get-Acl 'AD:\OU=Domain Controllers,...'` |
| V-205743 | high | Custom OU proper ACL | Check each OU |
| V-205844 | high | Separate admin accounts | Org policy |
| V-205845 | high | Admin accounts no internet/email | Org policy + GPO |
| V-205875 | high | Non-public directory no anonymous access | Check: `dsquery * -s localhost -scope base -attr ...` |
| V-205624 | med | Temp accounts removed 72 hours | `Search-ADAccount -AccountExpiring` |
| V-205658 | med | Passwords expire | `Get-ADUser -Filter {PasswordNeverExpires -eq $true}` |
| V-205661 | med | App account passwords ≥14 chars | Org policy |
| V-205677 | med | Roles/features documented | `Get-WindowsFeature \| Where Installed` |
| V-205695 | med | DC dedicated to DC function only | Check installed roles |
| V-205699 | med | No shared user accounts | Org policy |
| V-205701 | med | CAC/PIV required for auth | PKI infrastructure |
| V-205707 | med | Outdated/unused accounts removed | `Search-ADAccount -AccountInactive` |
| V-205710 | med | Emergency accounts removed 72 hours | Org procedure |
| V-205721 | med | Non-system shares limited | `Get-SmbShare` |
| V-205728 | med | Automated flaw remediation (ESS/scanning) | Security tooling |
| V-205785-V-205790 | med | AD audit settings on GPOs/Domain/Infrastructure/DC OU/AdminSDHolder/RID Manager$ | PowerShell SACL checks |
| V-205799 | med | Audit records backed up | Backup solution |
| V-205803 | med | System files monitored | FIM solution |
| V-205807 | med | Deny-all, permit-by-exception (AppLocker/WDAC) | AppLocker policy |
| V-205829 | med | TLS/VPN/IPsec for data integrity | Network config |
| V-205843 | med | Audit record offloading | SIEM/log forwarding |
| V-205847 | med | App account password change annually | Org procedure |
| V-205851 | med | Host-based IDS/IPS | Security tooling |
| V-214936 | med | Host-based firewall installed/enabled | Check: Firewall script handles this |

**WinServerDNS (22 checks):**
Most are architecture/documentation checks that require manual verification of DNS zone configuration, security procedures, and network topology.

---

## Answer File Strategy

### Approach: "Classified System" Checkbox

Default: **Classified = true** (most conservative)

When **Classified = true** (default):
- V-205649 (IRCA cross-certs Untrusted) → **Not_Applicable** (unclass only)
- V-205650 (CCEB cross-certs Untrusted) → **Not_Applicable** (unclass only)

When **Classified = false** (unclassified):
- V-205818 (NSA Type 1 crypto) → **Not_Applicable** (classified only)

### Conditional N/A (add checkboxes in OrgSettings)

| Checkbox | When Unchecked → N/A |
|----------|---------------------|
| AD CS Installed | V-269098, V-269099 |
| FTP Installed | V-205853, V-205854 |
| Split DNS Configuration | V-259352, V-259353, V-259356, V-259416 |
| Smart Card/PKI Available | V-243472, V-205701 |
| Multiple DCs | V-243500 |
| SIEM/Log Forwarding | V-205843 |
| Host-based IDS/IPS | V-205851 |
| AppLocker/WDAC Deployed | V-205807 |
| Backup Solution | V-205799, V-243493 |
| FIM Solution | V-205803 |

---

## Hardening Script Plan

| Step | Script | Target | Contents |
|------|--------|--------|----------|
| 07 | `07-dc01-dsc-hardening.ps1` | DC01 | PowerSTIG DSC: WindowsServer 2019-DC, Firewall, Defender, DotNet, IE11 (bulk settings) |
| 08 | `08-dc01-stig-gpos.ps1` | DC01 | Create STIG-Server2019, STIG-Defender, STIG-Firewall GPOs; link to Domain Controllers OU; link existing STIG-IE11, STIG-DotNet to DC OU |
| 09 | `09-dc01-local-hardening.ps1` | DC01 | Secedit (rename admin/guest), auditpol (25 subcategories), user rights (12 assignments), DNS hardening, LDAP config, AD checks, NTP |
| 10 | `10-dc01-install-certs.ps1` | DC01 | DoD Root CAs + cross-certs (same approach as WS01) |
| 11 | `11-dc01-scan.ps1` | DC01 | Run Evaluate-STIG and report compliance |

**Approach**: DSC first (handles most registry/audit/security settings in bulk), GPOs second (complement for settings DSC can't cover or that are better as GPO), local hardening third (user rights, AD-specific, DNS).
