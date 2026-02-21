# Schema Guide - STIGForge Next

## Purpose

These schemas define the canonical contracts for STIGForge Next pipeline artifacts and records.

## Schema Versioning

- `schema_version` uses semantic versioning (`MAJOR.MINOR.PATCH`).
- **MAJOR**: breaking contract changes.
- **MINOR**: additive backwards-compatible fields.
- **PATCH**: non-structural clarifications.

## Compatibility Policy

- Readers must reject unknown major versions.
- Readers should tolerate unknown additive fields on same major version.
- Writers must emit required fields for declared schema version.

## Migration Policy

- Every major schema bump requires:
  - migration notes
  - migration transformer
  - fixture conversion tests
- Migrations must preserve provenance metadata unless field is explicitly deprecated.

## Provenance Minimum

Every schema includes provenance metadata with at least:

- source pack/run identifiers
- imported/generated timestamps (UTC)
- producing tool/component version
- confidence/reason code where relevant

## Determinism Rule

Objects/lists that affect package indexing must use canonical sort rules documented in `BundleManifest` determinism policy fields.
