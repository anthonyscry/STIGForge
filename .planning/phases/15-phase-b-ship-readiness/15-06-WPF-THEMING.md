---
feature: wpf-theming
type: technical-spec
status: complete
---

# WPF UI Theming Overhaul

## Overview

Visual consistency improvements and compliance workflow reporting refinements for the WPF application. Enhances operator experience with better status visualization and workflow feedback.

## Changes

**Commit:** 1088ff2

### Visual Style Updates

**Workflow Status Cards:**
- Compliance percentage displayed prominently
- Color-coded status indicators (Green/Yellow/Red)
- Progress indicators for long-running operations

**Report Refinements:**
- Structured compliance summary view
- Actionable guidance for failed controls
- Evidence artifact linking

### Workflow Reporting

**Scan Workflow:**
- Real-time checklist progress
- Per-STIG status tracking
- Estimated time remaining

**Apply Workflow:**
- DSC compilation progress
- PowerSTIG execution status
- Rollback option visibility

**Verify Workflow:**
- SCAP tool execution status
- Evaluate-STIG result parsing
- Consolidated report generation

---

## Implementation

**Files Modified:**
- `src/STIGForge.App/App.xaml` — Theme resources
- `src/STIGForge.App/WorkflowViewModel.cs` — Status presentation logic
- `src/STIGForge.App/Views/Workflow*.xaml` — Workflow UI updates

**Key Classes:**
```csharp
// Compliance status presentation
public class ComplianceSummaryViewModel
{
    public double CompliancePercentage { get; set; }
    public Brush StatusColor { get; set; }
    public List<ControlStatusItem> FailedControls { get; set; }
    public string RecommendedAction { get; set; }
}
```

---

## Screens

### Main Dashboard

**Before:** Basic status text  
**After:** Visual cards with:
- Overall compliance gauge
- Recent activity timeline
- Quick action buttons

### Scan Results

**Before:** Raw checklist data  
**After:**
- Grouped by STIG
- Filterable by severity
- Export options

### Apply Progress

**Before:** Console-style log output  
**After:**
- Progress bars per phase
- DSC resource execution list
- Error highlighting with remediation

---

## Testing

**Visual Regression:**
- UI automation screenshots capture theming
- Baseline comparison for style consistency

**Accessibility:**
- Color contrast compliance (WCAG 2.1)
- Screen reader compatibility
- Keyboard navigation

---

## Design Principles

1. **Clarity:** Status is immediately obvious
2. **Actionability:** Failed items show next steps
3. **Consistency:** Same patterns across all workflows
4. **Feedback:** Progress visible for long operations

---

## Related Work

- UI Automation framework tests theming stability
- Phase 12 WPF parity ensures CLI/WPF consistency
- GAP features integrate with new workflow UI
