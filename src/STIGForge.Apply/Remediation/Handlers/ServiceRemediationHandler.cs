using System.Diagnostics;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public sealed class ServiceRemediationHandler : IRemediationHandler
{
    private readonly string _ruleId;
    private readonly string _serviceName;
    private readonly string _expectedStartType;
    private readonly string _expectedStatus;
    private readonly string _description;
    private readonly IProcessRunner? _processRunner;

    public ServiceRemediationHandler(
        string ruleId,
        string serviceName,
        string expectedStartType,
        string expectedStatus,
        string description,
        IProcessRunner? processRunner = null)
    {
        _ruleId = ruleId;
        _serviceName = serviceName;
        _expectedStartType = expectedStartType;
        _expectedStatus = expectedStatus;
        _description = description;
        _processRunner = processRunner;
    }

    public string RuleId => _ruleId;
    public string Category => "Service";
    public string Description => _description;

    public async Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
        var script = $@"
$service = Get-Service -Name '{_serviceName}' -ErrorAction SilentlyContinue
if ($null -eq $service) {{ '' }}
else {{
    $startMode = (Get-CimInstance -ClassName Win32_Service -Filter ""Name='{_serviceName}'"" -ErrorAction SilentlyContinue).StartMode
    ""$startMode|$($service.Status)""
}}";

        var output = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new RemediationResult
            {
                RuleId = _ruleId,
                HandlerCategory = Category,
                Success = false,
                Changed = false,
                ErrorMessage = $"Service '{_serviceName}' was not found.",
                Detail = "Unable to test service compliance"
            };
        }

        var parts = output.Split('|', 2, StringSplitOptions.TrimEntries);
        var currentStartType = parts.Length > 0 ? NormalizeStartType(parts[0]) : string.Empty;
        var currentStatus = parts.Length > 1 ? NormalizeStatus(parts[1]) : string.Empty;
        var expectedStartType = NormalizeStartType(_expectedStartType);
        var expectedStatus = NormalizeStatus(_expectedStatus);
        var isCompliant = string.Equals(currentStartType, expectedStartType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentStatus, expectedStatus, StringComparison.OrdinalIgnoreCase);

        return new RemediationResult
        {
            RuleId = _ruleId,
            HandlerCategory = Category,
            Success = true,
            Changed = false,
            PreviousValue = $"StartType={currentStartType};Status={currentStatus}",
            NewValue = isCompliant ? null : $"StartType={expectedStartType};Status={expectedStatus}",
            Detail = isCompliant
                ? "Already compliant"
                : $"Non-compliant: current StartType={currentStartType}, Status={currentStatus}; expected StartType={expectedStartType}, Status={expectedStatus}"
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

        var expectedValue = $"StartType={NormalizeStartType(_expectedStartType)};Status={NormalizeStatus(_expectedStatus)}";
        if (string.Equals(testResult.PreviousValue, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            return testResult;
        }

        var script = BuildApplyScript();
        await RunPowerShellAsync(script, ct).ConfigureAwait(false);

        return new RemediationResult
        {
            RuleId = _ruleId,
            HandlerCategory = Category,
            Success = true,
            Changed = true,
            PreviousValue = testResult.PreviousValue,
            NewValue = expectedValue,
            Detail = $"Configured service '{_serviceName}' with StartType={NormalizeStartType(_expectedStartType)} and Status={NormalizeStatus(_expectedStatus)}"
        };
    }

    private string BuildApplyScript()
    {
        var startType = NormalizeStartType(_expectedStartType);
        var status = NormalizeStatus(_expectedStatus);
        var statusScript = status.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
            ? $"if ($service.Status -ne 'Stopped') {{ Stop-Service -Name '{_serviceName}' -Force -ErrorAction Stop }}"
            : $"if ($service.Status -ne 'Running') {{ Start-Service -Name '{_serviceName}' -ErrorAction Stop }}";

        return $@"
$service = Get-Service -Name '{_serviceName}' -ErrorAction Stop
Set-Service -Name '{_serviceName}' -StartupType {startType} -ErrorAction Stop
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

    private async Task<string?> RunPowerShellAsync(string script, CancellationToken ct)
    {
        if (_processRunner == null)
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

        var result = await _processRunner.RunAsync(startInfo, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var stderr = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();
            throw new InvalidOperationException($"PowerShell service command failed: {stderr}");
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError.Trim()
            : result.StandardOutput.Trim();
    }
}
