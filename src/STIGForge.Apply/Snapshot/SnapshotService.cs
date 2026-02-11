using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Snapshot;

public sealed class SnapshotService
{
    private readonly ILogger<SnapshotService> _logger;
    private readonly IProcessRunner _processRunner;

    public SnapshotService(ILogger<SnapshotService> logger, IProcessRunner processRunner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<SnapshotResult> CreateSnapshot(string snapshotsDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(snapshotsDir))
            throw new ArgumentException("Snapshots directory cannot be empty.", nameof(snapshotsDir));

        Directory.CreateDirectory(snapshotsDir);

        var snapshotId = $"snapshot_{DateTimeOffset.Now:yyyyMMdd_HHmmss}";
        var snapshotPath = Path.Combine(snapshotsDir, snapshotId);
        Directory.CreateDirectory(snapshotPath);

        _logger.LogInformation("Creating snapshot {SnapshotId}", snapshotId);

        var securityPolicyPath = Path.Combine(snapshotPath, "security_policy.inf");
        var auditPolicyPath = Path.Combine(snapshotPath, "audit_policy.csv");
        string? lgpoStatePath = null;

        try
        {
            // Export security policy
            _logger.LogInformation("Exporting security policy using secedit...");
            await RunProcessAsync("secedit.exe", $"/export /cfg \"{securityPolicyPath}\"", snapshotPath, ct).ConfigureAwait(false);
            
            if (!File.Exists(securityPolicyPath))
                throw new SnapshotException($"Security policy export failed: {securityPolicyPath} not created");

            _logger.LogInformation("Security policy exported to {Path}", securityPolicyPath);

            // Export audit policy
            _logger.LogInformation("Exporting audit policy using auditpol...");
            await RunProcessAsync("auditpol.exe", $"/backup /file:\"{auditPolicyPath}\"", snapshotPath, ct).ConfigureAwait(false);
            
            if (!File.Exists(auditPolicyPath))
                throw new SnapshotException($"Audit policy export failed: {auditPolicyPath} not created");

            _logger.LogInformation("Audit policy exported to {Path}", auditPolicyPath);

            // Export LGPO state (optional)
            if (_processRunner.ExistsInPath("LGPO.exe"))
            {
                _logger.LogInformation("Exporting LGPO state...");
                lgpoStatePath = Path.Combine(snapshotPath, "lgpo_state");
                try
                {
                    await RunProcessAsync("LGPO.exe", $"/backup \"{lgpoStatePath}\"", snapshotPath, ct).ConfigureAwait(false);
                    _logger.LogInformation("LGPO state exported to {Path}", lgpoStatePath);
                }
                catch (SnapshotException ex)
                {
                    _logger.LogWarning(ex, "LGPO export failed (continuing - LGPO is optional)");
                    lgpoStatePath = null;
                }
            }
            else
            {
                _logger.LogWarning("LGPO.exe not found in PATH - skipping LGPO state export");
            }
        }
        catch (Exception ex) when (ex is not SnapshotException)
        {
            throw new SnapshotException($"Snapshot creation failed: {ex.Message}", ex);
        }

        var result = new SnapshotResult
        {
            SnapshotId = snapshotId,
            CreatedAt = DateTimeOffset.Now,
            SecurityPolicyPath = securityPolicyPath,
            AuditPolicyPath = auditPolicyPath,
            LgpoStatePath = lgpoStatePath,
            RollbackScriptPath = string.Empty // Will be set by RollbackScriptGenerator
        };

        _logger.LogInformation("Snapshot {SnapshotId} created successfully", snapshotId);
        return result;
    }

    public async Task RestoreSnapshot(SnapshotResult snapshot, CancellationToken ct)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        _logger.LogInformation("Restoring snapshot {SnapshotId}", snapshot.SnapshotId);

        // Validate files exist
        if (!File.Exists(snapshot.SecurityPolicyPath))
            throw new SnapshotException($"Security policy file not found: {snapshot.SecurityPolicyPath}");

        if (!File.Exists(snapshot.AuditPolicyPath))
            throw new SnapshotException($"Audit policy file not found: {snapshot.AuditPolicyPath}");

        try
        {
            // Restore security policy
            _logger.LogInformation("Restoring security policy using secedit...");
            await RunProcessAsync("secedit.exe", 
                $"/configure /cfg \"{snapshot.SecurityPolicyPath}\" /db secedit.sdb /overwrite /quiet",
                Path.GetDirectoryName(snapshot.SecurityPolicyPath) ?? string.Empty, ct).ConfigureAwait(false);
            
            _logger.LogInformation("Security policy restored from {Path}", snapshot.SecurityPolicyPath);

            // Restore audit policy
            _logger.LogInformation("Restoring audit policy using auditpol...");
            await RunProcessAsync("auditpol.exe", 
                $"/restore /file:\"{snapshot.AuditPolicyPath}\"",
                Path.GetDirectoryName(snapshot.AuditPolicyPath) ?? string.Empty, ct).ConfigureAwait(false);
            
            _logger.LogInformation("Audit policy restored from {Path}", snapshot.AuditPolicyPath);

            // Restore LGPO state (optional)
            if (!string.IsNullOrWhiteSpace(snapshot.LgpoStatePath) && File.Exists(snapshot.LgpoStatePath))
            {
                _logger.LogInformation("Restoring LGPO state...");
                try
                {
                    await RunProcessAsync("LGPO.exe", 
                        $"/restore \"{snapshot.LgpoStatePath}\"",
                        Path.GetDirectoryName(snapshot.LgpoStatePath) ?? string.Empty, ct).ConfigureAwait(false);
                    _logger.LogInformation("LGPO state restored from {Path}", snapshot.LgpoStatePath);
                }
                catch (SnapshotException ex)
                {
                    _logger.LogWarning(ex, "LGPO restore failed (continuing - LGPO is optional)");
                }
            }
        }
        catch (Exception ex) when (ex is not SnapshotException)
        {
            throw new SnapshotException($"Snapshot restoration failed: {ex.Message}", ex);
        }

        _logger.LogInformation("Snapshot {SnapshotId} restored successfully. Reboot recommended.", snapshot.SnapshotId);
    }

    private async Task RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory
        };

        var result = await _processRunner.RunAsync(psi, ct).ConfigureAwait(false);

        // Log process output
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            _logger.LogDebug("{FileName} stdout: {Output}", fileName, result.StandardOutput);
        
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            _logger.LogDebug("{FileName} stderr: {Error}", fileName, result.StandardError);

        if (result.ExitCode != 0)
        {
            var errorMessage = $"Process '{fileName}' failed with exit code {result.ExitCode}";
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                errorMessage += $": {result.StandardError}";
            
            throw new SnapshotException(errorMessage);
        }
    }
}
