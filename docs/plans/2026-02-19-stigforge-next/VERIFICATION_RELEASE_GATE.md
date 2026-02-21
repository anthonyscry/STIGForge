# Verification and Release-Gate Plan

## 1. Test-First Strategy

All milestones use red-green-refactor discipline:

1. Add failing test/contract fixture first.
2. Implement minimal change.
3. Re-run targeted and regression suites.
4. Record gate evidence in artifacts.

## 2. Test Layers

## 2.1 Unit

- Parsing and classifier confidence behavior
- Applicability and policy logic
- Strict mapping logic (`MAP-01` invariant)
- Deterministic sorting/hash helpers

## 2.2 Integration

- import -> normalize -> build path on fixture packs
- apply/verify wrapper orchestration with controlled fixtures
- export package builder + index/hash generation

## 2.3 Contract

- JSON schema validation for all canonical models
- Bundle structure contract tests
- Export index contract tests
- XAML/UI contract tests for critical layout/style invariants

## 2.4 End-to-End

- Win11 classified profile run
- Server2019 classified role run
- Offline mission simulation (no network access)

## 3. Critical Invariant Test Set

## 3.1 Strict STIG-to-SCAP Mapping Tests

- Benchmark overlap should win over fallback tags.
- Feature-specific SCAP cannot map to OS-only unrelated STIG.
- Ambiguous candidates produce review-required status.
- Deterministic tie-break remains stable for repeated runs.

## 3.2 Deterministic Export/Package Tests

- Same inputs, same ordering/hash behavior according to manifest policy.
- Index ordering remains canonical.
- Tamper test invalidates hash verification.

## 4. Release Gate (Hard Pass/Fail)

## Gate Stages

1. **Build Gate**
   - all targeted build commands succeed
2. **Contract Gate**
   - schema + structure contract tests pass
3. **Invariant Gate**
   - strict mapping tests + deterministic output tests pass
4. **Regression Gate**
   - milestone regression suite passes
5. **Integrity Gate**
   - hash manifest generated + verification succeeds
6. **Audit Gate**
   - audit verify passes on clean fixture and fails on tampered fixture

## Required Evidence per Gate

- command line used
- timestamp
- exit code
- result summary
- artifact/log path

## Failure Policy

- Any hard gate failure blocks milestone completion.
- No milestone can claim complete until all hard gates are green.
