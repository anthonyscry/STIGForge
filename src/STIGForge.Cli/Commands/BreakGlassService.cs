using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

/// <summary>Validates and audits break-glass high-risk command usage.</summary>
internal static class BreakGlassService
{
    public const string AckOptionName = "--break-glass-ack";
    public const string ReasonOptionName = "--break-glass-reason";

    /// <summary>
    /// Returns an error message if the break-glass arguments are invalid; null if they are valid.
    /// </summary>
    public static string? ValidateArguments(
        bool highRiskOptionEnabled, bool breakGlassAck, string? breakGlassReason, string optionName)
    {
        if (!highRiskOptionEnabled)
            return null;

        if (!breakGlassAck)
            return $"{optionName} is high risk. Add {AckOptionName} and provide a specific reason with {ReasonOptionName}.";

        try
        {
            new ManualAnswerService().ValidateBreakGlassReason(breakGlassReason);
        }
        catch (ArgumentException)
        {
            return $"{ReasonOptionName} is required for {optionName} and must be specific (minimum 8 characters).";
        }

        return null;
    }

    public static async Task RecordAuditAsync(
        IAuditTrailService? audit,
        bool highRiskOptionEnabled,
        string action,
        string target,
        string bypassName,
        string? reason,
        CancellationToken ct)
    {
        if (!highRiskOptionEnabled || audit == null)
            return;

        await audit.RecordAsync(new AuditEntry
        {
            Action = "break-glass",
            Target = string.IsNullOrWhiteSpace(target) ? action : target,
            Result = "acknowledged",
            Detail = $"Action={action}; Bypass={bypassName}; Reason={reason?.Trim()}",
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Timestamp = DateTimeOffset.Now
        }, ct).ConfigureAwait(false);
    }
}
