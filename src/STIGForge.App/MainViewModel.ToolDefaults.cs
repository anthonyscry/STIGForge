using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Infrastructure.System;

namespace STIGForge.App;

public partial class MainViewModel
{

  private static readonly Dictionary<string, string> EvalStigPresetArgs = new()
  {
    ["Recommended"] = "-AnswerFile .\\AnswerFile.xml",
    ["Unclassified Scan"] = "-ScanType Unclassified -AnswerFile .\\AnswerFile.xml",
    ["No Answer File"] = "",
    ["Custom"] = ""
  };

  public IReadOnlyList<string> EvaluateStigPresets { get; } = new[]
  {
    "Recommended",
    "Unclassified Scan",
    "No Answer File",
    "Custom"
  };

  [ObservableProperty] private string selectedEvalStigPreset = "Recommended";

  public bool IsEvalStigCustomArgs => SelectedEvalStigPreset == "Custom";

  partial void OnSelectedEvalStigPresetChanged(string value)
  {
    OnPropertyChanged(nameof(IsEvalStigCustomArgs));

    if (EvalStigPresetArgs.TryGetValue(value, out var args) && value != "Custom")
    {
      EvaluateStigArgs = args;
    }

    SaveUiState();
  }

  public ObservableCollection<FleetInventoryEntry> FleetInventoryItems { get; } = new();

  [ObservableProperty] private FleetInventoryEntry? selectedFleetInventoryItem;

  [RelayCommand]
  private void AddFleetHost()
  {
    if (string.IsNullOrWhiteSpace(FleetTargets)) return;

    foreach (var raw in FleetTargets.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
      var item = raw.Trim();
      if (item.Length == 0) continue;

      var colonIdx = item.IndexOf(':');
      string hostName;
      string? ipAddress = null;

      if (colonIdx > 0)
      {
        hostName = item.Substring(0, colonIdx).Trim();
        ipAddress = item.Substring(colonIdx + 1).Trim();
      }
      else
      {
        hostName = item;
      }

      var exists = false;
      foreach (var existing in FleetInventoryItems)
      {
        if (string.Equals(existing.HostName, hostName, StringComparison.OrdinalIgnoreCase))
        {
          exists = true;
          break;
        }
      }
      if (exists) continue;

      FleetInventoryItems.Add(new FleetInventoryEntry
      {
        HostName = hostName,
        IpAddress = ipAddress ?? "",
        WinRmStatus = "—",
        LastCheckLabel = "never"
      });
    }

    SaveFleetInventory();
  }

  [RelayCommand]
  private void RemoveFleetHost()
  {
    if (SelectedFleetInventoryItem == null) return;
    FleetInventoryItems.Remove(SelectedFleetInventoryItem);
    SaveFleetInventory();
  }

  [RelayCommand]
  private async Task PingAllFleet()
  {
    IsBusy = true;
    StatusText = "Pinging fleet targets...";

    try
    {
      foreach (var entry in FleetInventoryItems)
      {
        entry.WinRmStatus = "...";
      }

      await Task.Run(() =>
      {
        foreach (var entry in FleetInventoryItems)
        {
          try
          {
            var target = !string.IsNullOrWhiteSpace(entry.IpAddress) ? entry.IpAddress : entry.HostName;
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = ping.Send(target, 3000);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
              entry.WinRmStatus = reply.Status == System.Net.NetworkInformation.IPStatus.Success ? "Ping OK" : "No Ping";
              entry.LastCheckLabel = DateTimeOffset.Now.ToString("HH:mm");
            });
          }
          catch
          {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
              entry.WinRmStatus = "Error";
              entry.LastCheckLabel = DateTimeOffset.Now.ToString("HH:mm");
            });
          }
        }
      }, _cts.Token);

      StatusText = "Ping sweep complete.";
    }
    catch (Exception ex)
    {
      StatusText = "Ping failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task TestWinRmAllFleet()
  {
    if (_fleetService == null) { StatusText = "Fleet service not available."; return; }

    IsBusy = true;
    StatusText = "Testing WinRM connectivity...";

    try
    {
      var targets = new List<FleetTarget>();
      foreach (var entry in FleetInventoryItems)
      {
        targets.Add(new FleetTarget
        {
          HostName = entry.HostName,
          IpAddress = string.IsNullOrWhiteSpace(entry.IpAddress) ? null : entry.IpAddress
        });
      }

      var result = await _fleetService.CheckStatusAsync(targets, _cts.Token);

      for (var i = 0; i < result.MachineStatuses.Count && i < FleetInventoryItems.Count; i++)
      {
        var status = result.MachineStatuses[i];
        var entry = FleetInventoryItems.FirstOrDefault(e =>
          string.Equals(e.HostName, status.MachineName, StringComparison.OrdinalIgnoreCase));

        if (entry != null)
        {
          entry.WinRmStatus = status.IsReachable ? "OK" : "FAIL";
          entry.LastCheckLabel = DateTimeOffset.Now.ToString("HH:mm");
        }
      }

      StatusText = $"WinRM: {result.ReachableCount}/{result.TotalMachines} reachable.";
    }
    catch (Exception ex)
    {
      StatusText = "WinRM test failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task DiscoverAdHosts()
  {
    IsBusy = true;
    StatusText = "Querying Active Directory for computer objects...";

    try
    {
      var computers = await Task.Run(() =>
      {
        var results = new List<(string Name, string? DnsHost)>();
        try
        {
          using var entry = new System.DirectoryServices.DirectoryEntry("LDAP://RootDSE");
          var defaultNamingContext = entry.Properties["defaultNamingContext"]?.Value?.ToString();
          if (string.IsNullOrWhiteSpace(defaultNamingContext))
            throw new InvalidOperationException("Could not determine AD naming context. Is this machine domain-joined?");

          using var searchRoot = new System.DirectoryServices.DirectoryEntry("LDAP://" + defaultNamingContext);
          using var searcher = new System.DirectoryServices.DirectorySearcher(searchRoot)
          {
            Filter = "(objectClass=computer)",
            PageSize = 1000
          };
          searcher.PropertiesToLoad.Add("cn");
          searcher.PropertiesToLoad.Add("dNSHostName");

          using var found = searcher.FindAll();
          foreach (System.DirectoryServices.SearchResult sr in found)
          {
            var cn = sr.Properties["cn"]?.Count > 0 ? sr.Properties["cn"][0]?.ToString() : null;
            var dns = sr.Properties["dNSHostName"]?.Count > 0 ? sr.Properties["dNSHostName"][0]?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(cn))
              results.Add((cn, dns));
          }
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
          throw new InvalidOperationException("AD query failed. Machine may not be domain-joined: " + ex.Message, ex);
        }
        return results;
      }, _cts.Token);

      var added = 0;
      foreach (var (name, dnsHost) in computers)
      {
        var exists = false;
        foreach (var existing in FleetInventoryItems)
        {
          if (string.Equals(existing.HostName, name, StringComparison.OrdinalIgnoreCase))
          {
            exists = true;
            break;
          }
        }
        if (exists) continue;

        FleetInventoryItems.Add(new FleetInventoryEntry
        {
          HostName = name,
          IpAddress = dnsHost ?? "",
          WinRmStatus = "—",
          LastCheckLabel = "never"
        });
        added++;
      }

      SaveFleetInventory();
      StatusText = $"AD discovery: found {computers.Count} computers, added {added} new to inventory.";
    }
    catch (Exception ex)
    {
      StatusText = "AD discovery failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private void ImportFleetCsv()
  {
    var ofd = new Microsoft.Win32.OpenFileDialog
    {
      Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
      Title = "Import Fleet Targets"
    };

    if (ofd.ShowDialog() != true) return;

    try
    {
      var lines = File.ReadAllLines(ofd.FileName);
      foreach (var line in lines)
      {
        if (string.IsNullOrWhiteSpace(line)) continue;
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;

        if (parts[0].Equals("Hostname", StringComparison.OrdinalIgnoreCase) ||
            parts[0].Equals("Host", StringComparison.OrdinalIgnoreCase))
          continue;

        var hostName = parts[0].Trim();
        var ipAddress = parts.Length > 1 ? parts[1].Trim() : "";

        var exists = false;
        foreach (var existing in FleetInventoryItems)
        {
          if (string.Equals(existing.HostName, hostName, StringComparison.OrdinalIgnoreCase))
          {
            exists = true;
            break;
          }
        }
        if (exists) continue;

        FleetInventoryItems.Add(new FleetInventoryEntry
        {
          HostName = hostName,
          IpAddress = ipAddress,
          WinRmStatus = "—",
          LastCheckLabel = "never"
        });
      }

      SaveFleetInventory();
      StatusText = $"Imported fleet targets from {Path.GetFileName(ofd.FileName)}.";
    }
    catch (Exception ex)
    {
      StatusText = "CSV import failed: " + ex.Message;
    }
  }

  private void AutoFillHostnameDefaults()
  {
    var machineName = Environment.MachineName;

    if (string.IsNullOrWhiteSpace(CklHostName))
      CklHostName = machineName;

    if (string.IsNullOrWhiteSpace(PoamSystemName))
      PoamSystemName = machineName;
  }

  private string ResolveWorkflowLocalImportRoot()
  {
    return Path.Combine(_paths.GetAppDataRoot(), "import");
  }

  private string ResolveWorkflowLocalOutputRoot()
  {
    return Path.Combine(_paths.GetAppDataRoot(), "local-workflow");
  }

  private string ResolveWorkflowLocalMissionJsonPath()
  {
    return Path.Combine(ResolveWorkflowLocalOutputRoot(), "mission.json");
  }

  private void ApplyWorkflowLocalImportDefaults()
  {
    if (string.IsNullOrWhiteSpace(ScanImportFolderPath))
      ScanImportFolderPath = ResolveWorkflowLocalImportRoot();

    if (string.IsNullOrWhiteSpace(MissionJsonPath))
      MissionJsonPath = ResolveWorkflowLocalMissionJsonPath();
  }

  private void SaveFleetInventory()
  {
    try
    {
      var path = GetFleetInventoryPath();
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);

      var entries = new List<FleetInventoryData>();
      foreach (var item in FleetInventoryItems)
      {
        entries.Add(new FleetInventoryData
        {
          HostName = item.HostName,
          IpAddress = item.IpAddress
        });
      }

      var json = System.Text.Json.JsonSerializer.Serialize(entries,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(path, json);
    }
    catch
    {
    }
  }

  private void LoadFleetInventory()
  {
    try
    {
      var path = GetFleetInventoryPath();
      if (!File.Exists(path)) return;

      var json = File.ReadAllText(path);
      var entries = System.Text.Json.JsonSerializer.Deserialize<List<FleetInventoryData>>(json,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      if (entries == null) return;

      System.Windows.Application.Current.Dispatcher.Invoke(() =>
      {
        FleetInventoryItems.Clear();
        foreach (var entry in entries)
        {
          FleetInventoryItems.Add(new FleetInventoryEntry
          {
            HostName = entry.HostName ?? "",
            IpAddress = entry.IpAddress ?? "",
            WinRmStatus = "—",
            LastCheckLabel = "never"
          });
        }
      });

      var targetStrings = new List<string>();
      foreach (var entry in entries)
      {
        if (string.IsNullOrWhiteSpace(entry.HostName)) continue;
        targetStrings.Add(string.IsNullOrWhiteSpace(entry.IpAddress)
          ? entry.HostName
          : $"{entry.HostName}:{entry.IpAddress}");
      }
      if (targetStrings.Count > 0 && string.IsNullOrWhiteSpace(FleetTargets))
        FleetTargets = string.Join(",", targetStrings);
    }
    catch
    {
    }
  }

  private static string GetFleetInventoryPath()
  {
    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "STIGForge", "fleet-inventory.json");
  }

  private sealed class FleetInventoryData
  {
    public string? HostName { get; set; }
    public string? IpAddress { get; set; }
  }
}

public sealed class FleetInventoryEntry : ObservableObject
{
  private string _hostName = "";
  private string _ipAddress = "";
  private string _winRmStatus = "—";
  private string _lastCheckLabel = "never";

  public string HostName
  {
    get => _hostName;
    set => SetProperty(ref _hostName, value);
  }

  public string IpAddress
  {
    get => _ipAddress;
    set => SetProperty(ref _ipAddress, value);
  }

  public string WinRmStatus
  {
    get => _winRmStatus;
    set => SetProperty(ref _winRmStatus, value);
  }

  public string LastCheckLabel
  {
    get => _lastCheckLabel;
    set => SetProperty(ref _lastCheckLabel, value);
  }
}
