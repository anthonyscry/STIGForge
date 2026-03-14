using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class WorkflowViewModel
{
    private static void EnsurePowerStigDependenciesStaged(string bundleRoot, string importFolderPath)
    {
        var applyRoot = Path.Combine(bundleRoot, "Apply");
        var logPath = Path.Combine(bundleRoot, "Apply", "dep_staging.log");

        void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, DateTimeOffset.Now.ToString("HH:mm:ss") + " | " + msg + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Log write failed: {ex.Message}");
            }
        }

        Log("EnsurePowerStigDependenciesStaged started");
        Log("  bundleRoot: " + bundleRoot);
        Log("  importFolderPath: " + (importFolderPath ?? "(null)"));
        Log("  applyRoot: " + applyRoot);

        if (!Directory.Exists(applyRoot))
        {
            Log("  Apply directory does not exist, returning");
            return;
        }

        try
        {
            foreach (var d in Directory.EnumerateDirectories(applyRoot))
                Log("  Apply subdir: " + Path.GetFileName(d));
            foreach (var f in Directory.EnumerateFiles(applyRoot))
                Log("  Apply file: " + Path.GetFileName(f));
        }
        catch (Exception ex)
        {
            Log("  Error listing Apply contents: " + ex.Message);
        }

        var psd1 = Directory.EnumerateFiles(applyRoot, "PowerStig.psd1", SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(psd1))
        {
            Log("  No PowerStig.psd1 found in Apply, returning");
            return;
        }

        Log("  Found PowerStig.psd1: " + psd1);

        var knownDeps = new[] { "AuditPolicyDsc", "SecurityPolicyDsc", "WindowsDefenderDsc" };
        var hasDeps = knownDeps.Any(dep =>
            Directory.EnumerateDirectories(applyRoot, dep, SearchOption.TopDirectoryOnly).Any());
        if (hasDeps)
        {
            Log("  Dependencies already present, returning");
            return;
        }

        Log("  Dependencies missing, searching import folder for PowerSTIG zip");

        if (string.IsNullOrWhiteSpace(importFolderPath) || !Directory.Exists(importFolderPath))
        {
            Log("  Import folder not available: '" + (importFolderPath ?? "(null)") + "'");
            return;
        }

        string? powerStigZip = null;
        try
        {
            var allZips = Directory.EnumerateFiles(importFolderPath, "*.zip", SearchOption.TopDirectoryOnly).ToList();
            foreach (var z in allZips)
                Log("  Import zip: " + Path.GetFileName(z));
            powerStigZip = allZips
                .FirstOrDefault(f => Path.GetFileName(f).IndexOf("PowerStig", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch (Exception ex)
        {
            Log("  Error scanning import folder: " + ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(powerStigZip) || !File.Exists(powerStigZip))
        {
            Log("  No PowerSTIG zip found in import folder");
            return;
        }

        Log("  Found PowerSTIG zip: " + powerStigZip);

        var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-deps-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ExtractZipSafely(powerStigZip, tempDir);
            Log("  Extracted to: " + tempDir);

            try
            {
                foreach (var d in Directory.EnumerateDirectories(tempDir))
                    Log("  Extracted top-level dir: " + Path.GetFileName(d));
                foreach (var f in Directory.EnumerateFiles(tempDir))
                    Log("  Extracted top-level file: " + Path.GetFileName(f));
            }
            catch (Exception ex)
            {
                Log("  Error listing extracted contents: " + ex.Message);
            }

            var allManifests = Directory.EnumerateFiles(tempDir, "*.psd1", SearchOption.AllDirectories).ToList();
            Log("  Found " + allManifests.Count + " .psd1 manifests in zip");
            foreach (var m in allManifests)
                Log("    manifest: " + m.Substring(tempDir.Length));

            var moduleDirsToCopy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in allManifests)
            {
                var manifestDir = Path.GetDirectoryName(manifest)!;
                if (string.Equals(manifestDir, tempDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var current = manifestDir;
                while (current != null)
                {
                    var parent = Path.GetDirectoryName(current);
                    if (parent != null && string.Equals(parent, tempDir, StringComparison.OrdinalIgnoreCase))
                    {
                        moduleDirsToCopy.Add(current);
                        break;
                    }

                    current = parent;
                }
            }

            Log("  Module directories to copy: " + moduleDirsToCopy.Count);
            foreach (var dir in moduleDirsToCopy)
            {
                var dirName = Path.GetFileName(dir);
                var destDir = Path.Combine(applyRoot, dirName);
                Log("    Copying " + dirName + " -> " + destDir);
                CopyDirectoryRecursive(dir, destDir);
            }

            Log("  Dependency staging complete");
        }
        catch (Exception ex)
        {
            Log("  ERROR during extraction: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("EnsurePowerStigDependenciesStaged: temp dir cleanup failed: " + ex.Message);
            }
        }
    }

    private static int StageApplyArtifacts(IReadOnlyList<ImportInboxCandidate> candidates, string outputFolder)
    {
        var applyRoot = Path.Combine(outputFolder, "Apply");
        var staged = 0;
        var osTarget = DetectLocalOsTarget();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.ZipPath) || !File.Exists(candidate.ZipPath))
                continue;

            try
            {
                if (candidate.ArtifactKind == ImportArtifactKind.Gpo
                    || candidate.ArtifactKind == ImportArtifactKind.Admx)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-gpo-" + Guid.NewGuid().ToString("N")[..8]);
                    try
                    {
                        ExtractZipSafely(candidate.ZipPath, tempDir);
                        GpoPackageExtractor.StageForApply(tempDir, applyRoot, osTarget);
                        staged++;
                    }
                    finally
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.TraceWarning("StageApplyArtifacts (GPO): temp dir cleanup failed: " + ex.Message);
                        }
                    }
                }
                else if (candidate.ArtifactKind == ImportArtifactKind.Tool
                    && candidate.ToolKind == ToolArtifactKind.PowerStig)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-ps-" + Guid.NewGuid().ToString("N")[..8]);
                    try
                    {
                        ExtractZipSafely(candidate.ZipPath, tempDir);

                        var psd1 = Directory.EnumerateFiles(tempDir, "PowerSTIG.psd1", SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(psd1))
                        {
                            var moduleDir = Path.GetDirectoryName(psd1)!;
                            var moduleRoot = Path.GetDirectoryName(moduleDir);
                            var modulesParent = moduleRoot != null ? Path.GetDirectoryName(moduleRoot) : null;

                            var sharedRoot = modulesParent != null
                                && Directory.EnumerateDirectories(modulesParent).Count() > 1
                                ? modulesParent
                                : moduleRoot != null
                                  && Directory.EnumerateDirectories(moduleRoot).Count() > 1
                                  ? moduleRoot
                                  : null;

                            if (sharedRoot != null)
                            {
                                foreach (var dir in Directory.EnumerateDirectories(sharedRoot))
                                {
                                    var dirName = Path.GetFileName(dir);
                                    CopyDirectoryRecursive(dir, Path.Combine(applyRoot, dirName));
                                }
                            }
                            else
                            {
                                CopyDirectoryRecursive(moduleDir, Path.Combine(applyRoot, "PowerSTIG"));
                            }

                            staged++;
                        }
                    }
                    finally
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.TraceWarning("StageApplyArtifacts (PowerStig): temp dir cleanup failed: " + ex.Message);
                        }
                    }
                }
                else if (candidate.ArtifactKind == ImportArtifactKind.Tool
                    && candidate.ToolKind == ToolArtifactKind.Lgpo)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-lgpo-" + Guid.NewGuid().ToString("N")[..8]);
                    try
                    {
                        ExtractZipSafely(candidate.ZipPath, tempDir);
                        var lgpoExe = Directory.EnumerateFiles(tempDir, "LGPO.exe", SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(lgpoExe))
                        {
                            var toolsDir = Path.Combine(outputFolder, "tools");
                            Directory.CreateDirectory(toolsDir);
                            File.Copy(lgpoExe, Path.Combine(toolsDir, "LGPO.exe"), overwrite: true);
                            staged++;
                        }
                    }
                    finally
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StageApplyArtifacts: failed to stage {candidate.ZipPath}: {ex.Message}");
            }
        }

        return staged;
    }

    private static OsTarget DetectLocalOsTarget()
    {
        if (!OperatingSystem.IsWindows())
            return OsTarget.Unknown;

        try
        {
            var productName = (Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName", null) as string) ?? string.Empty;

            if (productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (productName.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0)
                    return OsTarget.Server2022;
                if (productName.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0)
                    return OsTarget.Server2019;
            }

            if (productName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0)
                return OsTarget.Win11;
            if (productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                return OsTarget.Win10;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DetectLocalOsTarget failed: {ex.Message}");
        }

        return OsTarget.Unknown;
    }

    private static RoleTemplate? DetectLocalRoleTemplate()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var ntdsStart = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\NTDS",
                "Start", null);
            if (ntdsStart is int startValue && startValue <= 2)
                return RoleTemplate.DomainController;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DetectLocalRoleTemplate failed: {ex.Message}");
        }

        return null;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }

    private static int CountPathSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return int.MaxValue;

        return path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar);
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var destinationFullRoot = Path.GetFullPath(destinationRoot);
        const int maxEntries = 4096;
        const long maxExtractedBytes = 512L * 1024 * 1024;
        long extractedBytes = 0;
        var count = 0;

        foreach (var entry in archive.Entries)
        {
            count++;
            if (count > maxEntries)
                throw new InvalidDataException($"Archive entry limit exceeded: {zipPath}");

            var destinationPath = Path.GetFullPath(Path.Combine(destinationFullRoot, entry.FullName));
            if (!destinationPath.StartsWith(destinationFullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes destination root: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            extractedBytes += entry.Length;
            if (extractedBytes > maxExtractedBytes)
                throw new InvalidDataException($"Extracted archive size exceeds limit: {zipPath}");

            var parentDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
                Directory.CreateDirectory(parentDir);

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }
}
