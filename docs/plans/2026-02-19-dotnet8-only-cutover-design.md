# .NET 8-Only Cutover Design

## Objective

Remove all `.NET Framework 4.x` compatibility targets from the active STIGForge codebase and standardize runtime targets on `.NET 8` for improved stability, reduced compatibility constraints, and lower bug surface.

## Approved Runtime Policy

- Standardize to **.NET 8 LTS only** for library/runtime targets.
- Preserve existing app entrypoint target:
  - `STIGForge.App` remains `net8.0-windows`.

## Approved Migration Strategy

**Approach:** Atomic cutover.

Why this approach:
- Eliminates mixed-target drift immediately.
- Removes ongoing pressure to avoid modern APIs.
- Reduces future bug-fix overhead caused by framework-conditional paths.

## Scope

### In scope

- Convert these multi-target projects from `net48;net8.0` to `net8.0`:
  - `src/STIGForge.Shared/STIGForge.Shared.csproj`
  - `src/STIGForge.Core/STIGForge.Core.csproj`
  - `src/STIGForge.Infrastructure/STIGForge.Infrastructure.csproj`
  - `src/STIGForge.Content/STIGForge.Content.csproj`
  - `src/STIGForge.Apply/STIGForge.Apply.csproj`
  - `src/STIGForge.Verify/STIGForge.Verify.csproj`
  - `src/STIGForge.Build/STIGForge.Build.csproj`
  - `src/STIGForge.Export/STIGForge.Export.csproj`
  - `src/STIGForge.Reporting/STIGForge.Reporting.csproj`
- Remove `net48`-only `ItemGroup` references/shims from those projects.
- Remove temporary `net48` compatibility code added to unblock `ContentPackImporter` and restore modern API usage where safe.
- Update active architecture documentation to remove dual-target claims.

### Out of scope

- Feature behavior changes.
- Runtime upgrade to .NET 9.
- Historical planning docs rewrite (only active architecture/source-of-truth docs change).

## Execution Design

1. **Project target cutover**
   - Replace each `TargetFrameworks` value `net48;net8.0` with `net8.0`.
   - Remove conditional `ItemGroup` blocks that existed only for `net48`.
2. **Code cleanup after target simplification**
   - Revert compatibility-only code paths introduced solely for `net48` support.
   - Keep behavior equivalent.
3. **Documentation alignment**
   - Update active docs that state dual-target constraints.

## Validation Gates

- Gate 1: No `net48` in active source project files.
  - Search target: `src/**/*.csproj`
- Gate 2: Solution test/build pass on new baseline.
  - Command: `dotnet test STIGForge.sln`
- Gate 3: Docs no longer claim dual-target in active architecture doc.
  - Search target: `docs/Architecture.md`

## Risk and Mitigation

- **Risk:** Unexpected dependency on framework-only APIs.
  - **Mitigation:** full solution test/build after cutover before merge.
- **Risk:** Drift between docs and actual targets.
  - **Mitigation:** explicit architecture doc gate in verification checklist.
- **Risk:** Hidden net48 assumptions in scripts/workflows.
  - **Mitigation:** quick grep pass on `tools/` and workflow files during verification.

## Acceptance Criteria

- All active runtime libraries target .NET 8 only.
- No active source file requires net48 compatibility shims.
- `dotnet test STIGForge.sln` passes.
- Active architecture documentation reflects .NET 8-only baseline.
