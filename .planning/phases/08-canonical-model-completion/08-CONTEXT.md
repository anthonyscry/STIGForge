# Phase 8: Canonical Model Completion - Context

**Gathered:** 2026-02-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Close 4 orphaned Phase 1 requirements (ING-01, ING-02, CORE-01, CORE-02) by adding missing fields to ContentPack and ControlRecord, and promoting verification/evidence/export schemas to Core.Models as canonical contracts. This is a model-layer gap closure phase — no new features, no new UI, no new CLI commands.

</domain>

<decisions>
## Implementation Decisions

### ContentPack field additions
- Add `BenchmarkIds` as `IReadOnlyList<string>` — matches existing collection patterns (Profile.OverlayIds uses `IReadOnlyList<string>`)
- Add `ApplicabilityTags` as `IReadOnlyList<string>` — flat string tags for downstream filtering (e.g., "Win10", "Server2022", "MemberServer"), not typed enums, since tags come from external STIG metadata and should not be constrained to the OsTarget/RoleTemplate enum values
- Both fields default to `Array.Empty<string>()` — consistent with existing nullable-safe patterns throughout Core.Models
- Add `Version` and `Release` string fields for benchmark version tracking (currently only in RevisionInfo on ControlRecord, but ContentPack needs pack-level version/release for ING-02)

### ControlRecord provenance
- Add `SourcePackId` as simple `string` — just a foreign key reference back to ContentPack.PackId
- Default to `string.Empty` — consistent with existing string field patterns in ControlRecord
- No cascading behavior on pack updates — ControlRecords are immutable snapshots from import time
- This field satisfies CORE-01's "provenance and external ID mapping" requirement alongside existing ExternalIds

### Canonical schema promotion to Core.Models
- `VerificationResult` — create a new canonical type in Core.Models that mirrors the essential fields of `NormalizedVerifyResult` from STIGForge.Verify. Do NOT move or delete the existing Verify-module type; the Core version is the contract, the Verify version is the implementation detail
- `EvidenceRecord` — create a new canonical type in Core.Models based on the shape of `EvidenceMetadata` from STIGForge.Evidence. Same principle: Core defines the contract, Evidence module keeps its implementation type
- `ExportIndexEntry` — new type in Core.Models for export package index entries (file path, artifact type, SHA-256 hash, timestamp). No existing type to mirror — this is a new schema
- All new Core.Models types get `SchemaVersion = CanonicalContract.Version` where applicable
- Add these type names to the `CanonicalContract` class as constants for schema versioning documentation

### Import infrastructure documentation
- Phase 1 VERIFICATION.md must document import infrastructure as "claimed" by mapping existing ingestion code (ImportQueuePlanner, ContentPackImporter paths) to ING-01/ING-02 requirements
- Verification should reference concrete test files that exercise import paths, not just claim coverage narratively
- This is documentation/verification only — no new import code in this phase

### Claude's Discretion
- Exact field ordering within each model class
- XML doc comment depth on new fields
- Whether CanonicalContract version bumps to "1.1.0" or stays at "1.0.0" (lean toward bump since schema is changing)
- Test file organization for new model types

</decisions>

<specifics>
## Specific Ideas

- Existing codebase patterns to follow: sealed classes, `IReadOnlyList<T>` / `IReadOnlyCollection<T>` for collections, `Array.Empty<T>()` for defaults, properties with `{ get; set; }` auto-accessors
- `NormalizedVerifyResult` in Verify module has rich fields (EvidencePaths, Metadata dict, RawArtifactPath) — the Core.Models `VerificationResult` should be leaner, capturing only the contract-level fields needed for cross-module communication
- `EvidenceIndexEntry` already exists in STIGForge.Evidence — the Core.Models `EvidenceRecord` should represent the canonical evidence contract (ControlId, Type, Sha256, Timestamp, RunId) not the index-specific shape

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 08-canonical-model-completion*
*Context gathered: 2026-02-22*
