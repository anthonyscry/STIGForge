# Proposal: Dashboard Tabs, CAT Severity Totals, and Harden Backend Expansion

## Purpose

Improve operator workflow clarity and compliance visibility by restructuring the dashboard into focused tabs, surfacing CAT I/II/III vulnerability totals, and ensuring the Harden step passes PowerSTIG, DSC, and local GPO/LGPO inputs to the apply pipeline.

## Scope

- UI: split dashboard into tabs: Import Library, Workflow, Results, Compliance Summary.
- UI: change workflow action cards to Scan, Harden, Verify (Import remains available in Import Library tab).
- Metrics: add CAT I, CAT II, CAT III, and total vulnerability counts in Compliance Summary.
- Pipeline: extend verification summary outputs with CAT counts.
- Pipeline: expand harden request construction to include discovered PowerSTIG module/data, DSC path, and local LGPO policy path/scope.
- Tests: update/add unit and contract tests for tabs, CAT metrics, and harden request shaping.

## Acceptance Criteria

1. Dashboard shows the four required top tabs in order.
2. Workflow tab contains Scan/Harden/Verify cards only.
3. Compliance Summary shows total vulnerabilities and CAT I/II/III counts.
4. Verification workflow result includes CAT count fields populated from fail/open findings by severity.
5. Harden step sends discovered PowerSTIG/DSC/LGPO inputs through `ApplyRequest` when present.
6. Unit tests compile with updated contracts and CAT/harden assertions.

## Risks

- Severity mapping mismatch (e.g., non-standard severity labels) could undercount CAT totals.
- Harden artifact discovery may miss custom directory layouts.
- UI tab migration may affect operator discoverability for existing keyboard-driven flows.
