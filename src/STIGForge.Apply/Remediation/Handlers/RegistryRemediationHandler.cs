using System.Diagnostics;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public sealed class RegistryRemediationHandler : IRemediationHandler
{
    private readonly string _ruleId;
    private readonly string _registryPath;
    private readonly string _valueName;
    private readonly string _expectedValue;
    private readonly string _valueType;
    private readonly string _description;
    private readonly IProcessRunner? _processRunner;

    public RegistryRemediationHandler(
        string ruleId,
        string registryPath,
        string valueName,
        string expectedValue,
        string valueType,
        string description,
        IProcessRunner? processRunner = null)
    {
        _ruleId = ruleId;
        _registryPath = registryPath;
        _valueName = valueName;
        _expectedValue = expectedValue;
        _valueType = valueType;
        _description = description;
        _processRunner = processRunner;
    }

    public string RuleId => _ruleId;
    public string Category => "Registry";
    public string Description => _description;

    public async Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
        var script = $"(Get-ItemProperty -Path '{_registryPath}' -Name '{_valueName}' -ErrorAction SilentlyContinue).'{_valueName}'";
        var currentValue = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
        var trimmedCurrent = currentValue?.Trim();
        var isCompliant = string.Equals(trimmedCurrent, _expectedValue, StringComparison.OrdinalIgnoreCase);

        return new RemediationResult
        {
            RuleId = _ruleId,
            HandlerCategory = Category,
            Success = true,
            Changed = false,
            PreviousValue = trimmedCurrent,
            NewValue = isCompliant ? null : _expectedValue,
            Detail = isCompliant
                ? "Already compliant"
                : $"Non-compliant: current='{trimmedCurrent}', expected='{_expectedValue}'"
        };
    }

    public async Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct)
    {
        if (context.DryRun)
        {
            return await TestAsync(context, ct).ConfigureAwait(false);
        }

        var testResult = await TestAsync(context, ct).ConfigureAwait(false);
        if (string.Equals(testResult.PreviousValue, _expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            return testResult;
        }

        var script = $@"
if (-not (Test-Path '{_registryPath}')) {{ New-Item -Path '{_registryPath}' -Force | Out-Null }}
Set-ItemProperty -Path '{_registryPath}' -Name '{_valueName}' -Value '{_expectedValue}' -Type {_valueType} -Force";

        await RunPowerShellAsync(script, ct).ConfigureAwait(false);

        return new RemediationResult
        {
            RuleId = _ruleId,
            HandlerCategory = Category,
            Success = true,
            Changed = true,
            PreviousValue = testResult.PreviousValue,
            NewValue = _expectedValue,
            Detail = $"Set {_registryPath}\\{_valueName} to {_expectedValue}"
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
            throw new InvalidOperationException($"PowerShell registry command failed: {stderr}");
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput.Trim();
        }

        return string.IsNullOrWhiteSpace(result.StandardError) ? null : result.StandardError.Trim();
    }
}
