# Phase 1 Plan - Foundations and Canonical Contracts

## Goal

Deliver M1 with schema-first ingestion and canonical policy foundations that unblock all downstream milestones.

## Requirements Covered

- ING-01, ING-02, ING-03
- CORE-01, CORE-02, CORE-03, CORE-04

## Workstreams

## 1) Contract Baseline

- Add/verify schemas for `ContentPack`, `ControlRecord`, `Profile`, `Overlay`.
- Add schema validation harness for fixture inputs.
- Add version metadata and provenance field checks.

## 2) Import and Normalization Core

- Implement/validate classifier reason codes and confidence behavior.
- Implement canonical metadata normalization and hash manifest creation.
- Implement malformed artifact diagnostics path with audit logging.

## 3) Profile/Overlay Policy Engine

- Implement profile policy evaluation (OS/role/classification/environment mode).
- Implement deterministic overlay merge precedence and conflict reporting.
- Implement scope filter behavior and report export row generation.

## 4) Quality Gates and Docs

- Add unit + contract tests for all above capabilities.
- Record architecture and interface boundary confirmations.
- Publish M1 gate report.

## Task Checklist (Execution Ready)

- [ ] Write failing contract tests for ingestion schemas.
- [ ] Implement schema validation helpers and pass tests.
- [ ] Write failing import classifier diagnostics tests.
- [ ] Implement classifier reason-code behavior and pass tests.
- [ ] Write failing canonical metadata/hash tests.
- [ ] Implement normalization + hash manifest path and pass tests.
- [ ] Write failing overlay precedence tests.
- [ ] Implement deterministic overlay merge and pass tests.
- [ ] Write failing scope-filter report tests.
- [ ] Implement scope-filter report export and pass tests.
- [ ] Run M1 regression subset and capture gate artifacts.

## M1 Definition of Done

- [ ] All M1 requirements pass contract + unit tests.
- [ ] Import fixtures produce canonical schema-valid outputs.
- [ ] Deterministic merge behavior proven via repeated-run test.
- [ ] M1 hard acceptance gate in roadmap is fully satisfied.

## Command to Start

`/gsd-plan-phase 1`
