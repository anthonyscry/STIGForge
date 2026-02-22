# Phase 8: Canonical Model Completion - Research

**Researched:** 2026-02-22
**Domain:** C#/.NET Core canonical data model design, schema versioning
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- ContentPack field additions:
  - Add `BenchmarkIds` as `IReadOnlyList<string>` — matches existing collection patterns (Profile.OverlayIds uses `IReadOnlyList<string>`)
  - Add `ApplicabilityTags` as `IReadOnlyList<string>` — flat string tags for downstream filtering (e.g., "Win10", "Server2022", "MemberServer"), not typed enums, since tags come from external STIG metadata and should not be constrained to the OsTarget/RoleTemplate enum values
  - Both fields default to `Array.Empty<string>()` — consistent with existing nullable-safe patterns throughout Core.Models
  - Add `Version` and `Release` string fields for benchmark version tracking (currently only in RevisionInfo on ControlRecord, but ContentPack needs pack-level version/release for ING-02)

- ControlRecord provenance:
  - Add `SourcePackId` as simple `string` — just a foreign key reference back to ContentPack.PackId
  - Default to `string.Empty` — consistent with existing string field patterns in ControlRecord
  - No cascading behavior on pack updates — ControlRecords are immutable snapshots from import time

- Canonical schema promotion to Core.Models:
  - `VerificationResult` — create a new canonical type in Core.Models that mirrors the essential fields of `NormalizedVerifyResult` from STIGForge.Verify. Do NOT move or delete the existing Verify-module type; the Core version is the contract, the Verify version is the implementation detail
  - `EvidenceRecord` — create a new canonical type in Core.Models based on the shape of `EvidenceMetadata` from STIGForge.Evidence. Same principle: Core defines the contract, Evidence module keeps its implementation type
  - `ExportIndexEntry` — new type in Core.Models for export package index entries (file path, artifact type, SHA-256 hash, timestamp). No existing type to mirror — this is a new schema
  - All new Core.Models types get `SchemaVersion = CanonicalContract.Version` where applicable
  - Add these type names to the `CanonicalContract` class as constants for schema versioning documentation

- Import infrastructure documentation:
  - Phase 1 VERIFICATION.md must document import infrastructure as "claimed" by mapping existing ingestion code (ImportQueuePlanner, ContentPackImporter paths) to ING-01/ING-02 requirements
  - Verification should reference concrete test files that exercise import paths, not just claim coverage narratively
  - This is documentation/verification only — no new import code in this phase

### Claude's Discretion
- Exact field ordering within each model class
- XML doc comment depth on new fields
- Whether CanonicalContract version bumps to "1.1.0" or stays at "1.0.0" (lean toward bump since schema is changing)
- Test file organization for new model types

### Deferred Ideas (OUT OF SCOPE)
None

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| ING-01 | Import compressed or raw STIG/SCAP/GPO/LGPO/ADMX sources with confidence-based classification and dedupe | Existing ContentPackImporter.cs already implements import logic with DetectionConfidence (High/Medium/Low) classification via DetectPackFormatWithConfidence(). ImportDedupService.cs provides deduplication. Research confirms infrastructure exists. |
| ING-02 | Persist pack metadata (`pack id/name`, benchmark IDs, release/version/date, source label, hash manifest, applicability tags) | Current ContentPack has PackId, Name, ReleaseDate, SourceLabel, HashAlgorithm, ManifestSha256. Missing BenchmarkIds and Version/Release fields identified. ApplicabilityTags to be added. |
| CORE-01 | Normalize all controls into canonical `ControlRecord` with provenance and external ID mapping | ControlRecord already has ExternalIds (VulnId, RuleId, SrgId, BenchmarkId). Missing SourcePackId for provenance tracking back to ContentPack. |
| CORE-02 | Version and publish schemas for `ContentPack`, `ControlRecord`, `Profile`, `Overlay`, `BundleManifest`, `VerificationResult`, `EvidenceRecord`, `ExportIndexEntry` | First 5 exist with SchemaVersion field. Last 3 need to be created as Core.Models types with SchemaVersion support. CanonicalContract.cs is the versioning authority. |

</phase_requirements>

## Summary

Phase 8 is a **model-layer gap closure phase** — no new features, no new UI, no new CLI commands. The research confirms that:

1. **Import infrastructure (ING-01, ING-02)** already exists and is battle-tested. ContentPackImporter supports STIG/SCAP/GPO/ADMX formats with confidence-based classification. The gap is purely **metadata fields on ContentPack** — missing BenchmarkIds, ApplicabilityTags, Version, Release.

2. **ControlRecord provenance (CORE-01)** has ExternalIds but is missing SourcePackId. This is a single-string field addition to establish foreign-key provenance back to ContentPack.

3. **Canonical schema promotion (CORE-02)** requires creating 3 new types in Core.Models that mirror existing module types without breaking those modules. The existing types (NormalizedVerifyResult, EvidenceMetadata) continue as implementation details.

4. **Import verification documentation** needs to map existing test files (ContentPackImporterTests.cs) to ING-01/ING-02 requirements, showing concrete test coverage.

**Primary recommendation:** Follow existing patterns in Core.Models (sealed classes, IReadOnlyList<T>, Array.Empty<T>() defaults, SchemaVersion = CanonicalContract.Version). Bump CanonicalContract.Version to "1.1.0" since new fields are being added.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET | 8.0 / net48 | Runtime target | Project targets both net8.0 and net48 for compatibility |
| System.Text.Json | Built-in | JSON serialization | Already used throughout Core.Models (see CanonicalContract.cs) |
| IReadOnlyList<T> | Built-in | Collection contracts | Pattern established in Profile.OverlayIds, ControlRecord.Applicability.RoleTags |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Moq | Existing | Mocking in tests | Already used in ContentPackImporterTests.cs |
| xUnit | Existing | Test framework | All tests use [Fact], [Theory], [MemberData] |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| IReadOnlyList<T> | List<T> or Array | IReadOnlyList provides immutability contract at the API level while still allowing efficient initialization. Pattern already established in Profile. |
| Array.Empty<T>() | null or new T[0] | Array.Empty<T>() is the established pattern in Core.Models for default collection values — provides singleton empty instance vs allocating new arrays. |

**Installation:**
No new packages required. All dependencies are already in the project.

## Architecture Patterns

### Recommended Project Structure
```
src/STIGForge.Core/Models/
├── ContentPack.cs           (MODIFY: add BenchmarkIds, ApplicabilityTags, Version, Release)
├── ControlRecord.cs         (MODIFY: add SourcePackId)
├── VerificationResult.cs    (NEW: canonical verification result contract)
├── EvidenceRecord.cs        (NEW: canonical evidence metadata contract)
├── ExportIndexEntry.cs      (NEW: export package index entry contract)
├── CanonicalContract.cs     (MODIFY: bump version, add type name constants)
└── ...existing models...    (UNCHANGED)
```

### Pattern 1: Canonical Model Design
**What:** Sealed classes with { get; set; } auto-accessors, IReadOnlyList<T> for collections, Array.Empty<T>() defaults, SchemaVersion property.
**When to use:** All Core.Models types representing canonical data contracts.
**Example:**
```csharp
// Source: /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/Profile.cs
public sealed class Profile
{
  public string ProfileId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public IReadOnlyList<string> OverlayIds { get; set; } = Array.Empty<string>();
  // ... other properties
}
```

### Pattern 2: Schema Versioning
**What:** All canonical types use `SchemaVersion = CanonicalContract.Version` for version tracking.
**When to use:** Any new Core.Models type representing a canonical contract.
**Example:**
```csharp
// Source: /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/ContentPack.cs
public sealed class ContentPack
{
  public string PackId { get; set; } = string.Empty;
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
  // ... other properties
}
```

### Pattern 3: Module Contract Separation
**What:** Core.Models defines canonical contracts, module-specific types remain in their modules.
**When to use:** When promoting module types to canonical contracts.
**Example:**
```csharp
// Core.Models - canonical contract (NEW)
namespace STIGForge.Core.Models;
public sealed class VerificationResult
{
  public string ControlId { get; set; } = string.Empty;
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
  // ... canonical fields only
}

// STIGForge.Verify - implementation detail (UNCHANGED)
namespace STIGForge.Verify;
public sealed class NormalizedVerifyResult
{
  public string ControlId { get; set; } = string.Empty;
  public IReadOnlyList<string> EvidencePaths { get; set; } = Array.Empty<string>();
  public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
  public string? RawArtifactPath { get; set; }
  // ... implementation-specific fields
}
```

### Anti-Patterns to Avoid
- **Changing existing CanonicalContract.Version without schema changes:** Version should only bump when schema changes. Since we're adding fields, bump to "1.1.0" is appropriate.
- **Breaking existing module types:** Do NOT delete or move NormalizedVerifyResult or EvidenceMetadata. Create new Core.Models types alongside them.
- **Using null for default collection values:** Follow the established pattern of `Array.Empty<T>()` for IReadOnlyList<T> defaults.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON serialization | Custom JSON writers | System.Text.Json | Already used throughout project, built into .NET |
| Immutable collections | Custom immutable wrappers | IReadOnlyList<T> / IReadOnlyCollection<T> | Built-in, established pattern in Core.Models |
| Empty collection defaults | new T[0] or null | Array.Empty<T>() | Singleton pattern, zero-allocation, established in Profile/ControlRecord |

**Key insight:** The project already has established patterns for all the data modeling work needed. Follow the existing conventions rather than introducing new patterns.

## Common Pitfalls

### Pitfall 1: Schema Version Bump Confusion
**What goes wrong:** Not bumping CanonicalContract.Version when adding fields to canonical models, or bumping it unnecessarily.
**Why it happens:** Unclear what constitutes a schema change vs an implementation change.
**How to avoid:** Bump CanonicalContract.Version from "1.0.0" to "1.1.0" since we are adding new fields to ContentPack and ControlRecord (canonical models). Document the change in CanonicalContract class comments.
**Warning signs:** Tests deserializing old JSON files into new models fail.

### Pitfall 2: Module Type vs Canonical Type Confusion
**What goes wrong:** Trying to reuse NormalizedVerifyResult or EvidenceMetadata directly in Core.Models, creating circular dependencies.
**Why it happens:** Tempting to move the existing types rather than creating new canonical contracts.
**How to avoid:** Create NEW types in Core.Models that mirror ONLY the contract-level fields. Keep existing module types unchanged. Adapters can map between module types and canonical types when needed.
**Warning signs:** Core.Models namespace referencing STIGForge.Verify or STIGForge.Evidence namespaces.

### Pitfall 3: Missing SchemaVersion on New Types
**What goes wrong:** Forgetting to add SchemaVersion property to new canonical types.
**Why it happens:** SchemaVersion is easy to overlook when focused on domain fields.
**How to avoid:** Follow the ContentPack.cs pattern exactly — include `SchemaVersion = CanonicalContract.Version` on all new canonical types.
**Warning signs:** Validation tests checking for SchemaVersion fail on new types.

## Code Examples

Verified patterns from official sources:

### Adding Fields to Existing Canonical Model
```csharp
// Source: /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/ContentPack.cs
// BEFORE:
public sealed class ContentPack
{
  public string PackId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public DateTimeOffset ImportedAt { get; set; }
  public DateTimeOffset? ReleaseDate { get; set; }
  public string SourceLabel { get; set; } = string.Empty;
  public string HashAlgorithm { get; set; } = "sha256";
  public string ManifestSha256 { get; set; } = string.Empty;
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

// AFTER (per CONTEXT.md decisions):
public sealed class ContentPack
{
  public string PackId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public DateTimeOffset ImportedAt { get; set; }
  public DateTimeOffset? ReleaseDate { get; set; }

  // NEW: Benchmark version tracking for ING-02
  public string Version { get; set; } = string.Empty;
  public string Release { get; set; } = string.Empty;

  public string SourceLabel { get; set; } = string.Empty;
  public string HashAlgorithm { get; set; } = "sha256";
  public string ManifestSha256 { get; set; } = string.Empty;

  // NEW: Benchmark IDs list for ING-02
  public IReadOnlyList<string> BenchmarkIds { get; set; } = Array.Empty<string>();

  // NEW: Applicability tags for ING-02
  public IReadOnlyList<string> ApplicabilityTags { get; set; } = Array.Empty<string>();

  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}
```

### Adding Provenance Field to ControlRecord
```csharp
// Source: /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/ControlRecord.cs
// BEFORE:
public sealed class ControlRecord
{
  public string ControlId { get; set; } = string.Empty;
  public ExternalIds ExternalIds { get; set; } = new();
  public string Title { get; set; } = string.Empty;
  // ... other fields
  public RevisionInfo Revision { get; set; } = new();
}

// AFTER (per CONTEXT.md decision):
public sealed class ControlRecord
{
  public string ControlId { get; set; } = string.Empty;
  public ExternalIds ExternalIds { get; set; } = new();
  public string Title { get; set; } = string.Empty;
  // ... other fields
  public RevisionInfo Revision { get; set; } = new();

  // NEW: Provenance tracking back to ContentPack for CORE-01
  public string SourcePackId { get; set; } = string.Empty;
}
```

### Creating New Canonical VerificationResult Type
```csharp
// NEW FILE: src/STIGForge.Core/Models/VerificationResult.cs
namespace STIGForge.Core.Models;

/// <summary>
/// Canonical verification result contract. Defines the schema for verification results
/// across all verification tools (SCAP, Evaluate-STIG, CKL).
/// </summary>
public sealed class VerificationResult
{
  /// <summary>Control identifier (VulnId preferred, fallback to RuleId)</summary>
  public string ControlId { get; set; } = string.Empty;

  /// <summary>VulnId if available (e.g., V-220697)</summary>
  public string? VulnId { get; set; }

  /// <summary>RuleId if available (e.g., SV-220697r569187_rule)</summary>
  public string? RuleId { get; set; }

  /// <summary>Verification status</summary>
  public VerifyStatus Status { get; set; }

  /// <summary>Tool that generated this result</summary>
  public string Tool { get; set; } = string.Empty;

  /// <summary>When this verification was performed</summary>
  public DateTimeOffset? VerifiedAt { get; set; }

  /// <summary>Schema version for contract versioning</summary>
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}

/// <summary>
/// Canonical verification status enumeration.
/// Mirrors STIGForge.Verify.VerifyStatus but defined in Core.Models
/// as the canonical contract.
/// </summary>
public enum VerifyStatus
{
  Unknown = 0,
  Pass = 1,
  Fail = 2,
  NotApplicable = 3,
  NotReviewed = 4,
  Informational = 5,
  Error = 6
}
```

### Creating New Canonical EvidenceRecord Type
```csharp
// NEW FILE: src/STIGForge.Core/Models/EvidenceRecord.cs
namespace STIGForge.Core.Models;

/// <summary>
/// Canonical evidence metadata contract. Defines the schema for evidence records
/// across all evidence collection sources.
/// </summary>
public sealed class EvidenceRecord
{
  /// <summary>Control identifier this evidence supports</summary>
  public string ControlId { get; set; } = string.Empty;

  /// <summary>Rule identifier if applicable</summary>
  public string? RuleId { get; set; }

  /// <summary>Evidence artifact type</summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>SHA-256 hash of the evidence file</summary>
  public string Sha256 { get; set; } = string.Empty;

  /// <summary>Timestamp when evidence was collected (UTC)</summary>
  public string TimestampUtc { get; set; } = string.Empty;

  /// <summary>Run ID for apply-run provenance linkage</summary>
  public string? RunId { get; set; }

  /// <summary>Schema version for contract versioning</summary>
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}
```

### Creating New ExportIndexEntry Type
```csharp
// NEW FILE: src/STIGForge.Core/Models/ExportIndexEntry.cs
namespace STIGForge.Core.Models;

/// <summary>
/// Canonical export index entry contract. Defines the schema for entries
/// in the export package index (control_evidence_index.csv, file_hashes.sha256).
/// </summary>
public sealed class ExportIndexEntry
{
  /// <summary>Relative path to the file within the export package</summary>
  public string RelativePath { get; set; } = string.Empty;

  /// <summary>Artifact type (Scan, Checklist, Evidence, Attestation, POAM, Manifest)</summary>
  public string ArtifactType { get; set; } = string.Empty;

  /// <summary>SHA-256 hash of the file contents</summary>
  public string Sha256 { get; set; } = string.Empty;

  /// <summary>Timestamp when the file was generated</summary>
  public DateTimeOffset TimestampUtc { get; set; }

  /// <summary>Schema version for contract versioning</summary>
  public string SchemaVersion { get; set; } = CanonicalContract.Version;
}
```

### Updating CanonicalContract with New Types
```csharp
// Source: /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/CanonicalContract.cs
// BEFORE:
public static class CanonicalContract
{
  public const string Version = "1.0.0";
}

// AFTER (per Claude's discretion to bump version):
public static class CanonicalContract
{
  /// <summary>
  /// Canonical schema version. Bumped to 1.1.0 for Phase 8 additions:
  /// - ContentPack: Added BenchmarkIds, ApplicabilityTags, Version, Release
  /// - ControlRecord: Added SourcePackId
  /// - New canonical types: VerificationResult, EvidenceRecord, ExportIndexEntry
  /// </summary>
  public const string Version = "1.1.0";

  /// <summary>Canonical type name constants for schema documentation</summary>
  public const string ContentPack = "ContentPack";
  public const string ControlRecord = "ControlRecord";
  public const string Profile = "Profile";
  public const string Overlay = "Overlay";
  public const string BundleManifest = "BundleManifest";
  public const string VerificationResult = "VerificationResult";
  public const string EvidenceRecord = "EvidenceRecord";
  public const string ExportIndexEntry = "ExportIndexEntry";
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Module-specific types only | Canonical contracts in Core.Models + module types | Phase 1 | Clear separation between contract and implementation |
| SchemaVersion not tracked | All canonical types have SchemaVersion property | Phase 1 | Enables schema migration and validation |
| Version "1.0.0" | Version "1.1.0" (to be added) | Phase 8 | Signals schema change for consumers |

**Existing patterns:**
- ContentPack, ControlRecord, Profile, Overlay, BundleManifest — already canonical with SchemaVersion
- NormalizedVerifyResult (Verify module), EvidenceMetadata (Evidence module) — implementation types to remain unchanged
- Import infrastructure (ContentPackImporter, ImportQueuePlanner) — already complete with test coverage

**Deprecated/outdated:**
- None identified in this research

## Open Questions

1. **Should VerifyStatus enum be defined in Core.Models or reference the existing enum in STIGForge.Verify?**
   - What we know: STIGForge.Verify already has VerifyStatus enum. Core.Models should be independent of other modules.
   - What's unclear: Whether to duplicate the enum or create an alias.
   - Recommendation: Define VerifyStatus in Core.Models as the canonical contract. The Verify module version becomes an implementation detail. Adapters can map between them.

2. **Should EvidenceArtifactType enum be promoted to Core.Models?**
   - What we know: STIGForge.Evidence defines EvidenceArtifactType (Command, File, Registry, PolicyExport, Screenshot, Other).
   - What's unclear: Whether EvidenceRecord.Type should use this enum or remain a string.
   - Recommendation: Keep Type as string in canonical EvidenceRecord for flexibility. The Evidence module can use its enum internally and map to string for the canonical contract.

## Sources

### Primary (HIGH confidence)
- /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/ContentPack.cs — Current ContentPack model shape
- /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/ControlRecord.cs — Current ControlRecord model shape
- /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/Profile.cs — Example of IReadOnlyList<T> usage
- /mnt/c/projects/STIGForge/src/STIGForge.Core/Models/CanonicalContract.cs — Schema versioning authority
- /mnt/c/projects/STIGForge/src/STIGForge.Verify/NormalizedVerifyResult.cs — Verify module implementation type
- /mnt/c/projects/STIGForge/src/STIGForge.Evidence/EvidenceModels.cs — Evidence module implementation type
- /mnt/c/projects/STIGForge/src/STIGForge.Content/Import/ContentPackImporter.cs — Import infrastructure (ING-01 implementation)
- /mnt/c/projects/STIGForge/src/STIGForge.Content/Import/ImportQueuePlanner.cs — Import planning with confidence classification
- /mnt/c/projects/STIGForge/.worktrees/main-run/tests/STIGForge.UnitTests/Content/ContentPackImporterTests.cs — Test coverage for import infrastructure

### Secondary (MEDIUM confidence)
- /mnt/c/projects/STIGForge/src/STIGForge.Export/EmassExporter.cs — Export index implementation reference for ExportIndexEntry design
- /mnt/c/projects/STIGForge/.planning/REQUIREMENTS.md — Requirements ING-01, ING-02, CORE-01, CORE-02
- /mnt/c/projects/STIGForge/.planning/STATE.md — Project decisions and existing phase completions

### Tertiary (LOW confidence)
- None required — all findings from primary source code analysis

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — All patterns observed directly in existing source code
- Architecture: HIGH — Core.Models patterns well-established and consistent
- Pitfalls: HIGH — Common C#/.NET versioning and contract design pitfalls

**Research date:** 2026-02-22
**Valid until:** 30 days (stable domain, established patterns)
