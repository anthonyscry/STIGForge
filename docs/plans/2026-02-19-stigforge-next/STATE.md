# STATE - STIGForge Next

## Project Reference

See: `docs/plans/2026-02-19-stigforge-next/PROJECT.md`

**Core value:** Deterministic, defensible compliance outcomes with strict mapping and offline-first execution.
**Current focus:** Milestone M1 - Foundations and Canonical Contracts.

## Canonical Inputs

- `PROJECT_SPEC.md` (canonical reboot source)
- `docs/plans/2026-02-19-stigforge-next/REQUIREMENTS.md`
- `docs/plans/2026-02-19-stigforge-next/ROADMAP.md`

## Immediate Next Action

- Run `/gsd-plan-phase 1` to produce executable plan for M1.

## Guardrails

- Do not preserve legacy implementation behavior without explicit contract validation.
- Enforce strict per-STIG SCAP mapping invariant at design, implementation, and test layers.
- Enforce deterministic output contract at bundle and export layers.
- Keep offline-first as a hard gate, not a best-effort objective.
