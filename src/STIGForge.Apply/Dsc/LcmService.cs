using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace STIGForge.Apply.Dsc;

public sealed class LcmService
{
    private readonly ILogger<LcmService> _logger;

    public LcmService(ILogger<LcmService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Configure Local Configuration Manager (LCM) for DSC application.
    /// </summary>
    public async Task ConfigureLcm(LcmConfig config, CancellationToken ct)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        _logger.LogInformation("Configuring Local Configuration Manager (LCM)...");
        _logger.LogInformation("  ConfigurationMode: {ConfigurationMode}", config.ConfigurationMode);
        _logger.LogInformation("  RebootNodeIfNeeded: {RebootNodeIfNeeded}", config.RebootNodeIfNeeded);
        _logger.LogInformation("  ConfigurationModeFrequencyMins: {ConfigurationModeFrequencyMins}", config.ConfigurationModeFrequencyMins);
        _logger.LogInformation("  AllowModuleOverwrite: {AllowModuleOverwrite}", config.AllowModuleOverwrite);

        // Create temp directory for LCM configuration MOF
        var tempDir = Path.Combine(Path.GetTempPath(), "stigforge_lcm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Generate LCM configuration MOF content
            var mofContent = GenerateLcmMof(config);
            var mofPath = Path.Combine(tempDir, "LCMConfig.mof");
            File.WriteAllText(mofPath, mofContent);

            // Build PowerShell command to configure LCM
            var command = $"Set-DscLocalConfigurationManager -Path \"{mofPath}\" -Force";

            _logger.LogDebug("Executing LCM configuration command: {Command}", command);

            // Execute PowerShell command
            var (exitCode, stdout, stderr) = await ExecutePowerShellCommand(command, ct);

            if (exitCode != 0)
            {
                _logger.LogError("LCM configuration failed. Exit code: {ExitCode}", exitCode);
                _logger.LogError("StdOut: {StdOut}", stdout);
                _logger.LogError("StdErr: {StdErr}", stderr);
                throw new LcmException($"LCM configuration failed with exit code {exitCode}. See logs for details.");
            }

            _logger.LogInformation("LCM configured successfully.");
        }
        finally
        {
            // Clean up temp files
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                    _logger.LogDebug("Cleaned up temp directory: {TempDir}", tempDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp directory: {TempDir}", tempDir);
                }
            }
        }
    }

    /// <summary>
    /// Get current LCM state.
    /// </summary>
    public async Task<LcmState> GetLcmState(CancellationToken ct)
    {
        _logger.LogInformation("Querying Local Configuration Manager (LCM) state...");

        var command = "Get-DscLocalConfigurationManager | ConvertTo-Json -Depth 3";

        _logger.LogDebug("Executing LCM query command: {Command}", command);

        var (exitCode, stdout, stderr) = await ExecutePowerShellCommand(command, ct);

        if (exitCode != 0)
        {
            _logger.LogError("LCM query failed. Exit code: {ExitCode}", exitCode);
            _logger.LogError("StdOut: {StdOut}", stdout);
            _logger.LogError("StdErr: {StdErr}", stderr);
            throw new LcmException($"LCM query failed with exit code {exitCode}.");
        }

        // Parse JSON output to LcmState
        LcmState? state;
        try
        {
            var jsonDoc = JsonDocument.Parse(stdout);
            var root = jsonDoc.RootElement;

            state = new LcmState
            {
                ConfigurationMode = TryGetString(root, "ConfigurationMode") ?? string.Empty,
                RebootNodeIfNeeded = TryGetBool(root, "RebootNodeIfNeeded"),
                ConfigurationModeFrequencyMins = TryGetInt(root, "ConfigurationModeFrequencyMins"),
                LCMState = TryGetString(root, "LCMState") ?? string.Empty
            };

            _logger.LogInformation("LCM state retrieved:");
            _logger.LogInformation("  ConfigurationMode: {ConfigurationMode}", state.ConfigurationMode);
            _logger.LogInformation("  RebootNodeIfNeeded: {RebootNodeIfNeeded}", state.RebootNodeIfNeeded);
            _logger.LogInformation("  ConfigurationModeFrequencyMins: {ConfigurationModeFrequencyMins}", state.ConfigurationModeFrequencyMins);
            _logger.LogInformation("  LCMState: {LCMState}", state.LCMState);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LCM state JSON output");
            _logger.LogError("Raw output: {StdOut}", stdout);
            throw new LcmException("Failed to parse LCM state from PowerShell output.", ex);
        }

        return state;
    }

    /// <summary>
    /// Reset LCM to original settings.
    /// </summary>
    public async Task ResetLcm(LcmState originalState, CancellationToken ct)
    {
        if (originalState == null)
            throw new ArgumentNullException(nameof(originalState));

        _logger.LogInformation("Resetting Local Configuration Manager (LCM) to original settings...");
        _logger.LogInformation("  Original ConfigurationMode: {ConfigurationMode}", originalState.ConfigurationMode);
        _logger.LogInformation("  Original RebootNodeIfNeeded: {RebootNodeIfNeeded}", originalState.RebootNodeIfNeeded);
        _logger.LogInformation("  Original ConfigurationModeFrequencyMins: {ConfigurationModeFrequencyMins}", originalState.ConfigurationModeFrequencyMins);

        try
        {
            var config = new LcmConfig
            {
                ConfigurationMode = originalState.ConfigurationMode,
                RebootNodeIfNeeded = originalState.RebootNodeIfNeeded,
                ConfigurationModeFrequencyMins = originalState.ConfigurationModeFrequencyMins,
                AllowModuleOverwrite = true
            };

            await ConfigureLcm(config, ct);

            _logger.LogInformation("LCM reset successfully.");
        }
        catch (LcmException ex)
        {
            _logger.LogError(ex, "Failed to reset LCM. Original state may not be restored.");
            _logger.LogWarning("This is a non-critical failure. Manual reset may be required.");
            // Don't throw - reset is non-critical
        }
    }

    private static string GenerateLcmMof(LcmConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("configuration LCMConfig");
        sb.AppendLine("{");
        sb.AppendLine("    Settings");
        sb.AppendLine("    {");
        sb.AppendLine($"        ConfigurationMode = '{config.ConfigurationMode}'");
        sb.AppendLine($"        RebootNodeIfNeeded = ${config.RebootNodeIfNeeded.ToString().ToLowerInvariant()}");
        sb.AppendLine($"        ConfigurationModeFrequencyMins = {config.ConfigurationModeFrequencyMins}");
        sb.AppendLine($"        AllowModuleOverwrite = ${config.AllowModuleOverwrite.ToString().ToLowerInvariant()}");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("LCMConfig");
        return sb.ToString();
    }

    private static async Task<(int exitCode, string stdout, string stderr)> ExecutePowerShellCommand(
        string command,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start PowerShell process.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask);
        process.WaitForExit();

        return (process.ExitCode, outputTask.Result, errorTask.Result);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
            return prop.GetString();
        return null;
    }

    private static bool TryGetBool(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
            return prop.GetBoolean();
        return false;
    }

    private static int TryGetInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
            return prop.GetInt32();
        return 0;
    }
}

public sealed class LcmException : Exception
{
    public LcmException(string message) : base(message) { }
    public LcmException(string message, Exception innerException) : base(message, innerException) { }
}
