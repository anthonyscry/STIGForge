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
        // Store name in PS variable and use .$n access to handle value names with spaces/special chars
        var qPath = ApplyProcessHelpers.ToPowerShellSingleQuoted(_registryPath);
        var qName = ApplyProcessHelpers.ToPowerShellSingleQuoted(_valueName);
        var script = $"$n = {qName}; $p = Get-ItemProperty -Path {qPath} -Name $n -ErrorAction SilentlyContinue; if ($null -ne $p) {{ $p.$n }}";
        var currentValue = await RunPowerShellAsync(script, "PowerShell registry command failed", ct, returnNullWhenOutputEmpty: true).ConfigureAwait(false);
        var trimmedCurrent = currentValue?.Trim();
        var isCompliant = string.Equals(trimmedCurrent, _expectedValue, StringComparison.OrdinalIgnoreCase);

        return BuildResult(
            success: true,
            changed: false,
            previousValue: trimmedCurrent,
            newValue: isCompliant ? null : _expectedValue,
            detail: isCompliant
                ? $"Already compliant ({_valueType})"
                : $"Non-compliant: current='{trimmedCurrent}', expected='{_expectedValue}' ({_valueType})");
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

        // Verify post-apply state to confirm the write took effect
        var verifyResult = await TestAsync(context, ct).ConfigureAwait(false);
        var verified = string.Equals(verifyResult.PreviousValue, _expectedValue, StringComparison.OrdinalIgnoreCase);

        return BuildResult(
            success: verified,
            changed: true,
            previousValue: testResult.PreviousValue,
            newValue: verifyResult.PreviousValue,
            detail: verified
                ? $"Set {_registryPath}\\{_valueName} to {_expectedValue}"
                : $"Set-ItemProperty completed but post-apply verify failed: current='{verifyResult.PreviousValue}', expected='{_expectedValue}'");
    }
}
