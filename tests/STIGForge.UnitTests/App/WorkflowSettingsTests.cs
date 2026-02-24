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
}
