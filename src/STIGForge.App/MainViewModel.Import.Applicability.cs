using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.IO;
using STIGForge.App.Views;
using STIGForge.Core.Constants;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class MainViewModel
{
  // ── Content Picker ─────────────────────────────────────────────────

  [RelayCommand]
  private async Task OpenContentPicker()
  {
    if (ContentPacks.Count == 0)
    {
      StatusText = "No content imported yet. Import STIG, SCAP, or GPO packages first.";
      return;
    }

    try
    {
      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);
      await RefreshApplicablePackIdsAsync(info);
    }
    catch
    {
    }

    var items = ContentPacks.Select(pack => new ContentPickerItem
    {
      PackId = pack.PackId,
      Name = pack.Name,
      Format = ResolvePackFormat(pack),
      SourceLabel = pack.SourceLabel,
      ImportedAtLabel = pack.ImportedAt.ToString("yyyy-MM-dd HH:mm"),
      IsSelected = SelectedMissionPacks.Any(s => s.PackId == pack.PackId)
    }).ToList();

    var dialog = new ContentPickerDialog(items, ApplicablePackIds);
    dialog.Owner = System.Windows.Application.Current.MainWindow;
    if (dialog.ShowDialog() != true) return;

    var selectedIds = new HashSet<string>(dialog.SelectedPackIds, StringComparer.OrdinalIgnoreCase);
    SelectedMissionPacks.Clear();
    foreach (var pack in ContentPacks)
    {
      if (selectedIds.Contains(pack.PackId))
        SelectedMissionPacks.Add(pack);
    }

    if (SelectedMissionPacks.Count == 0)
    {
      SelectedPack = null;
      SelectedContentSummary = "No content selected.";
    }
    else if (SelectedMissionPacks.Count == 1)
    {
      SelectedPack = SelectedMissionPacks[0];
      SelectedContentSummary = "Selected: " + SelectedMissionPacks[0].Name;
    }
    else
    {
      SelectedPack = SelectedMissionPacks[0];
      SelectedContentSummary = SelectedMissionPacks.Count + " packs selected: "
        + string.Join(", ", SelectedMissionPacks.Select(p => p.Name).Take(3));
      if (SelectedMissionPacks.Count > 3)
        SelectedContentSummary += " + " + (SelectedMissionPacks.Count - 3) + " more";
    }

    StatusText = "Content selection updated.";
  }

  // ── Machine Applicability Scan ──────────────────────────────────────

  [RelayCommand]
  private async Task ScanMachineApplicabilityAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      MachineApplicabilityStatus = "Scanning machine...";
      MachineApplicablePacks = "";
      MachineRecommendations = "";

      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);

      var machineLines = new List<string>
      {
        "Machine: " + info.Hostname,
        "OS: " + info.ProductName + " (Build " + info.BuildNumber + ")",
        "Role: " + info.Role,
        "Detected target: " + info.OsTarget
      };

      if (info.IsServer && info.InstalledFeatures.Count > 0)
      {
        machineLines.Add("Server features: " + string.Join(", ", info.InstalledFeatures.Take(10)));
        if (info.InstalledFeatures.Count > 10)
          machineLines.Add("  ... and " + (info.InstalledFeatures.Count - 10) + " more");
      }

      MachineApplicabilityStatus = string.Join("\n", machineLines);

      var filteredPacks = await RefreshApplicablePackIdsAsync(info);
      var packLines = new List<string>();
      if (ContentPacks.Count > 0)
      {
        var applicable = filteredPacks.Select(p => p.Name).ToList();
        if (applicable.Count > 0)
        {
          packLines.Add("Applicable imported packs (" + applicable.Count + "):");
          foreach (var name in applicable)
            packLines.Add("  " + name);
        }
        else
        {
          packLines.Add("No imported packs match this machine.");
          packLines.Add("Import the applicable STIG content above.");
        }
      }
      else
      {
        packLines.Add("No content packs imported yet.");
        packLines.Add("Import STIGs above, then re-scan.");
      }

      MachineApplicablePacks = string.Join("\n", packLines);

      var recommendations = GetStigRecommendations(info);
      MachineRecommendations = recommendations.Count > 0
        ? string.Join("\n", recommendations)
        : "";

      StatusText = "Machine scan complete: " + info.OsTarget + " / " + info.Role;
    }
    catch (Exception ex)
    {
      MachineApplicabilityStatus = "Scan failed: " + ex.Message;
      MachineApplicablePacks = "";
      MachineRecommendations = "";
      StatusText = "Machine scan failed.";
    }
    finally
    {
      IsBusy = false;
    }
  }

  private MachineInfo DetectMachineInfo()
  {
    var info = new MachineInfo { Hostname = Environment.MachineName };

    // Read OS info from registry
    try
    {
      using var ntKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      if (ntKey != null)
      {
        info.ProductName = ntKey.GetValue("ProductName") as string ?? "Unknown Windows";
        info.BuildNumber = ntKey.GetValue("CurrentBuildNumber") as string
                           ?? ntKey.GetValue("CurrentBuild") as string
                           ?? "0";
        info.EditionId = ntKey.GetValue("EditionID") as string ?? "";
        info.DisplayVersion = ntKey.GetValue("DisplayVersion") as string ?? "";
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
    }

    // Detect product type (Workstation / Server / DC)
    try
    {
      using var prodKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
      var productType = prodKey?.GetValue("ProductType") as string ?? "";
      if (string.Equals(productType, "WinNT", StringComparison.OrdinalIgnoreCase))
      {
        info.Role = "Workstation";
        info.RoleTemplate = RoleTemplate.Workstation;
        info.IsServer = false;
      }
      else if (string.Equals(productType, "LanmanNT", StringComparison.OrdinalIgnoreCase))
      {
        info.Role = "Domain Controller";
        info.RoleTemplate = RoleTemplate.DomainController;
        info.IsServer = true;
      }
      else if (string.Equals(productType, "ServerNT", StringComparison.OrdinalIgnoreCase))
      {
        info.Role = "Member Server";
        info.RoleTemplate = RoleTemplate.MemberServer;
        info.IsServer = true;
      }
      else
      {
        info.Role = "Unknown (" + productType + ")";
        info.RoleTemplate = RoleTemplate.Workstation;
        info.IsServer = false;
      }
    }
    catch
    {
      info.Role = "Unknown";
      info.RoleTemplate = RoleTemplate.Workstation;
    }

    // Map build number to OsTarget
    info.OsTarget = MapBuildToOsTarget(info.BuildNumber, info.IsServer, info.EditionId);

    // Detect installed server features (IIS, DNS, DHCP, etc.)
    if (info.IsServer)
    {
      try
      {
        using var featureKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\ServerManager\ServicingStorage\ServerComponentCache");
        if (featureKey != null)
        {
          foreach (var subName in featureKey.GetSubKeyNames())
          {
            try
            {
              using var sub = featureKey.OpenSubKey(subName);
              var installState = sub?.GetValue("InstallState");
              if (installState is int state && state == 1)
                info.InstalledFeatures.Add(subName);
            }
            catch (Exception ex)
            {
              System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
      }
    }

    // Detect IIS on workstation
    if (!info.IsServer)
    {
      try
      {
        using var iisKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
        if (iisKey != null)
        {
          var majorVersion = iisKey.GetValue("MajorVersion");
          if (majorVersion != null)
            info.InstalledFeatures.Add("IIS " + majorVersion);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
      }
    }

    // Detect SQL Server instances
    try
    {
      using var sqlKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
      if (sqlKey != null && sqlKey.GetValueNames().Length > 0)
        info.InstalledFeatures.Add("SQL Server");
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
    }

    // Detect .NET Framework version
    try
    {
      using var ndpKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
      var release = ndpKey?.GetValue("Release") as int?;
      if (release != null)
        info.InstalledFeatures.Add(".NET Framework 4.x");
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
    }

    // Detect Google Chrome
    try
    {
      using var chromeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
      if (chromeKey != null)
        info.InstalledFeatures.Add("Google Chrome");
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Chrome registry detection failed");
    }

    // Detect Microsoft Edge
    try
    {
      using var edgeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Edge");
      if (edgeKey != null)
      {
        info.InstalledFeatures.Add("Microsoft Edge");
      }
      else
      {
        using var edgeKey64 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Edge");
        if (edgeKey64 != null)
          info.InstalledFeatures.Add("Microsoft Edge");
      }
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Edge registry detection failed");
    }

    // Detect Mozilla Firefox
    try
    {
      using var ffKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Mozilla\Mozilla Firefox");
      if (ffKey != null)
        info.InstalledFeatures.Add("Mozilla Firefox");
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Firefox registry detection failed");
    }

    // Detect Adobe Acrobat / Reader
    try
    {
      using var adobeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\Acrobat Reader");
      using var acrobatKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\Adobe Acrobat");
      if (adobeKey != null || acrobatKey != null)
        info.InstalledFeatures.Add("Adobe Acrobat");
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Adobe registry detection failed");
    }

    // Detect Microsoft Office
    try
    {
      using var officeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
      if (officeKey != null)
        info.InstalledFeatures.Add("Microsoft Office");
      else
      {
        using var office16 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\16.0\Common\InstallRoot");
        if (office16 != null)
          info.InstalledFeatures.Add("Microsoft Office");
      }
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Office registry detection failed");
    }

    return info;
  }

  private static OsTarget MapBuildToOsTarget(string buildNumber, bool isServer, string editionId)
  {
    _ = editionId;

    if (!int.TryParse(buildNumber, out var build))
      return OsTarget.Unknown;

    if (isServer)
    {
      // Server 2022: build 20348+
      // Server 2019: build 17763
      // Server 2016: build 14393
      if (build >= 20348) return OsTarget.Server2022;
      if (build >= 17763) return OsTarget.Server2019;
      return OsTarget.Unknown;
    }

    // Workstation
    // Windows 11: build 22000+
    // Windows 10: build 10240–21996
    if (build >= 22000) return OsTarget.Win11;
    if (build >= 10240) return OsTarget.Win10;
    return OsTarget.Unknown;
  }

  private List<ContentPack> PreferHigherVersionStigs(List<ContentPack> packs)
  {
    var grouped = packs
      .GroupBy(pack => BuildFormatScopedProductKey(pack), StringComparer.OrdinalIgnoreCase)
      .ToList();

    var result = new List<ContentPack>();

    foreach (var group in grouped)
    {
      if (group.Count() == 1)
      {
        result.Add(group.First());
      }
      else
      {
        var highest = group
          .OrderByDescending(pack => ExtractVersionTuple(pack.Name))
          .ThenByDescending(pack => pack.ImportedAt)
          .First();
        result.Add(highest);
      }
    }

    return result;
  }

  private string BuildFormatScopedProductKey(ContentPack pack)
  {
    var format = (ResolvePackFormat(pack) ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(format))
      format = "UNKNOWN";

    var product = ExtractProductName(pack.Name ?? string.Empty);
    return format.ToUpperInvariant() + "|" + product;
  }

  private static string ExtractProductName(string packName)
  {
    var match = System.Text.RegularExpressions.Regex.Match(packName, @"^(.+?)\s+V\d+R\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (match.Success)
      return match.Groups[1].Value.Trim();
    return packName;
  }

  private static (int V, int R) ExtractVersionTuple(string packName)
  {
    var match = System.Text.RegularExpressions.Regex.Match(packName, @"V(\d+)R(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (match.Success && int.TryParse(match.Groups[1].Value, out var v) && int.TryParse(match.Groups[2].Value, out var r))
      return (v, r);
    return (0, 0);
  }

  private static readonly string[] UniversalPackKeywords =
  {
    "Defender", "Windows Firewall", "Firewall with Advanced Security",
    "Microsoft Edge", ".NET Framework", "DotNet"
  };

  private async Task<bool> IsPackApplicableAsync(ContentPack pack, MachineInfo info)
  {
    var name = (pack.Name + " " + pack.SourceLabel).Replace('_', ' ');

    // 1. Control-level OsTarget match (positive only - never early-exit false)
    try
    {
      var controls = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
      if (controls.Count > 0)
      {
        var packOsTargets = controls
          .Select(c => c.Applicability.OsTarget)
          .Where(t => t != OsTarget.Unknown)
          .Distinct()
          .ToList();

        if (packOsTargets.Count > 0 && packOsTargets.Contains(info.OsTarget))
          return true;
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Pack applicability check failed: " + ex.Message);
    }

    // 2. OS label matching (works for STIG, SCAP, GPO, ADMX - any format)
    var osLabels = info.OsTarget switch
    {
      OsTarget.Win11 => new[] { "Windows 11", "Win11" },
      OsTarget.Win10 => new[] { "Windows 10", "Win10" },
      OsTarget.Server2022 => new[] { "Server 2022" },
      OsTarget.Server2019 => new[] { "Server 2019" },
      _ => Array.Empty<string>()
    };

    foreach (var label in osLabels)
    {
      if (name.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    // 3. Universal packs
    foreach (var keyword in UniversalPackKeywords)
    {
      if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    // 4. Role-based matching
    if (info.RoleTemplate == RoleTemplate.DomainController
        && name.IndexOf("Domain Controller", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (info.RoleTemplate == RoleTemplate.MemberServer
        && name.IndexOf("Member Server", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (info.RoleTemplate == RoleTemplate.DomainController
        && (name.IndexOf("Active Directory", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("AD Domain", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("AD Forest", StringComparison.OrdinalIgnoreCase) >= 0))
      return true;

    // 5. Feature-based matching
    foreach (var feature in info.InstalledFeatures)
    {
      if (feature.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    // 6. GPO / ADMX / LocalPolicy must match machine OS
    var format = ResolvePackFormat(pack);
    var isGpoFormat = string.Equals(format, PackTypes.Gpo, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(format, PackTypes.Admx, StringComparison.OrdinalIgnoreCase);
    var isLocalPolicy = pack.SourceLabel != null
      && pack.SourceLabel.IndexOf("/LocalPolicy", StringComparison.OrdinalIgnoreCase) >= 0;

    if (isGpoFormat || isLocalPolicy)
    {
      foreach (var label in osLabels)
      {
        if (name.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
      }

      return false;
    }

    return false;
  }

  private static List<string> GetStigRecommendations(MachineInfo info)
  {
    var recs = new List<string>();

    var osLabel = info.OsTarget switch
    {
      OsTarget.Win11 => "Windows 11",
      OsTarget.Win10 => "Windows 10",
      OsTarget.Server2022 => "Windows Server 2022",
      OsTarget.Server2019 => "Windows Server 2019",
      _ => null
    };

    var roleTag = info.RoleTemplate switch
    {
      RoleTemplate.DomainController when info.IsServer => " Domain Controller",
      RoleTemplate.MemberServer when info.IsServer => " Member Server",
      _ => ""
    };

    recs.Add("[STIGs]");
    if (osLabel != null)
      recs.Add("  Microsoft " + osLabel + roleTag + " STIG");
    recs.Add("  Microsoft Windows Defender Antivirus STIG");
    recs.Add("  Microsoft Windows Firewall with Advanced Security STIG");
    recs.Add("  Microsoft Edge STIG");
    recs.Add("  Microsoft .NET Framework 4.0 STIG");

    foreach (var feature in info.InstalledFeatures)
    {
      if (feature.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        recs.Add("  Microsoft IIS 10.0 Site STIG");
        recs.Add("  Microsoft IIS 10.0 Server STIG");
      }
      if (feature.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0)
        recs.Add("  Microsoft Windows DNS Server STIG");
      if (feature.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
        recs.Add("  Microsoft SQL Server STIG");
      if (feature.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0)
        recs.Add("  Microsoft Windows DHCP Server STIG");
    }

    if (info.RoleTemplate == RoleTemplate.DomainController)
    {
      recs.Add("  Active Directory Domain STIG");
      recs.Add("  Active Directory Forest STIG");
    }

    recs.Add("");
    recs.Add("[SCAP Benchmarks]");
    if (osLabel != null)
      recs.Add("  " + osLabel + roleTag + " SCAP Benchmark");
    recs.Add("  Defender / Firewall / Edge / .NET benchmarks (if imported)");

    recs.Add("");
    recs.Add("");
    recs.Add("[GPO / ADMX]");
    if (osLabel != null)
      recs.Add("  " + osLabel + " Security Baseline GPO");
    recs.Add("  Defender / Edge / Firewall ADMX templates (if imported)");

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normalized = new List<string>(recs.Count);
    foreach (var rec in recs)
    {
      if (string.IsNullOrWhiteSpace(rec))
      {
        normalized.Add(string.Empty);
        continue;
      }

      if (seen.Add(rec))
        normalized.Add(rec);
    }

    return normalized;
  }

  private async Task<List<ContentPack>> RefreshApplicablePackIdsAsync(MachineInfo info)
  {
    ApplicablePackIds.Clear();
    if (ContentPacks.Count == 0)
      return new List<ContentPack>();

    var applicablePacks = new List<ContentPack>();
    foreach (var pack in ContentPacks)
    {
      if (await IsPackApplicableAsync(pack, info))
        applicablePacks.Add(pack);
    }

    var filteredPacks = PreferHigherVersionStigs(applicablePacks);
    foreach (var pack in filteredPacks)
    {
      var packId = (pack.PackId ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(packId))
        ApplicablePackIds.Add(packId);
    }

    return filteredPacks;
  }

  private async Task AutoPopulateApplicablePacksAsync()
  {
    if (ContentPacks.Count == 0) return;
    try
    {
      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);
      await RefreshApplicablePackIdsAsync(info);
    }
    catch
    {
    }
  }

  private sealed class MachineInfo
  {
    public string Hostname { get; set; } = "";
    public string ProductName { get; set; } = "Unknown Windows";
    public string BuildNumber { get; set; } = "0";
    public string EditionId { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Role { get; set; } = "Unknown";
    public RoleTemplate RoleTemplate { get; set; }
    public OsTarget OsTarget { get; set; } = OsTarget.Unknown;
    public bool IsServer { get; set; }
    public List<string> InstalledFeatures { get; set; } = new();
  }
}
