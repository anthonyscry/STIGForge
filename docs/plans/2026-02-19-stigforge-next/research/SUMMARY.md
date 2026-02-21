# Research Summary - STIGForge Next

## Ecosystem Assumptions

- Windows-first operations with mixed GUI and CLI usage are expected.
- Offline/air-gapped execution is a hard requirement, not an optimization.
- Deterministic artifacts are required for audit acceptance, not optional quality work.

## High-Risk Areas

1. Content ingestion quality and provenance consistency.
2. Strict STIG-to-SCAP association correctness.
3. Deterministic output contracts and hash stability.
4. Offline execution reliability and dependency sealing.

## Recommended Priorities

- Prioritize contracts/schemas and invariants before broad feature delivery.
- Enforce strict mapping and deterministic behaviors in milestone acceptance gates.
- Treat ambiguity as review-required across mapping and policy workflows.

## Carry-Forward Guidance

- Preserve only proven contracts from `PROJECT_SPEC.md`.
- Do not preserve legacy implementation behavior unless validated by contract tests.
- Make each milestone produce testable artifacts and a hard gate.
