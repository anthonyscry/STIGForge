namespace STIGForge.Cli.Commands;

/// <summary>Heuristic detection of STIGForge tool paths on the local machine.</summary>
internal static class ToolPathAutoDetector
{
    public static string? TryAutoDetectEvaluateStigRoot()
    {
        var candidates = new[]
        {
            Path.GetFullPath(@".\.stigforge\tools\Evaluate-STIG\Evaluate-STIG"),
            Path.GetFullPath(@".\.stigforge\tools\Evaluate-STIG"),
            @"C:\Evaluate-STIG",
            @"C:\Program Files\Evaluate-STIG"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "Evaluate-STIG.ps1")))
                return candidate;
        }

        return null;
    }

    public static string? TryAutoDetectScapCommand()
    {
        var candidates = new[]
        {
            Path.GetFullPath(@".\.stigforge\tools\SCC\scc.exe"),
            @"C:\SCC\scc.exe",
            @"C:\Program Files\SCC\scc.exe",
            @"C:\Program Files (x86)\SCC\scc.exe"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string? TryAutoDetectPowerStigModulePath()
    {
        var modulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
        var separators = modulePath.Contains(';') ? new[] { ';' } : new[] { Path.PathSeparator };

        foreach (var segment in modulePath.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var root = segment.Trim();
            if (root.Length == 0 || !Directory.Exists(root))
                continue;

            var directoryCandidate = Path.Combine(root, "PowerSTIG");
            if (!Directory.Exists(directoryCandidate))
                continue;

            var psd1 = Path.Combine(directoryCandidate, "PowerSTIG.psd1");
            if (File.Exists(psd1))
                return psd1;

            return directoryCandidate;
        }

        return null;
    }
}
