using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public sealed class AuditPolicyRemediationHandler : RemediationHandlerBase
{
    private readonly string _subcategory;
    private readonly string _expectedSetting;

    public AuditPolicyRemediationHandler(
        string ruleId,
        string subcategory,
        string expectedSetting,
        string description,
        IProcessRunner? processRunner = null)
        : base(ruleId, "AuditPolicy", description, processRunner)
    {
        _subcategory = subcategory;
        _expectedSetting = expectedSetting;
    }

    public override async Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
        var output = await RunAuditPolAsync($"/get /subcategory:\"{_subcategory}\"", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return BuildResult(
                success: false,
                changed: false,
                detail: "auditpol.exe returned no output",
                errorMessage: $"Failed to query audit policy subcategory '{_subcategory}'.");
        }

        var currentSetting = ParseAuditSetting(output, _subcategory) ?? output.Trim();
        var isCompliant = string.Equals(
            NormalizeSetting(currentSetting),
            NormalizeSetting(_expectedSetting),
            StringComparison.OrdinalIgnoreCase);

        return BuildResult(
            success: true,
            changed: false,
            previousValue: currentSetting,
            newValue: isCompliant ? null : _expectedSetting,
            detail: isCompliant
                ? "Already compliant"
                : $"Non-compliant: current='{currentSetting}', expected='{_expectedSetting}'");
    }

    public override async Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct)
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
            return BuildResult(
                success: false,
                changed: false,
                previousValue: testResult.PreviousValue,
                errorMessage: $"Unsupported audit policy setting '{_expectedSetting}'.");
        }

        await RunAuditPolAsync(setArguments, ct).ConfigureAwait(false);

        return BuildResult(
            success: true,
            changed: true,
            previousValue: testResult.PreviousValue,
            newValue: _expectedSetting,
            detail: $"Configured audit policy '{_subcategory}' to '{_expectedSetting}'");
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

    private Task<string?> RunAuditPolAsync(string arguments, CancellationToken ct)
      => RunProcessAsync("auditpol.exe", arguments, "auditpol.exe command failed", ct);
}
