using System.Diagnostics;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public abstract class RemediationHandlerBase : IRemediationHandler
{
    protected RemediationHandlerBase(string ruleId, string category, string description, IProcessRunner? processRunner = null)
    {
        RuleId = ruleId;
        Category = category;
        Description = description;
        ProcessRunner = processRunner;
    }

    public string RuleId { get; }
    public string Category { get; }
    public string Description { get; }

    protected IProcessRunner? ProcessRunner { get; }

    public abstract Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct);

    public abstract Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct);

    protected RemediationResult BuildResult(
        bool success,
        bool changed,
        string? previousValue = null,
        string? newValue = null,
        string? detail = null,
        string? errorMessage = null)
    {
        return new RemediationResult
        {
            RuleId = RuleId,
            HandlerCategory = Category,
            Success = success,
            Changed = changed,
            PreviousValue = previousValue,
            NewValue = newValue,
            Detail = detail,
            ErrorMessage = errorMessage
        };
    }

    protected async Task<string?> RunPowerShellAsync(
        string script,
        string failureMessagePrefix,
        CancellationToken ct,
        bool returnNullWhenOutputEmpty = false)
    {
        if (ProcessRunner == null)
        {
            return "[No process runner - simulation]";
        }

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var result = await ProcessRunner.RunAsync(startInfo, ct).ConfigureAwait(false);
        ThrowIfProcessFailed(result, failureMessagePrefix);
        return NormalizeOutput(result, returnNullWhenOutputEmpty);
    }

    protected async Task<string?> RunProcessAsync(
        string fileName,
        string arguments,
        string failureMessagePrefix,
        CancellationToken ct,
        bool returnNullWhenOutputEmpty = false)
    {
        if (ProcessRunner == null)
        {
            return "[No process runner - simulation]";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var result = await ProcessRunner.RunAsync(startInfo, ct).ConfigureAwait(false);
        ThrowIfProcessFailed(result, failureMessagePrefix);
        return NormalizeOutput(result, returnNullWhenOutputEmpty);
    }

    private static void ThrowIfProcessFailed(ProcessResult result, string failureMessagePrefix)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var stderr = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        throw new InvalidOperationException($"{failureMessagePrefix}: {stderr}");
    }

    private static string? NormalizeOutput(ProcessResult result, bool returnNullWhenOutputEmpty)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput.Trim();
        }

        if (returnNullWhenOutputEmpty && string.IsNullOrWhiteSpace(result.StandardError))
        {
            return null;
        }

        return result.StandardError.Trim();
    }
}
