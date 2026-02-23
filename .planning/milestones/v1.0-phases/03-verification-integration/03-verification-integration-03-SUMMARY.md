# 03-verification-integration-03 Summary

## Objective

Route CLI and WPF verify flows through the shared verification workflow service and add parity-oriented integration coverage.

## Delivered

- Refactored CLI verify commands to invoke workflow service:
  - `src/STIGForge.Cli/Commands/VerifyCommands.cs`
- Refactored WPF verify execution and orchestration verify stage:
  - `src/STIGForge.App/MainViewModel.ApplyVerify.cs`
  - `src/STIGForge.App/MainViewModel.cs`
  - `src/STIGForge.App/App.xaml.cs`
- Refactored bundle orchestration verify stage:
  - `src/STIGForge.Build/BundleOrchestrator.cs`
- Added parity-focused integration tests:
  - `tests/STIGForge.IntegrationTests/Cli/VerifyCommandFlowTests.cs`

## Verification

- User confirmed targeted unit and integration tests are green after wiring changes.

## Outcome

Plan goals met: verification execution is centralized via shared orchestration across CLI and WPF paths, with parity checks in integration coverage.
