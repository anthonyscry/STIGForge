# Architecture Research - STIGForge Next

## Recommended Component Shape

1. **Ingestion boundary** (`Content` + `Core` contracts)
2. **Policy boundary** (`Core` profile/overlay/rules)
3. **Execution boundary** (`Build`, `Apply`, `Verify`)
4. **Human loop boundary** (`App` manual wizard + `Evidence`)
5. **Packaging boundary** (`Export`, `Reporting`, `Infrastructure`)

## Data Flow Pattern

`import -> normalize -> build -> apply -> verify -> manual -> export`

Every transition writes explicit artifacts with stable IDs and provenance metadata.

## Build Order Implications

- Build contracts first (schemas + model invariants).
- Implement ingestion + normalization before apply/verify orchestration.
- Implement strict mapping before export/reporting to avoid propagating bad links.
- Implement release gates before mission autopilot/fleet expansion.
