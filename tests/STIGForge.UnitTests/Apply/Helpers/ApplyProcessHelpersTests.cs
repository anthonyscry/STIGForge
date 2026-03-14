using System.Text;
using FluentAssertions;
using STIGForge.Apply.Steps;

namespace STIGForge.UnitTests.Apply.Helpers;

public sealed class ApplyProcessHelpersTests
{
    // ── ToPowerShellSingleQuoted ─────────────────────────────────────────────

    [Fact]
    public void ToPowerShellSingleQuoted_NormalString_WrapsInSingleQuotes()
    {
        var result = ApplyProcessHelpers.ToPowerShellSingleQuoted("hello world");

        result.Should().Be("'hello world'");
    }

    [Fact]
    public void ToPowerShellSingleQuoted_StringWithSingleQuote_EscapesDoubleApostrophe()
    {
        var result = ApplyProcessHelpers.ToPowerShellSingleQuoted("it's here");

        result.Should().Be("'it''s here'");
    }

    [Fact]
    public void ToPowerShellSingleQuoted_MultipleSingleQuotes_AllEscaped()
    {
        var result = ApplyProcessHelpers.ToPowerShellSingleQuoted("a'b'c");

        result.Should().Be("'a''b''c'");
    }

    [Fact]
    public void ToPowerShellSingleQuoted_Null_WrapsEmptyString()
    {
        var result = ApplyProcessHelpers.ToPowerShellSingleQuoted(null);

        result.Should().Be("''");
    }

    [Fact]
    public void ToPowerShellSingleQuoted_EmptyString_WrapsEmpty()
    {
        var result = ApplyProcessHelpers.ToPowerShellSingleQuoted(string.Empty);

        result.Should().Be("''");
    }

    [Fact]
    public void ToPowerShellSingleQuoted_PathWithBackslashes_NothingEscaped()
    {
        var result = ApplyProcessHelpers.ToPowerShellSingleQuoted(@"C:\Program Files\Tool");

        result.Should().Be(@"'C:\Program Files\Tool'");
    }

    // ── BuildEncodedCommandArgs ──────────────────────────────────────────────

    [Fact]
    public void BuildEncodedCommandArgs_ReturnsStringContainingEncodedCommand()
    {
        var args = ApplyProcessHelpers.BuildEncodedCommandArgs("Write-Host 'hi'");

        args.Should().Contain("-EncodedCommand ");
        args.Should().Contain("-NoProfile");
        args.Should().Contain("-ExecutionPolicy Bypass");
    }

    [Fact]
    public void BuildEncodedCommandArgs_EncodedPartDecodesBackToOriginalScript()
    {
        const string script = "Get-Process | Select-Object Name";
        var args = ApplyProcessHelpers.BuildEncodedCommandArgs(script);

        var encodedPart = args.Split("-EncodedCommand ", 2)[1].Trim();
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encodedPart));

        decoded.Should().Be(script);
    }

    [Fact]
    public void BuildEncodedCommandArgs_EmptyScript_StillProducesValidBase64()
    {
        var args = ApplyProcessHelpers.BuildEncodedCommandArgs(string.Empty);

        var encodedPart = args.Split("-EncodedCommand ", 2)[1].Trim();
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encodedPart));

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void BuildEncodedCommandArgs_ScriptWithSpecialChars_RoundTrips()
    {
        const string script = "Import-Module GroupPolicy; Import-GPO -BackupGpoName 'Test Policy' -Path 'C:\\backup'";
        var args = ApplyProcessHelpers.BuildEncodedCommandArgs(script);

        var encodedPart = args.Split("-EncodedCommand ", 2)[1].Trim();
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encodedPart));

        decoded.Should().Be(script);
    }
}
