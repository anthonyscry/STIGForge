using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using STIGForge.Export;
using STIGForge.Infrastructure.System;

namespace STIGForge.App;

public partial class MainViewModel
{
  [ObservableProperty] private string fleetTargets = "";
  [ObservableProperty] private string fleetOperation = "apply";
  [ObservableProperty] private int fleetConcurrency = 5;
  [ObservableProperty] private int fleetTimeout = 600;
  [ObservableProperty] private string fleetStatus = "";
  [ObservableProperty] private string fleetLog = "";
  [ObservableProperty] private string fleetResultsDirectory = "";
  [ObservableProperty] private double fleetWideCompliance;
  [ObservableProperty] private bool fleetSummaryVisible;

  public ObservableCollection<FleetHostStats> FleetComplianceStats { get; } = new();

  public IReadOnlyList<string> FleetOperations { get; } = new[]
  {
    "apply",
    "verify",
    "orchestrate"
  };

  [RelayCommand]
  private async Task FleetExecute()
  {
    if (_fleetService == null) { StatusText = "Fleet service not available."; return; }
    try
    {
      if (string.IsNullOrWhiteSpace(FleetTargets))
      {
        StatusText = "Enter target hostnames (comma-separated).";
        return;
      }

      IsBusy = true;
      FleetStatus = "Running...";
      FleetLog = "";
      StatusText = $"Fleet {FleetOperation} started...";

      var targets = ParseFleetTargets(FleetTargets);
      var request = new FleetRequest
      {
        Targets = targets,
        Operation = FleetOperation,
        RemoteBundleRoot = string.IsNullOrWhiteSpace(BundleRoot) ? null : BundleRoot,
        MaxConcurrency = FleetConcurrency > 0 ? FleetConcurrency : 5,
        TimeoutSeconds = FleetTimeout > 0 ? FleetTimeout : 600
      };

      var result = await _fleetService.ExecuteAsync(request, CancellationToken.None);

      FleetStatus = $"{result.SuccessCount}/{result.TotalMachines} succeeded in {(result.FinishedAt - result.StartedAt).TotalSeconds:F1}s";
      FleetLog = FormatFleetResult(result);
      StatusText = $"Fleet {FleetOperation} complete: {result.SuccessCount}/{result.TotalMachines} OK.";
    }
    catch (Exception ex)
    {
      FleetStatus = "Failed: " + ex.Message;
      StatusText = "Fleet execute failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task FleetCheckStatus()
  {
    if (_fleetService == null) { StatusText = "Fleet service not available."; return; }
    try
    {
      if (string.IsNullOrWhiteSpace(FleetTargets))
      {
        StatusText = "Enter target hostnames (comma-separated).";
        return;
      }

      IsBusy = true;
      FleetStatus = "Checking connectivity...";
      StatusText = "Checking fleet status...";

      var targets = ParseFleetTargets(FleetTargets);
      var result = await _fleetService.CheckStatusAsync(targets, CancellationToken.None);

      FleetStatus = $"{result.ReachableCount}/{result.TotalMachines} reachable";
      var lines = new System.Text.StringBuilder();
      foreach (var s in result.MachineStatuses)
        lines.AppendLine($"{s.MachineName,-30} {(s.IsReachable ? "OK" : "FAIL"),-8} {s.Message}");
      FleetLog = lines.ToString();

      StatusText = $"Fleet status: {result.ReachableCount}/{result.TotalMachines} reachable.";
    }
    catch (Exception ex)
    {
      FleetStatus = "Failed: " + ex.Message;
      StatusText = "Fleet status check failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task FleetCollect()
  {
    if (_fleetService == null) { StatusText = "Fleet service not available."; return; }
    try
    {
      if (string.IsNullOrWhiteSpace(FleetTargets))
      {
        StatusText = "Enter target hostnames (comma-separated).";
        return;
      }

      var dlg = new OpenFolderDialog { Title = "Select local results directory" };
      if (dlg.ShowDialog() != true) return;

      IsBusy = true;
      FleetStatus = "Collecting artifacts...";
      StatusText = "Fleet artifact collection started...";

      var targets = ParseFleetTargets(FleetTargets);
      FleetResultsDirectory = dlg.FolderName;

      var request = new FleetCollectionRequest
      {
        Targets = targets,
        RemoteBundleRoot = string.IsNullOrWhiteSpace(BundleRoot) ? @"C:\STIGForge\bundle" : BundleRoot,
        LocalResultsRoot = FleetResultsDirectory,
        MaxConcurrency = FleetConcurrency > 0 ? FleetConcurrency : 5,
        TimeoutSeconds = FleetTimeout > 0 ? FleetTimeout : 600
      };

      var result = await _fleetService.CollectArtifactsAsync(request, CancellationToken.None);

      // Generate per-host CKL
      var fleetResultsDir = System.IO.Path.Combine(FleetResultsDirectory, "fleet_results");
      FleetSummaryService.GeneratePerHostCkl(fleetResultsDir);

      FleetStatus = $"Collected: {result.SuccessCount}/{result.TotalHosts} hosts ({result.HostResults.Sum(r => r.FilesCollected)} files)";
      StatusText = "Fleet collection complete.";

      // Auto-run summary
      await RunFleetSummary(fleetResultsDir);
    }
    catch (Exception ex)
    {
      FleetStatus = "Collection failed: " + ex.Message;
      StatusText = "Fleet collection failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task FleetSummaryGenerate()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(FleetResultsDirectory))
      {
        var dlg = new OpenFolderDialog { Title = "Select fleet_results directory" };
        if (dlg.ShowDialog() != true) return;
        FleetResultsDirectory = dlg.FolderName;
      }

      IsBusy = true;
      FleetStatus = "Generating fleet summary...";
      StatusText = "Fleet summary generation started...";

      await RunFleetSummary(FleetResultsDirectory);

      StatusText = "Fleet summary complete.";
    }
    catch (Exception ex)
    {
      FleetStatus = "Summary failed: " + ex.Message;
      StatusText = "Fleet summary failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private async Task RunFleetSummary(string resultsDir)
  {
    await Task.Run(() =>
    {
      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(resultsDir);

      var outputDir = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(resultsDir) ?? resultsDir, "fleet_summary");
      svc.WriteSummaryFiles(summary, outputDir);

      App.Current.Dispatcher.Invoke(() =>
      {
        FleetComplianceStats.Clear();
        foreach (var stat in summary.PerHostStats)
          FleetComplianceStats.Add(stat);

        FleetWideCompliance = summary.FleetWideCompliance;
        FleetSummaryVisible = true;
        FleetStatus = $"Fleet compliance: {summary.FleetWideCompliance:F1}% across {summary.PerHostStats.Count} hosts | Output: {outputDir}";
      });
    }, _cts.Token);
  }

  private static List<FleetTarget> ParseFleetTargets(string input)
  {
    var list = new List<FleetTarget>();
    foreach (var raw in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
      var item = raw.Trim();
      if (item.Length == 0) continue;
      var colonIdx = item.IndexOf(':');
      if (colonIdx < 0)
      {
        list.Add(new FleetTarget { HostName = item });
        continue;
      }

      if (colonIdx == 0)
        continue;

      list.Add(new FleetTarget
      {
        HostName = item.Substring(0, colonIdx).Trim(),
        IpAddress = item.Substring(colonIdx + 1).Trim()
      });
    }
    return list;
  }

  private static string FormatFleetResult(FleetResult result)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"Operation: {result.Operation}");
    sb.AppendLine($"Duration: {(result.FinishedAt - result.StartedAt).TotalSeconds:F1}s");
    sb.AppendLine($"Success: {result.SuccessCount}/{result.TotalMachines}");
    sb.AppendLine();
    foreach (var m in result.MachineResults)
    {
      var dur = (m.FinishedAt - m.StartedAt).TotalSeconds;
      sb.AppendLine($"{m.MachineName,-30} {(m.Success ? "OK" : "FAIL"),-8} exit={m.ExitCode} {dur:F1}s");
      if (!string.IsNullOrWhiteSpace(m.Error))
        sb.AppendLine($"  Error: {m.Error}");
    }
    return sb.ToString();
  }
}
