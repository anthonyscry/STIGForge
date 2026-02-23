# Stack Research - STIGForge Next

## Recommended Core Stack (2025/2026 baseline)

- **Runtime:** .NET 8 LTS
- **Desktop UI:** WPF on `net8.0-windows`
- **Automation Surface:** .NET CLI + PowerShell wrappers
- **Persistence:** SQLite for metadata; filesystem for bundles/evidence
- **Contracts:** JSON Schema draft 2020-12 for all external artifacts
- **Testing:** xUnit + FluentAssertions + fixture-driven integration tests
- **Hashing/Integrity:** SHA-256 for all package/artifact manifests
- **Packaging:** Deterministic directory and index generation with canonical sort rules

## Why This Stack

- Matches existing operator environment (Windows-first, PowerShell-aware).
- Supports offline execution with no external hosted dependencies.
- Enables strong schema-driven contracts for deterministic behavior.
- Keeps automation + UI parity through shared domain modules.

## Avoid

- Browser-only frontends requiring local web stacks for baseline operation.
- Non-deterministic artifact generation without canonical ordering rules.
- Hidden mapping heuristics without reason-code diagnostics.

## Confidence

- **Runtime/UI selection:** High
- **Storage and contract approach:** High
- **Fleet/remoting expansion details:** Medium (depends on enterprise constraints)
