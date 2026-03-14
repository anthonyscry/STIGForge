using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Apply;
using STIGForge.Apply.Lgpo;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class WorkflowViewModel
{
    private async Task<bool> RunHardenAsync()
    {
        StatusText = "Applying hardening configurations...";
        AppliedFixesCount = 0;

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            StatusText = "Output folder is required for hardening";
            return false;
        }

        if (!Directory.Exists(OutputFolderPath))
        {
            StatusText = "Hardening cannot run: output folder does not exist";
            return false;
        }

        if (_runApply == null)
        {
            StatusText = "Apply runner not configured";
            return false;
        }

        try
        {
            EnsurePowerStigDependenciesStaged(OutputFolderPath, ImportFolderPath);

            var request = BuildHardenApplyRequest(OutputFolderPath);
            if (!HasAnyHardenApplyInput(request))
            {
                StatusText = BuildMissingHardenArtifactsMessage(OutputFolderPath);
                return false;
            }

            var result = await _runApply(request, _cts.Token);

            if (!result.IsMissionComplete)
            {
                StatusText = "Hardening did not complete successfully";
                return false;
            }

            var blockingFailures = result.BlockingFailures ?? [];
            if (blockingFailures.Count > 0)
            {
                StatusText = "Hardening failed: " + string.Join(" | ", blockingFailures);
                return false;
            }

            var steps = result.Steps ?? [];
            AppliedFixesCount = steps.Count(s => s.ExitCode == 0);

            StatusText = $"Hardening complete: {AppliedFixesCount} fixes applied";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Hardening failed: {ex.Message}";
            return false;
        }
    }

    private static ApplyRequest BuildHardenApplyRequest(string bundleRoot)
    {
        var applyRoot = Path.Combine(bundleRoot, "Apply");
        var powerStigModulePath = ResolvePowerStigModulePath(applyRoot);
        var dscMofPath = ResolveDscMofPath(applyRoot);
        if (string.IsNullOrWhiteSpace(dscMofPath) && !string.IsNullOrWhiteSpace(powerStigModulePath))
            dscMofPath = Path.Combine(applyRoot, "Dsc");

        var lgpoPolPath = ResolveLgpoPolFilePath(applyRoot, out var lgpoScope);
        var powerStigDataPath = ResolvePowerStigDataPath(applyRoot);

        var (osTarget, roleTemplate) = ReadOsTargetFromManifest(bundleRoot);
        if (!osTarget.HasValue || osTarget.Value == OsTarget.Unknown)
            osTarget = DetectLocalOsTarget();
        if (!roleTemplate.HasValue)
            roleTemplate = DetectLocalRoleTemplate();

        var request = new ApplyRequest
        {
            BundleRoot = bundleRoot,
            DscMofPath = dscMofPath,
            PowerStigModulePath = powerStigModulePath,
            PowerStigDataFile = powerStigDataPath,
            PowerStigOutputPath = string.IsNullOrWhiteSpace(powerStigModulePath)
                ? null
                : Path.Combine(applyRoot, "Dsc"),
            PowerStigDataGeneratedPath = powerStigDataPath,
            LgpoPolFilePath = lgpoPolPath,
            LgpoScope = lgpoScope,
            AdmxTemplateRootPath = ResolveAdmxTemplateRootPath(applyRoot),
            OsTarget = osTarget,
            RoleTemplate = roleTemplate
        };

        var lgpoExePath = ResolveLgpoExePath(bundleRoot);
        if (!string.IsNullOrWhiteSpace(lgpoExePath))
            request.LgpoExePath = lgpoExePath;

        var domainGpoPath = Path.Combine(applyRoot, "DomainGPOs");
        if (Directory.Exists(domainGpoPath))
            request.DomainGpoBackupPath = domainGpoPath;

        return request;
    }

    private static (OsTarget? osTarget, RoleTemplate? roleTemplate) ReadOsTargetFromManifest(string bundleRoot)
    {
        try
        {
            var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
            if (!File.Exists(manifestPath))
                return (null, null);

            using var stream = File.OpenRead(manifestPath);
            using var doc = System.Text.Json.JsonDocument.Parse(stream);

            OsTarget? osTarget = null;
            RoleTemplate? roleTemplate = null;

            if (doc.RootElement.TryGetProperty("Profile", out var profile))
            {
                if (profile.TryGetProperty("OsTarget", out var osProp))
                {
                    var osStr = osProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? osProp.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(osStr) && Enum.TryParse<OsTarget>(osStr, true, out var parsed))
                        osTarget = parsed;
                }

                if (profile.TryGetProperty("RoleTemplate", out var roleProp))
                {
                    var roleStr = roleProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? roleProp.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(roleStr) && Enum.TryParse<RoleTemplate>(roleStr, true, out var parsedRole))
                        roleTemplate = parsedRole;
                }
            }

            if (!osTarget.HasValue && doc.RootElement.TryGetProperty("OsTarget", out var rootOs))
            {
                var osStr = rootOs.ValueKind == System.Text.Json.JsonValueKind.String
                    ? rootOs.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(osStr) && Enum.TryParse<OsTarget>(osStr, true, out var parsed))
                    osTarget = parsed;
            }

            if (!roleTemplate.HasValue && doc.RootElement.TryGetProperty("RoleTemplate", out var rootRole))
            {
                var roleStr = rootRole.ValueKind == System.Text.Json.JsonValueKind.String
                    ? rootRole.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(roleStr) && Enum.TryParse<RoleTemplate>(roleStr, true, out var parsedRole))
                    roleTemplate = parsedRole;
            }

            return (osTarget, roleTemplate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("ReadOsTargetFromManifest failed: " + ex.Message);
            return (null, null);
        }
    }

    private static bool HasAnyHardenApplyInput(ApplyRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.PowerStigModulePath)
            || !string.IsNullOrWhiteSpace(request.DscMofPath)
            || !string.IsNullOrWhiteSpace(request.LgpoPolFilePath)
            || !string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath)
            || !string.IsNullOrWhiteSpace(request.ScriptPath);
    }

    private static string BuildMissingHardenArtifactsMessage(string bundleRoot)
    {
        var applyRoot = Path.Combine(bundleRoot, "Apply");
        var dscPath = Path.Combine(applyRoot, "Dsc");
        var lgpoPath = Path.Combine(applyRoot, "GPO", "Machine", "Registry.pol");
        var admxPath = Path.Combine(applyRoot, "ADMX Templates");

        return "Hardening cannot run: no apply artifacts were found under " + applyRoot
            + ". Expected at least one of: PowerSTIG module in Apply, DSC directory " + dscPath
            + ", LGPO policy " + lgpoPath + ", or ADMX templates under " + admxPath + ".";
    }

    private static string? ResolveDscMofPath(string applyRoot)
    {
        var candidate = Path.Combine(applyRoot, "Dsc");
        if (!Directory.Exists(candidate))
            return null;

        var hasRootMof = Directory.EnumerateFiles(candidate, "*.mof", SearchOption.TopDirectoryOnly).Any();
        if (hasRootMof)
            return candidate;

        try
        {
            var osHints = GetOsDscHints();
            var childFolders = Directory.EnumerateDirectories(candidate, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    Path = path,
                    Name = Path.GetFileName(path) ?? string.Empty,
                    HasMof = Directory.EnumerateFiles(path, "*.mof", SearchOption.AllDirectories).Any()
                })
                .Where(x => x.HasMof)
                .ToList();

            if (childFolders.Count == 0)
                return candidate;

            var bestMatch = childFolders
                .Select(x => new
                {
                    x.Path,
                    Score = osHints.Any(h => x.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) ? 1 : 0
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return bestMatch?.Path ?? candidate;
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolveDscMofPath IO error: " + ex.Message);
            return candidate;
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolveDscMofPath access denied: " + ex.Message);
            return candidate;
        }
    }

    private static string[] GetOsDscHints()
    {
        var hints = new List<string>();
        var version = Environment.OSVersion.Version;
        var majorMinor = $"{version.Major}.{version.Minor}";
        hints.Add(majorMinor);

        if (OperatingSystem.IsWindows())
        {
            hints.Add("windows");
            hints.Add("win");

            var productName = ReadWindowsProductName();
            if (!string.IsNullOrWhiteSpace(productName))
            {
                if (productName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0)
                    hints.Add("11");
                else if (productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                    hints.Add("10");

                if (productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hints.Add("server");
                    if (productName.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0)
                        hints.Add("2022");
                    else if (productName.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0)
                        hints.Add("2019");
                    else if (productName.IndexOf("2016", StringComparison.OrdinalIgnoreCase) >= 0)
                        hints.Add("2016");
                    else if (productName.IndexOf("2012", StringComparison.OrdinalIgnoreCase) >= 0)
                        hints.Add("2012");
                }
            }
        }

        return hints
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadWindowsProductName()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName",
                null);
            return value as string;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("ReadWindowsProductName failed: " + ex.Message);
            return null;
        }
    }

    private static string? ResolvePowerStigDataPath(string applyRoot)
    {
        var candidate = Path.Combine(applyRoot, "PowerStigData", "stigdata.psd1");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolvePowerStigModulePath(string applyRoot)
    {
        if (!Directory.Exists(applyRoot))
            return null;

        try
        {
            var psd1 = Directory.EnumerateFiles(applyRoot, "PowerSTIG.psd1", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(psd1))
                return psd1;

            var psm1 = Directory.EnumerateFiles(applyRoot, "PowerSTIG.psm1", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(psm1))
                return psm1;

            var moduleDirectory = Directory.EnumerateDirectories(applyRoot, "PowerSTIG", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(moduleDirectory))
                return moduleDirectory;
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolvePowerStigModulePath IO error: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolvePowerStigModulePath access denied: " + ex.Message);
        }

        return null;
    }

    private static string? ResolveLgpoPolFilePath(string applyRoot, out LgpoScope? scope)
    {
        scope = null;
        if (!Directory.Exists(applyRoot))
            return null;

        var candidates = new[]
        {
            Path.Combine(applyRoot, "GPO", "Machine", "Registry.pol"),
            Path.Combine(applyRoot, "GPO", "Machine", "registry.pol"),
            Path.Combine(applyRoot, "GPO", "machine.pol"),
            Path.Combine(applyRoot, "LGPO", "Machine", "Registry.pol"),
            Path.Combine(applyRoot, "LGPO", "Machine", "registry.pol"),
            Path.Combine(applyRoot, "LGPO", "machine.pol"),
            Path.Combine(applyRoot, "Policies", "Machine", "Registry.pol"),
            Path.Combine(applyRoot, "Policies", "machine.pol")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            scope = ResolveLgpoScope(candidate);
            return candidate;
        }

        try
        {
            var discovered = Directory.EnumerateFiles(applyRoot, "*.pol", SearchOption.AllDirectories)
                .OrderBy(path => ResolveLgpoScope(path) == LgpoScope.Machine ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(discovered))
            {
                scope = ResolveLgpoScope(discovered);
                return discovered;
            }
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolveLgpoPolFilePath IO error: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolveLgpoPolFilePath access denied: " + ex.Message);
        }

        return null;
    }

    private static LgpoScope ResolveLgpoScope(string polPath)
    {
        if (polPath.IndexOf($"{Path.DirectorySeparatorChar}User{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
            || polPath.IndexOf($"{Path.AltDirectorySeparatorChar}User{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
            return LgpoScope.User;

        return LgpoScope.Machine;
    }

    private static string? ResolveLgpoExePath(string bundleRoot)
    {
        var candidate = Path.Combine(bundleRoot, "tools", "LGPO.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveAdmxTemplateRootPath(string applyRoot)
    {
        if (!Directory.Exists(applyRoot))
            return null;

        var directCandidates = new[]
        {
            Path.Combine(applyRoot, "ADMX Templates"),
            Path.Combine(applyRoot, "ADMX"),
            Path.Combine(applyRoot, "PolicyDefinitions")
        };

        foreach (var candidate in directCandidates)
        {
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.admx", SearchOption.AllDirectories).Any())
                return candidate;
        }

        try
        {
            var discovered = Directory.EnumerateFiles(applyRoot, "*.admx", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(discovered))
                return null;

            return Path.GetDirectoryName(discovered);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolveAdmxTemplateRootPath IO error: " + ex.Message);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Trace.TraceWarning("ResolveAdmxTemplateRootPath access denied: " + ex.Message);
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunHarden))]
    private async Task RunHardenStepAsync()
    {
        IsBusy = true;
        HardenState = StepState.Running;
        HardenError = string.Empty;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var success = await RunHardenAsync();
            if (success)
            {
                HardenState = StepState.Complete;
                if (VerifyState == StepState.Locked)
                    VerifyState = StepState.Ready;
            }
            else
            {
                HardenState = StepState.Error;
                HardenError = StatusText;
            }
        }
        catch (Exception ex)
        {
            HardenState = StepState.Error;
            HardenError = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            LastHardenDurationMs = stopwatch.ElapsedMilliseconds;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSkipHarden))]
    private void SkipHardenStep()
    {
        HardenState = StepState.Complete;
        HardenError = string.Empty;
        AppliedFixesCount = 0;
        InvalidateScanComplianceBaseline();
        StatusText = "Hardening skipped by operator";

        if (VerifyState == StepState.Locked)
            VerifyState = StepState.Ready;
    }
}
