# Phase 1 Plan 02 - Architectural Decisions

## [2026-02-03] Parser Strategy

### Decision: Use XmlReader for All Parsers
**Rationale:** Consistency with XccdfParser, proven performance benefits

### Decision: No AdmxParser Library
**Rationale:** AdmxParser cannot load custom ADMX file paths (only system PolicyDefinitions)
**Alternative:** Manual XmlReader streaming pattern

### Decision: OVAL Reference-Only Storage
**Rationale:** Do not execute OVAL logic, only store definitions as JSON metadata
**Benefit:** Simpler implementation, future-proof for OVAL execution if needed

### Decision: ScapBundleParser Coordinates Multiple Formats
**Pattern:** ZIP extraction → detect file types → delegate to specialized parsers
**Benefit:** Clean separation of concerns, reusable parsers
