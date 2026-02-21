---
phase: 06-security-and-operational-hardening
plan: 03
type: execute
wave: 1
depends_on: []
files_modified:
  - tools/release/Invoke-SecurityGate.ps1
  - tools/release/Invoke-ReleaseGate.ps1
  - tools/release/security-license-policy.json
  - tools/release/security-vulnerability-exceptions.json
  - tools/release/security-secrets-policy.json
  - .github/workflows/ci.yml
  - .github/workflows/release-package.yml
autonomous: true
must_haves:
  truths:
    - "Security gate behavior is deterministic in offline/air-gapped mode"
    - "Unresolved security findings are visible and never silently passed"
    - "Strict mode can block unresolved findings when required"
  artifacts:
    - path: "tools/release/Invoke-SecurityGate.ps1"
      provides: "Offline deterministic security gate mode with strict unresolved handling"
    - path: "tools/release/Invoke-ReleaseGate.ps1"
      provides: "Release gate wiring for hardened security gate modes"
    - path: ".github/workflows/ci.yml"
      provides: "CI execution path for deterministic gate mode"
  key_links:
    - from: "tools/release/Invoke-ReleaseGate.ps1"
      to: "tools/release/Invoke-SecurityGate.ps1"
      via: "gate invocation flags for offline and strict behavior"
      pattern: "Invoke-SecurityGate|strict|offline|unresolved"
    - from: "tools/release/security-*.json"
      to: "Invoke-SecurityGate.ps1"
      via: "policy-driven gate outcomes"
      pattern: "policy|allowed|exceptions|warnings"
---

<objective>
Make release/security gates deterministic and policy-driven in offline environments, with explicit strict-mode behavior for unresolved findings.

Purpose: Ensure air-gapped operation safety and predictability without runtime internet dependency.
Output: Hardened security/release gate scripts and CI wiring that expose unresolved findings and enforce strict-mode blocking.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/06-security-and-operational-hardening/06-CONTEXT.md
@.planning/phases/06-security-and-operational-hardening/06-RESEARCH.md
@docs/release/SecurityGatePolicies.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add deterministic offline mode to security gate with unresolved findings surfaced</name>
  <files>tools/release/Invoke-SecurityGate.ps1, tools/release/security-license-policy.json, tools/release/security-vulnerability-exceptions.json, tools/release/security-secrets-policy.json</files>
  <action>Implement a deterministic offline security gate mode that uses local policy/intel inputs only and never silently passes unresolved findings. Preserve existing resolved pass/fail behavior, but add explicit unresolved classification for missing external intelligence. Keep output artifacts structured and stable for downstream automation.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-SecurityGate.ps1 -OutputRoot .\.artifacts\security-gate\phase06-offline</verify>
  <done>Security gate can run offline deterministically with explicit unresolved reporting and no silent pass behavior.</done>
</task>

<task type="auto">
  <name>Task 2: Wire strict mode and CI/release execution semantics</name>
  <files>tools/release/Invoke-ReleaseGate.ps1, .github/workflows/ci.yml, .github/workflows/release-package.yml</files>
  <action>Expose strict mode in release and CI gate wiring so unresolved findings are blocking when strict mode is enabled, while preserving default deterministic behavior and clear diagnostics. Keep offline-first defaults and PowerShell 5.1 compatibility requirements intact. Include explicit scenario coverage for both outcomes: default mode marks unresolved findings for review (non-silent) and strict mode fails with blocking status.</action>
  <verify>powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\.artifacts\release-gate\phase06 && powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-SecurityGate.ps1 -OutputRoot .\.artifacts\security-gate\phase06-strict -Strict</verify>
  <done>CI and release workflows can execute deterministic gate mode, default mode surfaces unresolved findings for review, and strict mode blocks unresolved findings with clear artifacted diagnostics.</done>
</task>

</tasks>

<verification>
- Execute security gate in deterministic offline mode and inspect summary/report artifacts.
- Execute release gate to confirm security gate integration and strict-mode behavior.
</verification>

<success_criteria>
- Offline gate execution is deterministic and policy-driven.
- Strict mode can block unresolved findings with explicit diagnostics.
</success_criteria>

<output>
After completion, create `.planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-03-SUMMARY.md`
</output>
