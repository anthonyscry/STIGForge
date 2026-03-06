using STIGForge.Apply.Steps;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public sealed class ServiceRemediationHandler : RemediationHandlerBase
{
    private readonly string _serviceName;
    private readonly string _expectedStartType;
    private readonly string _expectedStatus;

    public ServiceRemediationHandler(
        string ruleId,
        string serviceName,
        string expectedStartType,
        string expectedStatus,
        string description,
        IProcessRunner? processRunner = null)
        : base(ruleId, "Service", description, processRunner)
    {
        _serviceName = serviceName;
        _expectedStartType = expectedStartType;
        _expectedStatus = expectedStatus;
    }

    public override async Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
        var qSvc = ApplyProcessHelpers.ToPowerShellSingleQuoted(_serviceName);
        var script = $@"
$service = Get-Service -Name {qSvc} -ErrorAction SilentlyContinue
if ($null -eq $service) {{ '' }}
else {{
    $svcName = {qSvc}
    $startMode = (Get-CimInstance -ClassName Win32_Service -Filter ""Name='$svcName'"" -ErrorAction SilentlyContinue).StartMode
    ""$startMode|$($service.Status)""
}}";

        var output = await RunPowerShellAsync(script, "PowerShell service command failed", ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return BuildResult(
                success: false,
                changed: false,
                detail: "Unable to test service compliance",
                errorMessage: $"Service '{_serviceName}' was not found.");
        }

        var parts = output.Split('|', 2, StringSplitOptions.TrimEntries);
        var currentStartType = parts.Length > 0 ? NormalizeStartType(parts[0]) : string.Empty;
        var currentStatus = parts.Length > 1 ? NormalizeStatus(parts[1]) : string.Empty;
        var expectedStartType = NormalizeStartType(_expectedStartType);
        var expectedStatus = NormalizeStatus(_expectedStatus);
        var isCompliant = string.Equals(currentStartType, expectedStartType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentStatus, expectedStatus, StringComparison.OrdinalIgnoreCase);

        return BuildResult(
            success: true,
            changed: false,
            previousValue: $"StartType={currentStartType};Status={currentStatus}",
            newValue: isCompliant ? null : $"StartType={expectedStartType};Status={expectedStatus}",
            detail: isCompliant
                ? "Already compliant"
                : $"Non-compliant: current StartType={currentStartType}, Status={currentStatus}; expected StartType={expectedStartType}, Status={expectedStatus}");
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

        var expectedValue = $"StartType={NormalizeStartType(_expectedStartType)};Status={NormalizeStatus(_expectedStatus)}";
        if (string.Equals(testResult.PreviousValue, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            return testResult;
        }

        var script = BuildApplyScript();
        await RunPowerShellAsync(script, "PowerShell service command failed", ct).ConfigureAwait(false);

        return BuildResult(
            success: true,
            changed: true,
            previousValue: testResult.PreviousValue,
            newValue: expectedValue,
            detail: $"Configured service '{_serviceName}' with StartType={NormalizeStartType(_expectedStartType)} and Status={NormalizeStatus(_expectedStatus)}");
    }

    private string BuildApplyScript()
    {
        var startType = NormalizeStartType(_expectedStartType);
        var status = NormalizeStatus(_expectedStatus);
        var qSvc = ApplyProcessHelpers.ToPowerShellSingleQuoted(_serviceName);
        var statusScript = status.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
            ? $"if ($service.Status -ne 'Stopped') {{ Stop-Service -Name {qSvc} -Force -ErrorAction Stop }}"
            : $"if ($service.Status -ne 'Running') {{ Start-Service -Name {qSvc} -ErrorAction Stop }}";

        return $@"
$service = Get-Service -Name {qSvc} -ErrorAction Stop
Set-Service -Name {qSvc} -StartupType {ApplyProcessHelpers.ToPowerShellSingleQuoted(startType)} -ErrorAction Stop
{statusScript}";
    }

    private static string NormalizeStartType(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => "Automatic",
            "automatic" => "Automatic",
            "automaticdelayedstart" => "Automatic",
            "manual" => "Manual",
            "disabled" => "Disabled",
            _ => value.Trim()
        };
    }

    private static string NormalizeStatus(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "running" => "Running",
            "stopped" => "Stopped",
            _ => value.Trim()
        };
    }

}
