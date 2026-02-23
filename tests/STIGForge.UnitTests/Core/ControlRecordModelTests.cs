using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class ControlRecordModelTests
{
    [Fact]
    public void SourcePackId_DefaultsToEmpty()
    {
        var control = new ControlRecord();
        Assert.Equal(string.Empty, control.SourcePackId);
    }

    [Fact]
    public void SourcePackId_CanBeAssigned()
    {
        var control = new ControlRecord
        {
            SourcePackId = "test-pack-001"
        };
        Assert.Equal("test-pack-001", control.SourcePackId);
    }

    [Fact]
    public void ExternalIds_DefaultsToNewInstance()
    {
        var control = new ControlRecord();
        Assert.NotNull(control.ExternalIds);
    }

    [Fact]
    public void ControlId_DefaultsToEmpty()
    {
        var control = new ControlRecord();
        Assert.Equal(string.Empty, control.ControlId);
    }

    [Fact]
    public void Title_DefaultsToEmpty()
    {
        var control = new ControlRecord();
        Assert.Equal(string.Empty, control.Title);
    }
}
