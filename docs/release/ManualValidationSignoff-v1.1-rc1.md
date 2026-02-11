# v1.1 RC1 Manual Validation Signoff

Use this worksheet to close checklist sections 3 and 4 in `docs/release/ShipReadinessChecklist.md`.

Candidate commit: `0b0f5ed206f6b22dcf58f82a543cf0ea8a796ce3`

## Test Environment

- Operator:
- Date (UTC):
- Hostname:
- OS version:
- Baseline version under test (for upgrade/rollback):

## Section 3 - Functional Mission Validation

### 3.1 End-to-end mission flow in clean environment

- Expected: import -> build -> apply -> verify -> export completes with no blocking mission failures.
- Evidence paths:
  - Bundle root:
  - `Apply/apply_run.json`:
  - `Verify/**/consolidated-results.json`:
  - `Export/*` output folder:
- Result: PASS / FAIL
- Notes:

### 3.2 Manual-control flow (answer + evidence + summary)

- Expected: at least one manual control is answered with rationale and evidence, and mission summary reflects updated state.
- Evidence paths:
  - Manual answers file:
  - Evidence artifact path:
  - Updated mission summary/report path:
- Result: PASS / FAIL
- Notes:

### 3.3 `export-emass` validator is clean

- Expected: export package is generated and validator reports no errors.
- Evidence paths:
  - Export root:
  - Validation report (`validation-report.md` or equivalent):
  - Validation summary JSON (if present):
- Result: PASS / FAIL
- Notes:

### 3.4 `support-bundle` redaction and manifest review

- Expected: support bundle contains manifest and expected redactions, with no sensitive-only artifacts unless explicitly opted in.
- Evidence paths:
  - Support bundle root:
  - Support bundle manifest:
  - Redaction summary/report path:
- Result: PASS / FAIL
- Notes:

## Section 4 - Upgrade, Rebase, and Rollback Assurance

### 4.1 Upgrade from previous production release

- Expected: upgrade from prior production release to RC succeeds and primary workflows remain operational.
- Evidence paths:
  - Upgrade execution log:
  - Post-upgrade sanity output (bundle/apply/verify):
- Result: PASS / FAIL
- Notes:

### 4.2 Overlay rebase against quarterly update

- Expected: overlay rebase workflow completes and rebase report is actionable with expected conflict semantics.
- Evidence paths:
  - Rebase report:
  - Rebased overlay artifact:
- Result: PASS / FAIL
- Notes:

### 4.3 Rollback/uninstall and post-rollback operability

- Expected: rollback path executes successfully and environment remains operational after rollback.
- Evidence paths:
  - Rollback script/log:
  - Post-rollback health checks:
- Result: PASS / FAIL
- Notes:

### 4.4 Data retention validation (`.stigforge` state)

- Expected: expected packs, overlays, and profile state are retained or removed per policy after upgrade/rollback.
- Evidence paths:
  - Retained artifacts list:
  - Removed artifacts list (if applicable):
- Result: PASS / FAIL
- Notes:

## Final Manual Signoff

- Section 3 overall: PASS / FAIL
- Section 4 overall: PASS / FAIL
- Blocking issues remaining:
- Signoff recommendation: GO / NO-GO

When complete, update `docs/release/GoNoGo-v1.1-rc1.md` checklist rows 3 and 4 with PASS evidence and finalize the decision block.
