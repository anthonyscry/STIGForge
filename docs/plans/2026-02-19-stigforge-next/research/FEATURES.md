# Feature Research - STIGForge Next

## Table Stakes (v1 expected)

- Deterministic content import for STIG/SCAP/GPO/LGPO/ADMX
- Profile and overlay policy model
- Build -> Apply -> Verify orchestration
- Manual control wizard with reusable answer files
- Evidence collection and indexing
- CKL + POA&M + eMASS package export
- Full audit trail + integrity hash verification

## Differentiators (v1+)

- Strict per-STIG SCAP association contract with explicit reasoning
- Quarterly diff/rebase assistant with confidence scoring
- Offline-first mission autopilot bundle flow
- Deterministic export contracts with machine-verifiable indices

## Anti-Features (explicitly avoid)

- Silent broad fallback matching across unrelated STIGs
- "Best effort" export packaging without integrity checks
- Internet-dependent critical workflows
- Hidden policy overrides without audit metadata

## Dependency Notes

- Strict mapping requires canonical benchmark IDs and normalized applicability tags.
- Deterministic export requires schema contracts before implementation spread.
- Manual/evidence workflows depend on stable `ControlRecord` IDs and provenance fields.
