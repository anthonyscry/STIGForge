using System.Diagnostics;

namespace STIGForge.Core.Abstractions;

public sealed class ProcessResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct);
    bool ExistsInPath(string fileName);
}
