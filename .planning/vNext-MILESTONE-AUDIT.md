---
milestone: vNext
audited: 2026-02-22
status: gaps_found
scores:
  requirements: 12/21
  phases: 4/5
  integration: 21/21
  flows: 6/7
---

# Milestone vNext (STIGForge Next) — Audit Report

**Audited:** 2026-02-22
**Status:** ⚠️ gaps_found
**Phases:** 1-5 (22 plans)

---

## Summary

| Category | Score | Status |
|----------|-------|--------|
| Requirements Coverage | 12/21 | 57% - 9 orphaned/partial |
| Phase Verification | 4/5 | Phase 3 unverified |
| Cross-Phase Integration | 21/21 | All wired |
| E2E Flows | 6/7 | Complete |

---

## Critical Gaps (Blockers)

### 1. Phase 3 Unverified

**Phase:** 03-deterministic-mission-execution-core
**Issue:** Missing VERIFICATION.md file
**Impact:** Cannot confirm BLD-01, APL-01, APL-02, VER-01, MAP-01 requirements were satisfied

This phase has 5 plans (03-01 through 03-05) but no verification was performed. According to workflow rules, unverified phases are blockers.

### 2. Phase 1 Requirement ID Misalignment

**Phase:** 01-mission-orchestration-and-apply-evidence
**Status:** gaps_found (2/4 success criteria)

| Requirement | Expected | Status | Issue |
|-------------|----------|--------|-------|
| ING-01 | Phase 1 | ORPHANED | Not claimed by any plan |
| ING-02 | Phase 1 | ORPHANED | ContentPack missing BenchmarkIds, ApplicabilityTags |
| CORE-01 | Phase 1 | ORPHANED | ControlRecord missing provenance field |
| CORE-02 | Phase 1 | ORPHANED | VerificationResult, EvidenceRecord, ExportIndexEntry absent from Core.Models |

Plans instead claim phantom IDs (FLOW-01/02/03, IMPT-01, APLY-01/02) that don't exist in REQUIREMENTS.md.

---

## Requirements Coverage

### By Status

| Status | Count | Requirements |
|--------|-------|--------------|
| ✅ Satisfied | 12 | POL-01, POL-02, SCOPE-01, SCOPE-02, SAFE-01, MAN-01, EVD-01, REB-01, REB-02, EXP-01, FLT-01, AUD-01 |
| ⚠️ Partial | 0 | — |
| ❌ Orphaned | 4 | ING-01, ING-02, CORE-01, CORE-02 |
| ❓ Unverified | 5 | BLD-01, APL-01, APL-02, VER-01, MAP-01 |

### Detailed Requirements Table

| Requirement | Phase | VERIFICATION.md | SUMMARY Frontmatter | REQUIREMENTS.md | Final Status |
|-------------|-------|-----------------|---------------------|-----------------|--------------|
| ING-01 | 1 | gaps_found | FLOW-* (phantom) | [ ] Pending | **ORPHANED** |
| ING-02 | 1 | gaps_found | FLOW-* (phantom) | [ ] Pending | **ORPHANED** |
| CORE-01 | 1 | gaps_found | FLOW-* (phantom) | [ ] Pending | **ORPHANED** |
| CORE-02 | 1 | gaps_found | FLOW-* (phantom) | [ ] Pending | **ORPHANED** |
| POL-01 | 2 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| POL-02 | 2 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| SCOPE-01 | 2 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| SCOPE-02 | 2 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| SAFE-01 | 2 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| BLD-01 | 3 | **MISSING** | BLD-01 mentioned | [ ] Pending | **UNVERIFIED** |
| APL-01 | 3 | **MISSING** | — | [ ] Pending | **UNVERIFIED** |
| APL-02 | 3 | **MISSING** | — | [ ] Pending | **UNVERIFIED** |
| VER-01 | 3 | **MISSING** | — | [ ] Pending | **UNVERIFIED** |
| MAP-01 | 3 | **MISSING** | — | [ ] Pending | **UNVERIFIED** |
| MAN-01 | 4 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| EVD-01 | 4 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| REB-01 | 4 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| REB-02 | 4 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| EXP-01 | 5 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| FLT-01 | 5 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |
| AUD-01 | 5 | passed | — | [ ] Pending | **SATISFIED** (update checkbox) |

---

## Phase Verification Summary

| Phase | Status | Plans | Gaps |
|-------|--------|-------|------|
| 1. Canonical Ingestion | gaps_found | 4/4 | ING-02 missing fields, CORE-01 missing provenance, CORE-02 missing models |
| 2. Policy Scope & Safety | passed | 5/5 | None |
| 3. Deterministic Mission Execution | **UNVERIFIED** | 5/5 | Missing VERIFICATION.md |
| 4. Human Resolution & Evidence | passed | 4/4 | None |
| 5. Proof Packaging & Fleet | passed | 4/4 | None |

---

## Cross-Phase Integration

### Status: HEALTHY

| Metric | Result |
|--------|--------|
| Cross-phase integrations | 42 verified |
| E2E flows complete | 6/7 |
| Orphaned exports | 0 |
| Broken flows | 0 |

### Complete E2E Flows

1. ✅ Import → Profile → Build → Apply → Verify → Export
2. ✅ Manual → Evidence → Export
3. ✅ Pack Diff → Rebase → Manual
4. ✅ Mission Timeline → Dashboard
5. ✅ Fleet Collection → Summary → Export
6. ✅ Audit Trail → Integrity Verification
7. ✅ Classification Scope → Review Queue → Manual Resolution

### DI Registration Notes

Phase 4 services (BaselineDiffService, OverlayRebaseService, ManualAnswerService) use different patterns:
- CLI: Registered in DI
- WPF: Direct instantiation in ViewModels

This is a conscious architectural choice (stateless services requiring runtime parameters), not a defect.

---

## Tech Debt

### Phase 1
- ContentPack missing `BenchmarkIds: IReadOnlyList<string>`
- ContentPack missing `ApplicabilityTags`
- ControlRecord missing provenance field (`SourcePackId`)
- VerificationResult, EvidenceRecord, ExportIndexEntry not in Core.Models
- BundleManifest in STIGForge.Build instead of Core.Models

### Phase 4
- Service registration pattern inconsistency between CLI and WPF (low priority)

### General
- Pre-existing flaky test: `BuildHost_UsesConfiguredPathBuilderForSerilogLogDirectory`

---

## Gap Objects

```yaml
gaps:
  requirements:
    - id: "ING-01"
      status: "orphaned"
      phase: "1"
      claimed_by_plans: []
      completed_by_plans: []
      verification_status: "gaps_found"
      evidence: "No plan in Phase 1 claims ING-01. Infrastructure exists but untracked."
    - id: "ING-02"
      status: "orphaned"
      phase: "1"
      claimed_by_plans: []
      completed_by_plans: []
      verification_status: "gaps_found"
      evidence: "ContentPack missing BenchmarkIds list and ApplicabilityTags fields"
    - id: "CORE-01"
      status: "orphaned"
      phase: "1"
      claimed_by_plans: []
      completed_by_plans: []
      verification_status: "gaps_found"
      evidence: "ControlRecord has no provenance field (SourcePackId)"
    - id: "CORE-02"
      status: "orphaned"
      phase: "1"
      claimed_by_plans: []
      completed_by_plans: []
      verification_status: "gaps_found"
      evidence: "VerificationResult, EvidenceRecord, ExportIndexEntry absent from Core.Models"
    - id: "BLD-01"
      status: "unverified"
      phase: "3"
      claimed_by_plans: ["03-01"]
      completed_by_plans: ["03-01"]
      verification_status: "missing"
      evidence: "Phase 3 has no VERIFICATION.md file"
    - id: "APL-01"
      status: "unverified"
      phase: "3"
      claimed_by_plans: ["03-02"]
      completed_by_plans: ["03-02"]
      verification_status: "missing"
      evidence: "Phase 3 has no VERIFICATION.md file"
    - id: "APL-02"
      status: "unverified"
      phase: "3"
      claimed_by_plans: ["03-04"]
      completed_by_plans: ["03-04"]
      verification_status: "missing"
      evidence: "Phase 3 has no VERIFICATION.md file"
    - id: "VER-01"
      status: "unverified"
      phase: "3"
      claimed_by_plans: ["03-05"]
      completed_by_plans: ["03-05"]
      verification_status: "missing"
      evidence: "Phase 3 has no VERIFICATION.md file"
    - id: "MAP-01"
      status: "unverified"
      phase: "3"
      claimed_by_plans: ["03-03"]
      completed_by_plans: ["03-03"]
      verification_status: "missing"
      evidence: "Phase 3 has no VERIFICATION.md file"
```

---

## Recommendations

1. **Create Phase 3 VERIFICATION.md** - Run verification for Phase 3 to confirm BLD-01, APL-01, APL-02, VER-01, MAP-01

2. **Resolve Phase 1 Orphaned Requirements** - Either:
   - Add gap-closure plans to satisfy ING-02, CORE-01, CORE-02 gaps, OR
   - Update REQUIREMENTS.md to include FLOW-*/IMPT-*/APLY-* IDs that were actually built

3. **Update REQUIREMENTS.md Checkboxes** - Mark 12 satisfied requirements as `[x]`:
   - POL-01, POL-02, SCOPE-01, SCOPE-02, SAFE-01
   - MAN-01, EVD-01, REB-01, REB-02
   - EXP-01, FLT-01, AUD-01

---

_Audited: 2026-02-22_
_Auditor: Claude (gsd-milestone-audit)_
