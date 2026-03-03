using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Security;

public sealed class FirewallRuleService : ISecurityFeatureService
{
    private readonly IProcessRunner? _processRunner;
    private readonly ILogger<FirewallRuleService>? _logger;

    public FirewallRuleService(IProcessRunner? processRunner = null, ILogger<FirewallRuleService>? logger = null)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public string FeatureName => "Firewall";

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
            const string script = "Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultInboundAction, DefaultOutboundAction | ConvertTo-Json -Depth 3";
            var output = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
            var normalized = output?.Trim() ?? string.Empty;

            var enabled = normalized.Contains("\"Enabled\": true", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("\"Enabled\": 1", StringComparison.OrdinalIgnoreCase);
            var inboundBlocked = normalized.Contains("\"DefaultInboundAction\": 4", StringComparison.Ordinal)
                || normalized.Contains("\"DefaultInboundAction\": \"Block\"", StringComparison.OrdinalIgnoreCase);

            status.IsEnabled = enabled;
            status.CurrentState = enabled && inboundBlocked ? "Enabled" : "NotCompliant";
            status.Detail = normalized;
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
        var expectedState = request.Mode == HardeningMode.AuditOnly ? "NotCompliant" : "Enabled";
        var matchesExpected = string.Equals(status.CurrentState, expectedState, StringComparison.OrdinalIgnoreCase);

        return new SecurityFeatureResult
        {
            FeatureName = FeatureName,
            Success = !string.Equals(status.CurrentState, "Error", StringComparison.OrdinalIgnoreCase),
            Changed = false,
            PreviousState = status.CurrentState,
            NewState = matchesExpected ? null : expectedState,
            Detail = matchesExpected
                ? $"Firewall is already in expected state ({expectedState})"
                : $"Firewall is {status.CurrentState}, expected {expectedState}",
            Diagnostics = status.Detail == null ? [] : new[] { status.Detail }
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
                ErrorMessage = "Cannot apply firewall settings without process runner"
            };
        }

        try
        {
            const string applyScript = "Set-NetFirewallProfile -All -Enabled True -DefaultInboundAction Block -DefaultOutboundAction Allow";
            _logger?.LogInformation("Applying Windows Firewall profile hardening");
            var output = await RunPowerShellAsync(applyScript, ct).ConfigureAwait(false);

            return new SecurityFeatureResult
            {
                FeatureName = FeatureName,
                Success = true,
                Changed = true,
                PreviousState = testResult.PreviousState,
                NewState = "Enabled",
                Detail = "Windows Firewall profiles enabled with inbound block and outbound allow",
                Diagnostics = string.IsNullOrWhiteSpace(output) ? [] : new[] { output! }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to configure Windows Firewall profiles");
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
}
