using System.Diagnostics;

namespace STIGForge.Core.Abstractions;

/// <summary>
/// Captures the exit code, standard output, and standard error from a completed process.
/// </summary>
public sealed class ProcessResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
}

/// <summary>
/// Runs external processes (PowerShell, LGPO, SCAP tools) and captures their output.
/// Provides a testable abstraction over <see cref="System.Diagnostics.Process"/>.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct);
    bool ExistsInPath(string fileName);
}
