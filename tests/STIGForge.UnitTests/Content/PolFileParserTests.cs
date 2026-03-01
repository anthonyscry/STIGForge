using System.Text;
using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Content;

public sealed class PolFileParserTests : IDisposable
{
    private readonly string _tempDir;

    public PolFileParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-pol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Parse_ValidPolFile_ExtractsEntries()
    {
        var polPath = WritePolFile(new[]
        {
            ("SOFTWARE\\Policies\\Microsoft\\Windows\\System", "DisableLockScreenAppNotifications", RegistryType.REG_DWORD, BitConverter.GetBytes((uint)1))
        });

        var result = PolFileParser.Parse(polPath, "TestPack");

        result.Entries.Should().HaveCount(1);
        result.Controls.Should().HaveCount(1);

        var entry = result.Entries[0];
        entry.Key.Should().Be("SOFTWARE\\Policies\\Microsoft\\Windows\\System");
        entry.ValueName.Should().Be("DisableLockScreenAppNotifications");
        entry.RegType.Should().Be(RegistryType.REG_DWORD);
        entry.DisplayValue.Should().Be("1");
    }

    [Fact]
    public void Parse_MultipleEntries_ParsesAll()
    {
        var polPath = WritePolFile(new[]
        {
            ("SOFTWARE\\Policies\\Key1", "Val1", RegistryType.REG_DWORD, BitConverter.GetBytes((uint)0)),
            ("SOFTWARE\\Policies\\Key2", "Val2", RegistryType.REG_DWORD, BitConverter.GetBytes((uint)1))
        });

        var result = PolFileParser.Parse(polPath, "TestPack");

        result.Entries.Should().HaveCount(2);
        result.Controls.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_StringValue_DecodesCorrectly()
    {
        var stringData = Encoding.Unicode.GetBytes("TestValue\0");
        var polPath = WritePolFile(new[]
        {
            ("SOFTWARE\\Test", "StringSetting", RegistryType.REG_SZ, stringData)
        });

        var result = PolFileParser.Parse(polPath, "TestPack");

        result.Entries.Should().HaveCount(1);
        result.Entries[0].DisplayValue.Should().Be("TestValue");
    }

    [Fact]
    public void Parse_InvalidSignature_ReturnsWarning()
    {
        var polPath = Path.Combine(_tempDir, "invalid.pol");
        File.WriteAllBytes(polPath, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 });

        var result = PolFileParser.Parse(polPath, "TestPack");

        result.Entries.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("Invalid .pol signature"));
    }

    [Fact]
    public void Parse_TooSmallFile_ReturnsWarning()
    {
        var polPath = Path.Combine(_tempDir, "tiny.pol");
        File.WriteAllBytes(polPath, new byte[] { 0x50, 0x52, 0x65, 0x67 });

        var result = PolFileParser.Parse(polPath, "TestPack");

        result.Entries.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("too small"));
    }

    [Fact]
    public void Parse_WithOsTarget_SetsApplicability()
    {
        var polPath = WritePolFile(new[]
        {
            ("SOFTWARE\\Test", "Value", RegistryType.REG_DWORD, BitConverter.GetBytes((uint)1))
        });

        var result = PolFileParser.Parse(polPath, "TestPack", OsTarget.Win11);

        result.Controls[0].Applicability.OsTarget.Should().Be(OsTarget.Win11);
    }

    /// <summary>
    /// Writes a valid .pol binary file with the given entries.
    /// Format: PReg signature (4B) + version 1 (4B) + entries
    /// Each entry: [key\0;value\0;type(4B);size(4B);data]
    /// All strings are null-terminated UTF-16LE.
    /// </summary>
    private string WritePolFile((string key, string value, RegistryType type, byte[] data)[] entries)
    {
        var polPath = Path.Combine(_tempDir, "Registry.pol");
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // PReg signature
        writer.Write((uint)0x67655250);
        // Version
        writer.Write((uint)1);

        foreach (var (key, value, type, data) in entries)
        {
            // Opening bracket '[' in UTF-16LE
            writer.Write((byte)0x5B);
            writer.Write((byte)0x00);

            // Key (null-terminated UTF-16LE)
            WriteNullTerminatedString(writer, key);

            // Separator ';'
            writer.Write((byte)0x3B);
            writer.Write((byte)0x00);

            // Value name (null-terminated UTF-16LE)
            WriteNullTerminatedString(writer, value);

            // Separator ';'
            writer.Write((byte)0x3B);
            writer.Write((byte)0x00);

            // Type (4 bytes)
            writer.Write((uint)type);

            // Separator ';'
            writer.Write((byte)0x3B);
            writer.Write((byte)0x00);

            // Size (4 bytes)
            writer.Write((uint)data.Length);

            // Separator ';'
            writer.Write((byte)0x3B);
            writer.Write((byte)0x00);

            // Data
            writer.Write(data);

            // Closing bracket ']'
            writer.Write((byte)0x5D);
            writer.Write((byte)0x00);
        }

        File.WriteAllBytes(polPath, ms.ToArray());
        return polPath;
    }

    private static void WriteNullTerminatedString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        writer.Write(bytes);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
    }
}
