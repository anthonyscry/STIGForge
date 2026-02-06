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

## Repo layout
- src/ STIGForge.* projects (WPF App + modules)
- tests/ unit + integration tests
- tools/schemas JSON schemas (profile/overlay/manifest)
- docs/spec contracts (bundle + eMASS export)
