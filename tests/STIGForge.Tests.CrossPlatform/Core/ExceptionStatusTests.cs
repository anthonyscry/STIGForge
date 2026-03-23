using FluentAssertions;
using STIGForge.Core.Models;

namespace STIGForge.Tests.CrossPlatform.Core;

public class ExceptionStatusTests
{
    [Fact]
    public void StatusValue_Active_ReturnsActive()
    {
        new ControlException { Status = "Active" }.StatusValue.Should().Be(ExceptionStatus.Active);
    }

    [Fact]
    public void StatusValue_Revoked_ReturnsRevoked()
    {
        new ControlException { Status = "Revoked" }.StatusValue.Should().Be(ExceptionStatus.Revoked);
    }

    [Fact]
    public void StatusValue_Expired_ReturnsExpired()
    {
        new ControlException { Status = "Expired" }.StatusValue.Should().Be(ExceptionStatus.Expired);
    }

    [Fact]
    public void StatusValue_LowercaseActive_ReturnsCaseInsensitiveActive()
    {
        new ControlException { Status = "active" }.StatusValue.Should().Be(ExceptionStatus.Active);
    }

    [Fact]
    public void StatusValue_UppercaseActive_ReturnsCaseInsensitiveActive()
    {
        new ControlException { Status = "ACTIVE" }.StatusValue.Should().Be(ExceptionStatus.Active);
    }

    [Fact]
    public void StatusValue_UnknownString_FallsBackToRevoked()
    {
        new ControlException { Status = "unknown_status_xyz" }.StatusValue.Should().Be(ExceptionStatus.Revoked);
    }

    [Fact]
    public void DefaultConstructed_StatusValueIsActive()
    {
        new ControlException().StatusValue.Should().Be(ExceptionStatus.Active);
    }
}
