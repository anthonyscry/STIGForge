using Microsoft.Extensions.Logging;

namespace STIGForge.Apply;

/// <summary>
/// Handles fallback strategies when primary apply methods fail.
/// Implements graceful degradation: DSC -> PowerShell script -> Manual-only mode.
/// </summary>
public sealed class ApplyFallbackHandler
{
    private readonly ILogger<ApplyFallbackHandler> _logger;

    public ApplyFallbackHandler(ILogger<ApplyFallbackHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts to apply a control using fallback strategies if primary method fails.
    /// Returns the outcome of the successful fallback or the final failure.
    /// </summary>
    public async Task<FallbackResult> ApplyWithFallbackAsync(
        string controlId,
        Func<CancellationToken, Task<bool>> primaryApply,
        Func<CancellationToken, Task<bool>>? secondaryApply,
        CancellationToken ct)
    {
        var attempts = new List<FallbackAttempt>();

        // Attempt 1: Primary method (e.g., DSC)
        try
        {
            _logger.LogDebug("Attempting primary apply for control {ControlId}", controlId);
            var success = await primaryApply(ct);
            
            if (success)
            {
                attempts.Add(new FallbackAttempt
                {
                    Method = "Primary",
                    Success = true,
                    Error = null
                });

                return new FallbackResult
                {
                    ControlId = controlId,
                    FinalSuccess = true,
                    FinalMethod = "Primary",
                    Attempts = attempts
                };
            }

            attempts.Add(new FallbackAttempt
            {
                Method = "Primary",
                Success = false,
                Error = "Primary method returned failure"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary apply failed for control {ControlId}: {Error}", controlId, ex.Message);
            
            attempts.Add(new FallbackAttempt
            {
                Method = "Primary",
                Success = false,
                Error = ex.Message
            });
        }

        // Attempt 2: Secondary method (e.g., PowerShell script fallback)
        if (secondaryApply != null)
        {
            try
            {
                _logger.LogDebug("Attempting secondary fallback for control {ControlId}", controlId);
                var success = await secondaryApply(ct);

                if (success)
                {
                    attempts.Add(new FallbackAttempt
                    {
                        Method = "Secondary",
                        Success = true,
                        Error = null
                    });

                    return new FallbackResult
                    {
                        ControlId = controlId,
                        FinalSuccess = true,
                        FinalMethod = "Secondary",
                        Attempts = attempts
                    };
                }

                attempts.Add(new FallbackAttempt
                {
                    Method = "Secondary",
                    Success = false,
                    Error = "Secondary method returned failure"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Secondary fallback failed for control {ControlId}: {Error}", controlId, ex.Message);
                
                attempts.Add(new FallbackAttempt
                {
                    Method = "Secondary",
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        // Final fallback: Manual-only mode
        _logger.LogInformation("All automated methods failed for control {ControlId} - marking as manual-only", controlId);
        
        attempts.Add(new FallbackAttempt
        {
            Method = "Manual",
            Success = false,
            Error = "Requires manual intervention"
        });

        return new FallbackResult
        {
            ControlId = controlId,
            FinalSuccess = false,
            FinalMethod = "Manual",
            Attempts = attempts,
            RequiresManual = true
        };
    }

    /// <summary>
    /// Determines if an error is retryable (transient) or permanent.
    /// Retryable errors: network timeouts, file locks, temporary resource unavailability.
    /// Permanent errors: configuration errors, missing dependencies, authorization failures.
    /// </summary>
    public bool IsRetryable(Exception ex)
    {
        var exType = ex.GetType().Name;
        
        // Network-related errors (retryable)
        if (exType == "HttpRequestException" || ex is TimeoutException)
            return true;

        // File system errors (some retryable)
        if (ex is IOException ioEx)
        {
            // File locks and sharing violations are retryable
            var message = ioEx.Message.ToLowerInvariant();
            if (message.Contains("being used by another process") ||
                message.Contains("lock") ||
                message.Contains("sharing violation"))
            {
                return true;
            }
        }

        // Configuration/authorization errors (permanent)
        if (ex is UnauthorizedAccessException ||
            ex is ArgumentException ||
            ex is InvalidOperationException)
        {
            return false;
        }

        // Default: treat as non-retryable to avoid infinite loops
        return false;
    }
}

public sealed class FallbackResult
{
    public string ControlId { get; set; } = string.Empty;
    public bool FinalSuccess { get; set; }
    public string FinalMethod { get; set; } = string.Empty;
    public bool RequiresManual { get; set; }
    public List<FallbackAttempt> Attempts { get; set; } = new();
}

public sealed class FallbackAttempt
{
    public string Method { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}
