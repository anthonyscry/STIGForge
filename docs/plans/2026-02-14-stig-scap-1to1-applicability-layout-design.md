# STIG-SCAP 1:1 Mapping, Applicability Hardening, and Expanded Layout Cohesion Design

## Goal

Fix three user-visible correctness problems with minimal disruption to existing workflows:

1. Enforce exactly one canonical SCAP mapping per STIG in the machine applicability view.
2. Remove applicability false positives by requiring stronger host evidence and introducing an explicit `Unknown` state.
3. Keep the Import tab layout usable when machine scan details are expanded.

## Current Behavior and Gaps

- A single STIG can show multiple SCAP rows in `Applicable Imported Packs`, which conflicts with intended one-to-one mapping.
- Applicability decisions rely too heavily on broad keyword matching, allowing false positives for unrelated products.
- Expanded machine scan details can visually compress the library region to an unusable size.

## Design Decisions

### 1) Canonical STIG->SCAP Mapping

- Build one canonical SCAP choice per STIG at selection time.
- Keep conflicts and tie-break rationale in diagnostics only, not in the primary table.
- Deterministic tie-break order:
  1. Benchmark/version alignment with STIG benchmark IDs and parsed DISA version tag.
  2. NIWC Enhanced + Consolidated Bundle preference when version matches but hashes differ.
  3. Most recent release/import signal.
  4. Stable lexical fallback for run-to-run determinism.

### 2) Applicability as Tri-State with Evidence

- Move from implicit boolean-only behavior to a richer decision model:
  - `Applicable` (high-confidence evidence)
  - `NotApplicable`
  - `Unknown` (insufficient confidence)
- Keep boolean compatibility by mapping `Applicable` to `true`; all other states map to `false` for existing callers.
- Require explicit product evidence for product-specific packs (for example, FortiGate and Symantec Endpoint):
  - service presence (name/display-name),
  - install registry evidence,
  - known file/path evidence.
- If no strong positive evidence exists, return `Unknown` instead of `Applicable`.

### 3) Debug Drilldown for Explainability

- Reuse machine diagnostics view to show compact per-pack reasoning:
  - state,
  - confidence tier,
  - key evidence lines,
  - canonical mapping conflict notes.
- Default table remains concise and workflow-first.

### 4) Expanded Layout Cohesion

- Enforce minimum usable size for the STIG Library and related lists in `ImportView`.
- Keep machine scan details internally scrollable when expanded.
- Use simple grid min sizing (and optional splitter only if needed) instead of broad layout refactors.

## File-Level Plan (Design)

- `src/STIGForge.Core/Services/PackApplicabilityRules.cs`
  - add tri-state applicability decision API and evidence collection.
  - retain existing `IsApplicable` signature as compatibility shim.
- `src/STIGForge.App/MainViewModel.Import.cs`
  - select exactly one SCAP candidate per STIG.
  - route ambiguity diagnostics to `MachineSelectionDiagnostics`.
  - keep unknown applicability packs out of primary `ApplicablePackPairs` table.
- `src/STIGForge.App/MainViewModel.cs`
  - extend `ApplicablePackPair` only as needed for stable display fields.
- `src/STIGForge.App/Views/ImportView.xaml`
  - add/adjust min sizes and scroll behavior to prevent collapse in expanded state.
- `tests/STIGForge.UnitTests/Services/PackApplicabilityRulesTests.cs`
  - add regression tests for FortiGate and Symantec false-positive prevention and Unknown behavior.
- `tests/STIGForge.UnitTests/Content/ImportDedupServiceTests.cs` (or new mapping-focused test file)
  - add deterministic canonical SCAP selection tests with NIWC and stable fallback conditions.

## Behavioral Guarantees

- No STIG row displays multiple SCAP entries in the default applicable table.
- FortiGate and Symantec Endpoint are never marked applicable without strong product evidence.
- Unknown applicability outcomes are visible in diagnostics and excluded from default applicable rows.
- Canonical SCAP selection remains stable across repeated runs with unchanged inputs.
- Expanded machine scan panel does not collapse STIG library into unusable dimensions.

## Non-Goals

- No storage schema migration for persisted canonical mappings in this pass.
- No broad redesign of Import tab information architecture.
- No changes to unrelated profile/build/orchestration workflows.

## Risk Controls

- Keep existing APIs operational while adding richer decision APIs.
- Add regression tests for known false-positive cases and deterministic tie-break behavior.
- Localize UI changes to `ImportView` sizing/scroll behavior only.
