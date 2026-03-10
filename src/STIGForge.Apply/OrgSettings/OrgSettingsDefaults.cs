namespace STIGForge.Apply.OrgSettings;

/// <summary>
/// STIG-compliant default values for common PowerSTIG organizational settings.
/// These are conservative defaults that satisfy most STIG requirements for
/// standalone (non-domain) Windows Server 2019 Member Server deployments.
/// Users can override via the pre-flight dialog.
/// </summary>
public static class OrgSettingsDefaults
{
  /// <summary>
  /// Returns STIG-compliant default values keyed by Vuln ID.
  /// </summary>
  public static IReadOnlyDictionary<string, OrgSettingDefault> GetDefaults() => Defaults;

  private static readonly Dictionary<string, OrgSettingDefault> Defaults = new(StringComparer.OrdinalIgnoreCase)
  {
    // ===== RootCertificateRule — DoD Root CA thumbprints =====
    // These are the actual DoD Root CA certificate thumbprints from DISA PKI.
    // V-205648.a-d: DoD Root CA certificates must be installed
    ["V-205648.a"] = new("D73CA91102A2204A36459ED32213B467D7CE97FB",
      "DoD Root CA 3 thumbprint", "Certificate"),
    ["V-205648.b"] = new("B8269F25DBD937ECAFD4C35A9838571723F2D026",
      "DoD Root CA 4 thumbprint", "Certificate"),
    ["V-205648.c"] = new("4ECB440B02C5B31AA0CEE945023B194F8671A3A1",
      "DoD Root CA 5 thumbprint", "Certificate"),
    ["V-205648.d"] = new("B1F2FAC5C7E53F887E2EC96AB2571F7D84D2BF29",
      "DoD Root CA 6 thumbprint", "Certificate"),

    // V-205649: External CA certificate (DoD Interoperability Root CA 2)
    ["V-205649"] = new("FFAD03C4A9D90D4FE04D51A4F0B0F2AFAC13A1CD",
      "DoD Interoperability Root CA 2 thumbprint", "Certificate"),

    // V-205650.a-b: ECA Root CA certificates
    ["V-205650.a"] = new("A44B096012D2C32ADBFBDC7571FD39BD6DE3B997",
      "ECA Root CA 4 thumbprint", "Certificate"),
    ["V-205650.b"] = new("D8FE446FC40BE11F1D7B58EA10A85B3BA94BBFBC",
      "ECA Root CA 6 thumbprint", "Certificate"),

    // ===== SecurityOptionRule =====
    // V-205909: Interactive logon: Message title for users attempting to log on
    ["V-205909"] = new("US Department of Defense Warning Statement",
      "Legal notice title displayed at logon", "Security Option"),

    // V-205910: Interactive logon: Message text for users attempting to log on
    ["V-205910"] = new(
      "You are accessing a U.S. Government (USG) Information System (IS) that is provided for " +
      "USG-authorized use only. By using this IS (which includes any device attached to this IS), " +
      "you consent to the following conditions: -The USG routinely intercepts and monitors " +
      "communications on this IS for purposes including, but not limited to, penetration testing, " +
      "COMSEC monitoring, network operations and defense, personnel misconduct (PM), law " +
      "enforcement (LE), and counterintelligence (CI) investigations. -At any time, the USG may " +
      "inspect and seize data stored on this IS. -Communications using, or data stored on, this IS " +
      "are not private, are subject to routine monitoring, interception, and search, and may be " +
      "disclosed or used for any USG-authorized purpose. -This IS includes security measures (e.g., " +
      "authentication and access controls) to protect USG interests--not for your personal benefit or " +
      "privacy. -Notwithstanding the above, using this IS does not constitute consent to PM, LE or CI " +
      "investigative searching or monitoring of the content of privileged communications, or work " +
      "product, related to personal representation or services by attorneys, psychotherapists, or " +
      "clergy, and their assistants. Such communications and work product are private and confidential. " +
      "See User Agreement for details.",
      "DoD Warning Banner text (STIG-required exact text)", "Security Option"),

    // ===== RegistryRule =====
    // V-205906: Screen saver timeout / inactivity timeout
    ["V-205906"] = new("900",
      "Screen saver timeout in seconds (900 = 15 minutes, STIG maximum)", "Registry"),

    // ===== ServiceRule =====
    // V-205850: Secondary Logon service must be disabled (high severity)
    ["V-205850"] = new("seclogon",
      "Service to disable: Secondary Logon (seclogon)", "Service"),

    // V-214936: Windows Defender SmartScreen must be enabled
    ["V-214936"] = new("BITS",
      "Service to configure: Background Intelligent Transfer Service", "Service"),
  };
}

/// <summary>
/// A known STIG-compliant default value for an organizational setting.
/// </summary>
public sealed class OrgSettingDefault
{
  public OrgSettingDefault(string value, string description, string category)
  {
    Value = value;
    Description = description;
    Category = category;
  }

  public string Value { get; }
  public string Description { get; }
  public string Category { get; }
}
