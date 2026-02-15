# Import Tab UI Readability Design

Date: 2026-02-15
Status: Approved
Scope: `src/STIGForge.App/Views/ImportView.xaml` presentation/readability updates only

## Context

The Import tab currently contains all required controls and data, but key actions, scan context, library operations, and details compete visually. This makes first-pass scanning harder than needed, especially during routine operator flow.

This design keeps existing command bindings and behavior unchanged, and focuses on conservative readability polish within the current STIGForge visual language.

## Goals

1. Reduce cognitive load in Import tab by improving information hierarchy.
2. Preserve all existing import behavior and bindings.
3. Make action intent, context, and state easier to scan in one pass.
4. Improve readability of dense sections without hiding diagnostics.

## Non-Goals

1. No command or workflow logic changes.
2. No view-model changes for Import behavior.
3. No cross-tab redesign.
4. No strong visual rebrand.

## Approaches Considered

### 1) Sectioned hierarchy (selected)

Reframe Import tab into clear visual zones with consistent headings, spacing rhythm, and helper text.

Pros:
- Strong readability improvement with low implementation risk.
- Keeps existing behavior and control set intact.
- Easy to validate with a structured UX checklist.

Cons:
- Layout-only changes cannot address deeper workflow inefficiencies.

### 2) Task-flow layout

Restructure full tab around linear operator sequence (scan, validate, select, inspect).

Pros:
- Potentially faster for first-time operators.

Cons:
- Higher change risk and likely behavior coupling.

### 3) Dense data-first polish

Keep structure mostly as-is and tune labels/columns only.

Pros:
- Minimal change footprint.

Cons:
- Smaller cognitive-load improvements.

## Selected Design

### Architecture

Keep all Import tab commands, bindings, and data sources intact. Apply readability improvements only in view structure and visual hierarchy.

Organize the surface into four zones:
1. Primary Actions
2. Machine Context
3. Content Library
4. Pack Details

Use existing theme resources and conservative spacing/typography adjustments to preserve established STIGForge look and behavior.

### Components

1. Primary Actions strip
   - Group top actions under a clear heading.
   - Keep command wiring unchanged.
   - Add concise helper line for purpose.

2. Machine Context card
   - Preserve Local/AD scan tabs.
   - Emphasize summary and applicability rows first.
   - Visually de-emphasize debug diagnostics while keeping content available.

3. Content Library card
   - Preserve search/filter/list/actions behavior.
   - Separate filter/search controls from row action controls.
   - Improve scanability of grid and destructive action visibility.

4. Pack Details card
   - Keep current fields and bindings.
   - Improve definition-style readability (label/value rhythm, wrapping, spacing).

5. Status/readout lines
   - Keep existing bound properties.
   - Place instruction/result/detail text in predictable positions.

### Data Flow and Read Path

Behavioral data flow is unchanged. The visual read path is improved to:

1. Act (primary buttons)
2. Understand machine context (summary and applicability)
3. Select/inspect in library
4. Confirm details in side panel

Diagnostic text remains accessible but visually secondary to operational summaries.

## Error Handling and Readability Safety

1. Existing error/status handling remains unchanged via current bound properties.
2. Long and empty state text must remain legible through wrapping and spacing.
3. Destructive actions remain visually distinct from routine actions.
4. Accessibility affordances (tooltips/automation names) are preserved.
5. No hidden or removed critical controls.

## Testing and Acceptance

Primary acceptance signal: structured UX checklist for Import tab readability.

Checklist categories:
1. Hierarchy clarity (actions, context, library, details are immediately distinguishable)
2. Label clarity (concise, unambiguous headings and helper text)
3. Spacing consistency (uniform rhythm within and across sections)
4. Scanability (operators can find primary action, status, and details quickly)

Operator walkthrough checks:
1. Identify primary action in under 3 seconds.
2. Locate machine scan summary without opening diagnostics.
3. Filter/select content and find pack details without ambiguity.

Regression validation:
1. Long `PackId` and path fields wrap without overlap.
2. Empty scan results remain readable.
3. Verbose diagnostics do not crowd primary operational content.
4. Existing tests/build still pass after layout changes.

## User Validation Record

Approved in sequence:
1. Architecture
2. Components
3. Data Flow
4. Error Handling and Readability Safety
5. Testing and Acceptance
