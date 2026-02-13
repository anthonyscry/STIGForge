# STIGForge Roadmap Execution (Sprint View)

## Scope
- Deliver full feature coverage, stronger UI/workflow, hardened apply pipeline, stable multi-format compatibility, and release readiness.
- Planning horizon: 12 sprints (1 week each).

## Roles
- Lead Engineer
- Backend Engineer
- Frontend Engineer
- QA/Automation
- Security/Platform

## Global Definition of Done
- Build and tests pass in CI.
- No silent data loss in format transformations.
- Boundary validation and actionable error messages are present.
- Docs updated for behavior and compatibility changes.

## Phase 0 - Baseline Stabilization (Sprint 1)
### Goal
Clean, deterministic baseline for all future phases.

### Backlog
- P0-1 (8 SP, Lead): fix compile blockers and critical warnings.
- P0-2 (5 SP, Backend): standardize structured logs and error codes across import/build/apply/verify/export.
- P0-3 (5 SP, QA): enforce CI quality gates (build, tests, fixture smoke).
- P0-4 (3 SP, Backend): remove non-determinism from artifact generation.

### Exit Criteria
- `dotnet build` passes in CI.
- Deterministic fixture run succeeds repeatedly.

## Phase 1 - Canonical Contract (Sprint 2)
### Goal
Single versioned canonical model across modules.

### Backlog
- P1-1 (8 SP, Lead): finalize canonical schema and version marker.
- P1-2 (5 SP, Backend): strict validators at import/storage/export boundaries.
- P1-3 (3 SP, Backend): migration scaffold for schema evolution.
- P1-4 (5 SP, QA): canonical contract fixture suite.

### Exit Criteria
- All core modules consume canonical model only.
- Invalid payloads fail fast with actionable diagnostics.

## Phase 2 - Format Compatibility Layer (Sprints 3-4)
### Goal
Reliable adapters and explicit compatibility guarantees.

### Backlog
- P2-1 (8 SP, Backend): harden STIG/XCCDF adapter, extract classification metadata.
- P2-2 (8 SP, Backend): complete SCAP adapter and preserve OVAL references.
- P2-3 (5 SP, Backend): normalize GPO/ADMX adapter with deterministic defaults.
- P2-4 (5 SP, Backend): add PowerSTIG mapping validation and traceability.
- P2-5 (3 SP, QA): generate compatibility matrix report in CI artifacts.

### Exit Criteria
- Golden fixtures pass for STIG/XCCDF/SCAP/GPO paths.
- Lossy mappings are explicitly reported, never silent.

## Phase 3 - Data Integration Flow and Migration Safety (Sprint 5)
### Goal
Deterministic ingest-normalize-store flow with conflict handling.

### Backlog
- P3-1 (5 SP, Backend): detect duplicate/contradictory control identity conflicts.
- P3-2 (5 SP, Backend): atomic writes and crash-safe recovery checkpoints.
- P3-3 (8 SP, QA): round-trip tests for supported import/export combinations.

### Exit Criteria
- No corruption on interruption scenarios.
- Round-trip behavior is documented and test-enforced.

## Phase 4 - Apply/Verify/Export Completion (Sprints 6-7)
### Goal
Complete E2E operational pipeline.

### Backlog
- P4-1 (8 SP, Backend): finalize apply fallbacks and idempotency controls.
- P4-2 (8 SP, Backend): unify verify outputs (SCAP/Evaluate-STIG/CKL) into one normalized report.
- P4-3 (8 SP, Backend): complete eMASS package outputs (indexes, hashes, required docs).
- P4-4 (5 SP, QA): CI E2E suite for import->build->apply-sim->verify->export.

### Exit Criteria
- Full pipeline succeeds on fixture corpus in CI.
- eMASS package validator checks pass.

## Phase 5 - UI/Workflow Build-Out (Sprints 8-9)
### Goal
Operator-complete workflow in app UI.

### Backlog
- P5-1 (8 SP, Frontend): project dashboard + import/review/filter workflow.
- P5-2 (8 SP, Frontend): plan/dry-run/apply UX with resumable status.
- P5-3 (8 SP, Frontend+Backend): manual check wizard + answer workflow integration.
- P5-4 (5 SP, Frontend): evidence workflow UI linked to control IDs.

### Exit Criteria
- Full mission flow is executable from UI without CLI fallback.

## Phase 6 - Security and Operational Hardening (Sprint 10)
### Goal
Secure-by-default and failure-safe operations.

### Backlog
- P6-1 (8 SP, Security): input/file/process hardening controls.
- P6-2 (3 SP, Security): dependency + supply-chain scanning gates in CI.
- P6-3 (5 SP, Backend): complete auditable event trail.
- P6-4 (5 SP, Backend): rollback rails and destructive-action guards.

### Exit Criteria
- Security checklist passes; critical findings remediated.

## Phase 7 - Stability, Compatibility, and Release Readiness (Sprints 11-12)
### Goal
Release candidate with compatibility guarantees.

### Backlog
- P7-1 (8 SP, QA): soak tests on mixed-format large corpora.
- P7-2 (5 SP, QA): quarterly-update compatibility regression pack and drift alerting.
- P7-3 (3 SP, Lead): release checklist + upgrade/migration docs.
- P7-4 (2 SP, Lead): final GA sign-off across quality/security/ops.

### Exit Criteria
- Release candidate passes stability budget and compatibility gates.

## Special Track - Answerfile Standardization (Sprints 3-4)
### Goal
One canonical answerfile format for test workflows.

### Backlog
- AX-1 (2 SP, Lead): select canonical answerfile format and document source of truth.
- AX-2 (3 SP, Backend): align templates and README examples to canonical format.
- AX-3 (3 SP, QA): add schema validation test in CI for answerfile fixtures.

### Exit Criteria
- No template mismatch across root/testing/tooling answerfile paths.
