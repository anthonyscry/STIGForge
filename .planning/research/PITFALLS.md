# Pitfalls Research

**Domain:** Windows compliance workflow tooling (merging Evaluate-STIG + SCC into one checklist with strict mapping)
**Researched:** 2026-02-20
**Confidence:** MEDIUM

## Critical Pitfalls

### Pitfall 1: Cross-STIG Broad Fallback Masquerading as "Better Coverage"

**What goes wrong:**
Results from SCC/Evaluate-STIG are matched to controls outside the selected STIG boundary, so pass/fail status looks complete but is attached to the wrong control set.

**Why it happens:**
Teams optimize for fewer "unmapped" rows and implement loose fallback matching across benchmark families.

**How to avoid:**
Implement mapping contract as code and tests: per-selected-STIG scope only, benchmark overlap first, deterministic tie-breakers second, strict feature/OS fallback third, otherwise `review_required`.

**Warning signs:**
"Unmapped" drops suddenly after parser changes, controls verify without benchmark overlap, repeated operator remapping of the same rule classes.

**Phase to address:**
Phase 4: Verification and Strict SCAP Association.

---

### Pitfall 2: Non-Deterministic Checklist Assembly

**What goes wrong:**
The merged checklist changes between identical runs (iSTIG order, UUID values, rule ordering, or manifest hashes), breaking audit reproducibility.

**Why it happens:**
Input order is not canonicalized, random UUIDs leak into artifacts, and serialization is not normalized.

**How to avoid:**
Sort all scanner/control inputs before merge, replace random IDs in exported artifacts with deterministic IDs derived from canonical fields, and gate releases with same-input/same-output regression fixtures.

**Warning signs:**
Checksum drift on rerun, noisy diffs with no content changes, CI intermittently failing deterministic package tests.

**Phase to address:**
Phase 3: Deterministic Build and Apply Core (implementation), reinforced in Phase 6 export gates.

---

### Pitfall 3: Rule Identity Collisions Across Tool Outputs

**What goes wrong:**
Two tools emit similarly named identifiers (`V-`, `Rule_ID`, benchmark-local IDs), and merge logic treats them as globally unique, causing overwritten or conflated findings.

**Why it happens:**
Identity keys are modeled as single fields instead of composite keys that include benchmark/STIG lineage.

**How to avoid:**
Adopt canonical verification key: `{selected_stig, benchmark_id, vuln_or_rule_id, scanner_source}`; reject writes that omit one of these fields; persist raw source identifiers alongside normalized IDs.

**Warning signs:**
Unexpected duplicate-key upserts, one scanner "disappearing" after merge, inconsistent findings count between raw files and canonical model.

**Phase to address:**
Phase 1: Canonical Ingestion Contracts (schema), validated again in Phase 4 parser contracts.

---

### Pitfall 4: Parser Coupling to One SCC/Evaluate-STIG Flavor

**What goes wrong:**
A quarterly tool/content update changes output shape, and normalization silently degrades or hard-fails.

**Why it happens:**
Parsers are written for one happy-path format and do not preserve raw artifacts for replay.

**How to avoid:**
Split extraction from normalization, maintain fixture corpus by tool version/release, archive raw scanner outputs with parser diagnostics, and require contract tests before accepting new scanner versions.

**Warning signs:**
Spike in `unknown`/`unmapped`, parser exceptions clustered after scanner updates, emergency parser hotfixes late in release.

**Phase to address:**
Phase 4: Verification and Strict SCAP Association.

---

### Pitfall 5: False "One Checklist" UX That Hides Provenance

**What goes wrong:**
Merged checklist shows a single status per control without exposing which scanner produced it, making disputes non-resolvable and reducing trust.

**Why it happens:**
UI/exports compress multi-source evidence too early, prioritizing simplicity over audit traceability.

**How to avoid:**
Keep canonical finding provenance (`source tool`, `raw artifact path`, `parser version`, `timestamp policy`) visible in record detail and export index; summarize in UI but never discard source links.

**Warning signs:**
Reviewers ask "where did this result come from?" and system cannot answer in one click; POA&M entries cannot be traced back to raw scan evidence.

**Phase to address:**
Phase 5: Human Resolution and Update Rebase (UX/policy), Phase 6 for export evidence completeness.

---

### Pitfall 6: Ambiguity Treated as Success Instead of Work Queue

**What goes wrong:**
When SCC and Evaluate-STIG disagree or mapping confidence is low, system auto-picks a winner and marks control resolved.

**Why it happens:**
Pressure to reduce manual queue size and ship "green" dashboards.

**How to avoid:**
Make ambiguity first-class: unresolved state, explicit reason codes, reviewer assignment, and policy gate that blocks auto-apply/auto-close for ambiguous mappings.

**Warning signs:**
Near-zero review queue despite high scanner disagreement rates, sudden drops in manual workload after mapping changes.

**Phase to address:**
Phase 2: Policy Scope and Safety Gates (decision policy), enforced in Phase 4 verification orchestration.

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| "Match by Vuln ID only" | Fast integration | Cross-benchmark false joins | Never |
| Keep random checklist UUIDs | Easy CKL generation | Non-reproducible exports | Never |
| Auto-close ambiguous mappings | Smaller review queue | Audit credibility loss | Never |
| Normalize without saving raw scanner files | Lower storage | Impossible forensic replay | Never |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| SCC output ingest | Assume one stable result schema forever | Version parser contracts and fixture sets by SCC release |
| Evaluate-STIG ingest | Treat script output as canonical without source metadata | Persist raw output + parser diagnostics + tool version |
| CKL export | Merge results but drop scanner provenance | Keep one merged control view plus per-source trace links |

## "Looks Done But Isn't" Checklist

- [ ] **Merged checklist:** Identical inputs produce byte-stable output and checksums.
- [ ] **Strict mapping:** No cross-STIG fallback path exists in code or data migration scripts.
- [ ] **Parser resilience:** Latest SCC/Evaluate-STIG outputs are in fixture corpus and pass.
- [ ] **Auditability:** Every merged finding links to raw artifact and parser metadata.
- [ ] **Ambiguity handling:** Conflicts route to review-required, not silent resolution.

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Cross-STIG broad fallback | Phase 4 | Invariant tests proving no mapping outside selected STIG and benchmark-overlap precedence |
| Non-deterministic checklist assembly | Phase 3 (core), Phase 6 (export) | Rerun fixtures produce identical checklist + manifest hashes |
| Rule identity collisions | Phase 1 | Schema validation requires composite identity fields; duplicate join tests |
| Parser coupling to one format | Phase 4 | Multi-version SCC/Evaluate fixture suite in CI |
| Provenance hidden in merged view | Phase 5 and Phase 6 | Export index contains source tool, raw path, parser version for each finding |
| Ambiguity auto-resolved | Phase 2 and Phase 4 | Policy tests enforce `review_required` on low confidence or scanner disagreement |

## Sources

- `PROJECT_SPEC.md` (strict mapping contract, deterministic outputs, verify pipeline requirements).
- `.planning/ROADMAP.md` (phase boundaries and where mapping/determinism gates belong).
- `https://raw.githubusercontent.com/microsoft/PowerStig/dev/source/Module/STIG/Functions.Checklist.ps1` (multi-STIG checklist generation behavior and deterministic-risk patterns like unsorted inputs/random UUID usage).
- `https://raw.githubusercontent.com/microsoft/PowerStig/dev/source/StigData/Schema/Checklist.xsd` (CKL structure constraints).
- `https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/xccdf` (XCCDF benchmark/rule/result model context; updated 2025-12-22).
- `https://csrc.nist.gov/projects/security-content-automation-protocol/specifications/arf` (ARF transport/report model context; updated 2025-12-22).

---
*Pitfalls research for: STIGForge Next verify/mapping dimension*
*Researched: 2026-02-20*
