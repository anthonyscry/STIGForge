using System.Text;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

/// <summary>
/// Parses Windows Registry.pol binary policy files from DISA STIG GPO packages.
/// The .pol format is a binary file with a 4-byte signature (PReg) followed by
/// 4-byte version, then a sequence of registry entries each enclosed in [ ].
/// Each entry contains: key;value;type;size;data as null-terminated UTF-16LE strings.
///
/// Reference: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/policy/registry-policy-file-format
/// </summary>
public static class PolFileParser
{
    private const uint PolSignature = 0x67655250; // "PReg" in little-endian
    private const uint PolVersion = 1;

    public static PolFileParseResult Parse(string polPath, string packName, OsTarget osTarget = OsTarget.Unknown)
    {
        if (!File.Exists(polPath))
            throw new FileNotFoundException("Registry.pol file not found", polPath);

        var bytes = File.ReadAllBytes(polPath);
        if (bytes.Length < 8)
            return new PolFileParseResult { SourcePath = polPath, Warnings = { "File too small to be a valid .pol file." } };

        var signature = BitConverter.ToUInt32(bytes, 0);
        var version = BitConverter.ToUInt32(bytes, 4);

        if (signature != PolSignature)
            return new PolFileParseResult { SourcePath = polPath, Warnings = { $"Invalid .pol signature: 0x{signature:X8} (expected 0x{PolSignature:X8})." } };

        if (version != PolVersion)
            return new PolFileParseResult { SourcePath = polPath, Warnings = { $"Unsupported .pol version: {version} (expected {PolVersion})." } };

        var entries = new List<PolEntry>();
        var controls = new List<ControlRecord>();
        var warnings = new List<string>();
        var offset = 8;

        while (offset < bytes.Length)
        {
            if (!TryReadEntry(bytes, ref offset, out var entry, warnings))
                break;

            if (entry != null)
            {
                entries.Add(entry);
                controls.Add(BuildControlRecord(entry, packName, osTarget));
            }
        }

        return new PolFileParseResult
        {
            Controls = controls,
            Entries = entries,
            SourcePath = polPath,
            Warnings = warnings
        };
    }

    private static bool TryReadEntry(byte[] data, ref int offset, out PolEntry? entry, List<string> warnings)
    {
        entry = null;

        // Each entry starts with '[' (0x5B 0x00 in UTF-16LE)
        if (offset + 2 > data.Length)
            return false;

        if (data[offset] != 0x5B || data[offset + 1] != 0x00)
        {
            // Try to find next entry bracket
            var nextBracket = FindNextEntryBracket(data, offset);
            if (nextBracket < 0)
                return false;
            offset = nextBracket;
        }

        offset += 2; // Skip '['

        // Read key (null-terminated UTF-16LE string)
        if (!TryReadNullTerminatedString(data, ref offset, out var key))
        {
            warnings.Add("Failed to read registry key at offset " + offset);
            return false;
        }

        // Skip ';' separator (0x3B 0x00)
        if (!TrySkipSeparator(data, ref offset))
            return false;

        // Read value name (null-terminated UTF-16LE string)
        if (!TryReadNullTerminatedString(data, ref offset, out var valueName))
        {
            warnings.Add("Failed to read value name at offset " + offset);
            return false;
        }

        // Skip ';'
        if (!TrySkipSeparator(data, ref offset))
            return false;

        // Read type (4 bytes, little-endian uint32)
        if (offset + 4 > data.Length)
            return false;
        var regType = BitConverter.ToUInt32(data, offset);
        offset += 4;

        // Skip ';'
        if (!TrySkipSeparator(data, ref offset))
            return false;

        // Read size (4 bytes, little-endian uint32)
        if (offset + 4 > data.Length)
            return false;
        var dataSize = BitConverter.ToUInt32(data, offset);
        offset += 4;

        // Skip ';'
        if (!TrySkipSeparator(data, ref offset))
            return false;

        // Read data bytes
        if (dataSize > 0 && offset + (int)dataSize > data.Length)
        {
            warnings.Add($"Data size {dataSize} exceeds remaining bytes at offset {offset}");
            return false;
        }

        var entryData = dataSize > 0 ? new byte[dataSize] : Array.Empty<byte>();
        if (dataSize > 0)
        {
            Array.Copy(data, offset, entryData, 0, (int)dataSize);
            offset += (int)dataSize;
        }

        // Skip ']' (0x5D 0x00)
        if (offset + 2 <= data.Length && data[offset] == 0x5D && data[offset + 1] == 0x00)
            offset += 2;

        entry = new PolEntry
        {
            Key = key,
            ValueName = valueName,
            RegType = (RegistryType)regType,
            Data = entryData,
            DisplayValue = FormatData((RegistryType)regType, entryData)
        };

        return true;
    }

    private static bool TryReadNullTerminatedString(byte[] data, ref int offset, out string result)
    {
        result = string.Empty;
        var start = offset;

        while (offset + 1 < data.Length)
        {
            if (data[offset] == 0x00 && data[offset + 1] == 0x00)
            {
                result = Encoding.Unicode.GetString(data, start, offset - start);
                offset += 2; // Skip null terminator
                return true;
            }
            offset += 2;
        }

        return false;
    }

    private static bool TrySkipSeparator(byte[] data, ref int offset)
    {
        if (offset + 2 > data.Length)
            return false;
        if (data[offset] == 0x3B && data[offset + 1] == 0x00)
        {
            offset += 2;
            return true;
        }
        return false;
    }

    private static int FindNextEntryBracket(byte[] data, int startOffset)
    {
        for (var i = startOffset; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0x5B && data[i + 1] == 0x00)
                return i;
        }
        return -1;
    }

    private static string FormatData(RegistryType type, byte[] data)
    {
        try
        {
            return type switch
            {
                RegistryType.REG_SZ when data.Length >= 2 =>
                    Encoding.Unicode.GetString(data).TrimEnd('\0'),
                RegistryType.REG_DWORD when data.Length >= 4 =>
                    BitConverter.ToUInt32(data, 0).ToString(),
                RegistryType.REG_QWORD when data.Length >= 8 =>
                    BitConverter.ToUInt64(data, 0).ToString(),
                RegistryType.REG_EXPAND_SZ when data.Length >= 2 =>
                    Encoding.Unicode.GetString(data).TrimEnd('\0'),
                RegistryType.REG_MULTI_SZ when data.Length >= 2 =>
                    Encoding.Unicode.GetString(data).TrimEnd('\0').Replace('\0', '|'),
                _ => data.Length > 0
                    ? BitConverter.ToString(data).Replace("-", " ")
                    : "(empty)"
            };
        }
        catch
        {
            return data.Length > 0 ? $"({data.Length} bytes)" : "(empty)";
        }
    }

    private static ControlRecord BuildControlRecord(PolEntry entry, string packName, OsTarget osTarget)
    {
        return new ControlRecord
        {
            ControlId = Guid.NewGuid().ToString("n"),
            ExternalIds = new ExternalIds
            {
                RuleId = $"GPO_Reg_{SanitizeKey(entry.Key)}_{SanitizeKey(entry.ValueName)}",
                BenchmarkId = "gpo-registry-policy"
            },
            Title = $"Registry Policy: {entry.Key}\\{entry.ValueName} = {entry.DisplayValue}",
            Severity = "medium",
            Discussion = $"Registry Type: {entry.RegType}",
            CheckText = $"Verify registry value '{entry.ValueName}' under '{entry.Key}' is set to '{entry.DisplayValue}'.",
            FixText = $"Apply Group Policy registry setting: {entry.Key}\\{entry.ValueName} = {entry.DisplayValue} (Type: {entry.RegType}).",
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

    private static string SanitizeKey(string key)
    {
        return key.Replace('\\', '_').Replace('/', '_').Replace(' ', '_');
    }
}

public enum RegistryType : uint
{
    REG_NONE = 0,
    REG_SZ = 1,
    REG_EXPAND_SZ = 2,
    REG_BINARY = 3,
    REG_DWORD = 4,
    REG_DWORD_BIG_ENDIAN = 5,
    REG_LINK = 6,
    REG_MULTI_SZ = 7,
    REG_QWORD = 11
}

public sealed class PolEntry
{
    public string Key { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public RegistryType RegType { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string DisplayValue { get; set; } = string.Empty;
}

public sealed class PolFileParseResult
{
    public IReadOnlyList<ControlRecord> Controls { get; set; } = Array.Empty<ControlRecord>();
    public IReadOnlyList<PolEntry> Entries { get; set; } = Array.Empty<PolEntry>();
    public string SourcePath { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}
