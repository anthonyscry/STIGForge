using System.Diagnostics;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public sealed class AuditPolicyRemediationHandler : IRemediationHandler
{
    private readonly string _ruleId;
    private readonly string _subcategory;
    private readonly string _expectedSetting;
    private readonly string _description;
    private readonly IProcessRunner? _processRunner;

    public AuditPolicyRemediationHandler(
        string ruleId,
        string subcategory,
        string expectedSetting,
        string description,
        IProcessRunner? processRunner = null)
    {
        _ruleId = ruleId;
        _subcategory = subcategory;
        _expectedSetting = expectedSetting;
        _description = description;
        _processRunner = processRunner;
    }

    public string RuleId => _ruleId;
    public string Category => "AuditPolicy";
    public string Description => _description;

    public async Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
        var output = await RunAuditPolAsync($"/get /subcategory:\"{_subcategory}\"", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new RemediationResult
            {
                RuleId = _ruleId,
                HandlerCategory = Category,
                Success = false,
                Changed = false,
                ErrorMessage = $"Failed to query audit policy subcategory '{_subcategory}'.",
                Detail = "auditpol.exe returned no output"
            };
        }

        var currentSetting = ParseAuditSetting(output, _subcategory) ?? output.Trim();
        var isCompliant = string.Equals(
            NormalizeSetting(currentSetting),
            NormalizeSetting(_expectedSetting),
            StringComparison.OrdinalIgnoreCase);

        return new RemediationResult
        {
            RuleId = _ruleId,
            HandlerCategory = Category,
            Success = true,
            Changed = false,
            PreviousValue = currentSetting,
            NewValue = isCompliant ? null : _expectedSetting,
            Detail = isCompliant
                ? "Already compliant"
                : $"Non-compliant: current='{currentSetting}', expected='{_expectedSetting}'"
        };
    }

    public async Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct)
    {
        if (context.DryRun)
        {
            return await TestAsync(context, ct).ConfigureAwait(false);
        }

        var testResult = await TestAsync(context, ct).ConfigureAwait(false);
        if (!testResult.Success)
        {
            return testResult;
        }

        if (string.Equals(NormalizeSetting(testResult.PreviousValue), NormalizeSetting(_expectedSetting), StringComparison.OrdinalIgnoreCase))
        {
            return testResult;
        }

        var setArguments = BuildSetArguments(_subcategory, _expectedSetting);
        if (setArguments == null)
        {
            return new RemediationResult
            {
                RuleId = _ruleId,
                HandlerCategory = Category,
                Success = false,
                Changed = false,
                PreviousValue = testResult.PreviousValue,
                ErrorMessage = $"Unsupported audit policy setting '{_expectedSetting}'."
            };
        }

        await RunAuditPolAsync(setArguments, ct).ConfigureAwait(false);

        return new RemediationResult
        {
            RuleId = _ruleId,
            HandlerCategory = Category,
            Success = true,
            Changed = true,
            PreviousValue = testResult.PreviousValue,
            NewValue = _expectedSetting,
            Detail = $"Configured audit policy '{_subcategory}' to '{_expectedSetting}'"
        };
    }

    private static string? BuildSetArguments(string subcategory, string expectedSetting)
    {
        var normalized = NormalizeSetting(expectedSetting);
        return normalized switch
        {
            "successandfailure" => $"/set /subcategory:\"{subcategory}\" /success:enable /failure:enable",
            "success" => $"/set /subcategory:\"{subcategory}\" /success:enable /failure:disable",
            "failure" => $"/set /subcategory:\"{subcategory}\" /success:disable /failure:enable",
            "noauditing" => $"/set /subcategory:\"{subcategory}\" /success:disable /failure:disable",
            _ => null
        };
    }

    private static string NormalizeSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
    }

    private static string? ParseAuditSetting(string output, string subcategory)
    {
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.Contains(subcategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("Success and Failure", StringComparison.OrdinalIgnoreCase))
            {
                return "Success and Failure";
            }

            if (line.Contains("No Auditing", StringComparison.OrdinalIgnoreCase))
            {
                return "No Auditing";
            }

            var hasSuccess = line.Contains("Success", StringComparison.OrdinalIgnoreCase);
            var hasFailure = line.Contains("Failure", StringComparison.OrdinalIgnoreCase);
            if (hasSuccess && hasFailure)
            {
                return "Success and Failure";
            }

            if (hasSuccess)
            {
                return "Success";
            }

            if (hasFailure)
            {
                return "Failure";
            }
        }

        return null;
    }

    private async Task<string?> RunAuditPolAsync(string arguments, CancellationToken ct)
    {
        if (_processRunner == null)
        {
            return "[No process runner - simulation]";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "auditpol.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var result = await _processRunner.RunAsync(startInfo, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var stderr = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();
            throw new InvalidOperationException($"auditpol.exe command failed: {stderr}");
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError.Trim()
            : result.StandardOutput.Trim();
    }
}
