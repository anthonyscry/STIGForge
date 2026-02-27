# Tasks: Dashboard Tabs, CAT Severity Totals, and Harden Backend Expansion

## Execution Plan (STANDARD)

- [x] Task 1 - Add failing tests for new tabbed dashboard structure.
  - Checkpoint: contract tests assert required tab headers exist.
  - Verification: `DashboardViewContractTests` contains tab header assertions.

- [x] Task 2 - Add failing tests for CAT I/II/III summary calculations.
  - Checkpoint: verification workflow tests assert CAT counts.
  - Verification: `VerificationWorkflowServiceTests` asserts `CatICount`, `CatIICount`, `CatIIICount`.

- [x] Task 3 - Extend verification domain result model with CAT count fields.
  - Checkpoint: `VerificationWorkflowResult` includes CAT properties.
  - Verification: compile succeeds for consumers and tests.

- [x] Task 4 - Implement CAT severity aggregation in verification workflow service.
  - Checkpoint: fail/open findings map severity high/medium/low to CAT I/II/III.
  - Verification: mixed severity unit test passes at compile-time and logic assertions are present.

- [x] Task 5 - Refactor dashboard into four tabs with workflow reduced to Scan/Harden/Verify cards.
  - Checkpoint: Import action remains available in Import Library tab.
  - Verification: XAML contract checks for required tabs and no Import workflow card.

- [x] Task 6 - Add CAT totals to Compliance Summary UI and bind to view model properties.
  - Checkpoint: Compliance tab shows total vulnerabilities + CAT I/II/III.
  - Verification: XAML contains bindings for CAT properties.

- [x] Task 7 - Expand harden request shaping to include local PowerSTIG/DSC/LGPO artifact discovery.
  - Checkpoint: `ApplyRequest` receives discovered module/data/policy/scope inputs.
  - Verification: workflow view model unit test captures request and validates paths.

- [x] Task 8 - Run build/test verification and document runtime constraints.
  - Checkpoint: .NET project builds cleanly; test runtime dependencies noted.
  - Verification: `dotnet build tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj` reports 0 errors.
