using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class PackApplicabilityInput
{
  public string PackName { get; set; } = string.Empty;
  public string SourceLabel { get; set; } = string.Empty;
  public string Format { get; set; } = string.Empty;
  public OsTarget MachineOs { get; set; } = OsTarget.Unknown;
  public RoleTemplate MachineRole { get; set; } = RoleTemplate.Workstation;
  public IReadOnlyList<string> InstalledFeatures { get; set; } = Array.Empty<string>();
  public IReadOnlyList<OsTarget> ControlOsTargets { get; set; } = Array.Empty<OsTarget>();
  public IReadOnlyList<string> HostSignals { get; set; } = Array.Empty<string>();
}

public enum ApplicabilityState
{
  Applicable,
  NotApplicable,
  Unknown
}

public enum ApplicabilityConfidence
{
  High,
  Low
}

public sealed class PackApplicabilityDecision
{
  public ApplicabilityState State { get; set; }
  public ApplicabilityConfidence Confidence { get; set; }
  public string ReasonCode { get; set; } = string.Empty;
  public IReadOnlyList<string> Evidence { get; set; } = Array.Empty<string>();
}

public static class PackApplicabilityRules
{
  public static bool IsApplicable(PackApplicabilityInput input)
  {
    if (input == null)
      throw new ArgumentNullException(nameof(input));

    return Evaluate(input).State == ApplicabilityState.Applicable;
  }

  public static PackApplicabilityDecision Evaluate(PackApplicabilityInput input)
  {
    if (input == null)
      throw new ArgumentNullException(nameof(input));

    var name = ((input.PackName ?? string.Empty) + " " + (input.SourceLabel ?? string.Empty)).Replace('_', ' ');
    var format = input.Format ?? string.Empty;
    var machineIsWindowsHost = input.MachineOs == OsTarget.Win11
      || input.MachineOs == OsTarget.Win10
      || input.MachineOs == OsTarget.Server2022
      || input.MachineOs == OsTarget.Server2019;

    if (machineIsWindowsHost
      && name.IndexOf("android", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      return CreateDecision(
        ApplicabilityState.NotApplicable,
        ApplicabilityConfidence.High,
        "android_on_windows",
        new[] { "Android pack excluded on Windows host." });
    }

    if (machineIsWindowsHost && IsFirewallDeviceScopePack(name))
    {
      return CreateDecision(
        ApplicabilityState.NotApplicable,
        ApplicabilityConfidence.High,
        "firewall_device_scope_mismatch",
        new[] { "Firewall device SRG/STIG scope does not apply to Windows workstation/server host baselines." });
    }

    if (machineIsWindowsHost && IsSymEdgeDspPack(name))
    {
      return CreateDecision(
        ApplicabilityState.Unknown,
        ApplicabilityConfidence.Low,
        "sym_edge_ambiguous",
        new[] { "Sym Edge DSP frame name matched keyword-only heuristic without strong host product evidence." });
    }

    var hostSignals = NormalizeSignals(input.HostSignals);
    var vendorSpecific = TryEvaluateVendorSpecificPack(name, hostSignals);
    if (vendorSpecific != null)
      return vendorSpecific;

    var packTags = ExtractMatchingTags(name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var packFeatureTags = packTags.Where(IsFeatureTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var machineFeatureTags = GetMachineFeatureTags(input.InstalledFeatures);

    if (packTags.Any(IsOsTag) && !IsPackOsCompatible(packTags, input.MachineOs, requireOsTag: true))
    {
      return CreateDecision(
        ApplicabilityState.NotApplicable,
        ApplicabilityConfidence.High,
        "os_mismatch",
        new[] { "Pack OS tags do not match detected host OS." });
    }

    if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase))
    {
      if (IsGenericMicrosoftAdmxPack(name)
          && (input.MachineOs == OsTarget.Win11 || input.MachineOs == OsTarget.Win10))
      {
        return CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "admx_microsoft_windows",
          new[] { "Generic Microsoft ADMX templates apply to Windows client hosts (Win10/Win11)." });
      }

      if (packFeatureTags.Count == 0)
      {
        var osOnlyMatch = packTags.Any(IsOsTag) && IsPackOsCompatible(packTags, input.MachineOs, requireOsTag: true);
        return osOnlyMatch
          ? CreateDecision(
            ApplicabilityState.Applicable,
            ApplicabilityConfidence.High,
            "admx_os_match",
            new[] { "ADMX OS tags align with host OS." })
          : CreateDecision(
            ApplicabilityState.NotApplicable,
            ApplicabilityConfidence.High,
            "admx_no_feature_match",
            new[] { "ADMX pack requires feature or OS match that was not found." });
      }

      var overlaps = packFeatureTags.Intersect(machineFeatureTags, StringComparer.OrdinalIgnoreCase).ToList();
      return overlaps.Count > 0
        ? CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "admx_feature_match",
          new[] { "Matched host feature tags: " + string.Join(", ", overlaps) })
        : CreateDecision(
          ApplicabilityState.NotApplicable,
          ApplicabilityConfidence.High,
          "admx_feature_mismatch",
          new[] { "ADMX feature tags did not match detected host features." });
    }

    if (IsDomainGpoPack(input.PackName, input.SourceLabel, format))
    {
      if (input.MachineRole == RoleTemplate.DomainController)
      {
        return CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "domain_gpo_dc_role_match",
          new[] { "Domain GPO scope matched domain controller role." });
      }

      return CreateDecision(
        ApplicabilityState.Unknown,
        ApplicabilityConfidence.Low,
        "domain_gpo_requires_domain_context",
        new[] { "Domain GPO requires domain controller context for high-confidence applicability." });
    }

    if (IsSecurityBaselinePack(name, format))
    {
      if (packFeatureTags.Count == 0)
      {
        var osCompatible = !packTags.Any(IsOsTag) || IsPackOsCompatible(packTags, input.MachineOs, requireOsTag: true);
        return osCompatible
          ? CreateDecision(
            ApplicabilityState.Applicable,
            ApplicabilityConfidence.High,
            "gpo_baseline_os_match",
            new[] { "Baseline GPO aligns with host OS scope." })
          : CreateDecision(
            ApplicabilityState.NotApplicable,
            ApplicabilityConfidence.High,
            "gpo_baseline_os_mismatch",
            new[] { "Baseline GPO OS scope does not match host OS." });
      }

      var overlaps = packFeatureTags.Intersect(machineFeatureTags, StringComparer.OrdinalIgnoreCase).ToList();
      return overlaps.Count > 0
        ? CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "gpo_baseline_feature_match",
          new[] { "Matched baseline feature tags: " + string.Join(", ", overlaps) })
        : CreateDecision(
          ApplicabilityState.NotApplicable,
          ApplicabilityConfidence.High,
          "gpo_baseline_feature_mismatch",
          new[] { "Baseline feature tags did not match detected host features." });
    }

    var explicitControlTargets = (input.ControlOsTargets ?? Array.Empty<OsTarget>())
      .Where(t => t != OsTarget.Unknown)
      .Distinct()
      .ToList();

    if (explicitControlTargets.Count > 0)
    {
      if (explicitControlTargets.Contains(input.MachineOs))
      {
        return CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "control_target_match",
          new[] { "Control applicability targets include detected host OS." });
      }

      if (packTags.Any(IsOsTag))
      {
        return CreateDecision(
          ApplicabilityState.NotApplicable,
          ApplicabilityConfidence.High,
          "control_target_mismatch",
          new[] { "Control applicability targets do not include detected host OS." });
      }
    }

    if (packFeatureTags.Count > 0)
    {
      var overlaps = packFeatureTags.Intersect(machineFeatureTags, StringComparer.OrdinalIgnoreCase).ToList();
      if (overlaps.Count > 0)
      {
        return CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "feature_tag_match",
          new[] { "Matched feature tags: " + string.Join(", ", overlaps) });
      }

      return CreateDecision(
        ApplicabilityState.NotApplicable,
        ApplicabilityConfidence.High,
        "feature_tag_mismatch",
        new[] { "Feature-specific pack tags did not match host features." });
    }

    if (packTags.Any(IsOsTag) && IsPackOsCompatible(packTags, input.MachineOs, requireOsTag: true))
    {
      return CreateDecision(
        ApplicabilityState.Applicable,
        ApplicabilityConfidence.High,
        "os_tag_match",
        new[] { "Pack OS tags align with detected host OS." });
    }

    if (input.MachineRole == RoleTemplate.DomainController
        && (name.IndexOf("Domain Controller", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Active Directory", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("AD Domain", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("AD Forest", StringComparison.OrdinalIgnoreCase) >= 0))
    {
      return CreateDecision(
        ApplicabilityState.Applicable,
        ApplicabilityConfidence.High,
        "role_dc_match",
        new[] { "Domain controller role matched pack scope." });
    }

    if (input.MachineRole == RoleTemplate.MemberServer
        && name.IndexOf("Member Server", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      return CreateDecision(
        ApplicabilityState.Applicable,
        ApplicabilityConfidence.High,
        "role_member_server_match",
        new[] { "Member server role matched pack scope." });
    }

    return CreateDecision(
      ApplicabilityState.Unknown,
      ApplicabilityConfidence.Low,
      "insufficient_signal",
      new[] { "No strong host signal found; requires operator confirmation." });
  }

  public static bool IsFeatureTag(string? tag)
  {
    if (string.IsNullOrWhiteSpace(tag))
      return false;

    return string.Equals(tag, "defender", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "firewall", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "edge", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "dotnet", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "iis", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "sql", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "dns", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "dhcp", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "chrome", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "firefox", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "adobe", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "office", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "onedrive", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "android", StringComparison.OrdinalIgnoreCase);
  }

  public static bool IsOsTag(string? tag)
  {
    if (string.IsNullOrWhiteSpace(tag))
      return false;

    return string.Equals(tag, "win11", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "win10", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "server", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "server2025", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "server2022", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "server2019", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "server2016", StringComparison.OrdinalIgnoreCase)
      || string.Equals(tag, "server2012r2", StringComparison.OrdinalIgnoreCase);
  }

  public static bool IsPackOsCompatible(ISet<string> packTags, OsTarget machineOs, bool requireOsTag)
  {
    if (packTags == null)
      throw new ArgumentNullException(nameof(packTags));

    var osTags = packTags.Where(IsOsTag).ToList();
    if (osTags.Count == 0)
      return !requireOsTag;

    var expectedTag = GetOsTag(machineOs);
    if (!string.IsNullOrWhiteSpace(expectedTag)
        && osTags.Contains(expectedTag, StringComparer.OrdinalIgnoreCase))
      return true;

    var machineIsServer = machineOs == OsTarget.Server2022 || machineOs == OsTarget.Server2019;
    if (machineIsServer && osTags.Contains("server", StringComparer.OrdinalIgnoreCase))
      return true;

    return false;
  }

  public static HashSet<string> GetMachineFeatureTags(IEnumerable<string>? installedFeatures)
  {
    var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "defender",
      "firewall",
      "edge",
      "dotnet"
    };

    if (installedFeatures == null)
      return tags;

    foreach (var feature in installedFeatures)
    {
      var value = feature ?? string.Empty;
      if (value.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("iis");
      if (value.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("sql");
      if (value.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("dns");
      if (value.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("dhcp");
      if (value.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("dotnet");
      if (value.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0
          || value.IndexOf("Google", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("chrome");
      if (value.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0
          || value.IndexOf("Mozilla", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("firefox");
      if (value.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("adobe");
      if (value.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0
          || value.IndexOf("M365", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("office");
      if (value.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("onedrive");
      if (value.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("android");
    }

    return tags;
  }

  public static IEnumerable<string> ExtractMatchingTags(string? text)
  {
    var normalized = (text ?? string.Empty).ToLowerInvariant();

    if (normalized.IndexOf("windows 11", StringComparison.Ordinal) >= 0 || normalized.IndexOf("win11", StringComparison.Ordinal) >= 0)
      yield return "win11";
    if (normalized.IndexOf("windows 10", StringComparison.Ordinal) >= 0 || normalized.IndexOf("win10", StringComparison.Ordinal) >= 0)
      yield return "win10";

    if (normalized.IndexOf("windows server", StringComparison.Ordinal) >= 0)
      yield return "server";
    if (normalized.IndexOf("server 2025", StringComparison.Ordinal) >= 0)
      yield return "server2025";
    if (normalized.IndexOf("server 2022", StringComparison.Ordinal) >= 0)
      yield return "server2022";
    if (normalized.IndexOf("server 2019", StringComparison.Ordinal) >= 0)
      yield return "server2019";
    if (normalized.IndexOf("server 2016", StringComparison.Ordinal) >= 0)
      yield return "server2016";
    if (normalized.IndexOf("server 2012 r2", StringComparison.Ordinal) >= 0
        || normalized.IndexOf("server 2012r2", StringComparison.Ordinal) >= 0)
      yield return "server2012r2";

    if (normalized.IndexOf("defender", StringComparison.Ordinal) >= 0)
      yield return "defender";
    if (normalized.IndexOf("windows firewall", StringComparison.Ordinal) >= 0
        || normalized.IndexOf("defender firewall", StringComparison.Ordinal) >= 0
        || normalized.IndexOf("microsoft defender firewall", StringComparison.Ordinal) >= 0)
      yield return "firewall";
    if (normalized.IndexOf("microsoft edge", StringComparison.Ordinal) >= 0
        || normalized.IndexOf("msedge", StringComparison.Ordinal) >= 0)
      yield return "edge";
    if (normalized.IndexOf(".net", StringComparison.Ordinal) >= 0 || normalized.IndexOf("dotnet", StringComparison.Ordinal) >= 0)
      yield return "dotnet";
    if (normalized.IndexOf("iis", StringComparison.Ordinal) >= 0)
      yield return "iis";
    if (normalized.IndexOf("sql", StringComparison.Ordinal) >= 0)
      yield return "sql";
    if (normalized.IndexOf("dns", StringComparison.Ordinal) >= 0)
      yield return "dns";
    if (normalized.IndexOf("dhcp", StringComparison.Ordinal) >= 0)
      yield return "dhcp";
    if (normalized.IndexOf("android", StringComparison.Ordinal) >= 0)
      yield return "android";
    if (normalized.IndexOf("chrome", StringComparison.Ordinal) >= 0
      || (normalized.IndexOf("google", StringComparison.Ordinal) >= 0 && normalized.IndexOf("android", StringComparison.Ordinal) < 0))
      yield return "chrome";
    if (normalized.IndexOf("firefox", StringComparison.Ordinal) >= 0 || normalized.IndexOf("mozilla", StringComparison.Ordinal) >= 0)
      yield return "firefox";
    if (normalized.IndexOf("adobe", StringComparison.Ordinal) >= 0)
      yield return "adobe";
    if (normalized.IndexOf("office", StringComparison.Ordinal) >= 0 || normalized.IndexOf("m365", StringComparison.Ordinal) >= 0)
      yield return "office";
    if (normalized.IndexOf("onedrive", StringComparison.Ordinal) >= 0)
      yield return "onedrive";
  }

  private static PackApplicabilityDecision? TryEvaluateVendorSpecificPack(string name, ISet<string> hostSignals)
  {
    var normalizedName = (name ?? string.Empty).ToLowerInvariant();

    var fortiTokens = new[] { "fortigate", "forticlient", "fortinet" };
    var symantecTokens = new[] { "symantec", "endpoint protection", "sep" };

    if (ContainsAny(normalizedName, fortiTokens))
    {
      var matched = FindMatchingSignals(hostSignals, fortiTokens).ToList();
      if (matched.Count > 0)
      {
        return CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "fortigate_signal_match",
          new[] { "Detected FortiGate/FortiClient evidence: " + string.Join(", ", matched) });
      }

      return CreateDecision(
        ApplicabilityState.Unknown,
        ApplicabilityConfidence.Low,
        "fortigate_signal_missing",
        new[] { "No FortiGate/FortiClient service, registry, or file evidence detected." });
    }

    if (ContainsAny(normalizedName, symantecTokens))
    {
      var matched = FindMatchingSignals(hostSignals, symantecTokens).ToList();
      if (matched.Count > 0)
      {
        return CreateDecision(
          ApplicabilityState.Applicable,
          ApplicabilityConfidence.High,
          "symantec_signal_match",
          new[] { "Detected Symantec Endpoint evidence: " + string.Join(", ", matched) });
      }

      return CreateDecision(
        ApplicabilityState.Unknown,
        ApplicabilityConfidence.Low,
        "symantec_signal_missing",
        new[] { "No Symantec Endpoint service, registry, or file evidence detected." });
    }

    return null;
  }

  private static HashSet<string> NormalizeSignals(IEnumerable<string>? signals)
  {
    var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (signals == null)
      return normalized;

    foreach (var signal in signals)
    {
      if (string.IsNullOrWhiteSpace(signal))
        continue;

      normalized.Add(signal.Trim().ToLowerInvariant());
    }

    return normalized;
  }

  private static IEnumerable<string> FindMatchingSignals(ISet<string> hostSignals, IReadOnlyList<string> tokens)
  {
    foreach (var signal in hostSignals)
    {
      if (ContainsAny(signal, tokens))
        yield return signal;
    }
  }

  private static bool ContainsAny(string text, IReadOnlyList<string> tokens)
  {
    foreach (var token in tokens)
    {
      if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    return false;
  }

  private static PackApplicabilityDecision CreateDecision(
    ApplicabilityState state,
    ApplicabilityConfidence confidence,
    string reasonCode,
    IReadOnlyList<string> evidence)
  {
    return new PackApplicabilityDecision
    {
      State = state,
      Confidence = confidence,
      ReasonCode = reasonCode,
      Evidence = evidence
    };
  }

  private static bool IsSecurityBaselinePack(string name, string format)
  {
    if (!string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase))
      return false;

    return name.IndexOf("Local Policy", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("Baseline", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("LGPO", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsDomainGpoPack(string packName, string sourceLabel, string format)
  {
    if (!string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase))
      return false;

    if (string.Equals(sourceLabel, "gpo_domain_import", StringComparison.OrdinalIgnoreCase))
      return true;

    var normalized = (packName ?? string.Empty).ToLowerInvariant();
    return normalized.IndexOf("domain gpo", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("active directory domain", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("ad domain", StringComparison.Ordinal) >= 0;
  }

  private static bool IsFirewallDeviceScopePack(string name)
  {
    var normalized = (name ?? string.Empty).ToLowerInvariant();
    if (normalized.IndexOf("firewall", StringComparison.Ordinal) < 0)
      return false;

    var hasDeviceScope = normalized.IndexOf("device", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("network", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("appliance", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("srg", StringComparison.Ordinal) >= 0;

    var isWindowsFirewall = normalized.IndexOf("windows firewall", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("defender firewall", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("microsoft defender firewall", StringComparison.Ordinal) >= 0;

    return hasDeviceScope && !isWindowsFirewall;
  }

  private static bool IsSymEdgeDspPack(string name)
  {
    var normalized = (name ?? string.Empty).ToLowerInvariant();
    return normalized.IndexOf("sym edge", StringComparison.Ordinal) >= 0
      || (normalized.IndexOf("dsp", StringComparison.Ordinal) >= 0
          && normalized.IndexOf("edge", StringComparison.Ordinal) >= 0);
  }

  private static bool IsGenericMicrosoftAdmxPack(string name)
  {
    var normalized = (name ?? string.Empty).ToLowerInvariant();
    if (normalized.IndexOf("admx", StringComparison.Ordinal) < 0
        && normalized.IndexOf("templates", StringComparison.Ordinal) < 0)
      return false;

    if (normalized.IndexOf("microsoft", StringComparison.Ordinal) < 0)
      return false;

    var hasSpecificFeature = normalized.IndexOf("office", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("onedrive", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("edge", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("defender", StringComparison.Ordinal) >= 0
      || normalized.IndexOf("firewall", StringComparison.Ordinal) >= 0;

    return !hasSpecificFeature;
  }

  private static string GetOsTag(OsTarget target)
  {
    return target switch
    {
      OsTarget.Win11 => "win11",
      OsTarget.Win10 => "win10",
      OsTarget.Server2022 => "server2022",
      OsTarget.Server2019 => "server2019",
      _ => string.Empty
    };
  }
}
