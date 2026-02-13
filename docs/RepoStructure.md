# Repository Structure

This document describes the current STIGForge layout and where to place new code.

## Top-level

- `src/` runtime projects
- `tests/` unit and integration tests
- `docs/` architecture, user/CLI guides, specs, release guidance, planning
- `tools/` scripts, schemas, release helpers
- `STIG_SCAP/` large offline content/tooling artifacts

## Runtime Projects (`src/`)

- `STIGForge.App` WPF UI (MVVM, CommunityToolkit)
- `STIGForge.Cli` CLI entrypoint and command handlers
- `STIGForge.Core` shared models, constants, abstractions, domain services
- `STIGForge.Infrastructure` storage/system/platform implementations
- `STIGForge.Content` pack import/parsing
- `STIGForge.Build` bundle generation/orchestration
- `STIGForge.Apply` hardening/apply pipeline
- `STIGForge.Verify` verification orchestration and adapters
- `STIGForge.Export` eMASS/POA&M/CKL export pipeline
- `STIGForge.Evidence` evidence collection helpers
- `STIGForge.Reporting` reporting services
- `STIGForge.Shared` cross-target shared code

## App ViewModel Organization

`src/STIGForge.App/ViewModels/` is grouped by domain:

- `Main/` root/shared-state orchestration VMs (`MainViewModel` facades)
- `Manual/` manual review and wizard VMs
- `Import/` import/rebase VMs
- `Common/` reusable/shared dialog VMs

Keep namespaces under `STIGForge.App.ViewModels` unless there is a clear reason to split.

## Conventions

- Favor small, focused changes over broad rewrites.
- Shared parsing/format helpers should live in `STIGForge.Core/Utilities`.
- Avoid duplicate helper implementations across App/CLI/Export.
- Put non-runtime planning material under `docs/planning/`.
