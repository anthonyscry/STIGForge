using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Security;

public sealed class WdacPolicyService : ISecurityFeatureService
{
    private readonly IProcessRunner? _processRunner;
    private readonly ILogger<WdacPolicyService>? _logger;

    public WdacPolicyService(IProcessRunner? processRunner = null, ILogger<WdacPolicyService>? logger = null)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public string FeatureName => "WDAC";

    public async Task<SecurityFeatureStatus> GetStatusAsync(CancellationToken ct)
    {
        var status = new SecurityFeatureStatus
        {
            FeatureName = FeatureName,
            CheckedAt = DateTimeOffset.UtcNow
        };

        if (_processRunner == null)
        {
            status.IsEnabled = false;
            status.CurrentState = "Unknown (no process runner)";
            return status;
        }

        try
        {
            const string script = "Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\\Microsoft\\Windows\\DeviceGuard | Select-Object -ExpandProperty CodeIntegrityPolicyEnforcementStatus";
            var enforcementStatus = (await RunPowerShellAsync(script, ct).ConfigureAwait(false))?.Trim();

            status.IsEnabled = enforcementStatus is "1" or "2";
            status.CurrentState = enforcementStatus switch
            {
                "0" => "Off",
                "1" => "Audit",
                "2" => "Enforced",
                _ => $"Unknown ({enforcementStatus})"
            };
            status.Detail = "Queried Win32_DeviceGuard.CodeIntegrityPolicyEnforcementStatus";
        }
        catch (Exception ex)
        {
            status.IsEnabled = false;
            status.CurrentState = "Error";
            status.Detail = ex.Message;
        }

        return status;
    }

    public async Task<SecurityFeatureResult> TestAsync(SecurityFeatureRequest request, CancellationToken ct)
    {
        var status = await GetStatusAsync(ct).ConfigureAwait(false);
        var expectedState = request.Mode == HardeningMode.Full ? "Enforced" : "Audit";
        var alreadyInExpectedState = string.Equals(status.CurrentState, expectedState, StringComparison.OrdinalIgnoreCase);

        return new SecurityFeatureResult
        {
            FeatureName = FeatureName,
            Success = !string.Equals(status.CurrentState, "Error", StringComparison.OrdinalIgnoreCase),
            Changed = false,
            PreviousState = status.CurrentState,
            NewState = alreadyInExpectedState ? null : expectedState,
            Detail = alreadyInExpectedState
                ? $"WDAC is already in {expectedState} mode"
                : $"WDAC is in {status.CurrentState} mode, expected {expectedState}",
            Diagnostics = status.Detail == null ? Array.Empty<string>() : new[] { status.Detail }
        };
    }

    public async Task<SecurityFeatureResult> ApplyAsync(SecurityFeatureRequest request, CancellationToken ct)
    {
        if (request.DryRun)
            return await TestAsync(request, ct).ConfigureAwait(false);

        var testResult = await TestAsync(request, ct).ConfigureAwait(false);
        if (testResult.NewState == null)
            return testResult;

        if (_processRunner == null)
        {
            return new SecurityFeatureResult
            {
                FeatureName = FeatureName,
                Success = false,
                Changed = false,
                PreviousState = testResult.PreviousState,
                NewState = testResult.NewState,
                ErrorMessage = "Cannot apply WDAC policy without process runner"
            };
        }

        var policyPath = request.ConfigPath;
        if (string.IsNullOrWhiteSpace(policyPath))
            policyPath = Path.Combine(request.BundleRoot, "Apply", "Security", "wdac-policy.xml");

        try
        {
            var enforce = request.Mode == HardeningMode.Full;
            var modeLabel = enforce ? "Enforced" : "Audit";
            var option = enforce ? 0 : 3;

            var script = $"$p='{EscapePowerShellLiteral(policyPath)}'; if (!(Test-Path $p)) {{ throw 'WDAC policy not found: ' + $p }}; "
                + "$bin=[System.IO.Path]::ChangeExtension($p,'cip'); "
                + "Set-RuleOption -FilePath $p -Option " + option + "; "
                + "ConvertFrom-CIPolicy -XmlFilePath $p -BinaryFilePath $bin; "
                + "& CiTool.exe --update-policy $bin --json";

            _logger?.LogInformation("Deploying WDAC policy in {Mode} mode from {PolicyPath}", modeLabel, policyPath);
            var output = await RunPowerShellAsync(script, ct).ConfigureAwait(false);

            return new SecurityFeatureResult
            {
                FeatureName = FeatureName,
                Success = true,
                Changed = true,
                PreviousState = testResult.PreviousState,
                NewState = modeLabel,
                Detail = $"WDAC policy deployed in {modeLabel} mode",
                Diagnostics = string.IsNullOrWhiteSpace(output) ? Array.Empty<string>() : new[] { output! }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to deploy WDAC policy");
            return new SecurityFeatureResult
            {
                FeatureName = FeatureName,
                Success = false,
                Changed = false,
                PreviousState = testResult.PreviousState,
                NewState = testResult.NewState,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<string?> RunPowerShellAsync(string script, CancellationToken ct)
    {
        if (_processRunner == null)
            return null;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var result = await _processRunner.RunAsync(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded
        }, ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "PowerShell command failed" : result.StandardError);

        return result.StandardOutput;
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
