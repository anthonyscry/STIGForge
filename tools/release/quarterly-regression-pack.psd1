@{
  SchemaVersion = 1
  PackId = "2026-Q1"
  Quarter = "2026Q1"
  BaselineLabel = "phase07-compatibility-fixture-baseline"

  DriftPolicy = @{
    FailOnMissingFixture = $true
    FailOnBaselineMismatch = $true
    ThresholdBreachSeverity = "warning"
    MaxWarnings = 2
    DefaultThresholds = @{
      MaxSizeDeltaPercent = 35
      MaxLineDeltaPercent = 35
    }
  }

  Fixtures = @(
    @{
      Scenario = "unit-stig-baseline"
      Format = "Stig"
      Path = "tests/STIGForge.UnitTests/fixtures/compat-stig-baseline-xccdf.xml"
      Baseline = @{
        Sha256 = "6d1a15bd342945be5a0ee271653df4ab0525904fcd93b40c42d35c973645a1fa"
        SizeBytes = 485
        RootElement = "Benchmark"
      }
    }
    @{
      Scenario = "unit-stig-quarterly-delta"
      Format = "Stig"
      Path = "tests/STIGForge.UnitTests/fixtures/compat-stig-quarterly-delta-xccdf.xml"
      CompareAgainstScenario = "unit-stig-baseline"
      ExpectedHashChange = $true
      Thresholds = @{
        MaxSizeDeltaPercent = 40
        MaxLineDeltaPercent = 25
      }
    }
    @{
      Scenario = "unit-scap-baseline-xccdf"
      Format = "Scap"
      Path = "tests/STIGForge.UnitTests/fixtures/compat-scap-baseline-xccdf.xml"
      Baseline = @{
        Sha256 = "cdef44b960828417aac65c923a6d78fd5c136cd9fa2e55609bad2c6caf9071ed"
        SizeBytes = 504
        RootElement = "Benchmark"
      }
    }
    @{
      Scenario = "unit-scap-baseline-oval"
      Format = "Scap"
      Path = "tests/STIGForge.UnitTests/fixtures/compat-scap-baseline-oval.xml"
      Baseline = @{
        Sha256 = "4a4dcfbe5e16c1c9b2e5ea50ba44526c8b0be4537214f45cd2ca6f81f94870e3"
        SizeBytes = 357
        RootElement = "oval_definitions"
      }
    }
    @{
      Scenario = "unit-gpo-baseline"
      Format = "Gpo"
      Path = "tests/STIGForge.UnitTests/fixtures/compat-gpo-baseline.admx"
      Baseline = @{
        Sha256 = "e55aaeb459aa3cb153ca1bcffb5308ecbeb64d540e581a047a4dfe7289fa4722"
        SizeBytes = 658
        RootElement = "policyDefinitions"
      }
    }
    @{
      Scenario = "unit-gpo-quarterly-delta"
      Format = "Gpo"
      Path = "tests/STIGForge.UnitTests/fixtures/compat-gpo-quarterly-delta.admx"
      CompareAgainstScenario = "unit-gpo-baseline"
      ExpectedHashChange = $true
      Thresholds = @{
        MaxSizeDeltaPercent = 15
        MaxLineDeltaPercent = 10
      }
    }
  )
}
