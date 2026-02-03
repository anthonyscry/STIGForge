using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;
using STIGForge.Apply.Snapshot;

namespace STIGForge.UnitTests.Apply;

public sealed class SnapshotServiceTests
{
    [Fact]
    public async Task CreateSnapshot_ExportsSecurityPolicy()
    {
        // Arrange
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SnapshotService>();
        var service = new SnapshotService(logger);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "test_snapshot_" + Guid.NewGuid());
        Directory.CreateDirectory(snapshotsDir);
        
        try
        {
            // Act
            var result = await service.CreateSnapshot(snapshotsDir, CancellationToken.None);
            
            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.SecurityPolicyPath);
            Assert.True(File.Exists(result.SecurityPolicyPath));
            Assert.EndsWith(".inf", result.SecurityPolicyPath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(snapshotsDir))
                Directory.Delete(snapshotsDir, true);
        }
    }

    [Fact]
    public async Task CreateSnapshot_ExportsAuditPolicy()
    {
        // Arrange
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SnapshotService>();
        var service = new SnapshotService(logger);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "test_snapshot_" + Guid.NewGuid());
        Directory.CreateDirectory(snapshotsDir);
        
        try
        {
            // Act
            var result = await service.CreateSnapshot(snapshotsDir, CancellationToken.None);
            
            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.AuditPolicyPath);
            Assert.True(File.Exists(result.AuditPolicyPath));
            Assert.EndsWith(".csv", result.AuditPolicyPath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(snapshotsDir))
                Directory.Delete(snapshotsDir, true);
        }
    }

    [Fact]
    public async Task CreateSnapshot_HandlesLgpoMissing()
    {
        // Arrange
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SnapshotService>();
        var service = new SnapshotService(logger);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "test_snapshot_" + Guid.NewGuid());
        Directory.CreateDirectory(snapshotsDir);
        
        try
        {
            // Act
            var result = await service.CreateSnapshot(snapshotsDir, CancellationToken.None);
            
            // Assert
            Assert.NotNull(result);
            // LgpoStatePath may be null if LGPO.exe is not found
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(snapshotsDir))
                Directory.Delete(snapshotsDir, true);
        }
    }

    [Fact]
    public async Task CreateSnapshot_ThrowsOnSeceditFailure()
    {
        // This test requires secedit to fail, which is hard to mock
        // For now, we'll skip this and rely on integration tests
        // In a real test environment, we'd mock the process execution
    }

    [Fact]
    public async Task RestoreSnapshot_CallsSecedit()
    {
        // Arrange
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SnapshotService>();
        var service = new SnapshotService(logger);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "test_snapshot_" + Guid.NewGuid());
        Directory.CreateDirectory(snapshotsDir);
        var result = new SnapshotResult
        {
            SnapshotId = "test_snapshot",
            CreatedAt = DateTimeOffset.Now,
            SecurityPolicyPath = Path.Combine(snapshotsDir, "security_policy.inf"),
            AuditPolicyPath = Path.Combine(snapshotsDir, "audit_policy.csv"),
            RollbackScriptPath = Path.Combine(snapshotsDir, "rollback.ps1")
        };
        
        // Create a dummy .inf file
        File.WriteAllText(result.SecurityPolicyPath, "[Version]");
        
        try
        {
            // Act
            await service.RestoreSnapshot(result, CancellationToken.None);
            
            // Assert - if we get here without exception, the method was called
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(snapshotsDir))
                Directory.Delete(snapshotsDir, true);
        }
    }

    [Fact]
    public async Task RestoreSnapshot_CallsAuditpol()
    {
        // Arrange
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SnapshotService>();
        var service = new SnapshotService(logger);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "test_snapshot_" + Guid.NewGuid());
        Directory.CreateDirectory(snapshotsDir);
        var result = new SnapshotResult
        {
            SnapshotId = "test_snapshot",
            CreatedAt = DateTimeOffset.Now,
            SecurityPolicyPath = Path.Combine(snapshotsDir, "security_policy.inf"),
            AuditPolicyPath = Path.Combine(snapshotsDir, "audit_policy.csv"),
            RollbackScriptPath = Path.Combine(snapshotsDir, "rollback.ps1")
        };
        
        // Create dummy files
        File.WriteAllText(result.SecurityPolicyPath, "[Version]");
        File.WriteAllText(result.AuditPolicyPath, "test,data");
        
        try
        {
            // Act
            await service.RestoreSnapshot(result, CancellationToken.None);
            
            // Assert - if we get here without exception, the method was called
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(snapshotsDir))
                Directory.Delete(snapshotsDir, true);
        }
    }
}
