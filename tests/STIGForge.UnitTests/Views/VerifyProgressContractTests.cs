namespace STIGForge.UnitTests.Views;

public sealed class VerifyProgressContractTests
{
    [Fact]
    public void VerifyView_ContainsVerifyToolStatusesBinding()
    {
        var xaml = LoadVerifyViewXaml();
        Assert.Contains("{Binding VerifyToolStatuses}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyView_ContainsToolProgressColumns()
    {
        var xaml = LoadVerifyViewXaml();
        Assert.Contains("{Binding ToolName}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding StateDisplay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ElapsedTime", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyView_ContainsStateColorTriggers()
    {
        var xaml = LoadVerifyViewXaml();
        Assert.Contains("DataTrigger", xaml, StringComparison.Ordinal);
        Assert.Contains("AccentBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("SuccessBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DangerBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyView_ContainsErrorRecoveryPanel()
    {
        var xaml = LoadVerifyViewXaml();
        Assert.Contains("{Binding HasVerifyError", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding VerifyError.ErrorMessage}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding VerifyError.RecoverySteps}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyView_ContainsDangerBrushBorderForErrorPanel()
    {
        var xaml = LoadVerifyViewXaml();
        Assert.Contains("Verification Failed", xaml, StringComparison.Ordinal);
        Assert.Contains("Recovery Steps:", xaml, StringComparison.Ordinal);
        Assert.Contains("Retry Verify", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyView_RetryButtonBindsToVerifyRunCommand()
    {
        var xaml = LoadVerifyViewXaml();

        // The VerifyRunCommand binding should appear at least twice (main + retry)
        var commandCount = CountOccurrences(xaml, "{Binding VerifyRunCommand}");
        Assert.True(commandCount >= 2, $"Expected VerifyRunCommand binding at least twice (main + retry), found {commandCount}");
    }

    [Fact]
    public void VerifyView_ProgressPanelAppearsBeforeOverlapAnalysis()
    {
        var xaml = LoadVerifyViewXaml();
        var progressIndex = xaml.IndexOf("VerifyToolStatuses", StringComparison.Ordinal);
        var overlapIndex = xaml.IndexOf("Scanner Overlap Analysis", StringComparison.Ordinal);

        Assert.True(progressIndex > 0, "Verify progress panel not found");
        Assert.True(overlapIndex > 0, "Scanner overlap section not found");
        Assert.True(progressIndex < overlapIndex, "Progress panel should appear before overlap analysis");
    }

    [Fact]
    public void VerifyView_ErrorPanelUsesLeftBorderAccent()
    {
        var xaml = LoadVerifyViewXaml();
        // The error panel uses a 4px left border with DangerBrush
        Assert.Contains("BorderThickness=\"4,0,0,0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyToolStatusModel_ExistsWithRequiredProperties()
    {
        // Verify the model file exists and has required members via source inspection
        var source = LoadSourceFile("src", "STIGForge.App", "Models", "VerifyToolStatus.cs");

        Assert.Contains("class VerifyToolStatus", source, StringComparison.Ordinal);
        Assert.Contains("toolName", source, StringComparison.Ordinal);
        Assert.Contains("VerifyToolState", source, StringComparison.Ordinal);
        Assert.Contains("elapsedTime", source, StringComparison.Ordinal);
        Assert.Contains("findingCount", source, StringComparison.Ordinal);
        Assert.Contains("stateDisplay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorPanelInfoModel_ExistsWithExceptionMapping()
    {
        var source = LoadSourceFile("src", "STIGForge.App", "Models", "VerifyToolStatus.cs");

        Assert.Contains("class ErrorPanelInfo", source, StringComparison.Ordinal);
        Assert.Contains("ErrorMessage", source, StringComparison.Ordinal);
        Assert.Contains("RecoverySteps", source, StringComparison.Ordinal);
        Assert.Contains("FromException", source, StringComparison.Ordinal);
        Assert.Contains("IOException", source, StringComparison.Ordinal);
        Assert.Contains("TimeoutException", source, StringComparison.Ordinal);
        Assert.Contains("FileNotFoundException", source, StringComparison.Ordinal);
    }

    private static string LoadVerifyViewXaml()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
            current = current.Parent;

        Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

        var viewPath = Path.Combine(current!.FullName, "src", "STIGForge.App", "Views", "VerifyView.xaml");
        Assert.True(File.Exists(viewPath), $"Expected VerifyView XAML at '{viewPath}'.");

        return File.ReadAllText(viewPath);
    }

    private static string LoadSourceFile(params string[] pathSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
            current = current.Parent;

        Assert.True(current is not null, "Could not locate repository root containing STIGForge.sln.");

        var filePath = Path.Combine(current!.FullName, Path.Combine(pathSegments));
        Assert.True(File.Exists(filePath), $"Expected source file at '{filePath}'.");

        return File.ReadAllText(filePath);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
