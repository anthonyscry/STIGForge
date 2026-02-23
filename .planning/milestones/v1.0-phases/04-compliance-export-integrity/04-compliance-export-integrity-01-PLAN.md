---
phase: 04-compliance-export-integrity
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Export/ExportStatusMapper.cs
  - src/STIGForge.Export/EmassExporter.cs
  - src/STIGForge.Export/CklExporter.cs
  - src/STIGForge.Export/StandalonePoamExporter.cs
  - tests/STIGForge.UnitTests/Export/ExportStatusMapperTests.cs
  - tests/STIGForge.UnitTests/Export/EmassExporterConsistencyTests.cs
autonomous: true
must_haves:
  truths:
    - "The same control status maps consistently across eMASS export, CKL export, and standalone POA&M export."
    - "Control/evidence index rows are deterministic across repeated exports from identical bundle inputs."
    - "Export metadata includes source-trace information for audit and troubleshooting."
  artifacts:
    - path: "src/STIGForge.Export/ExportStatusMapper.cs"
      provides: "Shared status normalization policy"
      contains: "MapToVerifyStatus|MapToCklStatus"
    - path: "src/STIGForge.Export/EmassExporter.cs"
      provides: "Deterministic index assembly and export trace output"
      contains: "WriteControlEvidenceIndex|WriteManifest"
    - path: "tests/STIGForge.UnitTests/Export/EmassExporterConsistencyTests.cs"
      provides: "Determinism regression coverage"
  key_links:
    - from: "src/STIGForge.Export/ExportStatusMapper.cs"
      to: "src/STIGForge.Export/CklExporter.cs"
      via: "shared status mapping call"
      pattern: "ExportStatusMapper\.MapToCklStatus"
    - from: "src/STIGForge.Export/ExportStatusMapper.cs"
      to: "src/STIGForge.Export/StandalonePoamExporter.cs"
      via: "shared status normalization"
      pattern: "ExportStatusMapper\.MapToVerifyStatus"
---

<objective>
Create a single export status-mapping policy and deterministic index assembly so exported artifacts stay internally consistent and reproducible.

Purpose: Remove drift between export paths and eliminate nondeterministic output that complicates audits and regression checks.
Output: Shared status mapper, deterministic export index/trace behavior, and unit regression tests.
</objective>

<execution_context>
@/home/ajt/.config/opencode/get-shit-done/workflows/execute-plan.md
@/home/ajt/.config/opencode/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@src/STIGForge.Export/EmassExporter.cs
@src/STIGForge.Export/CklExporter.cs
@src/STIGForge.Export/StandalonePoamExporter.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Centralize export status normalization</name>
  <files>src/STIGForge.Export/ExportStatusMapper.cs, src/STIGForge.Export/EmassExporter.cs, src/STIGForge.Export/CklExporter.cs, src/STIGForge.Export/StandalonePoamExporter.cs</files>
  <action>Create one shared status-mapping utility for VerifyStatus/CKL status conversion and refactor all export entry points to use it. Preserve existing accepted status spellings and keep manual-answer precedence behavior unchanged.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ExportStatusMapperTests|FullyQualifiedName~CklExporterTests|FullyQualifiedName~StandalonePoamExporterTests"</verify>
  <done>All export paths produce aligned status semantics for pass/fail/not-applicable/not-reviewed values without duplicated mapping logic.</done>
</task>

<task type="auto">
  <name>Task 2: Make control evidence index and manifest trace deterministic</name>
  <files>src/STIGForge.Export/EmassExporter.cs</files>
  <action>Refactor control/evidence index generation to use stable ordering and deterministic grouping keys, then include a machine-readable export trace section in manifest output (source report files, tool counts, status totals). Do not change top-level export directory structure.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~EmassExporterConsistencyTests"</verify>
  <done>Repeated export runs against the same bundle produce byte-stable index ordering and a consistent trace payload in manifest metadata.</done>
</task>

<task type="auto">
  <name>Task 3: Add deterministic export regression coverage</name>
  <files>tests/STIGForge.UnitTests/Export/ExportStatusMapperTests.cs, tests/STIGForge.UnitTests/Export/EmassExporterConsistencyTests.cs</files>
  <action>Add tests for status conversion edge cases, manual-answer override mapping, and deterministic index row order across repeated runs. Use fixture-based or temp-directory bundles and keep assertions explicit on row ordering and status values.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --filter "FullyQualifiedName~ExportStatusMapperTests|FullyQualifiedName~EmassExporterConsistencyTests"</verify>
  <done>Tests fail when export mappings diverge or index ordering becomes non-deterministic, and pass when deterministic behavior is preserved.</done>
</task>

</tasks>

<verification>
Run targeted export unit tests and confirm deterministic output content by comparing repeated export runs on identical fixtures.
</verification>

<success_criteria>
Export status normalization is centralized and deterministic export index generation is covered by repeatability tests.
</success_criteria>

<output>
After completion, create `.planning/phases/04-compliance-export-integrity/04-compliance-export-integrity-01-SUMMARY.md`
</output>
