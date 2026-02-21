---
phase: 03-verification-integration
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Verify/Adapters/CklAdapter.cs
  - src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs
  - src/STIGForge.Verify/Adapters/ScapResultAdapter.cs
  - src/STIGForge.Verify/VerifyOrchestrator.cs
  - tests/STIGForge.UnitTests/Verify/VerifyOrchestratorTests.cs
  - tests/STIGForge.UnitTests/Verify/VerifyAdapterParsingTests.cs
autonomous: true
must_haves:
  truths:
    - "Merged verify output resolves each control to one deterministic status."
    - "Conflicting tool results produce explicit conflict diagnostics."
    - "Parser errors do not crash consolidation and are reported clearly."
  artifacts:
    - path: "src/STIGForge.Verify/VerifyOrchestrator.cs"
      provides: "Deterministic merge and conflict resolution"
      contains: "MergeReports"
    - path: "src/STIGForge.Verify/Adapters/ScapResultAdapter.cs"
      provides: "SCAP to normalized mapping"
      contains: "MapScapStatus"
    - path: "tests/STIGForge.UnitTests/Verify/VerifyOrchestratorTests.cs"
      provides: "Conflict precedence regression coverage"
  key_links:
    - from: "src/STIGForge.Verify/Adapters/*.cs"
      to: "src/STIGForge.Verify/VerifyOrchestrator.cs"
      via: "NormalizedVerifyReport -> MergeReports"
      pattern: "ParseResults|MergeReports"
---

<objective>
Harden verification normalization and reconciliation so merged outputs are deterministic, conservative, and auditable.

Purpose: Prevent silent drift and inconsistent compliance reporting when SCAP, Evaluate-STIG, and manual CKL data disagree.
Output: Robust adapter parsing, deterministic merge behavior, and regression tests for conflict paths.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Verify/VerifyOrchestrator.cs
@src/STIGForge.Verify/Adapters/CklAdapter.cs
@src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs
@src/STIGForge.Verify/Adapters/ScapResultAdapter.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Normalize adapter status and timestamp handling</name>
  <files>src/STIGForge.Verify/Adapters/CklAdapter.cs, src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs, src/STIGForge.Verify/Adapters/ScapResultAdapter.cs</files>
  <action>Align all adapters to one canonical status mapping policy, strict-but-safe null handling, and stable timestamp parsing. Ensure unknown or malformed values degrade to explicit NotReviewed/Unknown states instead of throwing. Keep existing offline-first file parsing patterns; do not add external dependencies.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~VerifyAdapterParsingTests"</verify>
  <done>Each adapter maps malformed or unexpected values deterministically and returns a NormalizedVerifyReport with diagnostics instead of process-terminating exceptions.</done>
</task>

<task type="auto">
  <name>Task 2: Enforce deterministic merge reconciliation rules</name>
  <files>src/STIGForge.Verify/VerifyOrchestrator.cs</files>
  <action>Refine MergeReports/ReconcileResults to guarantee deterministic output ordering and reproducible precedence resolution (Manual CKL > Evaluate-STIG > SCAP, then timestamp, then severity). Ensure conflict diagnostics include resolution reason and competing tool statuses for auditability.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~VerifyOrchestratorTests"</verify>
  <done>Given identical inputs across runs, consolidated output order and resolved statuses remain stable; pass/fail conflicts always emit an explanatory conflict entry.</done>
</task>

<task type="auto">
  <name>Task 3: Add regression tests for adapter and merge edge cases</name>
  <files>tests/STIGForge.UnitTests/Verify/VerifyOrchestratorTests.cs, tests/STIGForge.UnitTests/Verify/VerifyAdapterParsingTests.cs</files>
  <action>Create focused tests for malformed input files, mixed-status conflicts, duplicate control IDs, and metadata/evidence merges. Include at least one scenario where manual CKL intentionally overrides automated Fail/Pass disagreement to validate precedence intent.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~Verify"</verify>
  <done>New verification tests fail on non-deterministic merge behavior and pass when precedence, diagnostics, and parsing resilience are correct.</done>
</task>

</tasks>

<verification>
Run targeted verification unit tests and confirm deterministic report output with repeated execution on the same fixture set.
</verification>

<success_criteria>
Verification merge behavior is deterministic, conflict reasoning is explicit, and adapter failures are captured as diagnostics rather than hard failures.
</success_criteria>

<output>
After completion, create `.planning/phases/03-verification-integration/03-verification-integration-01-SUMMARY.md`
</output>
