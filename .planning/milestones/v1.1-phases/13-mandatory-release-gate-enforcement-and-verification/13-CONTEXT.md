# Phase 13: Mandatory Release-Gate Enforcement and Verification - Context

**Gathered:** 2026-02-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Enforce fail-closed release-package behavior and restore QA requirement verification evidence for promotion paths (`QA-01`, `QA-02`, `QA-03`).

This phase locks promotion semantics and evidence contracts for CI, release, and VM flows. It does not add new product capabilities.

</domain>

<decisions>
## Implementation Decisions

### Required evidence set
- Promotion requires a full proof bundle (contract checks, release summary, CI/VM evidence, and verification report).
- Missing any required proof item blocks promotion.
- Evidence locations and names are deterministic (fixed paths/names).
- CI, release, and VM flows share one mandatory core evidence set, with flow-specific extras allowed.

### Failure behavior policy
- Missing required proof must stop execution before packaging artifacts are produced.
- Failures must be explicit fail-closed blockers, not delayed soft warnings.
- Failure output should prioritize actionable recovery guidance.

### Diagnostics and recovery guidance
- Failure output uses a checklist-first format: what is missing, why blocked, and exact next actions.
- Blocked runs include copy-paste recovery commands.
- Failure categories are explicit blocker types (missing-proof, failed-check, disabled-check), not generic failure text.
- Recovery guidance appears both inline in workflow output and in persisted report artifacts.

### Cross-flow consistency
- CI, release, and VM enforce identical blocked/fail semantics.
- All three flows validate the same required contract signals.
- Disabling any required check is treated as a blocker in that flow.
- Contract signal naming is standardized across workflows and reports.

### Claude's Discretion
- Exact wording and formatting of checklist/recovery output, as long as it stays actionable and consistent.
- Exact ordering of contract checks, as long as fail-closed stop-before-package semantics are preserved.
- Additional non-blocking diagnostics fields that improve operator triage without weakening enforcement.

</decisions>

<specifics>
## Specific Ideas

- Operator-facing failures should answer three things immediately: what blocked, why it blocked, and what command to run next.
- Keep enforcement deterministic and auditable across all promotion surfaces (CI, release, VM, packaging evidence).

</specifics>

<deferred>
## Deferred Ideas

- Warning-only bypass behavior for required checks was discussed but deferred. This would weaken fail-closed enforcement and belongs in a separate future phase if policy changes.

</deferred>

---

*Phase: 13-mandatory-release-gate-enforcement-and-verification*
*Context gathered: 2026-02-16*
