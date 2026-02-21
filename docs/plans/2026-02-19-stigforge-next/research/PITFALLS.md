# Pitfalls Research - STIGForge Next

## Critical Pitfalls and Mitigations

### 1) Ingestion drift between artifact types

- **Warning signs:** benchmark IDs missing, mixed metadata quality, duplicate content keys.
- **Prevention:** classifier confidence + canonical metadata normalization + reject/diagnose path.
- **Phase:** M1

### 2) Broad SCAP fallback causing false associations

- **Warning signs:** one SCAP benchmark repeatedly paired with unrelated STIG rows.
- **Prevention:** strict per-STIG fallback rules + benchmark-first mapping + review-required on ambiguity.
- **Phase:** M2

### 3) Non-deterministic package/index output

- **Warning signs:** same input yields different file ordering/hash outputs.
- **Prevention:** canonical sort/version policy + deterministic manifest tests + fixed timestamp policy option.
- **Phase:** M2-M4

### 4) Offline assumptions broken by hidden dependencies

- **Warning signs:** runtime reaches internet for modules/tool metadata.
- **Prevention:** explicit offline preflight checks and sealed bundle dependencies.
- **Phase:** M2

### 5) Rebase automation carries wrong decisions

- **Warning signs:** high auto-carry rates with low traceability.
- **Prevention:** confidence-scored rebase + explicit review queue + no silent policy mutation.
- **Phase:** M5
