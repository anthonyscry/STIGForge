# Phase 14 Design: Test Coverage Expansion

Date: 2026-02-23
Phase: 14 (v1.1 Operational Maturity)
Status: Approved

## Objective

Achieve enforceable, audit-friendly test quality gates for critical assemblies by combining line coverage thresholds, branch coverage visibility, and staged mutation testing.

This design targets roadmap success criteria for:
- TEST-02 (80% line coverage on critical assemblies)
- TEST-03 (branch coverage reporting in CI)
- TEST-04 (CI gating for coverage regressions)
- TEST-06 (mutation testing as a quality signal)

## Confirmed Decisions

1. Use a hybrid staged-gates rollout.
2. Roll out incrementally in slices, not as one large change.
3. Enforce 80% line coverage for critical assemblies early.
4. Start mutation testing in baseline mode, then switch to regression blocking.

## Scope

In scope:
- Coverage collection and reporting in CI.
- Threshold enforcement for critical assemblies only:
  - STIGForge.Build
  - STIGForge.Apply
  - STIGForge.Verify
  - STIGForge.Infrastructure
- Branch coverage publication on every CI run.
- Mutation testing introduction with bounded initial scope.

Out of scope:
- New product/runtime dependencies.
- Broad test refactoring outside what is required for deterministic gate behavior.
- Global coverage enforcement across all assemblies.

## Architecture

Phase 14 extends existing CI workflow (`.github/workflows/ci.yml`) and keeps quality controls in the same release discipline path already used by this repo.

Core architecture choices:
- Keep coverage collection/reporting and enforcement as separate concerns.
- Add one centralized gate entrypoint under `tools/` for deterministic threshold evaluation.
- Store scope and threshold policy in one repo-level configuration source.
- Run mutation in a dedicated bounded step/job to isolate performance and diagnostics.

This avoids duplicated ad hoc pipeline logic and keeps policy changes configurable.

## Components and Data Flow

1. Test execution with coverage enabled emits machine-readable reports (Cobertura/OpenCover style outputs) during CI.
2. Coverage artifacts are published under `.artifacts/` for auditability and post-run inspection.
3. A gate script reads the reports, applies scoped assembly filtering, computes line coverage, and enforces the 80% threshold.
4. Branch coverage metrics are summarized in CI output and preserved in artifacts (non-blocking visibility signal).
5. Mutation testing runs in a separate bounded step and emits baseline score/report artifacts.
6. Policy toggles control mutation mode (baseline/report vs enforcement) without rewriting workflow structure.

## Error Handling Model

The gate must clearly separate:
- Tooling/infrastructure failures (missing report, parse error, invalid format).
- Policy failures (valid report but threshold not met).

Failure output must include:
- Required scoped threshold and actual scoped result.
- Per-assembly contributors to failure.
- Artifact paths for traceability.

This keeps remediation fast and reduces CI churn from ambiguous errors.

## Testing Strategy for the Gate

Add tests for gate logic itself so policy enforcement remains deterministic:
- Report parsing behavior.
- Assembly scope filtering behavior.
- Threshold pass/fail evaluation.
- Diagnostics formatting for failures.

This ensures the quality gate is reliable infrastructure, not a brittle pipeline script.

## Rollout Strategy

Incremental slices:
1. Coverage collection + branch/line artifact publication.
2. Scoped 80% line coverage gate for critical assemblies.
3. Mutation baseline collection (non-blocking).
4. Mutation regression enforcement after baseline stability is established.

Each slice is independently verifiable and can be tuned via configuration.

## Acceptance Mapping

- Success Criterion 1: Gate enforces >= 80% line coverage for critical assemblies.
- Success Criterion 2: CI outputs include branch coverage summaries and raw artifacts.
- Success Criterion 3: PRs fail when scoped line coverage drops below threshold.
- Success Criterion 4: Mutation testing produces baseline and later blocks meaningful regressions.

## Risks and Mitigations

- Risk: Initial CI instability from gate introduction.
  - Mitigation: Keep scope narrow (critical assemblies only), provide clear diagnostics.
- Risk: Mutation runtime overhead.
  - Mitigation: Start with bounded project scope and baseline mode.
- Risk: Policy drift across workflow steps.
  - Mitigation: Single source for scope/threshold config and a single gate entrypoint.

## Implementation Handoff

Next step is implementation planning using the writing-plans workflow, decomposed into the four incremental slices above.
