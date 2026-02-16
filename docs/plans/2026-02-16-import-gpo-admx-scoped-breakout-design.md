# Import GPO/ADMX Scoped Breakout Design

Date: 2026-02-16
Status: Approved
Scope: Import folder scan, consolidated package breakout, and library listing behavior

## Context

Current import behavior is over-importing ADMX and under-splitting GPO content from large DISA GPO bundles.

Evidence from the latest run:
- `.stigforge/logs/import_scan_20260216_084109_250.json` shows `Admx: 25`, `Gpo: 1`.
- `U_STIG_GPO_Package_January_2026.zip` contains 25 `.admx` files in `ADMX Templates/*` and local/domain policy structures (`Support Files/Local Policies`, `gpos/*`).

Operator intent:
1. Local policy must come from `Support Files/Local Policies` and be split by OS baseline.
2. Domain GPO content is separate from local baseline and should map to domain/DC deployment context.
3. ADMX should not create one library item per file; use meaningful grouping.
4. Large outer bundle ZIP names should not appear as imported content items in the library.

## Goals

1. Import ADMX as one pack per folder under `ADMX Templates/*`.
2. Import local GPO as OS-scoped packs from `Support Files/Local Policies/*`.
3. Import domain GPO as domain-scoped packs from `gpos/*`.
4. Ensure imported library shows only inner imported content packs, not outer bundle placeholders.
5. Preserve existing STIG/SCAP behavior and queue orchestration.

## Non-Goals

1. Reworking overall mission-pack selection UX.
2. Replacing existing dedup architecture.
3. Adding new external dependencies.

## Chosen Approach

Use scoped extraction roots in importer routes:

- ADMX route (`ImportAdmxTemplatesFromZipAsync`):
  - Strict scoped mode.
  - Only import `.admx` found under `ADMX Templates/<folder>/...`.
  - Create one imported pack per `<folder>`.
  - Ignore `.admx` outside that subtree.

- Consolidated GPO route (`ImportConsolidatedZipAsync`):
  - Detect and import `Support Files/Local Policies/<os-or-scope>` as local GPO packs.
  - Detect and import `gpos/<domain-or-scope>` as domain GPO packs.
  - Skip creating single fallback pack from outer ZIP when scoped roots are present.

## Architecture

### Scanner and Queue

- Keep scanner candidate detection and queue planning model.
- Retain two-route plan for mixed GPO+ADMX ZIPs:
  - `ConsolidatedZip` for GPO/LGPO content.
  - `AdmxTemplatesFromZip` for ADMX content.

### Importer

- Add scoped root discovery helpers for:
  - ADMX folder group roots.
  - Local policy OS roots.
  - Domain GPO roots.
- Route-specific import names/source labels:
  - Local policy: `gpo_lgpo_import`
  - Domain GPO: `gpo_domain_import`
  - ADMX templates: `admx_template_import`

### Library Listing

- No explicit UI filtering hack required.
- Library naturally lists only created `ContentPack` records.
- By removing outer fallback pack creation for scoped bundles, outer ZIP names no longer appear.

## Data Flow

1. Scan `C:\Projects\STIGForge\import` for ZIPs.
2. Detect candidates and build route plan per ZIP.
3. For ADMX route:
   - Extract ZIP.
   - Group `ADMX Templates/<folder>`.
   - Import one pack per folder.
4. For GPO route:
   - Extract ZIP.
   - Import one local pack per `Support Files/Local Policies/<scope>`.
   - Import one domain pack per `gpos/<scope>`.
5. Persist only those imported packs.
6. Refresh library from persisted packs.

## Error Handling and Telemetry

1. If scoped roots are missing, importer logs warning and uses existing behavior only where safe.
2. For strict ADMX mode, `.admx` outside `ADMX Templates/*` are ignored and logged.
3. Continue per-pack error isolation; one failed scope must not fail all scopes.

## Applicability Model

1. Local GPO packs remain OS baseline scoped.
2. Domain GPO packs use `gpo_domain_import` and are treated as domain-context content.
3. Workstation/member server keeps domain GPO as `Unknown` unless domain context signals allow applicability.
4. Domain controller role can mark domain GPO applicable.

## Testing Strategy

### Unit Tests

1. ADMX folder grouping test:
   - Input ZIP with multiple files in one folder and multiple folders.
   - Assert one imported pack per folder.
2. ADMX strict scope test:
   - Input `.admx` both inside and outside `ADMX Templates/*`.
   - Assert only scoped files are imported.
3. GPO scoped split test:
   - Input local and domain structures in one ZIP.
   - Assert local and domain packs are both imported and source labels correct.
4. No-outer-pack regression test:
   - Assert scoped import does not create fallback outer bundle pack.

### Integration/Smoke

1. Run Import tab scan against real `U_STIG_GPO_Package_January_2026.zip`.
2. Verify summary shifts from `Admx: 25, Gpo: 1` to grouped counts.
3. Verify library list contains only inner scoped packs.

## Success Criteria

1. ADMX count reflects template folders, not raw file count.
2. Local GPO is split by OS/scope under `Support Files/Local Policies`.
3. Domain GPO is imported as separate domain-scoped packs.
4. Outer consolidated bundle name is absent from imported library list.
5. Existing STIG/SCAP import behavior remains stable.
