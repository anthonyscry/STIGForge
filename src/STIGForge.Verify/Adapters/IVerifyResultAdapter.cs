namespace STIGForge.Verify.Adapters;

/// <summary>
/// Interface for adapters that convert tool-specific verification outputs to NormalizedVerifyResult.
/// Each verification tool (SCAP, Evaluate-STIG, CKL) implements this to provide format translation.
/// </summary>
public interface IVerifyResultAdapter
{
  /// <summary>Tool name this adapter handles (e.g., "SCAP", "Evaluate-STIG", "Manual CKL")</summary>
  string ToolName { get; }

  /// <summary>
  /// Parse tool-specific output file and convert to normalized results.
  /// </summary>
  /// <param name="outputPath">Path to tool output file (XCCDF XML, CKL XML, etc.)</param>
  /// <returns>Normalized report with all results</returns>
  NormalizedVerifyReport ParseResults(string outputPath);

  /// <summary>
  /// Check if this adapter can handle the given file format.
  /// </summary>
  /// <param name="filePath">Path to file to check</param>
  /// <returns>True if adapter can parse this file</returns>
  bool CanHandle(string filePath);
}
