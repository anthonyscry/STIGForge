using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Security;

public sealed class BitLockerService : ISecurityFeatureService
{
    private readonly IProcessRunner? _processRunner;
    private readonly ILogger<BitLockerService>? _logger;

    public BitLockerService(IProcessRunner? processRunner = null, ILogger<BitLockerService>? logger = null)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public string FeatureName => "BitLocker";

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
            const string script = "Get-BitLockerVolume | Select-Object MountPoint, VolumeStatus, EncryptionPercentage, ProtectionStatus | ConvertTo-Json -Depth 3";
            var output = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
            var normalized = output?.Trim() ?? string.Empty;

            var enabled = normalized.Contains("\"ProtectionStatus\": 1", StringComparison.Ordinal)
                || normalized.Contains("\"ProtectionStatus\": \"On\"", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("FullyEncrypted", StringComparison.OrdinalIgnoreCase);

            status.IsEnabled = enabled;
            status.CurrentState = enabled ? "Enabled" : "Disabled";
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
        var expectedState = request.Mode == HardeningMode.AuditOnly ? "Disabled" : "Enabled";
        var alreadyExpected = string.Equals(status.CurrentState, expectedState, StringComparison.OrdinalIgnoreCase);

        return new SecurityFeatureResult
        {
            FeatureName = FeatureName,
            Success = !string.Equals(status.CurrentState, "Error", StringComparison.OrdinalIgnoreCase),
            Changed = false,
            PreviousState = status.CurrentState,
            NewState = alreadyExpected ? null : expectedState,
            Detail = alreadyExpected
                ? $"BitLocker is already {expectedState}"
                : $"BitLocker is {status.CurrentState}, expected {expectedState}",
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
                ErrorMessage = "Cannot apply BitLocker settings without process runner"
            };
        }

        try
        {
            if (request.Mode != HardeningMode.AuditOnly)
            {
                const string enableScript = "Enable-BitLocker -MountPoint 'C:' -EncryptionMethod XtsAes256 -TpmProtector -ErrorAction Stop";
                const string recoveryScript = "Add-BitLockerKeyProtector -MountPoint 'C:' -RecoveryPasswordProtector -ErrorAction Stop";

                _logger?.LogInformation("Enabling BitLocker with TPM protector");
                var enableOutput = await RunPowerShellAsync(enableScript, ct).ConfigureAwait(false);
                var recoveryOutput = await RunPowerShellAsync(recoveryScript, ct).ConfigureAwait(false);
                var diagnostics = new List<string>();
                if (!string.IsNullOrWhiteSpace(enableOutput))
                    diagnostics.Add(enableOutput);
                if (!string.IsNullOrWhiteSpace(recoveryOutput))
                    diagnostics.Add(recoveryOutput);

                return new SecurityFeatureResult
                {
                    FeatureName = FeatureName,
                    Success = true,
                    Changed = true,
                    PreviousState = testResult.PreviousState,
                    NewState = "Enabled",
                    Detail = "BitLocker enabled on C: with TPM and recovery protector",
                    Diagnostics = diagnostics
                };
            }

            return new SecurityFeatureResult
            {
                FeatureName = FeatureName,
                Success = true,
                Changed = false,
                PreviousState = testResult.PreviousState,
                NewState = "Disabled",
                Detail = "Audit-only mode does not enforce BitLocker state changes"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enforce BitLocker configuration");
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
