using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

/// <summary>
/// Extracts and stages DISA STIG GPO package artifacts (ADMX templates, Registry.pol files,
/// GptTmpl.inf security templates) into the bundle's Apply directory structure so the
/// harden step can import them after PowerSTIG DSC is applied.
///
/// DISA GPO package layout:
///   ADMX Templates/{OS}/*.admx, *.adml
///   .Support Files/Local Policies/{OS}/DomainSysvol/GPO/Machine/Registry.pol
///   .Support Files/Local Policies/{OS}/DomainSysvol/GPO/Machine/microsoft/windows nt/secedit/GptTmpl.inf
///
/// Staged output (under bundleRoot/Apply/):
///   ADMX Templates/{OS}/*.admx, *.adml    → for ADMX import step
///   GPO/Machine/Registry.pol               → for LGPO apply step
///   GPO/SecurityTemplate/GptTmpl.inf       → for security template import
/// </summary>
public static class GpoPackageExtractor
{
    /// <summary>
    /// Discovers and stages GPO artifacts from an extracted GPO package root,
    /// filtering to only include artifacts applicable to the given OS target.
    /// Returns a summary of what was staged.
    /// </summary>
    public static GpoStagingResult StageForApply(string extractedRoot, string applyRoot, OsTarget osTarget)
    {
        if (!Directory.Exists(extractedRoot))
            throw new DirectoryNotFoundException("GPO extraction root not found: " + extractedRoot);

        Directory.CreateDirectory(applyRoot);

        var result = new GpoStagingResult();

        // Stage ADMX templates
        result.AdmxFileCount = StageAdmxTemplates(extractedRoot, applyRoot, osTarget);

        // Stage Registry.pol files
        result.PolFilePath = StageRegistryPol(extractedRoot, applyRoot, osTarget);

        // Stage GptTmpl.inf security templates
        result.SecurityTemplatePath = StageSecurityTemplate(extractedRoot, applyRoot, osTarget);

        return result;
    }

    /// <summary>
    /// Detects the OS scope folders present in a GPO package extraction root.
    /// Used to determine which OS targets are available for staging.
    /// </summary>
    public static IReadOnlyList<GpoOsScope> DetectOsScopes(string extractedRoot)
    {
        var scopes = new List<GpoOsScope>();

        // Check ADMX Templates subfolders
        var admxRoot = FindCaseInsensitive(extractedRoot, "ADMX Templates");
        if (admxRoot != null)
        {
            foreach (var dir in Directory.EnumerateDirectories(admxRoot))
            {
                var name = Path.GetFileName(dir);
                var target = MapFolderToOsTarget(name);
                if (target != OsTarget.Unknown)
                    scopes.Add(new GpoOsScope { FolderName = name, OsTarget = target, ScopePath = dir });
            }
        }

        // Check .Support Files/Local Policies subfolders
        var localPoliciesRoot = FindLocalPoliciesRoot(extractedRoot);
        if (localPoliciesRoot != null)
        {
            foreach (var dir in Directory.EnumerateDirectories(localPoliciesRoot))
            {
                var name = Path.GetFileName(dir);
                var target = MapFolderToOsTarget(name);
                if (target != OsTarget.Unknown && !scopes.Any(s => s.OsTarget == target))
                    scopes.Add(new GpoOsScope { FolderName = name, OsTarget = target, ScopePath = dir });
            }
        }

        return scopes;
    }

    /// <summary>
    /// Maps a DISA GPO package folder name to the corresponding OsTarget.
    /// DISA uses names like "Windows 11", "Windows Server 2022 Member Server", etc.
    /// </summary>
    public static OsTarget MapFolderToOsTarget(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return OsTarget.Unknown;

        var name = folderName.Trim();

        if (ContainsAny(name, "Windows 11", "Win11", "Windows_11"))
            return OsTarget.Win11;
        if (ContainsAny(name, "Windows 10", "Win10", "Windows_10"))
            return OsTarget.Win10;
        if (ContainsAny(name, "2022"))
            return OsTarget.Server2022;
        if (ContainsAny(name, "2019"))
            return OsTarget.Server2019;

        return OsTarget.Unknown;
    }

    private static int StageAdmxTemplates(string extractedRoot, string applyRoot, OsTarget osTarget)
    {
        var admxRoot = FindCaseInsensitive(extractedRoot, "ADMX Templates");
        if (admxRoot == null)
            return 0;

        var osFolder = FindBestOsFolder(admxRoot, osTarget);
        if (osFolder == null)
            return 0;

        var admxTargetRoot = Path.Combine(applyRoot, "ADMX Templates");
        Directory.CreateDirectory(admxTargetRoot);

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(osFolder, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!string.Equals(ext, ".admx", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".adml", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(osFolder, file);
            var dest = Path.Combine(admxTargetRoot, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(file, dest, true);
            if (string.Equals(ext, ".admx", StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static string? StageRegistryPol(string extractedRoot, string applyRoot, OsTarget osTarget)
    {
        var localPoliciesRoot = FindLocalPoliciesRoot(extractedRoot);
        if (localPoliciesRoot == null)
            return null;

        var osFolder = FindBestOsFolder(localPoliciesRoot, osTarget);
        if (osFolder == null)
            return null;

        // Search for Registry.pol under the OS folder
        var polFiles = Directory.EnumerateFiles(osFolder, "Registry.pol", SearchOption.AllDirectories)
            .ToList();

        if (polFiles.Count == 0)
        {
            // Try any .pol file
            polFiles = Directory.EnumerateFiles(osFolder, "*.pol", SearchOption.AllDirectories)
                .ToList();
        }

        if (polFiles.Count == 0)
            return null;

        // Prefer Machine scope
        var machinePolFile = polFiles.FirstOrDefault(p =>
            p.Replace('\\', '/').IndexOf("/Machine/", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? polFiles[0];

        var gpoDir = Path.Combine(applyRoot, "GPO", "Machine");
        Directory.CreateDirectory(gpoDir);

        var destPath = Path.Combine(gpoDir, "Registry.pol");
        File.Copy(machinePolFile, destPath, true);
        return destPath;
    }

    private static string? StageSecurityTemplate(string extractedRoot, string applyRoot, OsTarget osTarget)
    {
        var localPoliciesRoot = FindLocalPoliciesRoot(extractedRoot);
        if (localPoliciesRoot == null)
            return null;

        var osFolder = FindBestOsFolder(localPoliciesRoot, osTarget);
        if (osFolder == null)
            return null;

        // Search for GptTmpl.inf under the OS folder
        var infFiles = Directory.EnumerateFiles(osFolder, "GptTmpl.inf", SearchOption.AllDirectories)
            .ToList();

        if (infFiles.Count == 0)
            return null;

        var securityTemplateDir = Path.Combine(applyRoot, "GPO", "SecurityTemplate");
        Directory.CreateDirectory(securityTemplateDir);

        var destPath = Path.Combine(securityTemplateDir, "GptTmpl.inf");
        File.Copy(infFiles[0], destPath, true);
        return destPath;
    }

    private static string? FindLocalPoliciesRoot(string extractedRoot)
    {
        // .Support Files/Local Policies or Support Files/Local Policies
        var candidates = new[]
        {
            Path.Combine(extractedRoot, ".Support Files", "Local Policies"),
            Path.Combine(extractedRoot, "Support Files", "Local Policies")
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        // Deep search: find any directory named "Local Policies"
        try
        {
            return Directory.EnumerateDirectories(extractedRoot, "Local Policies", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindCaseInsensitive(string root, string folderName)
    {
        try
        {
            return Directory.EnumerateDirectories(root, folderName, SearchOption.TopDirectoryOnly)
                .FirstOrDefault()
                ?? Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(d => string.Equals(Path.GetFileName(d), folderName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindBestOsFolder(string parentDir, OsTarget osTarget)
    {
        if (!Directory.Exists(parentDir))
            return null;

        var subdirs = Directory.EnumerateDirectories(parentDir).ToList();
        if (subdirs.Count == 0)
            return parentDir; // No OS subfolders, use root

        // Direct match
        foreach (var dir in subdirs)
        {
            var mapped = MapFolderToOsTarget(Path.GetFileName(dir));
            if (mapped == osTarget)
                return dir;
        }

        // If osTarget is Unknown, return the first subfolder
        if (osTarget == OsTarget.Unknown && subdirs.Count > 0)
            return subdirs[0];

        return null;
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}

public sealed class GpoStagingResult
{
    public int AdmxFileCount { get; set; }
    public string? PolFilePath { get; set; }
    public string? SecurityTemplatePath { get; set; }

    public bool HasAnyArtifacts =>
        AdmxFileCount > 0 || !string.IsNullOrWhiteSpace(PolFilePath) || !string.IsNullOrWhiteSpace(SecurityTemplatePath);
}

public sealed class GpoOsScope
{
    public string FolderName { get; set; } = string.Empty;
    public OsTarget OsTarget { get; set; }
    public string ScopePath { get; set; } = string.Empty;
}
