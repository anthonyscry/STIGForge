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
}
