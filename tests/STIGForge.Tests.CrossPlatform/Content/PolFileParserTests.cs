using System.Text;
using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.Tests.CrossPlatform.Content;

/// <summary>
/// Tests for PolFileParser binary .pol file parsing.
/// The .pol format: header (8 bytes) + entries.
/// Header: 50 52 65 67 01 00 00 00 (PReg v1)
/// Entry:  [key\0;value\0;type(4LE);size(4LE);data]
/// All strings are null-terminated UTF-16LE; brackets and semicolons are also UTF-16LE.
/// </summary>
public sealed class PolFileParserTests : IDisposable
{
    private readonly string _tempDir;

    public PolFileParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pol-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Binary builder helpers ──────────────────────────────────────────────

    private static byte[] Header() =>
        new byte[] { 0x50, 0x52, 0x65, 0x67, 0x01, 0x00, 0x00, 0x00 };

    private static byte[] InvalidHeader() =>
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x00, 0x00, 0x00 };

    private static byte[] U16Str(string s) =>
        Encoding.Unicode.GetBytes(s).Concat(new byte[] { 0x00, 0x00 }).ToArray();

    private static byte[] Sep() => new byte[] { 0x3B, 0x00 };      // ;
    private static byte[] Open() => new byte[] { 0x5B, 0x00 };     // [
    private static byte[] Close() => new byte[] { 0x5D, 0x00 };    // ]
    private static byte[] U32LE(uint v) => BitConverter.GetBytes(v);

    private static byte[] BuildEntry(string key, string valueName, uint type, byte[] data)
    {
        var size = (uint)data.Length;
        return Open()
            .Concat(U16Str(key))
            .Concat(Sep())
            .Concat(U16Str(valueName))
            .Concat(Sep())
            .Concat(U32LE(type))
            .Concat(Sep())
            .Concat(U32LE(size))
            .Concat(Sep())
            .Concat(data)
            .Concat(Close())
            .ToArray();
    }

    private string WriteTempPol(byte[] bytes)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pol");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Concat(params byte[][] arrays) =>
        arrays.SelectMany(a => a).ToArray();

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBytes_ReturnsEmpty()
    {
        var path = WriteTempPol(Array.Empty<byte>());

        var result = PolFileParser.Parse(path, "TestPack");

        result.Controls.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("too small"));
    }

    [Fact]
    public void Parse_InvalidSignature_ReturnsEmpty()
    {
        var path = WriteTempPol(InvalidHeader());

        var result = PolFileParser.Parse(path, "TestPack");

        result.Controls.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("signature"));
    }

    [Fact]
    public void Parse_ValidMinimalEntry_ParsesCorrectly()
    {
        var valueData = Encoding.Unicode.GetBytes("TestValue\0");
        var entry = BuildEntry(
            @"HKLM\Software\Test",
            "Setting1",
            1,  // REG_SZ
            valueData);
        var path = WriteTempPol(Concat(Header(), entry));

        var result = PolFileParser.Parse(path, "TestPack");

        result.Warnings.Should().BeEmpty();
        result.Entries.Should().HaveCount(1);
        result.Entries[0].Key.Should().Be(@"HKLM\Software\Test");
        result.Entries[0].ValueName.Should().Be("Setting1");
        result.Entries[0].RegType.Should().Be(RegistryType.REG_SZ);
        result.Controls.Should().HaveCount(1);
        result.Controls[0].Revision.PackName.Should().Be("TestPack");
    }

    [Fact]
    public void Parse_LargeDataSize_DoesNotOverflow()
    {
        // Build an entry where the declared size is near uint.MaxValue but data ends before that.
        // The parser uses (long) casts to avoid integer overflow in the bounds check.
        var keyBytes = U16Str(@"HKLM\Test");
        var valBytes = U16Str("Value");
        var type = U32LE(1);
        var hugeSizeBytes = U32LE(uint.MaxValue);
        var fakeData = Array.Empty<byte>(); // no actual data bytes

        var entry = Open()
            .Concat(keyBytes)
            .Concat(Sep())
            .Concat(valBytes)
            .Concat(Sep())
            .Concat(type)
            .Concat(Sep())
            .Concat(hugeSizeBytes)
            .Concat(Sep())
            .Concat(fakeData)
            .ToArray(); // deliberately no Close bracket

        var bytes = Concat(Header(), entry);
        var path = WriteTempPol(bytes);

        // Must not throw OverflowException
        var act = () => PolFileParser.Parse(path, "TestPack");
        act.Should().NotThrow<OverflowException>();
    }

    [Fact]
    public void Parse_ScanLimit_VeryLongData_ReturnsBeforeScanLimit()
    {
        // Create a buffer with no valid entry brackets after the header — only junk bytes.
        // FindNextEntryBracket has a 1MB scan cap; this should not throw.
        var junk = new byte[2 * 1024 * 1024]; // 2 MB of zeros
        var bytes = Concat(Header(), junk);
        var path = WriteTempPol(bytes);

        var act = () => PolFileParser.Parse(path, "TestPack");
        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_OffByOneAtBufferEnd_DoesNotThrow()
    {
        // Build an entry that ends exactly at the buffer boundary (Close bracket at the last 2 bytes).
        var data = new byte[] { 0x01, 0x00, 0x00, 0x00 }; // 4-byte REG_DWORD value
        var entry = BuildEntry(@"HKLM\Boundary", "Val", 4, data);
        var path = WriteTempPol(Concat(Header(), entry));

        var act = () => PolFileParser.Parse(path, "TestPack");
        act.Should().NotThrow();
        var result = PolFileParser.Parse(path, "TestPack");
        result.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_MultipleEntries_ParsesAll()
    {
        var dword1 = U32LE(1);
        var dword2 = U32LE(2);
        var entry1 = BuildEntry(@"HKLM\Key1", "Setting1", 4, dword1);
        var entry2 = BuildEntry(@"HKLM\Key2", "Setting2", 4, dword2);
        var path = WriteTempPol(Concat(Header(), entry1, entry2));

        var result = PolFileParser.Parse(path, "TestPack");

        result.Entries.Should().HaveCount(2);
        result.Entries[0].Key.Should().Be(@"HKLM\Key1");
        result.Entries[1].Key.Should().Be(@"HKLM\Key2");
        result.Controls.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_NullTerminatedValues_StripsNulls()
    {
        // REG_SZ value that contains a trailing null character in the data bytes
        var valueWithNull = Encoding.Unicode.GetBytes("Hello\0");
        var entry = BuildEntry(@"HKLM\Test", "Val", 1, valueWithNull);
        var path = WriteTempPol(Concat(Header(), entry));

        var result = PolFileParser.Parse(path, "TestPack");

        result.Entries.Should().HaveCount(1);
        // DisplayValue should strip trailing null
        result.Entries[0].DisplayValue.Should().Be("Hello");
    }
}
