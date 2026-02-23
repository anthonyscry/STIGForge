# Phase 2: Policy Scope and Safety Gates - Research

**Researched:** 2026-02-22
**Domain:** Deterministic policy gating, scope filtering, safety gates for offline compliance operations
**Confidence:** HIGH

## Summary

Phase 2 wires existing core services (ClassificationScopeService, ReleaseAgeGate, OverlayRebaseService) into full operator workflows with CLI commands, WPF surfaces, and deterministic reporting. The foundational service layer is already implemented: ClassificationScopeService.Compile() handles scope decisions, ReleaseAgeGate.ShouldAutoApply() enforces grace periods, and OverlayRebaseService manages overlay precedence with confidence scoring. BundleBuilder already consumes these services and emits automation_gate.json and na_scope_filter_report.csv.

The primary work is surfacing these capabilities through operator-facing CLI commands (profile CRUD, overlay diff, bundle review-queue) and WPF views (profile editor in settings, review queue step in guided run), plus adding the overlay_conflict_report.csv artifact. All new code follows established patterns: System.CommandLine for CLI, MVVM with WPF UserControls, SQLite repositories via Dapper, and xUnit/FluentAssertions/Moq for testing.

**Primary recommendation:** Build on the existing service contracts and BundleBuilder integration. Add CLI/WPF surfaces incrementally, keeping the determinism contract (identical Profile + identical Controls = identical gate outcomes) as the primary invariant.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Profile is the single carrier for all policy knobs (NaPolicy, AutomationPolicy already modeled)
- JSON file import/export is the primary configuration mechanism (established pattern in BuildCommands.cs)
- CLI commands: `profile list`, `profile show <id>`, `profile create --from-json`, `profile update`, `profile export`
- WPF: Profile editor form in settings area, not inline during guided run — keep guided run focused on execution
- Policy knobs are edited at rest, not during a live mission run
- Determinism contract: identical Profile + identical Controls = identical gate outcomes, always
- Overlay precedence is positional — array order in Profile.OverlayIds defines priority (last wins)
- OverlayRebaseService already scores confidence and flags blocking conflicts
- Conflict report emitted as `overlay_conflict_report.csv` alongside existing `na_scope_filter_report.csv`
- Report columns: ControlKey, WinningOverlayId, OverriddenOverlayId, WinningValue, OverriddenValue, Reason
- Blocking conflicts (IsBlockingConflict=true) halt bundle build with explicit error — no silent resolution
- CLI: `overlay diff <overlay-a> <overlay-b>` shows field-level conflicts before build
- ClassificationScopeService.Compile() is the single entry point for scope decisions (already implemented)
- Three modes enforced: Classified, Unclassified, Mixed — set on Profile.ClassificationMode
- Auto-NA behavior: controls outside scope are marked NotApplicable when confidence meets profile threshold
- Ambiguous decisions (Confidence < threshold OR ScopeTag=Unknown) route to ReviewQueue
- `na_scope_filter_report.csv` emitted by BundleBuilder (already implemented) — no format changes needed
- Review queue items surface in CLI (`bundle review-queue <bundle-path>`) and WPF guided run step
- ReleaseAgeGate blocks auto-apply for new/changed controls within grace period (already implemented)
- Grace period is Profile.AutomationPolicy.NewRuleGraceDays (default 30, configurable)
- Break-glass override pattern preserved: --force-auto-apply + --break-glass-ack + --break-glass-reason (CLI)
- WPF equivalent: confirmation dialog with reason text field, logged to AuditTrailService
- All gate decisions (pass/block/override) logged to automation_gate.json per build (already implemented)
- Gate decisions are append-only audit entries — no retroactive modification
- Break-glass overrides must capture operator identity and reason in the audit trail — compliance requirement
- The review queue should show WHY each control was flagged (confidence score, scope tag, gate reason)
- Overlay conflict reports should be deterministic — same overlays in same order always produce identical report content
- Follow existing BundleBuilder report pattern: write artifacts to Reports/ directory in bundle tree

### Claude's Discretion
- Exact CSV column ordering and formatting for new overlay_conflict_report.csv
- Profile editor WPF layout and field grouping
- CLI output formatting (table vs structured text) for profile/overlay commands
- Whether to add a `profile validate` command for checking policy consistency before build

### Deferred Ideas (OUT OF SCOPE)
- Policy versioning and historical tracking — could be its own phase for audit trail completeness
- Policy templates/presets for common deployment scenarios — Phase 3+ after core execution works
- Interactive overlay conflict resolution wizard — Phase 4 (Human Resolution) is the right home
- Fleet-wide policy distribution — Phase 5 (Fleet-lite)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| POL-01 | Profile dimensions and policy knobs support deterministic gating (`new_rule_grace_days`, confidence thresholds, automation guardrails) | Profile model already has NaPolicy and AutomationPolicy. Need CLI CRUD commands and WPF editor to expose all knobs. Determinism verified through identical-input tests. |
| POL-02 | Overlay precedence and conflict resolution are deterministic and reportable | OverlayRebaseService exists with confidence scoring. Need overlay conflict resolution during build (positional precedence), overlay_conflict_report.csv emission, and `overlay diff` CLI command. |
| SCOPE-01 | Classification filter supports `classified`, `unclassified`, and `mixed` modes with confidence-threshold auto-NA | ClassificationScopeService.Compile() and FilterControls() already implement this. Need to extend Unclassified mode handling (currently only Classified mode fully evaluates scope). Need CLI/WPF surfaces. |
| SCOPE-02 | Ambiguous scope decisions route to review queue and emit `na_scope_filter_report.csv` | CompiledControls.ReviewQueue already populated. na_scope_filter_report.csv already emitted. Need `bundle review-queue` CLI command and WPF review-queue step in guided run. |
| SAFE-01 | Release-age gate blocks auto-apply for new/changed controls until grace period and trusted mapping criteria are satisfied | ReleaseAgeGate.ShouldAutoApply() and FilterControls() implemented. Break-glass pattern in BuildCommands.cs. Need WPF break-glass dialog and audit logging for WPF path. |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 8 | 8.0 (net8.0-windows) | Runtime and framework | Already in use across all projects |
| System.CommandLine | 2.x | CLI command registration and parsing | Already used in all CLI commands |
| WPF (net8.0-windows) | Built-in | Desktop UI framework | Already used for STIGForge.App |
| Dapper | 2.x | Lightweight ORM for SQLite | Already used in Infrastructure layer |
| Microsoft.Data.Sqlite | 8.x | SQLite provider | Already used for all persistence |
| System.Text.Json | Built-in | JSON serialization | Already used for profile/overlay import/export |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| FluentAssertions | 6.x | Test assertion library | All unit/integration tests |
| Moq | 4.x | Mocking framework | Service isolation in unit tests |
| xUnit | 2.x | Test framework | All test projects |

### Alternatives Considered
None — the stack is fully established. No new libraries needed for Phase 2.

## Architecture Patterns

### Existing Project Structure
```
src/
├── STIGForge.Core/          # Models, abstractions, pure services (no I/O)
│   ├── Abstractions/        # Interfaces: repositories, services
│   ├── Models/              # Profile, Overlay, ControlRecord, enums
│   └── Services/            # ClassificationScopeService, ReleaseAgeGate, OverlayRebaseService
├── STIGForge.Build/         # BundleBuilder — consumes Core services, emits bundle tree
├── STIGForge.Infrastructure/# SQLite repositories, AuditTrailService, file I/O
├── STIGForge.Cli/           # System.CommandLine commands (BuildCommands, BundleCommands, etc.)
├── STIGForge.App/           # WPF application (MVVM ViewModels + Views)
└── tests/
    └── STIGForge.UnitTests/ # xUnit + FluentAssertions + Moq
```

### Pattern 1: CLI Command Registration
**What:** Each command group lives in a static class with `Register(RootCommand, Func<IHost>)` method.
**When to use:** All new CLI commands (profile, overlay diff, bundle review-queue).
**Example:** See `BuildCommands.Register()` — resolves services from DI host, validates arguments, executes.

### Pattern 2: Break-Glass Validation
**What:** Three-part safety pattern: `--force-X` + `--break-glass-ack` + `--break-glass-reason` with `ValidateBreakGlassArguments()` and `RecordBreakGlassAuditAsync()`.
**When to use:** Any high-risk override that bypasses safety gates.
**Example:** Already implemented in BuildCommands for `--force-auto-apply` and `--skip-snapshot`.

### Pattern 3: Report Artifact Emission
**What:** CSV/JSON reports written to `Reports/` directory in bundle tree during build.
**When to use:** New overlay_conflict_report.csv follows same pattern as na_scope_filter_report.csv.
**Example:** `BundleBuilder.WriteNaScopeReport()` — CSV with header row, deterministic ordering.

### Pattern 4: Repository Interface + SQLite Implementation
**What:** Interface in Core/Abstractions, implementation in Infrastructure/Storage with Dapper.
**When to use:** IProfileRepository and IOverlayRepository already exist with CRUD operations.
**Example:** See `Repositories.cs` for interfaces, `SqliteRepositories.cs` for implementations.

### Pattern 5: WPF MVVM with Partial ViewModels
**What:** MainViewModel split across partial files (MainViewModel.ApplyVerify.cs, MainViewModel.AuditLog.cs, etc.).
**When to use:** Profile editor and review queue UI additions.
**Example:** Each feature area gets its own partial class file to keep MainViewModel manageable.

### Anti-Patterns to Avoid
- **Mutable gate decisions:** Gate decisions are append-only audit entries. Never retroactively modify gate outcomes.
- **Inline policy editing during execution:** Policy knobs are edited at rest, not during live mission runs.
- **Silent conflict resolution:** Blocking conflicts must halt build with explicit error, never silently resolve.
- **Non-deterministic report output:** Same inputs must always produce byte-identical CSV/JSON artifacts.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CSV escaping | Custom string escaping | Existing `BundleBuilder.Csv()` helper | Already handles quoting, commas, newlines |
| Break-glass validation | New validation logic | Existing `ValidateBreakGlassArguments()` + `RecordBreakGlassAuditAsync()` | Established three-part pattern, audit chain intact |
| Scope evaluation | New scope logic | `ClassificationScopeService.Compile()` | Already handles Classified/Unclassified/Mixed modes |
| Release-age gating | New date comparison | `ReleaseAgeGate.ShouldAutoApply()` + `FilterControls()` | Already handles grace period, null dates, edge cases |
| Hash-chained audit | Custom audit chain | `AuditTrailService.RecordAsync()` | SHA-256 chaining already implemented and verified |

**Key insight:** Phase 2 is primarily a wiring and surfacing phase — the core decision logic exists. The risk is building parallel implementations instead of composing existing services.

## Common Pitfalls

### Pitfall 1: ClassificationScopeService Only Handles Classified Mode Fully
**What goes wrong:** The current `Evaluate()` method only has detailed logic for `ClassificationMode.Classified`. The `Unclassified` and `Mixed` branches need equivalent scope evaluation.
**Why it happens:** Phase 1 focused on Classified mode as the primary use case.
**How to avoid:** Extend `Evaluate()` with symmetric Unclassified logic (ClassifiedOnly controls are out-of-scope in Unclassified mode) and Mixed mode (no auto-NA, all controls in scope).
**Warning signs:** Tests pass for Classified but not Unclassified inputs.

### Pitfall 2: Overlay Conflict Detection vs. Overlay Rebase
**What goes wrong:** OverlayRebaseService compares baseline-to-new pack controls. Overlay conflict detection (POL-02) compares multiple overlays against each other for the same control set.
**Why it happens:** These are two different operations that share vocabulary ("conflict", "confidence") but operate on different axes.
**How to avoid:** Create a distinct `OverlayConflictDetector` service that takes `IReadOnlyList<Overlay>` and returns conflicts where multiple overlays override the same control with different values. OverlayRebaseService handles pack-version migration; OverlayConflictDetector handles multi-overlay precedence.
**Warning signs:** Trying to repurpose OverlayRebaseService for multi-overlay conflict detection.

### Pitfall 3: Non-Deterministic CSV Output
**What goes wrong:** Overlay conflict report or review queue output varies between runs due to dictionary iteration order or unsorted collections.
**Why it happens:** .NET dictionary enumeration order is not guaranteed.
**How to avoid:** Always sort output by a deterministic key (ControlKey, then OverlayId) before writing. Use `OrderBy()` with `StringComparer.OrdinalIgnoreCase`.
**Warning signs:** Diff between two runs of identical inputs shows different row ordering.

### Pitfall 4: Break-Glass Audit Gap in WPF Path
**What goes wrong:** CLI break-glass overrides are fully audited, but the WPF equivalent skips audit logging.
**Why it happens:** WPF confirmation dialog is added but the audit trail call is forgotten.
**How to avoid:** WPF break-glass confirmation dialog must call `IAuditTrailService.RecordAsync()` with the same entry structure as `RecordBreakGlassAuditAsync()`.
**Warning signs:** Audit trail has CLI break-glass entries but no WPF entries.

### Pitfall 5: Profile JSON Deserialization Without Validation
**What goes wrong:** Profile loaded from JSON has invalid policy values (negative grace days, null NaPolicy) that cause runtime errors during build.
**Why it happens:** `JsonSerializer.Deserialize<Profile>()` creates objects with default values, not validated ranges.
**How to avoid:** Add a `Profile.Validate()` method or `ProfileValidator` that checks required fields, value ranges, and policy consistency. Call it after deserialization in both CLI and WPF paths.
**Warning signs:** Build fails with cryptic NullReferenceException deep in scope service because NaPolicy was null.

## Code Examples

### CLI Command Registration Pattern (from BuildCommands.cs)
```csharp
// Register a new command group following the established pattern
internal static class ProfileCommands
{
    public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
    {
        var profileCmd = new Command("profile", "Manage policy profiles");

        var listCmd = new Command("list", "List all profiles");
        listCmd.SetHandler(async () =>
        {
            using var host = buildHost();
            await host.StartAsync();
            var profiles = host.Services.GetRequiredService<IProfileRepository>();
            var list = await profiles.ListAsync(CancellationToken.None);
            // Format and output
            await host.StopAsync();
        });

        profileCmd.AddCommand(listCmd);
        rootCmd.AddCommand(profileCmd);
    }
}
```

### Overlay Conflict Detection Pattern
```csharp
// Positional precedence: last overlay in Profile.OverlayIds wins
public sealed class OverlayConflictDetector
{
    public IReadOnlyList<OverlayConflict> DetectConflicts(
        IReadOnlyList<Overlay> overlays,
        IReadOnlyList<ControlRecord> controls)
    {
        var conflicts = new List<OverlayConflict>();
        var overridesByControl = new Dictionary<string, List<(string OverlayId, ControlOverride Override, int Priority)>>();

        for (int i = 0; i < overlays.Count; i++)
        {
            foreach (var ovr in overlays[i].Overrides)
            {
                var key = GetControlKey(ovr);
                if (!overridesByControl.ContainsKey(key))
                    overridesByControl[key] = new();
                overridesByControl[key].Add((overlays[i].OverlayId, ovr, i));
            }
        }

        foreach (var (key, entries) in overridesByControl.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count <= 1) continue;
            var winner = entries.OrderByDescending(e => e.Priority).First();
            foreach (var loser in entries.Where(e => e.OverlayId != winner.OverlayId))
            {
                conflicts.Add(new OverlayConflict
                {
                    ControlKey = key,
                    WinningOverlayId = winner.OverlayId,
                    OverriddenOverlayId = loser.OverlayId,
                    WinningValue = FormatOverrideValue(winner.Override),
                    OverriddenValue = FormatOverrideValue(loser.Override),
                    Reason = "Positional precedence (index " + winner.Priority + " > " + loser.Priority + ")"
                });
            }
        }

        return conflicts;
    }
}
```

### Break-Glass Dialog Pattern (WPF)
```csharp
// WPF break-glass follows same three-part pattern as CLI
var dialog = new BreakGlassDialog
{
    BypassDescription = "Force auto-apply bypasses release-age gate safety check",
    ReasonMinLength = 8
};

if (dialog.ShowDialog() == true)
{
    await _audit.RecordAsync(new AuditEntry
    {
        Action = "break-glass",
        Target = bundleRoot,
        Result = "acknowledged",
        Detail = $"Action=wpf-force-auto-apply; Bypass=release-age-gate; Reason={dialog.Reason}",
        User = Environment.UserName,
        Machine = Environment.MachineName,
        Timestamp = DateTimeOffset.UtcNow
    }, CancellationToken.None);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Implicit policy in code | Explicit Profile model with NaPolicy + AutomationPolicy | Already implemented | Policy decisions are data-driven, not hardcoded |
| Manual scope assessment | ClassificationScopeService.Compile() with confidence thresholds | Already implemented | Deterministic auto-NA with traceable rationale |
| No release-age checking | ReleaseAgeGate with configurable grace period | Already implemented | New controls are held back until maturity threshold |
| Ad-hoc break-glass | Three-part pattern: force + ack + reason | Already implemented (CLI) | Audit-complete safety overrides |

**Deprecated/outdated:**
- None — all current implementations follow .NET 8 patterns and are recent.

## Open Questions

1. **Profile `validate` command**
   - What we know: CONTEXT.md lists this as Claude's discretion
   - What's unclear: What specific validations beyond null/range checks
   - Recommendation: Add `profile validate` that checks NaPolicy consistency, AutomationPolicy ranges, and OverlayIds existence. Low effort, high operator value.

2. **Unclassified mode evaluation symmetry**
   - What we know: ClassificationScopeService.Evaluate() only has detailed logic for Classified mode
   - What's unclear: Whether Unclassified mode should mirror Classified logic (ClassifiedOnly controls become NA) or use a different strategy
   - Recommendation: Symmetric implementation — in Unclassified mode, controls tagged ClassifiedOnly are auto-NA when confidence meets threshold. ScopeTag.Unknown still routes to review queue.

3. **Overlay conflict blocking behavior**
   - What we know: CONTEXT.md says blocking conflicts halt build
   - What's unclear: Whether this refers to overlay-vs-overlay conflicts (same control overridden differently) or only rebase conflicts
   - Recommendation: Both — OverlayConflictDetector identifies when overlays disagree on the same control. If any conflict has IsBlockingConflict=true (different status overrides), build halts.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: ClassificationScopeService.cs, ReleaseAgeGate.cs, OverlayRebaseService.cs — full service implementations reviewed
- Codebase analysis: Profile.cs, Overlay.cs — model contracts reviewed
- Codebase analysis: BuildCommands.cs — CLI patterns, break-glass pattern, profile resolution reviewed
- Codebase analysis: BundleBuilder.cs — report emission patterns, scope/gate integration reviewed
- Codebase analysis: AuditTrailService.cs — hash-chained audit implementation reviewed
- Codebase analysis: Repositories.cs, Services.cs — interface contracts reviewed
- Codebase analysis: OverlayRebaseServiceTests.cs — test patterns (xUnit, FluentAssertions, Moq) reviewed

### Secondary (MEDIUM confidence)
- Phase 2 CONTEXT.md — user decisions and implementation constraints

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - fully established, no new libraries needed
- Architecture: HIGH - all patterns already exist in codebase, this phase extends them
- Pitfalls: HIGH - identified from direct code analysis of existing gaps

**Research date:** 2026-02-22
**Valid until:** 2026-03-22 (stable — internal codebase, no external dependency changes expected)
