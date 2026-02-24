# STIGForge

Offline-first Windows hardening platform: Build -> Apply -> Verify -> Prove.

## Quick start
1) Install .NET 8 SDK
2) Run app:
```powershell
dotnet run --project .\src\STIGForge.App\STIGForge.App.csproj
```
3) Import a pack (CLI):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- import-pack C:\path\to\pack.zip --name Q1_2026
```
4) Build a bundle (CLI):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- build-bundle --pack-id <PACK_ID> --profile-json .\docs\samples\Profile-Classified-Win11.json --save-profile --force-auto-apply
```
5) Apply (CLI):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- apply-run --bundle C:\path\to\bundle --mode Safe --script C:\path\to\apply.ps1 --script-args "-Example 1"
```
6) PowerSTIG compile + apply (CLI):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- apply-run --bundle C:\path\to\bundle --powerstig-module C:\path\to\PowerStig --powerstig-data C:\path\to\your.psd1 --powerstig-verbose --dsc-path C:\path\to\bundle\Apply\Dsc --dsc-verbose
```
7) Verify with SCAP tool (CLI):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- verify-scap --cmd "C:\path\to\scc.exe" --args "-u -s -r -f" --output-root C:\path\to\scap\output --tool "DISA SCAP"
```
8) Orchestrate (Apply + Verify):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- orchestrate --bundle C:\path\to\bundle --powerstig-module C:\path\to\PowerStig --powerstig-data C:\path\to\your.psd1 --evaluate-stig C:\path\to\Evaluate-STIG --evaluate-args "-AnswerFile .\AnswerFile.xml" --scap-cmd "C:\path\to\scc.exe" --scap-args "-u -s -r -f" --scap-label "DISA SCAP"
```
8b) Seamless autopilot mission (optional NIWC enhanced SCAP import + build + apply + verify):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- mission-autopilot --niwc-source-url https://github.com/niwc-atlantic/scap-content-library --disa-stig-url https://www.cyber.mil/stigs/downloads --powerstig-source-url https://github.com/microsoft/PowerStig --evaluate-stig C:\path\to\Evaluate-STIG --scap-cmd "C:\path\to\scc.exe"
```
   Remote source archives downloaded by this command are cached in `.stigforge/airgap-transfer` for air-gap transfer.
9) eMASS export (CLI):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- export-emass --bundle C:\path\to\bundle
```
10) Import PowerSTIG overrides (CSV):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- overlay-import-powerstig --csv C:\path\to\powerstig_overrides.csv --name "PowerSTIG Overrides"
```
11) Export PowerSTIG mapping template:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- powerstig-map-export --pack-id <PACK_ID> --output C:\path\to\powerstig_map.csv
```
12) List imported packs:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- list-packs
```
13) List overlays:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- list-overlays
```
14) Diff two packs (quarterly update review):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- diff-packs --baseline <OLD_PACK_ID> --target <NEW_PACK_ID> --output diff-report.md
```
15) Rebase an overlay to a new pack:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- rebase-overlay --overlay <OVERLAY_ID> --baseline <OLD_PACK_ID> --target <NEW_PACK_ID> --apply
```
16) List manual controls and answer status:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- list-manual-controls --bundle C:\path\to\bundle --status Open
```
17) Save manual answer (single):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- manual-answer --bundle C:\path\to\bundle --rule-id SV-12345 --status NotApplicable --reason "Not in scope"
```
18) Save manual answers (batch CSV):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- manual-answer --bundle C:\path\to\bundle --csv C:\path\to\answers.csv
```
19) Save evidence artifact:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- evidence-save --bundle C:\path\to\bundle --rule-id SV-12345 --type File --source-file C:\path\to\evidence.txt
```
20) Bundle summary (dashboard):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- bundle-summary --bundle C:\path\to\bundle --json
```
21) Edit overlay (add/remove overrides):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- overlay-edit --overlay <OVERLAY_ID> --add-rule SV-12345 --status NotApplicable --reason "Org policy"
```

22) Export standalone POA&M (Plan of Action & Milestones):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- export-poam --bundle C:\path\to\bundle --system-name "MySystem"
```
23) Export CKL (STIG Viewer Checklist):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- export-ckl --bundle C:\path\to\bundle --host-name MYHOST --stig-id "Win11_STIG"
```
24) Query audit trail:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- audit-log --action apply --limit 50 --json
```
25) Verify audit trail integrity:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- audit-verify
```
26) Schedule re-verification (Windows Task Scheduler):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- schedule-verify --name "DailyVerify" --bundle C:\path\to\bundle --frequency DAILY --time 06:00
```
27) Remove scheduled task:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- schedule-remove --name "DailyVerify"
```
28) List scheduled tasks:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- schedule-list
```
29) Fleet apply (multi-machine via WinRM):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- fleet-apply --targets "SRV01,SRV02:10.0.0.2,SRV03" --remote-bundle-path "C:\STIGForge\bundle" --mode Safe --concurrency 5
```
30) Fleet verify (multi-machine via WinRM):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- fleet-verify --targets "SRV01,SRV02,SRV03" --scap-cmd "C:\SCC\scc.exe" --json
```
31) Fleet status (check WinRM connectivity):
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- fleet-status --targets "SRV01,SRV02,SRV03"
```
32) Collect diagnostics for support:
```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- support-bundle --output .\artifacts\support --bundle C:\path\to\bundle --max-log-files 30
```

33) Run security gate only (dependency vuln/license/secrets policy checks):
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\release\Invoke-SecurityGate.ps1 -OutputRoot .\.artifacts\security-gate\local
```

34) Build release packages (CLI + WPF publish zips, optional signing):
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\release\Invoke-PackageBuild.ps1 -Configuration Release -Runtime win-x64 -OutputRoot .\.artifacts\release-package\local
```

## Local workflow usage (`workflow-local`)

Run the v1 local mission path (`Setup -> Import -> Scan`) and emit `mission.json`:

```powershell
dotnet run --project .\src\STIGForge.Cli\STIGForge.Cli.csproj -- workflow-local --import-root .\.stigforge\import --tool-root .\.stigforge\tools\Evaluate-STIG\Evaluate-STIG --output-root .\.stigforge\local-workflow
```

Defaults when options are omitted:
- `--import-root`: `.stigforge/import`
- `--tool-root`: `.stigforge/tools/Evaluate-STIG/Evaluate-STIG`
- `--output-root`: `.stigforge/local-workflow`

Behavior notes:
- Strict setup gate: command fails immediately when required Evaluate-STIG path is missing or does not contain `Evaluate-STIG.ps1`.
- Import gate: command fails if import scanning does not produce canonical checklist items.
- Unmapped scanner findings are warnings, not hard failures; workflow still writes `mission.json` and records unmapped entries under `unmapped`.

## Ship readiness gate
Run the automated release gate (build + tests + artifact manifest/checksums):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\local
```

Outputs:
- `report/release-gate-report.md` - human-readable gate summary
- `report/release-gate-summary.json` - machine-readable step results
- `report/sha256-checksums.txt` - checksums for generated artifacts
- `logs/*.log` - per-step command logs
- `security/reports/security-gate-report.md` - security gate summary (vuln/license/secrets)
- `security/reports/security-gate-summary.json` - machine-readable security summary
- `sbom/dotnet-packages.json` - dependency inventory (unless `-SkipSbom`)

See `docs/release/ShipReadinessChecklist.md` for go/no-go criteria.
See `docs/release/SecurityGatePolicies.md` for policy file and exception management.

## Release workflows
- `ci.yml` runs release gate and uploads gate artifacts for every push/PR.
- `release-package.yml` (manual dispatch) runs optional release gate + package build and uploads release bundles.
- `vm-smoke-matrix.yml` (manual dispatch) runs release gate + E2E tests on self-hosted VM runners labeled:
  - `win11`
  - `server2019`
  - `server2022`

## Repo layout
- src/ STIGForge.* projects (WPF App + modules)
- tests/ unit + integration tests
- tools/schemas JSON schemas (profile/overlay/manifest)
- docs/spec contracts (bundle + eMASS export)

## Docker test lane (WSL)

For Docker-on-WSL unit/integration test commands, see `docs/testing/DockerWslTestLane.md`.
