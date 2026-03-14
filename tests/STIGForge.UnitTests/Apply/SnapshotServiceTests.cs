using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using STIGForge.Apply.Snapshot;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Apply;

public sealed class SnapshotServiceTests
{
    private readonly Mock<ILogger<SnapshotService>> _loggerMock;
    private readonly Mock<IProcessRunner> _processRunnerMock;
    private readonly SnapshotService _service;
    private readonly string _snapshotsDir;

    public SnapshotServiceTests()
    {
        _loggerMock = new Mock<ILogger<SnapshotService>>();
        _processRunnerMock = new Mock<IProcessRunner>();
        _service = new SnapshotService(_loggerMock.Object, _processRunnerMock.Object);
        _snapshotsDir = Path.Combine(Path.GetTempPath(), "test_snapshot_" + Guid.NewGuid());
        Directory.CreateDirectory(_snapshotsDir);
    }

    [Fact]
    public async Task CreateSnapshot_ExportsSecurityPolicy()
    {
        // Arrange
        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "secedit.exe" && p.ArgumentList.Contains("/export")), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 })
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => 
            {
                var path = ExtractPathFromArgList(psi, "/cfg");
                if (path != null) File.WriteAllText(path, "[Version]");
            });

        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "auditpol.exe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 })
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => 
            {
                var path = ExtractFileArgFromArgList(psi);
                if (path != null) File.WriteAllText(path, "test,data");
            });

        // Act
        var result = await _service.CreateSnapshot(_snapshotsDir, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.SecurityPolicyPath);
        Assert.True(File.Exists(result.SecurityPolicyPath));
        Assert.EndsWith(".inf", result.SecurityPolicyPath);
        
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
            p.FileName == "secedit.exe" && p.ArgumentList.Contains("/export")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSnapshot_ExportsAuditPolicy()
    {
        // Arrange
        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "secedit.exe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 })
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => 
            {
                var path = ExtractPathFromArgList(psi, "/cfg");
                if (path != null) File.WriteAllText(path, "[Version]");
            });

        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "auditpol.exe" && p.ArgumentList.Contains("/backup")), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 })
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => 
            {
                var path = ExtractFileArgFromArgList(psi);
                if (path != null) File.WriteAllText(path, "test,data");
            });

        // Act
        var result = await _service.CreateSnapshot(_snapshotsDir, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.AuditPolicyPath);
        Assert.True(File.Exists(result.AuditPolicyPath));
        Assert.EndsWith(".csv", result.AuditPolicyPath);

        _processRunnerMock.Verify(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
            p.FileName == "auditpol.exe" && p.ArgumentList.Contains("/backup")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSnapshot_HandlesLgpoMissing()
    {
        // Arrange
        _processRunnerMock.Setup(x => x.ExistsInPath("LGPO.exe")).Returns(false);
        
        // Mock successful secedit/auditpol
        MockSuccessfulExports();

        // Act
        var result = await _service.CreateSnapshot(_snapshotsDir, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.LgpoStatePath);
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
            p.FileName == "LGPO.exe"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSnapshot_ThrowsOnSeceditFailure()
    {
        // Arrange
        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "secedit.exe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 1, StandardError = "Error exporting" });

        // Act & Assert
        await Assert.ThrowsAsync<SnapshotException>(() => 
            _service.CreateSnapshot(_snapshotsDir, CancellationToken.None));
    }

    [Fact]
    public async Task RestoreSnapshot_CallsSecedit()
    {
        // Arrange
        var result = CreateDummySnapshotResult();
        _processRunnerMock.Setup(x => x.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 });

        // Act
        await _service.RestoreSnapshot(result, CancellationToken.None);

        // Assert
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
            p.FileName == "secedit.exe" && p.ArgumentList.Contains("/configure")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreSnapshot_CallsAuditpol()
    {
        // Arrange
        var result = CreateDummySnapshotResult();
        _processRunnerMock.Setup(x => x.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 });

        // Act
        await _service.RestoreSnapshot(result, CancellationToken.None);

        // Assert
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
            p.FileName == "auditpol.exe" && p.ArgumentList.Contains("/restore")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void MockSuccessfulExports()
    {
        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "secedit.exe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 })
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => 
            {
                var path = ExtractPathFromArgList(psi, "/cfg");
                if (path != null) File.WriteAllText(path, "[Version]");
            });

        _processRunnerMock.Setup(x => x.RunAsync(It.Is<ProcessStartInfo>(p => 
                p.FileName == "auditpol.exe"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 })
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) => 
            {
                var path = ExtractFileArgFromArgList(psi);
                if (path != null) File.WriteAllText(path, "test,data");
            });
    }

    private SnapshotResult CreateDummySnapshotResult()
    {
        var result = new SnapshotResult
        {
            SnapshotId = "test_snapshot",
            CreatedAt = DateTimeOffset.Now,
            SecurityPolicyPath = Path.Combine(_snapshotsDir, "security_policy.inf"),
            AuditPolicyPath = Path.Combine(_snapshotsDir, "audit_policy.csv"),
            RollbackScriptPath = Path.Combine(_snapshotsDir, "rollback.ps1")
        };
        
        File.WriteAllText(result.SecurityPolicyPath, "[Version]");
        File.WriteAllText(result.AuditPolicyPath, "test,data");
        
        return result;
    }

    // Extract the value after a named switch from ArgumentList (e.g. "/cfg" → next element)
    private static string? ExtractPathFromArgList(ProcessStartInfo psi, string switchName)
    {
        var idx = psi.ArgumentList.IndexOf(switchName);
        if (idx >= 0 && idx + 1 < psi.ArgumentList.Count)
            return psi.ArgumentList[idx + 1];
        return null;
    }

    // Extract path from a /file:<path> style entry in ArgumentList
    private static string? ExtractFileArgFromArgList(ProcessStartInfo psi)
    {
        var entry = psi.ArgumentList.FirstOrDefault(a => a.StartsWith("/file:", StringComparison.OrdinalIgnoreCase));
        return entry?.Substring("/file:".Length);
    }
}
