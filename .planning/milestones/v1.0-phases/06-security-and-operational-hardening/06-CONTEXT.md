# Phase 6: Security and Operational Hardening - Context

**Gathered:** 2026-02-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Harden existing build/apply/verify/export/operator workflows to be secure-by-default and failure-safe for offline and air-gapped environments. This phase clarifies safeguard behavior (input/process/file protections, audit robustness, rollback rails, and destructive-action guards) for capabilities that already exist. It does not add new mission capabilities.

</domain>

<decisions>
## Implementation Decisions

### High-risk action protection policy
- Keep safe defaults enabled: snapshot-on, release-age gate-on, and integrity checks-on.
- Any destructive or safety-bypass operation must require explicit operator acknowledgment and write an audit entry with reason.
- UI and CLI must enforce equivalent guard semantics; no silent CLI-only bypass for core safety rails.
- Break-glass overrides are allowed for mission continuity, but they must be clearly marked high risk and never silent.

### Failure handling posture
- Fail closed for integrity-critical failures (audit chain invalid, hash/tamper mismatch, required artifact missing, export package invalid).
- Treat optional capability gaps as warnings only when mission integrity is not compromised.
- Final run summaries must separate blocking failures, recoverable warnings, and optional skipped steps.

### Rollback and recovery contract
- Pre-apply snapshot is mandatory in normal operation; skip-snapshot is break-glass only.
- Rollback remains operator-initiated (not automatic), with clear guided recovery steps and explicit rollback artifact pointers.
- Reboot/resume remains first-class; if resume context is invalid or exhausted, stop and require explicit operator decision before continuation.

### Audit and evidence strictness
- Tamper-evident audit chain integrity is required evidence; integrity failure blocks mission completion.
- Export validation (eMASS/CKL/POA&M linkage + hash checks) is blocking for "ready for submission" status.
- Support/diagnostic bundles default to least disclosure: include troubleshooting essentials while excluding secrets and credential material by default.

### Offline security gate behavior
- Security/hardening gates must execute deterministically without runtime internet dependency in default mode.
- If external intelligence is unavailable, use local policy data and mark uncertain findings for review instead of silently passing.
- Provide a strict mode option where unresolved security findings become blocking.

### Claude's Discretion
- Exact UX wording and placement for warnings and confirmations.
- Visual presentation of risk tiers and run-summary diagnostics in the WPF app.
- Internal thresholds for retry/warning aggregation, provided they preserve the policies above.

</decisions>

<specifics>
## Specific Ideas

- Align with the product mission: conservative defaults, explicit break-glass rails, and audit-ready outcomes.
- Guardrails should prevent accidental risk while still allowing deliberate mission-critical action.
- Maintain safety-model parity between CLI and WPF so operator behavior is predictable across interfaces.

</specifics>

<deferred>
## Deferred Ideas

- Multi-party approval workflow for destructive actions (future governance-focused phase).
- Central SIEM/event-stream export for audit data (future integration phase).

</deferred>

---

*Phase: 06-security-and-operational-hardening*
*Context gathered: 2026-02-08*
