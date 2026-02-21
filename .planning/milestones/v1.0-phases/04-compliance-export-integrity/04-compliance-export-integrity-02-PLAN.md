---
phase: 04-compliance-export-integrity
plan: 02
type: execute
wave: 2
depends_on:
  - 04-compliance-export-integrity-01
files_modified:
  - src/STIGForge.Export/EmassPackageValidator.cs
  - src/STIGForge.Export/ExportModels.cs
  - tests/STIGForge.UnitTests/Export/EmassPackageValidatorTests.cs
autonomous: true
must_haves:
  truths:
    - "Package validation catches missing required artifacts and malformed core documents before submission."
    - "Validator reports cross-artifact mismatches between control index, POA&M content, and attestations."
    - "Validation output is actionable (clear errors vs warnings) for operators."
  artifacts:
    - path: "src/STIGForge.Export/EmassPackageValidator.cs"
      provides: "Structural and linkage integrity checks"
      contains: "ValidatePackage|ValidateContentIntegrity"
    - path: "src/STIGForge.Export/ExportModels.cs"
      provides: "Validation result fields needed by consumers"
      contains: "ValidationResult"
    - path: "tests/STIGForge.UnitTests/Export/EmassPackageValidatorTests.cs"
      provides: "Validator regression scenarios"
  key_links:
    - from: "src/STIGForge.Export/EmassPackageValidator.cs"
      to: "06_Index/control_evidence_index.csv"
      via: "index parsing and control key checks"
      pattern: "control_evidence_index"
    - from: "src/STIGForge.Export/EmassPackageValidator.cs"
      to: "03_POAM/poam.json"
      via: "control linkage validation"
      pattern: "poam\.json"
---

<objective>
Upgrade package validation from basic structure checks to submission-grade integrity checks across all export artifacts.

Purpose: Prevent incomplete or internally inconsistent submission packages from reaching eMASS workflows.
Output: Stronger validator logic, richer validation results, and focused unit coverage for failure modes.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Export/EmassPackageValidator.cs
@src/STIGForge.Export/EmassExporter.cs
@.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-01-PLAN.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add cross-artifact consistency checks</name>
  <files>src/STIGForge.Export/EmassPackageValidator.cs</files>
  <action>Extend validator logic to parse control index, POA&amp;M JSON, and attestation records, then flag inconsistent control references, missing linked artifacts, and invalid status combinations. Keep checks offline and file-based only.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~EmassPackageValidatorTests"</verify>
  <done>Validator reports deterministic errors for broken linkage and does not pass packages with cross-artifact control mismatches.</done>
</task>

<task type="auto">
  <name>Task 2: Improve validation result model for operator diagnostics</name>
  <files>src/STIGForge.Export/ExportModels.cs, src/STIGForge.Export/EmassPackageValidator.cs</files>
  <action>Enhance validation result output with structured summary fields (checked control counts, mismatched control counts, missing artifact counts) while preserving existing `IsValid`, `Errors`, and `Warnings` semantics for compatibility.</action>
  <verify>dotnet build STIGForge.sln</verify>
  <done>Validation results include machine-usable summary metrics without breaking existing consumers that rely on legacy fields.</done>
</task>

<task type="auto">
  <name>Task 3: Build validator regression suite for common package defects</name>
  <files>tests/STIGForge.UnitTests/Export/EmassPackageValidatorTests.cs</files>
  <action>Add fixture-driven validator tests covering missing required files, malformed JSON artifacts, hash mismatch scenarios, and control-reference mismatch scenarios. Include at least one fully valid package fixture proving pass behavior.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~EmassPackageValidatorTests"</verify>
  <done>Test suite reliably detects key package integrity defects and validates successful pass behavior for a complete package.</done>
</task>

</tasks>

<verification>
Execute validator unit tests and confirm defect scenarios return expected errors/warnings and summary metrics.
</verification>

<success_criteria>
Validation catches structural and linkage defects before submission and returns actionable diagnostics for remediation.
</success_criteria>

<output>
After completion, create `.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-02-SUMMARY.md`
</output>
