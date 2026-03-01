using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

/// <summary>
/// Parses GptTmpl.inf security templates from DISA STIG GPO packages.
/// These INF files contain Windows security settings organized by section:
/// [System Access] - account policies (password length, lockout, etc.)
/// [Event Audit] - audit policy settings
/// [Privilege Rights] - user rights assignments (SeRemoteInteractiveLogonRight, etc.)
/// [Registry Values] - explicit registry value settings
/// </summary>
public static class GptTmplParser
{
    private static readonly HashSet<string> ParsedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Access",
        "Event Audit",
        "Privilege Rights",
        "Registry Values"
    };

    public static GptTmplParseResult Parse(string infPath, string packName, OsTarget osTarget = OsTarget.Unknown)
    {
        if (!File.Exists(infPath))
            throw new FileNotFoundException("GptTmpl.inf not found", infPath);

        var lines = File.ReadAllLines(infPath);
        var records = new List<ControlRecord>();
        var rawSettings = new List<GptTmplSetting>();
        string? currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line.Substring(1, line.Length - 2).Trim();
                continue;
            }

            if (currentSection == null || !ParsedSections.Contains(currentSection))
                continue;

            var setting = ParseSetting(line, currentSection);
            if (setting == null)
                continue;

            rawSettings.Add(setting);
            records.Add(BuildControlRecord(setting, currentSection, packName, osTarget));
        }

        return new GptTmplParseResult
        {
            Controls = records,
            Settings = rawSettings,
            SourcePath = infPath
        };
    }

    private static GptTmplSetting? ParseSetting(string line, string section)
    {
        var eqIndex = line.IndexOf('=');
        if (eqIndex < 0)
            return null;

        var key = line.Substring(0, eqIndex).Trim();
        var value = line.Substring(eqIndex + 1).Trim();

        if (string.IsNullOrWhiteSpace(key))
            return null;

        return new GptTmplSetting
        {
            Section = section,
            Key = key,
            Value = value
        };
    }

    private static ControlRecord BuildControlRecord(
        GptTmplSetting setting, string section, string packName, OsTarget osTarget)
    {
        var severity = InferSeverity(section, setting.Key);
        var title = BuildTitle(section, setting);
        var checkText = BuildCheckText(section, setting);
        var fixText = BuildFixText(section, setting);

        return new ControlRecord
        {
            ControlId = Guid.NewGuid().ToString("n"),
            ExternalIds = new ExternalIds
            {
                RuleId = $"GPO_{section.Replace(" ", "")}_{setting.Key}",
                BenchmarkId = $"gpo-security-template"
            },
            Title = title,
            Severity = severity,
            Discussion = $"Security template setting in [{section}]",
            CheckText = checkText,
            FixText = fixText,
            IsManual = false,
            Applicability = new Applicability
            {
                OsTarget = osTarget,
                RoleTags = Array.Empty<RoleTemplate>(),
                ClassificationScope = ScopeTag.Unknown,
                Confidence = Confidence.High
            },
            Revision = new RevisionInfo
            {
                PackName = packName
            }
        };
    }

    private static string InferSeverity(string section, string key)
    {
        // Account lockout and password policies are typically CAT II
        if (string.Equals(section, "System Access", StringComparison.OrdinalIgnoreCase))
        {
            if (key.IndexOf("LockoutBadCount", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("MinimumPasswordLength", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("PasswordComplexity", StringComparison.OrdinalIgnoreCase) >= 0)
                return "high";
        }

        // Audit settings are typically CAT II
        if (string.Equals(section, "Event Audit", StringComparison.OrdinalIgnoreCase))
            return "medium";

        // Privilege rights with remote access are CAT I
        if (string.Equals(section, "Privilege Rights", StringComparison.OrdinalIgnoreCase))
        {
            if (key.IndexOf("SeRemoteInteractiveLogonRight", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("SeTcbPrivilege", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("SeDebugPrivilege", StringComparison.OrdinalIgnoreCase) >= 0)
                return "high";
        }

        return "medium";
    }

    private static string BuildTitle(string section, GptTmplSetting setting)
    {
        return section switch
        {
            "System Access" => $"Account Policy: {HumanizeKey(setting.Key)} = {setting.Value}",
            "Event Audit" => $"Audit Policy: {HumanizeKey(setting.Key)} = {MapAuditValue(setting.Value)}",
            "Privilege Rights" => $"User Right: {HumanizeKey(setting.Key)}",
            "Registry Values" => $"Registry Security: {setting.Key}",
            _ => $"{section}: {setting.Key} = {setting.Value}"
        };
    }

    private static string BuildCheckText(string section, GptTmplSetting setting)
    {
        return section switch
        {
            "System Access" => $"Verify the security setting '{setting.Key}' is configured to '{setting.Value}' in Local Security Policy > Account Policies.",
            "Event Audit" => $"Verify audit policy '{setting.Key}' is set to '{MapAuditValue(setting.Value)}' in Local Security Policy > Audit Policy.",
            "Privilege Rights" => $"Verify user right '{setting.Key}' is assigned only to: {setting.Value}",
            "Registry Values" => $"Verify registry value at '{setting.Key}' is set to the required security value.",
            _ => $"Verify [{section}] setting '{setting.Key}' = '{setting.Value}'."
        };
    }

    private static string BuildFixText(string section, GptTmplSetting setting)
    {
        return section switch
        {
            "System Access" => $"Configure Local Security Policy > Account Policies: Set '{HumanizeKey(setting.Key)}' to '{setting.Value}'.",
            "Event Audit" => $"Configure Local Security Policy > Audit Policy: Set '{HumanizeKey(setting.Key)}' to '{MapAuditValue(setting.Value)}'.",
            "Privilege Rights" => $"Configure Local Security Policy > User Rights Assignment: Set '{HumanizeKey(setting.Key)}' to include only: {setting.Value}.",
            "Registry Values" => $"Apply the security registry value at '{setting.Key}'.",
            _ => $"Configure [{section}] setting '{setting.Key}' = '{setting.Value}'."
        };
    }

    private static string HumanizeKey(string key)
    {
        // Insert spaces before capitals: MinimumPasswordLength -> Minimum Password Length
        var result = new System.Text.StringBuilder(key.Length + 8);
        for (var i = 0; i < key.Length; i++)
        {
            if (i > 0 && char.IsUpper(key[i]) && char.IsLower(key[i - 1]))
                result.Append(' ');
            result.Append(key[i]);
        }
        return result.ToString();
    }

    private static string MapAuditValue(string value)
    {
        return value.Trim() switch
        {
            "0" => "No Auditing",
            "1" => "Success",
            "2" => "Failure",
            "3" => "Success and Failure",
            _ => value
        };
    }
}

public sealed class GptTmplSetting
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class GptTmplParseResult
{
    public IReadOnlyList<ControlRecord> Controls { get; set; } = Array.Empty<ControlRecord>();
    public IReadOnlyList<GptTmplSetting> Settings { get; set; } = Array.Empty<GptTmplSetting>();
    public string SourcePath { get; set; } = string.Empty;
}
