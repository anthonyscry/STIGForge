# Phase 1 Plan 01 - Architectural Decisions

## [2026-02-03] XmlReader Streaming Pattern

### Decision: Use XmlReader instead of XDocument
**Rationale:** Current XDocument.Load() causes OutOfMemoryException on 50MB+ STIG files.

**Performance Targets:**
- 5x faster parsing
- 39x less memory usage
- <500MB memory for 50MB files (vs 5GB with XDocument)

### Decision: Forward-Only Parsing
**Pattern:** `while (reader.Read())` with state tracking
**Benefit:** No DOM tree in memory, constant memory footprint

### Decision: Extension Methods for Readability
**Location:** `src/STIGForge.Content/Extensions/XmlReaderExtensions.cs`
**Methods:**
- `GetAttribute()` - Non-destructive attribute reading
- `ReadElementContent()` - Safe content extraction
- `ReadCheckContent()` - Multi-line check content parsing
- `MoveToPreviousAttribute()` - Position restoration

### Decision: Enhanced IsManual Heuristics
**Three-tier detection:**
1. Explicit "manual" in check/@system → Manual
2. SCC system (scap.nist.gov) → Automated (NOT manual)
3. Keywords in content (manually, review, examine, inspect, audit) → Manual

**Critical:** SCC detection prevents false positives where automated checks contain manual keywords in descriptions.
