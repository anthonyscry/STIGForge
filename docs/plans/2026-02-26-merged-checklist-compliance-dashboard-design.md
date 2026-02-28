# Merged Checklist and Compliance Dashboard Design

Date: 2026-02-26
Status: Approved for planning

## Context

STIGForge currently runs Evaluate-STIG and SCC during Verify, then collects results from CKL files under the output root. We need a deterministic snapshot merge that produces a STIG Viewer-importable merged checklist and a dashboard compliance visualization with full rule count.

## Goals

1. Produce one merged checklist view where findings from multiple sources are integrated into a single exportable result.
2. Apply snapshot semantics: one final control outcome per benchmark/control key, with appended provenance and evidence.
3. Keep STIG Viewer import compatibility as a first-class requirement.
4. Add a dashboard compliance pie/donut view including full rule count.

## Non-Goals

1. Redesigning the full Verify architecture in one pass.
2. Introducing a heavy charting dependency.
3. Changing existing export defaults away from CKL.

## Current State

- Verify workflow consolidates by parsing CKLs under output root (`VerificationWorkflowService` + `VerifyReportWriter.BuildFromCkls`).
- CKL export already supports one `CHECKLIST` containing multiple `iSTIG` sections.
- Dashboard tracks baseline/fixed/remaining counts but does not render a chart.

## Decision Summary

### 1) Snapshot Merge Service in Verify Layer

Add a dedicated merge service in the Verify layer and invoke it before export. Keep exporter focused on serialization and file output.

Rationale:
- Minimal risk to existing flow.
- Avoids overloading exporter with merge policy.
- Allows isolated unit testing of precedence logic.

### 2) Canonical Merge Key

Use a composite key:

- `AssetIdentity + BenchmarkId + ControlId`

ControlId resolution order:
1. `VulnId`
2. `RuleId`
3. deterministic fallback key

If `BenchmarkId` is unavailable, use legacy fallback and emit a diagnostic marker.

### 3) Status Precedence

For duplicate controls across sources, resolve status by precedence:

1. Manual override / reviewer input
2. SCC
3. Evaluate-STIG
4. Baseline fallback

The merged snapshot keeps one final status per merged key.

### 4) Evidence and Provenance Policy

Never discard source evidence. Append source details with provenance tags and stable ordering:

- source label
- timestamp (if available)
- selected evidence text

Persist source path metadata where available for traceability.

### 5) STIG Viewer Export Safety

For generated merged CKL:

- single `ASSET` per checklist
- multiple `iSTIG` sections allowed
- exactly one `VULN` per `Vuln_Num` per `iSTIG`
- map statuses strictly to CKL-safe values:
  - `NotAFinding`
  - `Open`
  - `Not_Applicable`
  - `Not_Reviewed`
- preserve `STIG_INFO` integrity per `iSTIG`

Default export remains CKL, with CKLB optional.

## Dashboard Compliance Visualization

### UI Placement

Add a `Compliance Summary` card in `DashboardView` below workflow cards and above failure/results sections.

### Data Model

Extend verify summary data surfaced to the app with:

- `TotalRuleCount`
- `PassCount`
- `FailCount`
- `NotApplicableCount`
- `NotReviewedCount`
- `ErrorCount`

Derived dashboard fields:

- `CompliancePassCount = PassCount`
- `ComplianceFailCount = FailCount`
- `ComplianceOtherCount = NotApplicableCount + NotReviewedCount + ErrorCount`
- `CompliancePercent = Pass / (Pass + Fail + Error)` when denominator > 0

### Rendering Approach

Use a lightweight custom WPF donut/pie control (no additional chart package).

Behavior:
- pre-verify: placeholder message
- post-verify: donut + legend + total rule count + compliance percent

## Testing Strategy

### Unit Tests

1. Merge key resolution and dedupe behavior.
2. Precedence correctness across conflicting sources.
3. Evidence append ordering and provenance retention.
4. Status normalization to CKL-safe values.

### Integration Tests

1. Baseline + SCC + Evaluate + manual override fixture merges into one snapshot row per key.
2. Exported CKL validates one `VULN` per key per `iSTIG`.
3. STIG info and asset metadata remain valid.
4. Dashboard summary values map correctly from verify result counts.

## Risks and Mitigations

1. Missing benchmark identifiers from some sources.
   - Mitigation: deterministic fallback key + diagnostics.
2. Evidence text growth from append-only policy.
   - Mitigation: structured separators and bounded formatting policy.
3. Drift between verify summary and UI calculations.
   - Mitigation: centralize mapping and test viewmodel projections.

## Rollout

1. Introduce merge service and tests behind existing workflow path.
2. Enable merged checklist export path as default behavior.
3. Add dashboard compliance card and verify no regressions in existing workflow UX.

## Acceptance Criteria

1. Merged checklist exports import in STIG Viewer without duplicate-control issues.
2. Conflicting control outcomes resolve by agreed precedence.
3. Evidence from multiple tools is preserved in merged output.
4. Dashboard shows total rule count and compliance pie/donut after verify.
5. Existing CKL/CKLB export paths remain functional.
