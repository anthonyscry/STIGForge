using STIGForge.Apply.Steps;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation.Handlers;

public sealed class RegistryRemediationHandler : RemediationHandlerBase
{
    private readonly string _registryPath;
    private readonly string _valueName;
    private readonly string _expectedValue;
    private readonly string _valueType;

    public RegistryRemediationHandler(
        string ruleId,
        string registryPath,
        string valueName,
        string expectedValue,
        string valueType,
        string description,
        IProcessRunner? processRunner = null)
        : base(ruleId, "Registry", description, processRunner)
    {
        _registryPath = registryPath;
        _valueName = valueName;
        _expectedValue = expectedValue;
        _valueType = valueType;
    }

    public override async Task<RemediationResult> TestAsync(RemediationContext context, CancellationToken ct)
    {
        var script = "(Get-ItemProperty -Path " + ApplyProcessHelpers.ToPowerShellSingleQuoted(_registryPath) + " -Name " + ApplyProcessHelpers.ToPowerShellSingleQuoted(_valueName) + " -ErrorAction SilentlyContinue)." + ApplyProcessHelpers.ToPowerShellSingleQuoted(_valueName);
        var currentValue = await RunPowerShellAsync(script, "PowerShell registry command failed", ct, returnNullWhenOutputEmpty: true).ConfigureAwait(false);
        var trimmedCurrent = currentValue?.Trim();
        var isCompliant = string.Equals(trimmedCurrent, _expectedValue, StringComparison.OrdinalIgnoreCase);

        return BuildResult(
            success: true,
            changed: false,
            previousValue: trimmedCurrent,
            newValue: isCompliant ? null : _expectedValue,
            detail: isCompliant
                ? "Already compliant"
                : $"Non-compliant: current='{trimmedCurrent}', expected='{_expectedValue}'");
    }

    public override async Task<RemediationResult> ApplyAsync(RemediationContext context, CancellationToken ct)
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

        var qPath = ApplyProcessHelpers.ToPowerShellSingleQuoted(_registryPath);
        var qName = ApplyProcessHelpers.ToPowerShellSingleQuoted(_valueName);
        var qValue = ApplyProcessHelpers.ToPowerShellSingleQuoted(_expectedValue);
        var qType = ApplyProcessHelpers.ToPowerShellSingleQuoted(_valueType);
        var script = $@"
if (-not (Test-Path {qPath})) {{ New-Item -Path {qPath} -Force | Out-Null }}
Set-ItemProperty -Path {qPath} -Name {qName} -Value {qValue} -Type {qType} -Force";

        await RunPowerShellAsync(script, "PowerShell registry command failed", ct, returnNullWhenOutputEmpty: true).ConfigureAwait(false);

        return BuildResult(
            success: true,
            changed: true,
            previousValue: testResult.PreviousValue,
            newValue: _expectedValue,
            detail: $"Set {_registryPath}\\{_valueName} to {_expectedValue}");
    }
}
