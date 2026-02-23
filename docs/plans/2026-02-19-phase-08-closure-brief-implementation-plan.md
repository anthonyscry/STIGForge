# Phase 08 Closure Brief Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Produce a decision-ready Phase 08 closure brief that gives mixed-audience reviewers a clear, artifact-backed verdict on whether Phase 08 can be trusted as release decision input.

**Architecture:** Create one canonical closure memo under the Phase 08 milestone folder, driven by fail-closed evidence checks across three sources: verification artifact, phase summaries, and requirements traceability. Build the brief in decision order (verdict first), then validate completeness and consistency with explicit command checks before finalizing links.

**Tech Stack:** Markdown documentation, git, ripgrep (`rg`), PowerShell/C# repo artifacts as evidence inputs.

---

### Task 1: Establish brief skeleton and fail-first validation

**Files:**
- Create: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`
- Test: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`

**Step 1: Write the failing check command first**

Prepare a section-presence check that must fail before content exists.

Run:
`rg --line-number "^## (Closure Verdict|Requirement Proof Set|Residual Risk and Controls|Release Go/No-Go Implication|Quick Evidence Index)$" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`

Expected: FAIL (file missing or no required sections).

**Step 2: Create minimal brief skeleton**

Add file with only:
- title,
- placeholder one-line context,
- five required section headers named exactly as in Step 1.

**Step 3: Re-run section check to verify pass**

Run the same `rg` command.

Expected: PASS with five heading matches.

**Step 4: Commit skeleton checkpoint**

Run:
`git add .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md && git commit -m "docs(phase-08): scaffold closure brief structure"`

Expected: commit succeeds with one new file.

### Task 2: Fill decision verdict and requirement proof cards (UR-01..UR-04)

**Files:**
- Modify: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`
- Read for evidence: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md`
- Read for evidence: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md`
- Read for evidence: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md`
- Read for evidence: `.planning/REQUIREMENTS.md`
- Test: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`

**Step 1: Add closure verdict block**

Write a top verdict line using one of: `closed`, `conditionally-closed`, `open`, plus a short confidence rationale tied to artifacts.

**Step 2: Add four proof cards**

For each of `UR-01`, `UR-02`, `UR-03`, `UR-04`, add:
- requirement statement,
- implemented behavior summary,
- explicit evidence links (verification + summary/requirements trace),
- confidence note.

**Step 3: Validate requirement coverage exists in brief**

Run:
`rg --line-number "UR-01|UR-02|UR-03|UR-04" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`

Expected: PASS with all four requirement IDs present.

**Step 4: Validate source evidence IDs still align**

Run:
`rg --line-number "UR-01|UR-02|UR-03|UR-04" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-VERIFICATION.md .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-01-SUMMARY.md .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-upgrade-rebase-operator-workflow-02-SUMMARY.md .planning/REQUIREMENTS.md`

Expected: PASS; all four IDs appear across canonical sources.

**Step 5: Commit requirement-proof checkpoint**

Run:
`git add .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md && git commit -m "docs(phase-08): add closure verdict and UR proof mapping"`

Expected: commit succeeds with proof content update.

### Task 3: Add residual risk, decision implication, and evidence index

**Files:**
- Modify: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`
- Read for context: `.planning/STATE.md`
- Read for context: `.planning/ROADMAP.md`
- Test: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`

**Step 1: Write residual risk and controls section**

Document what could still regress and which existing controls/evidence detect it.

**Step 2: Write release implication section**

Translate closure state into reviewer-ready language, including what would change verdict confidence.

**Step 3: Write quick evidence index**

List canonical files with one-line purpose per file for fast reviewer navigation.

**Step 4: Validate all required sections are now substantive**

Run:
`rg --line-number "^## (Closure Verdict|Requirement Proof Set|Residual Risk and Controls|Release Go/No-Go Implication|Quick Evidence Index)$|UR-0[1-4]|closed|conditionally-closed|open" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`

Expected: PASS with section headers, requirement IDs, and explicit verdict state.

**Step 5: Commit decision-readiness checkpoint**

Run:
`git add .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md && git commit -m "docs(phase-08): complete decision-ready closure brief"`

Expected: commit succeeds with full memo content.

### Task 4: Cross-link the new closure brief for discoverability

**Files:**
- Modify: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md`
- Test: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md`

**Step 1: Add closure brief reference in Phase 08 context doc**

Add a short "Decision Evidence" entry pointing to `08-CLOSURE-BRIEF.md` and `08-VERIFICATION.md`.

**Step 2: Validate reference exists**

Run:
`rg --line-number "08-CLOSURE-BRIEF.md|08-VERIFICATION.md" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md`

Expected: PASS with both references present.

**Step 3: Commit discoverability checkpoint**

Run:
`git add .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md && git commit -m "docs(phase-08): link closure brief from context"`

Expected: commit succeeds with context link update.

### Task 5: Final verification sweep and handoff notes

**Files:**
- Verify: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`
- Verify: `.planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md`
- Verify: `docs/plans/2026-02-19-phase-08-closure-brief-design.md`

**Step 1: Run combined verification checks**

Run:
- `rg --line-number "UR-0[1-4]|^## Closure Verdict|^## Requirement Proof Set|^## Residual Risk and Controls|^## Release Go/No-Go Implication|^## Quick Evidence Index" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md`
- `rg --line-number "08-CLOSURE-BRIEF.md|08-VERIFICATION.md" .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md`

Expected: PASS; all required sections and cross-links present.

**Step 2: Run status/diff review before handoff**

Run:
- `git status --short`
- `git diff -- .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md`

Expected: only intended documentation changes appear.

**Step 3: Commit final handoff marker**

Run:
`git add .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CLOSURE-BRIEF.md .planning/milestones/v1.1-phases/08-upgrade-rebase-operator-workflow/08-CONTEXT.md && git commit -m "docs(phase-08): finalize closure brief and evidence cross-links"`

Expected: final commit records complete decision-ready artifact.
