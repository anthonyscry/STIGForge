---
phase: 06-security-and-operational-hardening
plan: 03
subsystem: security
tags: [release-gate, security-gate, offline, strict-mode, ci]

requires:
  - phase: 06-security-and-operational-hardening-02
    provides: hardened file and parser boundaries used by release/security verification flow
provides:
  - Deterministic offline security gate behavior with explicit unresolved-intelligence reporting
  - Strict mode wiring that blocks unresolved findings when requested
  - CI and release workflow controls for strict security gate execution semantics
affects: [release, ci, packaging, security-policy]

tech-stack:
  added: []
  patterns:
    - Offline-first security gate default with local-policy-only intelligence handling
    - Strict mode escalation from review-required unresolved findings to blocking gate failures

key-files:
  created:
    - .planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-03-SUMMARY.md
  modified:
    - tools/release/Invoke-SecurityGate.ps1
    - tools/release/Invoke-ReleaseGate.ps1
    - tools/release/security-license-policy.json
    - tools/release/security-vulnerability-exceptions.json
    - tools/release/security-secrets-policy.json
    - .github/workflows/ci.yml
    - .github/workflows/release-package.yml

key-decisions:
  - "Offline deterministic mode now treats missing external intelligence as explicit unresolved findings instead of silent pass/fail ambiguity."
  - "Strict mode is opt-in and blocks unresolved findings while default mode remains non-silent review-required behavior."

patterns-established:
  - "Security summary/report artifacts now include unresolved intelligence classification and strict-mode impact metadata."
  - "Release and CI security gate invocation expose strict-mode controls without changing offline-first defaults."

duration: 2h 26m
completed: 2026-02-08
---

# Phase 06 Plan 03: Deterministic Offline Security/Release Gate Summary

**Security and release gates now execute in deterministic offline mode by default, surface unresolved intelligence findings explicitly, and support strict-mode escalation that blocks unresolved outcomes when required.**

## Performance

- **Duration:** 2h 26m
- **Started:** 2026-02-08T18:58:26Z
- **Completed:** 2026-02-08T21:24:26Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments
- Confirmed deterministic offline gate behavior with local-policy-only evaluation and explicit unresolved intelligence reporting in artifacts.
- Wired strict-mode semantics through release gate + CI/release workflows so unresolved findings become blocking when strict mode is enabled.
- Preserved existing blocking behavior for resolved findings (vulnerabilities, license rejections, secrets) while making unresolved handling explicit and non-silent.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add deterministic offline mode to security gate with unresolved findings surfaced** - `80032d4` (fix)
2. **Task 2: Wire strict mode and CI/release execution semantics** - `b1bc0a3` (fix)
3. **Task 1 verification hardening: stabilize summary serialization across PowerShell hosts** - `64d995f` (fix)

**Plan metadata:** recorded in docs commit for this summary/state/roadmap update.

## Files Created/Modified
- `tools/release/Invoke-SecurityGate.ps1` - Adds deterministic offline unresolved-intelligence handling, strict-mode blocking semantics, and stable summary/report output.
- `tools/release/security-license-policy.json` - Extends policy schema for local package intelligence inputs.
- `tools/release/security-vulnerability-exceptions.json` - Adds deterministic offline policy metadata for unresolved vulnerability intelligence behavior.
- `tools/release/security-secrets-policy.json` - Adds deterministic local-scan policy metadata.
- `tools/release/Invoke-ReleaseGate.ps1` - Adds strict/network flag plumbing for security gate invocation and mode metadata in release summary.
- `.github/workflows/ci.yml` - Adds strict-mode toggle wiring through repository variable while preserving default deterministic behavior.
- `.github/workflows/release-package.yml` - Adds release-dispatch strict-mode input and passes strict mode to release gate.

## Decisions Made
- Used unresolved-intelligence classification as the default offline path so missing external intelligence is always visible and deterministic.
- Kept strict mode opt-in in CI/release orchestration to preserve current default pass criteria while enabling fail-closed behavior when policy requires it.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fix PowerShell summary serialization failure in deterministic security gate output**
- **Found during:** Task 1 verification (`Invoke-SecurityGate.ps1` execution under `pwsh`)
- **Issue:** Nested `[pscustomobject]` summary payload construction failed at runtime (`Argument types do not match`) in the updated unresolved-intelligence schema path.
- **Fix:** Reworked summary payload construction to hashtable-backed objects with explicit unresolved finding projection, preserving artifact schema and strict/offline semantics.
- **Files modified:** `tools/release/Invoke-SecurityGate.ps1`
- **Verification:** `pwsh -NoProfile -ExecutionPolicy Bypass -File ./tools/release/Invoke-SecurityGate.ps1 -OutputRoot ./.artifacts/security-gate/phase06-offline`
- **Committed in:** `64d995f`

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Runtime serialization fix was required for deterministic artifact generation; no scope creep.

## Issues Encountered
- Release-gate build step fails on pre-existing `net48` compile errors in `ScapBundleParser` and `ContentPackImporter` (`char` to `string` conversion), unrelated to this plan's file set.
- `lsp_diagnostics` cannot run for `.ps1` files in this environment because no PowerShell LSP server is configured.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Plan 03 security/release gate behavior is complete and verified for default and strict unresolved-finding semantics.
- Phase 06 plan 04 can proceed, but repository-level `net48` build errors should be tracked separately since they currently cause release-gate overall FAIL despite passing tests and security gate checks.

---
*Phase: 06-security-and-operational-hardening*
*Completed: 2026-02-08*
