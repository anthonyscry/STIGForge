# Phase 2: Policy Scope and Safety Gates - Context

**Gathered:** 2026-02-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver deterministic policy gating, scope filtering, and safety gates so operators can make auditable scope/automation decisions before execution touches hosts. This phase wires existing services (ClassificationScopeService, ReleaseAgeGate, OverlayRebaseService) into full operator workflows with CLI commands, WPF surfaces, and deterministic reporting. Phase 3 (Deterministic Mission Execution Core) consumes these gates during build/apply/verify.

</domain>

<decisions>
## Implementation Decisions

### Policy Configuration Surface
- Profile is the single carrier for all policy knobs (NaPolicy, AutomationPolicy already modeled)
- JSON file import/export is the primary configuration mechanism (established pattern in BuildCommands.cs)
- CLI commands: `profile list`, `profile show <id>`, `profile create --from-json`, `profile update`, `profile export`
- WPF: Profile editor form in settings area, not inline during guided run — keep guided run focused on execution
- Policy knobs are edited at rest, not during a live mission run
- Determinism contract: identical Profile + identical Controls = identical gate outcomes, always

### Overlay Precedence and Conflict Resolution
- Overlay precedence is positional — array order in Profile.OverlayIds defines priority (last wins)
- OverlayRebaseService already scores confidence and flags blocking conflicts
- Conflict report emitted as `overlay_conflict_report.csv` alongside existing `na_scope_filter_report.csv`
- Report columns: ControlKey, WinningOverlayId, OverriddenOverlayId, WinningValue, OverriddenValue, Reason
- Blocking conflicts (IsBlockingConflict=true) halt bundle build with explicit error — no silent resolution
- CLI: `overlay diff <overlay-a> <overlay-b>` shows field-level conflicts before build

### Scope Filtering Behavior
- ClassificationScopeService.Compile() is the single entry point for scope decisions (already implemented)
- Three modes enforced: Classified, Unclassified, Mixed — set on Profile.ClassificationMode
- Auto-NA behavior: controls outside scope are marked NotApplicable when confidence meets profile threshold
- Ambiguous decisions (Confidence < threshold OR ScopeTag=Unknown) route to ReviewQueue
- `na_scope_filter_report.csv` emitted by BundleBuilder (already implemented) — no format changes needed
- Review queue items surface in CLI (`bundle review-queue <bundle-path>`) and WPF guided run step

### Safety Gate Enforcement
- ReleaseAgeGate blocks auto-apply for new/changed controls within grace period (already implemented)
- Grace period is Profile.AutomationPolicy.NewRuleGraceDays (default 30, configurable)
- Break-glass override pattern preserved: --force-auto-apply + --break-glass-ack + --break-glass-reason (CLI)
- WPF equivalent: confirmation dialog with reason text field, logged to AuditTrailService
- All gate decisions (pass/block/override) logged to automation_gate.json per build (already implemented)
- Gate decisions are append-only audit entries — no retroactive modification

### Claude's Discretion
- Exact CSV column ordering and formatting for new overlay_conflict_report.csv
- Profile editor WPF layout and field grouping
- CLI output formatting (table vs structured text) for profile/overlay commands
- Whether to add a `profile validate` command for checking policy consistency before build

</decisions>

<specifics>
## Specific Ideas

- Break-glass overrides must capture operator identity and reason in the audit trail — this is a compliance requirement, not a nice-to-have
- The review queue should show WHY each control was flagged (confidence score, scope tag, gate reason) so operators can make informed decisions
- Overlay conflict reports should be deterministic — same overlays in same order always produce identical report content
- Follow existing BundleBuilder report pattern: write artifacts to Reports/ directory in bundle tree

</specifics>

<deferred>
## Deferred Ideas

- Policy versioning and historical tracking — could be its own phase for audit trail completeness
- Policy templates/presets for common deployment scenarios — Phase 3+ after core execution works
- Interactive overlay conflict resolution wizard — Phase 4 (Human Resolution) is the right home
- Fleet-wide policy distribution — Phase 5 (Fleet-lite)

</deferred>

---

*Phase: 02-apply-logic*
*Context gathered: 2026-02-22*
