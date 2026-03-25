using System.Diagnostics;
using System.IO;
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
            if (!HardenArtifactResolver.HasAnyInput(request))
            {
                StatusText = HardenArtifactResolver.BuildMissingArtifactsMessage(OutputFolderPath);
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
        var powerStigModulePath = HardenArtifactResolver.ResolvePowerStigModulePath(applyRoot);
        var dscMofPath = HardenArtifactResolver.ResolveDscMofPath(applyRoot);
        if (string.IsNullOrWhiteSpace(dscMofPath) && !string.IsNullOrWhiteSpace(powerStigModulePath))
            dscMofPath = Path.Combine(applyRoot, "Dsc");

        var lgpoPolPath = HardenArtifactResolver.ResolveLgpoPolFilePath(applyRoot, out var lgpoScope);
        var powerStigDataPath = HardenArtifactResolver.ResolvePowerStigDataPath(applyRoot);

        var (osTarget, roleTemplate) = HardenArtifactResolver.ReadOsTargetFromManifest(bundleRoot);
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
            AdmxTemplateRootPath = HardenArtifactResolver.ResolveAdmxTemplateRootPath(applyRoot),
            OsTarget = osTarget,
            RoleTemplate = roleTemplate
        };

        var lgpoExePath = HardenArtifactResolver.ResolveLgpoExePath(bundleRoot);
        if (!string.IsNullOrWhiteSpace(lgpoExePath))
            request.LgpoExePath = lgpoExePath;

        var domainGpoPath = Path.Combine(applyRoot, "DomainGPOs");
        if (Directory.Exists(domainGpoPath))
            request.DomainGpoBackupPath = domainGpoPath;

        return request;
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
