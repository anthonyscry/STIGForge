# Compatibility Fixture Matrix

This matrix defines deterministic fixture coverage for release compatibility checks.
Fixtures are immutable, scenario-named, and reviewed as part of quarterly content updates.

## Coverage Matrix

| Format | Scenario | Unit Fixture Inputs | Integration Fixture Inputs | Expected Compatibility Outcome |
| --- | --- | --- | --- | --- |
| STIG/XCCDF | Baseline | `compat-stig-baseline-xccdf.xml` | `compat-stig-baseline-xccdf.xml` | `detectedFormat=Stig`, `usedFallbackParser=false`, `parsedControls>0`, no parsing errors |
| STIG/XCCDF | Quarterly Delta | `compat-stig-quarterly-delta-xccdf.xml` | `compat-stig-quarterly-delta-xccdf.xml` | `detectedFormat=Stig`, deterministic keys, warnings/errors stable across runs |
| STIG/XCCDF | Malformed/Adversarial | `compat-stig-malformed-xccdf.xml` | `compat-stig-malformed-xccdf.xml` | `detectedFormat=Stig`, `parsingErrors>0`, mismatch surfaced in unsupported mappings |
| SCAP (XCCDF + OVAL) | Baseline | `compat-scap-baseline-xccdf.xml` + `compat-scap-baseline-oval.xml` | `compat-scap-baseline-xccdf.xml` + `compat-scap-baseline-oval.xml` | `detectedFormat=Scap`, `support.ovalMetadata=true`, no parsing errors |
| SCAP (XCCDF + OVAL) | Malformed/Adversarial OVAL | `compat-scap-baseline-xccdf.xml` + `compat-scap-malformed-oval.xml` | n/a (unit-only parser fault injection) | `detectedFormat=Scap`, OVAL parsing errors emitted with deterministic error type |
| GPO/ADMX | Baseline | `compat-gpo-baseline.admx` | `compat-gpo-baseline.admx` | `detectedFormat=Gpo`, `support.admx=true`, deterministic policy count |
| GPO/ADMX | Quarterly Delta | `compat-gpo-quarterly-delta.admx` | n/a (unit-only) | `detectedFormat=Gpo`, stable warnings/errors and structure |
| OVAL-only (adversarial format ambiguity) | Baseline | `compat-unknown-oval-only.xml` | n/a (unit-only) | `detectedFormat=Unknown`, `usedFallbackParser=true`, no crash |

## Fixture Contract

- Scenario fixtures live under:
  - `tests/STIGForge.UnitTests/fixtures`
  - `tests/STIGForge.IntegrationTests/fixtures`
- Source-of-truth contracts:
  - `tests/STIGForge.UnitTests/fixtures/compatibility-fixture-contract.json`
  - `tests/STIGForge.IntegrationTests/fixtures/compatibility-fixture-contract.json`
- Contract requirements:
  - Stable filenames (no date-stamped names)
  - Stable content unless intentional quarterly delta change
  - Matrix changes must update both this document and contract JSON
  - Compatibility contract tests must fail if required fixture metadata drifts

## Update Rules

1. Add new fixture scenario files using `compat-<format>-<scenario>-...` naming.
2. Update both fixture contract JSON files with expected format/outcome semantics.
3. Update this matrix table and keep scenario language synchronized.
4. Run content compatibility tests locally before pushing:
   - `dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~Content"`
   - `dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration Release --framework net8.0 --filter "FullyQualifiedName~RoundTrip"`
5. CI must enforce compatibility contract tests as release readiness gates.
