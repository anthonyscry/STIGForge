using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using STIGForge.Apply;

namespace STIGForge.UnitTests.Apply;

/// <summary>
/// Tests for ApplyFallbackHandler: fallback strategy ordering, error classification,
/// and result population.
/// </summary>
public sealed class ApplyFallbackHandlerTests
{
  private static ApplyFallbackHandler CreateHandler()
  {
    var logger = new Mock<ILogger<ApplyFallbackHandler>>();
    return new ApplyFallbackHandler(logger.Object);
  }

  // ── Constructor ─────────────────────────────────────────────────────────────

  [Fact]
  public void Constructor_ThrowsWhenLoggerIsNull()
  {
    var act = () => new ApplyFallbackHandler(null!);
    act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
  }

  // ── Primary success ──────────────────────────────────────────────────────────

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenPrimarySucceeds_ReturnsPrimarySuccess()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100001",
      _ => Task.FromResult(true),
      null,
      CancellationToken.None);

    result.FinalSuccess.Should().BeTrue();
    result.FinalMethod.Should().Be("Primary");
    result.ControlId.Should().Be("V-100001");
    result.RequiresManual.Should().BeFalse();
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenPrimarySucceeds_HasOneAttemptRecorded()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100001",
      _ => Task.FromResult(true),
      null,
      CancellationToken.None);

    result.Attempts.Should().HaveCount(1);
    result.Attempts[0].Method.Should().Be("Primary");
    result.Attempts[0].Success.Should().BeTrue();
    result.Attempts[0].Error.Should().BeNull();
  }

  // ── Primary failure → secondary ─────────────────────────────────────────────

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenPrimaryReturnsFalse_AttemptsSecondary()
  {
    var handler = CreateHandler();
    var secondaryCalled = false;

    await handler.ApplyWithFallbackAsync(
      "V-100001",
      _ => Task.FromResult(false),
      ct => { secondaryCalled = true; return Task.FromResult(true); },
      CancellationToken.None);

    secondaryCalled.Should().BeTrue();
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenPrimaryReturnsFalseAndSecondarySucceeds_ReturnsSecondarySuccess()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100002",
      _ => Task.FromResult(false),
      _ => Task.FromResult(true),
      CancellationToken.None);

    result.FinalSuccess.Should().BeTrue();
    result.FinalMethod.Should().Be("Secondary");
    result.RequiresManual.Should().BeFalse();
    result.Attempts.Should().HaveCount(2);
    result.Attempts[0].Method.Should().Be("Primary");
    result.Attempts[0].Success.Should().BeFalse();
    result.Attempts[1].Method.Should().Be("Secondary");
    result.Attempts[1].Success.Should().BeTrue();
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenPrimaryThrows_AttemptsSecondary()
  {
    var handler = CreateHandler();
    var secondaryCalled = false;

    await handler.ApplyWithFallbackAsync(
      "V-100001",
      _ => throw new InvalidOperationException("DSC failed"),
      ct => { secondaryCalled = true; return Task.FromResult(true); },
      CancellationToken.None);

    secondaryCalled.Should().BeTrue();
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenPrimaryThrows_ExceptionMessageStoredInAttempt()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100001",
      _ => throw new InvalidOperationException("DSC config failed"),
      _ => Task.FromResult(true),
      CancellationToken.None);

    result.Attempts[0].Error.Should().Contain("DSC config failed");
  }

  // ── Both fail → manual ───────────────────────────────────────────────────────

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenBothFail_ReturnsManualFallback()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100003",
      _ => Task.FromResult(false),
      _ => Task.FromResult(false),
      CancellationToken.None);

    result.FinalSuccess.Should().BeFalse();
    result.FinalMethod.Should().Be("Manual");
    result.RequiresManual.Should().BeTrue();
    result.Attempts.Should().HaveCount(3);
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenBothThrow_ReturnsManualFallback()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100003",
      _ => throw new Exception("primary boom"),
      _ => throw new Exception("secondary boom"),
      CancellationToken.None);

    result.FinalSuccess.Should().BeFalse();
    result.FinalMethod.Should().Be("Manual");
    result.RequiresManual.Should().BeTrue();
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_WhenNoSecondaryAndPrimaryFails_ReturnsManualFallback()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100004",
      _ => Task.FromResult(false),
      null,
      CancellationToken.None);

    result.FinalSuccess.Should().BeFalse();
    result.FinalMethod.Should().Be("Manual");
    result.RequiresManual.Should().BeTrue();
    // Primary attempt + Manual attempt
    result.Attempts.Should().HaveCount(2);
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_ManualAttempt_HasExpectedError()
  {
    var handler = CreateHandler();

    var result = await handler.ApplyWithFallbackAsync(
      "V-100004",
      _ => Task.FromResult(false),
      null,
      CancellationToken.None);

    var manualAttempt = result.Attempts.Last();
    manualAttempt.Method.Should().Be("Manual");
    manualAttempt.Success.Should().BeFalse();
    manualAttempt.Error.Should().Contain("manual");
  }

  // ── Cancellation token is forwarded ─────────────────────────────────────────

  [Fact]
  public async Task ApplyWithFallbackAsync_CancellationToken_IsPassedToPrimary()
  {
    var handler = CreateHandler();
    using var cts = new CancellationTokenSource();
    CancellationToken capturedToken = default;

    await handler.ApplyWithFallbackAsync(
      "V-100001",
      ct => { capturedToken = ct; return Task.FromResult(true); },
      null,
      cts.Token);

    capturedToken.Should().Be(cts.Token);
  }

  [Fact]
  public async Task ApplyWithFallbackAsync_CancellationToken_IsPassedToSecondary()
  {
    var handler = CreateHandler();
    using var cts = new CancellationTokenSource();
    CancellationToken capturedToken = default;

    await handler.ApplyWithFallbackAsync(
      "V-100001",
      _ => Task.FromResult(false),
      ct => { capturedToken = ct; return Task.FromResult(true); },
      cts.Token);

    capturedToken.Should().Be(cts.Token);
  }

  // ── IsRetryable ──────────────────────────────────────────────────────────────

  [Fact]
  public void IsRetryable_TimeoutException_ReturnsTrue()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new TimeoutException()).Should().BeTrue();
  }

  [Fact]
  public void IsRetryable_IOException_WithFileLockMessage_ReturnsTrue()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new IOException("The file is being used by another process")).Should().BeTrue();
  }

  [Fact]
  public void IsRetryable_IOException_WithSharingViolation_ReturnsTrue()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new IOException("Sharing violation on path")).Should().BeTrue();
  }

  [Fact]
  public void IsRetryable_IOException_WithLockMessage_ReturnsTrue()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new IOException("File lock prevented access")).Should().BeTrue();
  }

  [Fact]
  public void IsRetryable_UnauthorizedAccessException_ReturnsFalse()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new UnauthorizedAccessException()).Should().BeFalse();
  }

  [Fact]
  public void IsRetryable_ArgumentException_ReturnsFalse()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new ArgumentException("bad arg")).Should().BeFalse();
  }

  [Fact]
  public void IsRetryable_InvalidOperationException_ReturnsFalse()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new InvalidOperationException("bad state")).Should().BeFalse();
  }

  [Fact]
  public void IsRetryable_GenericException_ReturnsFalse()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new Exception("unknown error")).Should().BeFalse();
  }

  [Fact]
  public void IsRetryable_IOException_WithGenericMessage_ReturnsFalse()
  {
    var handler = CreateHandler();
    handler.IsRetryable(new IOException("Disk error")).Should().BeFalse();
  }

  // ── Result model ─────────────────────────────────────────────────────────────

  [Fact]
  public void FallbackResult_DefaultsAreCorrect()
  {
    var result = new FallbackResult();
    result.ControlId.Should().BeEmpty();
    result.FinalSuccess.Should().BeFalse();
    result.FinalMethod.Should().BeEmpty();
    result.RequiresManual.Should().BeFalse();
    result.Attempts.Should().NotBeNull().And.BeEmpty();
  }

  [Fact]
  public void FallbackAttempt_DefaultsAreCorrect()
  {
    var attempt = new FallbackAttempt();
    attempt.Method.Should().BeEmpty();
    attempt.Success.Should().BeFalse();
    attempt.Error.Should().BeNull();
  }
}
