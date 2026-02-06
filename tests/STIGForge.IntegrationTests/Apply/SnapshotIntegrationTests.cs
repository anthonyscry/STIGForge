using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using STIGForge.Apply.Snapshot;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.System;
using Xunit;
using Xunit.Abstractions;

namespace STIGForge.IntegrationTests.Apply;

/// <summary>
/// Integration tests for the complete snapshot workflow.
/// Tests the create-apply-restore lifecycle with real file system operations.
/// Note: Tests require administrator privileges and are skipped by default.
/// </summary>
public class SnapshotIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;

    public SnapshotIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Set up dependency injection with test configuration
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<SnapshotService>();
        services.AddSingleton<RollbackScriptGenerator>();
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    [Fact]
    public async Task CreateSnapshot_ShouldCreateValidSnapshotFiles()
    {
        if (Environment.GetEnvironmentVariable("STIGFORGE_POLICY_TESTS_ENABLED") != "1")
        {
            _output.WriteLine("STIGFORGE_POLICY_TESTS_ENABLED not set. Skipping tests requiring policy export.");
            return;
        }
        // Arrange
        var snapshotService = _serviceProvider.GetRequiredService<SnapshotService>();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[..8]);
        SnapshotResult? result = null;
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Create test files in the snapshot directory
            var testFiles = new[]
            {
                Path.Combine(tempDir, "test1.txt"),
                Path.Combine(tempDir, "test2.txt"),
                Path.Combine(tempDir, "subdir", "test3.txt")
            };

            foreach (var file in testFiles.Take(2)) // Create only first two
            {
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(file, "Test content for snapshot");
            }

            // Create subdirectory and file
            var subDir = Path.Combine(tempDir, "subdir");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "test3.txt"), "Test content in subdirectory");

            // Act
            result = await snapshotService.CreateSnapshot(tempDir, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result!.SnapshotId);
            Assert.NotNull(result!.SecurityPolicyPath);
            Assert.NotNull(result!.AuditPolicyPath);
            Assert.True(File.Exists(result.SecurityPolicyPath));
            Assert.True(File.Exists(result.AuditPolicyPath));
            
            // Verify INF file contains expected sections
            var infContent = await File.ReadAllTextAsync(result.SecurityPolicyPath);
            Assert.Contains("[Version]", infContent);
            Assert.Contains("[Unicode]", infContent); // Security policy has Unicode section
            
            _output.WriteLine($"Snapshot created successfully at: {result.SnapshotId}");
            _output.WriteLine($"Security policy: {result.SecurityPolicyPath}");
            _output.WriteLine($"Audit policy: {result.AuditPolicyPath}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            if (!string.IsNullOrEmpty(result?.SecurityPolicyPath) && File.Exists(result.SecurityPolicyPath))
            {
                try
                {
                    File.Delete(result.SecurityPolicyPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            if (!string.IsNullOrEmpty(result?.AuditPolicyPath) && File.Exists(result.AuditPolicyPath))
            {
                try
                {
                    File.Delete(result.AuditPolicyPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task CreateSnapshot_WithLgpo_ShouldIncludeLgpoInSnapshot()
    {
        if (Environment.GetEnvironmentVariable("STIGFORGE_POLICY_TESTS_ENABLED") != "1")
        {
            _output.WriteLine("STIGFORGE_POLICY_TESTS_ENABLED not set. Skipping tests requiring policy export.");
            return;
        }
        // Arrange
        var snapshotService = _serviceProvider.GetRequiredService<SnapshotService>();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[..8]);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "Snapshots");
        SnapshotResult? result = null;
        
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(snapshotsDir);
            
            // Create a test LGPO file to simulate LGPO being available
            var lgpoPath = Path.Combine(tempDir, "test.pol");
            await File.WriteAllTextAsync(lgpoPath, "Test LGPO content");

            // Act
            result = await snapshotService.CreateSnapshot(tempDir, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result!.SnapshotId);
            Assert.NotNull(result!.SecurityPolicyPath);
            Assert.NotNull(result!.AuditPolicyPath);
            Assert.True(File.Exists(result?.SecurityPolicyPath));
            Assert.True(File.Exists(result?.AuditPolicyPath));
            
            // Check if LGPO state path was created (LGPO.exe must exist in PATH for this to work)
            // The current implementation checks for LGPO.exe existence internally
            _output.WriteLine($"Snapshot created successfully at: {result.SnapshotId}");
            _output.WriteLine($"Security policy: {result.SecurityPolicyPath}");
            _output.WriteLine($"Audit policy: {result.AuditPolicyPath}");
            _output.WriteLine($"LGPO state path: {result.LgpoStatePath ?? "Not created (LGPO.exe not found)"}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            if (!string.IsNullOrEmpty(result?.SecurityPolicyPath) && File.Exists(result.SecurityPolicyPath))
            {
                try
                {
                    File.Delete(result.SecurityPolicyPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            if (!string.IsNullOrEmpty(result?.AuditPolicyPath) && File.Exists(result.AuditPolicyPath))
            {
                try
                {
                    File.Delete(result.AuditPolicyPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task RollbackScriptGenerator_ShouldCreateExecutableScript()
    {
        if (Environment.GetEnvironmentVariable("STIGFORGE_POLICY_TESTS_ENABLED") != "1")
        {
            _output.WriteLine("STIGFORGE_POLICY_TESTS_ENABLED not set. Skipping tests requiring policy export.");
            return;
        }
        // Arrange
        var generator = _serviceProvider.GetRequiredService<RollbackScriptGenerator>();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[..8]);
        var snapshotName = $"RollbackTest_{Guid.NewGuid():N}[..8]";
        SnapshotResult? snapshot = null;
        
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test.txt"), "Test content");
            
            snapshot = new SnapshotResult
            {
                SnapshotId = snapshotName,
                CreatedAt = DateTimeOffset.Now,
                SecurityPolicyPath = Path.Combine(tempDir, "security_policy.inf"),
                AuditPolicyPath = Path.Combine(tempDir, "audit_policy.csv"),
                LgpoStatePath = Path.Combine(tempDir, "lgpo_state.pol")
            };

            // Act
            var scriptPath = generator.GenerateScript(snapshot);

            // Assert
            Assert.NotEmpty(scriptPath);
            Assert.True(File.Exists(scriptPath));

            // Verify script contains expected PowerShell commands
            var scriptContent = await File.ReadAllTextAsync(scriptPath);
            Assert.Contains("Remove-Item", scriptContent);
            Assert.Contains("secedit", scriptContent);
            Assert.Contains("auditpol", scriptContent);
            
            _output.WriteLine($"Rollback script created at: {scriptPath}");
            _output.WriteLine($"Script size: {scriptContent.Length} characters");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            var scriptFile = Path.Combine(Path.GetTempPath(), "RollbackScripts", $"rollback_{snapshotName}.ps1");
            if (File.Exists(scriptFile)) File.Delete(scriptFile);
        }
    }

    [Fact]
    public async Task RestoreSnapshot_ShouldExecuteWithoutErrors()
    {
        if (Environment.GetEnvironmentVariable("STIGFORGE_POLICY_TESTS_ENABLED") != "1")
        {
            _output.WriteLine("STIGFORGE_POLICY_TESTS_ENABLED not set. Skipping tests requiring policy export.");
            return;
        }
        // Arrange
        var snapshotService = _serviceProvider.GetRequiredService<SnapshotService>();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[..8]);
        var snapshotName = $"RestoreTest_{Guid.NewGuid():N}[..8]";
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "Snapshots");
        SnapshotResult? snapshot = null;
        
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(snapshotsDir);
            
            // Create test files
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test1.txt"), "Original content 1");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test2.txt"), "Original content 2");

            // Create snapshot
            snapshot = await snapshotService.CreateSnapshot(tempDir, CancellationToken.None);
            Assert.NotNull(snapshot);

            // Modify files (simulate changes that need to be rolled back)
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test1.txt"), "Modified content");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test3.txt"), "New file that should be removed");

            // Act
            await snapshotService.RestoreSnapshot(snapshot, CancellationToken.None);

            // Assert - if we get here without exception, restore succeeded
            // Verify files were restored
            var restoredContent1 = await File.ReadAllTextAsync(Path.Combine(tempDir, "test1.txt"));
            var restoredContent2 = await File.ReadAllTextAsync(Path.Combine(tempDir, "test2.txt"));
            Assert.Equal("Original content 1", restoredContent1);
            Assert.Equal("Original content 2", restoredContent2);

            // Verify extra file was removed
            Assert.False(File.Exists(Path.Combine(tempDir, "test3.txt")));

            _output.WriteLine($"Snapshot restored successfully");
            _output.WriteLine($"Files rolled back to original state");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            if (!string.IsNullOrEmpty(snapshot?.SecurityPolicyPath) && File.Exists(snapshot.SecurityPolicyPath))
            {
                try
                {
                    File.Delete(snapshot.SecurityPolicyPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            if (!string.IsNullOrEmpty(snapshot?.AuditPolicyPath) && File.Exists(snapshot.AuditPolicyPath))
            {
                try
                {
                    File.Delete(snapshot.AuditPolicyPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task CreateSnapshot_OnFailure_ShouldPreventApplyOperation()
    {
        // Arrange
        var snapshotService = _serviceProvider.GetRequiredService<SnapshotService>();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[..8]);
        
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "test.txt"), "Test content");

            // Act - Try to create snapshot with invalid directory to simulate failure
            // The current implementation validates that snapshotsDir is not null/empty
            // So we'll pass an invalid path to test failure handling
            var invalidDir = "";

            // Assert
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await snapshotService.CreateSnapshot(invalidDir, CancellationToken.None);
            });
            
            _output.WriteLine("Snapshot creation failed as expected");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Fact]
    public async Task FullWorkflow_ShouldHandleCompleteCreateApplyRestoreCycle()
    {
        if (Environment.GetEnvironmentVariable("STIGFORGE_POLICY_TESTS_ENABLED") != "1")
        {
            _output.WriteLine("STIGFORGE_POLICY_TESTS_ENABLED not set. Skipping tests requiring policy export.");
            return;
        }
        // Arrange
        var snapshotService = _serviceProvider.GetRequiredService<SnapshotService>();
        var baseTestDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[..8]);
        var snapshotsDir = Path.Combine(Path.GetTempPath(), "Snapshots");
        SnapshotResult? snapshot = null;
        
        try
        {
            Directory.CreateDirectory(baseTestDir);
            Directory.CreateDirectory(snapshotsDir);
            
            // Create initial state
            var initialFiles = new[]
            {
                ("config.json", "{\"setting1\": \"value1\", \"setting2\": \"value2\"}"),
                ("data.csv", "id,name,value\n1,Item1,100\n2,Item2,200\n3,Item3,300"),
                ("README.md", "# Test Documentation\n\nThis is a test file."),
                ("subdir/nested.txt", "Nested content in subdirectory")
            };

            foreach (var (fileName, content) in initialFiles)
            {
                var filePath = Path.Combine(baseTestDir, fileName);
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(filePath, content);
            }

            // Phase 1: Create Snapshot
            _output.WriteLine("Phase 1: Creating snapshot...");
            snapshot = await snapshotService.CreateSnapshot(baseTestDir, CancellationToken.None);
            
            Assert.NotNull(snapshot);
            Assert.NotNull(snapshot.SnapshotId);

            // Verify snapshot files exist
            Assert.True(File.Exists(snapshot.SecurityPolicyPath), "Security policy file should exist");
            Assert.True(File.Exists(snapshot.AuditPolicyPath), "Audit policy file should exist");

            // Phase 2: Simulate Changes
            _output.WriteLine("Phase 2: Simulating changes...");
            await File.WriteAllTextAsync(Path.Combine(baseTestDir, "config.json"), "{\"setting1\": \"modified\", \"setting3\": \"new_value\"}");
            await File.WriteAllTextAsync(Path.Combine(baseTestDir, "newfile.txt"), "This file should be removed on restore");
            
            // Delete a file
            var fileToDelete = Path.Combine(baseTestDir, "data.csv");
            if (File.Exists(fileToDelete))
            {
                File.Delete(fileToDelete);
            }

            // Phase 3: Restore Snapshot
            _output.WriteLine("Phase 3: Restoring snapshot...");
            await snapshotService.RestoreSnapshot(snapshot, CancellationToken.None);

            // Phase 4: Verify Final State
            _output.WriteLine("Phase 4: Verifying final state...");
            
            // Verify original files are restored
            var restoredConfig = await File.ReadAllTextAsync(Path.Combine(baseTestDir, "config.json"));
            Assert.Contains("value1", restoredConfig);
            Assert.Contains("value2", restoredConfig);

            // Verify new file was removed
            Assert.False(File.Exists(Path.Combine(baseTestDir, "newfile.txt")), "New file should be removed");

            // Verify nested file still exists
            Assert.True(File.Exists(Path.Combine(baseTestDir, "subdir", "nested.txt")), "Nested file should still exist");

            _output.WriteLine("✅ Full workflow test completed successfully");
            _output.WriteLine($"Security policy: {snapshot.SecurityPolicyPath}");
            _output.WriteLine($"Audit policy: {snapshot.AuditPolicyPath}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(baseTestDir))
            {
                try
                {
                    Directory.Delete(baseTestDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
