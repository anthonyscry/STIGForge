namespace STIGForge.Core.Abstractions;

/// <summary>
/// Input for evidence compilation — lightweight record that carries only
/// the fields needed from Verify.ControlResult without coupling to the Verify project.
/// </summary>
public sealed record EvidenceCompilationInput(
    string? VulnId,
    string? RuleId,
    string? Status,
    string? Tool,
    DateTimeOffset? VerifiedAt,
    string? FindingDetails,
    string? Comments);

/// <summary>
/// Compiled evidence output — FINDING_DETAILS (machine-grade) and COMMENTS (human-grade).
/// Null fields mean "no enrichment available for this field."
/// </summary>
public sealed record CompiledEvidence(
    string? FindingDetails,
    string? Comments);

/// <summary>
/// Compiles raw evidence artifacts + verify/apply context into
/// auditor-ready FINDING_DETAILS and COMMENTS text for CKL export.
/// The implementation handles evidence index building/caching internally.
/// </summary>
public interface IEvidenceCompiler
{
    /// <summary>
    /// Compile evidence for a single control. Returns null if no evidence is available.
    /// The implementation reads evidence artifacts from {bundleRoot}/Evidence/by_control/.
    /// Index is built and cached per bundleRoot automatically.
    /// </summary>
    /// <param name="input">Control data from the verify pipeline.</param>
    /// <param name="bundleRoot">Bundle root path — evidence is at {bundleRoot}/Evidence/by_control/.</param>
    CompiledEvidence? CompileEvidence(
        EvidenceCompilationInput input,
        string bundleRoot);
}
