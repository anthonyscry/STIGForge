using STIGForge.App;

namespace STIGForge.UnitTests.App;

public class WorkflowSettingsTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"stigforge-test-{Guid.NewGuid()}.json");
        try
        {
            var settings = new WorkflowSettings
            {
                ImportFolderPath = @"C:\test\import",
                EvaluateStigToolPath = @"C:\test\tool",
                OutputFolderPath = @"C:\test\output"
            };

            WorkflowSettings.Save(settings, tempPath);
            var loaded = WorkflowSettings.Load(tempPath);

            Assert.Equal(settings.ImportFolderPath, loaded.ImportFolderPath);
            Assert.Equal(settings.EvaluateStigToolPath, loaded.EvaluateStigToolPath);
            Assert.Equal(settings.OutputFolderPath, loaded.OutputFolderPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_ExportFormats()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"stigforge-test-{Guid.NewGuid()}.json");
        try
        {
            var settings = new WorkflowSettings
            {
                ExportCkl = true,
                ExportCsv = false,
                ExportXccdf = true
            };

            WorkflowSettings.Save(settings, tempPath);
            var loaded = WorkflowSettings.Load(tempPath);

            Assert.True(loaded.ExportCkl);
            Assert.False(loaded.ExportCsv);
            Assert.True(loaded.ExportXccdf);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_EvaluateAdvancedOptions()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"stigforge-test-{Guid.NewGuid()}.json");
        try
        {
            var settings = new WorkflowSettings
            {
                EvaluateAfPath = @"C:\test\evaluate\af",
                EvaluateSelectStig = "U_MS_Windows_11_STIG",
                EvaluateAdditionalArgs = "-ThrottleLimit 4 -SkipSignatureCheck",
                RequireElevationForScan = false
            };

            WorkflowSettings.Save(settings, tempPath);
            var loaded = WorkflowSettings.Load(tempPath);

            Assert.Equal(settings.EvaluateAfPath, loaded.EvaluateAfPath);
            Assert.Equal(settings.EvaluateSelectStig, loaded.EvaluateSelectStig);
            Assert.Equal(settings.EvaluateAdditionalArgs, loaded.EvaluateAdditionalArgs);
            Assert.Equal(settings.RequireElevationForScan, loaded.RequireElevationForScan);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
