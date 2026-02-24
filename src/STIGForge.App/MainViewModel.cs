using System.Windows;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using STIGForge.Apply;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;
using STIGForge.Evidence;
using STIGForge.Export;
using STIGForge.Infrastructure.Paths;
using STIGForge.Infrastructure.System;
using STIGForge.Verify;

namespace STIGForge.App;

/// <summary>
/// Main view model stub - full implementation pending.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _missionJsonPath = string.Empty;

    public MainViewModel(
        ContentPackImporter importer,
        IContentPackRepository packs,
        IProfileRepository profiles,
        IControlRepository controls,
        IOverlayRepository overlays,
        BundleBuilder builder,
        ApplyRunner applyRunner,
        IVerificationWorkflowService verificationWorkflow,
        EmassExporter emassExporter,
        IPathBuilder paths,
        EvidenceCollector evidence,
        IBundleMissionSummaryService bundleMissionSummary,
        VerificationArtifactAggregationService artifactAggregation,
        ImportSelectionOrchestrator importSelectionOrchestrator,
        IAuditTrailService audit,
        ScheduledTaskService scheduledTaskService,
        FleetService fleetService)
    {
    }

    public void StartInitialLoad()
    {
        // Stub - full implementation pending
    }

    public static void SetDarkTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
