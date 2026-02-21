---
phase: 06-security-and-operational-hardening
plan: 02
type: execute
wave: 1
depends_on: []
files_modified:
  - src/STIGForge.Content/Import/ContentPackImporter.cs
  - src/STIGForge.Content/Import/ScapBundleParser.cs
  - src/STIGForge.Content/Import/OvalParser.cs
  - src/STIGForge.Verify/Adapters/CklAdapter.cs
  - src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs
  - src/STIGForge.Verify/Adapters/ScapResultAdapter.cs
  - tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs
  - tests/STIGForge.UnitTests/Verify/VerifyAdapterParsingTests.cs
autonomous: true
must_haves:
  truths:
    - "Unsafe archive contents are rejected before extraction"
    - "XML parsing paths use consistent hardened settings"
    - "Malformed or adversarial input fails predictably with actionable diagnostics"
  artifacts:
    - path: "src/STIGForge.Content/Import/ContentPackImporter.cs"
      provides: "Validated archive extraction for content import"
    - path: "src/STIGForge.Content/Import/OvalParser.cs"
      provides: "Secure XML parse path aligned with hardened parser settings"
    - path: "src/STIGForge.Verify/Adapters/CklAdapter.cs"
      provides: "Verify adapter secure XML loading behavior"
  key_links:
    - from: "src/STIGForge.Content/Import/ContentPackImporter.cs"
      to: "raw extraction root"
      via: "canonical path validation before file write"
      pattern: "GetFullPath|StartsWith|Extract"
    - from: "src/STIGForge.Verify/Adapters/*Adapter.cs"
      to: "XML input files"
      via: "XmlReaderSettings with secure resolver settings"
      pattern: "XmlReaderSettings|DtdProcessing|XmlResolver"
---

<objective>
Harden file and parser boundaries so import/verify flows reject unsafe input deterministically.

Purpose: Eliminate input-path and parser weaknesses in existing workflows without expanding scope.
Output: Safe archive extraction and unified secure XML parsing across import and verification adapters.
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
@.planning/phases/03-verification-integration/03-verification-integration-01-SUMMARY.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Enforce safe archive extraction for import pipelines</name>
  <files>src/STIGForge.Content/Import/ContentPackImporter.cs, src/STIGForge.Content/Import/ScapBundleParser.cs, tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs</files>
  <action>Replace permissive ZIP extraction with validated entry-by-entry extraction that blocks path traversal and rejects writes outside target roots. Add deterministic bounds checks (e.g., entry count and extracted size policy) to protect offline operator hosts from archive abuse. Keep existing import semantics for valid bundles.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~ContentPackImporterTests"</verify>
  <done>Unsafe archives are rejected with clear error messages, and valid bundle imports continue to work.</done>
</task>

<task type="auto">
  <name>Task 2: Standardize secure XML loading in import and verify adapters</name>
  <files>src/STIGForge.Content/Import/OvalParser.cs, src/STIGForge.Verify/Adapters/CklAdapter.cs, src/STIGForge.Verify/Adapters/EvaluateStigAdapter.cs, src/STIGForge.Verify/Adapters/ScapResultAdapter.cs, tests/STIGForge.UnitTests/Verify/VerifyAdapterParsingTests.cs</files>
  <action>Align all XML parsing entry points to hardened reader settings and resolver behavior already used in secured parsers. Preserve adapter output contracts while ensuring malformed XML and unsafe constructs fail consistently. Include regression assertions for malformed XML handling and secure parsing behavior.</action>
  <verify>dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~VerifyAdapterParsingTests"</verify>
  <done>All targeted parsers use secure XML loading behavior with stable outputs for valid inputs and deterministic failures for unsafe inputs.</done>
</task>

</tasks>

<verification>
- Run targeted import and verify adapter tests.
- Run one representative content import and verify flow using known-good fixtures.
</verification>

<success_criteria>
- Input extraction and XML parse boundaries are hardened in all targeted paths.
- Hardening changes preserve existing valid workflow behavior.
</success_criteria>

<output>
After completion, create `.planning/phases/06-security-and-operational-hardening/06-security-and-operational-hardening-02-SUMMARY.md`
</output>
