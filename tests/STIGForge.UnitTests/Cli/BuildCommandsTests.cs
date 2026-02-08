using System.Reflection;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Cli;

public sealed class BuildCommandsTests
{
  private static readonly MethodInfo ValidateBreakGlassArguments =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.BuildCommands", throwOnError: true)!
      .GetMethod("ValidateBreakGlassArguments", BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo RecordBreakGlassAuditAsync =
    Assembly.Load("STIGForge.Cli")
      .GetType("STIGForge.Cli.Commands.BuildCommands", throwOnError: true)!
      .GetMethod("RecordBreakGlassAuditAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

  [Fact]
  public void ValidateBreakGlassArguments_HighRiskWithoutAck_ReturnsError()
  {
    var error = InvokeValidate(highRiskOptionEnabled: true, breakGlassAck: false, breakGlassReason: "Emergency maintenance", optionName: "--skip-snapshot");

    error.Should().NotBeNull();
    error.Should().Contain("--break-glass-ack");
  }

  [Fact]
  public void ValidateBreakGlassArguments_HighRiskWithoutReason_ReturnsError()
  {
    var error = InvokeValidate(highRiskOptionEnabled: true, breakGlassAck: true, breakGlassReason: " ", optionName: "--force-auto-apply");

    error.Should().NotBeNull();
    error.Should().Contain("--break-glass-reason");
  }

  [Fact]
  public void ValidateBreakGlassArguments_HighRiskWithReasonAndAck_Passes()
  {
    var error = InvokeValidate(highRiskOptionEnabled: true, breakGlassAck: true, breakGlassReason: "Quarterly emergency baseline drift", optionName: "--skip-snapshot");

    error.Should().BeNull();
  }

  [Fact]
  public void ValidateBreakGlassArguments_NonHighRisk_DoesNotRequireBreakGlass()
  {
    var error = InvokeValidate(highRiskOptionEnabled: false, breakGlassAck: false, breakGlassReason: null, optionName: "--skip-snapshot");

    error.Should().BeNull();
  }

  [Fact]
  public async Task RecordBreakGlassAuditAsync_HighRiskIncludesReasonInAuditDetail()
  {
    var audit = new Mock<IAuditTrailService>();
    AuditEntry? captured = null;
    audit
      .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
      .Callback<AuditEntry, CancellationToken>((entry, _) => captured = entry)
      .Returns(Task.CompletedTask);

    await InvokeRecordAudit(
      audit.Object,
      highRiskOptionEnabled: true,
      action: "apply-run",
      target: "C:/bundle",
      bypassName: "skip-snapshot",
      reason: "Emergency rollback baseline unavailable");

    captured.Should().NotBeNull();
    captured!.Action.Should().Be("break-glass");
    captured.Detail.Should().Contain("Bypass=skip-snapshot");
    captured.Detail.Should().Contain("Reason=Emergency rollback baseline unavailable");
    audit.Verify(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  private static string? InvokeValidate(bool highRiskOptionEnabled, bool breakGlassAck, string? breakGlassReason, string optionName)
  {
    return (string?)ValidateBreakGlassArguments.Invoke(
      obj: null,
      parameters: new object?[] { highRiskOptionEnabled, breakGlassAck, breakGlassReason, optionName });
  }

  private static async Task InvokeRecordAudit(
    IAuditTrailService? audit,
    bool highRiskOptionEnabled,
    string action,
    string target,
    string bypassName,
    string? reason)
  {
    var task = (Task)RecordBreakGlassAuditAsync.Invoke(
      obj: null,
      parameters: new object?[] { audit, highRiskOptionEnabled, action, target, bypassName, reason, CancellationToken.None })!;

    await task.ConfigureAwait(false);
  }
}
