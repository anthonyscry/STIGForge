# Testing Guide

Use the commands below based on your runtime environment.

## Linux (WSL)

Run cross-platform integration tests:

```bash
dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --nologo
```

## Windows (PowerShell)

Run the full solution test suite, including `net8.0-windows` projects:

```powershell
dotnet test STIGForge.sln --nologo
```

Notes:
- Some projects target Windows Desktop and do not execute on Linux.
- If full-suite tests fail on Windows, review integration and unit failures separately.
- Keyboard focus/tab-order regression checklist: `docs/testing/WpfKeyboardTabSequence.md`.

Common failure buckets (Windows):
- Bundle orchestrator timeline: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~BundleOrchestratorTimelineTests --nologo`
- Overlay merge builder tests: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~BundleBuilderOverlayMergeTests --nologo`
- Orchestrator control override tests: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~BundleOrchestratorControlOverrideTests --nologo`
- Overlay editor view model tests: `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter FullyQualifiedName~OverlayEditorViewModelTests --nologo`
