# STIGForge

Offline-first Windows hardening platform: Build -> Apply -> Verify -> Prove.

## Quick start
1) Install .NET 8 SDK
2) Run app:
```powershell
dotnet run --project .\src\STIGForge.App\STIGForge.App.csproj
```

## Repo layout
- src/ STIGForge.* projects (WPF App + modules)
- tests/ unit + integration tests
- tools/schemas JSON schemas (profile/overlay/manifest)
- docs/spec contracts (bundle + eMASS export)
