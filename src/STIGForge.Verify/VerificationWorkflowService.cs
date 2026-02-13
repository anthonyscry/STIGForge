using System.Linq;
using STIGForge.Core.Abstractions;
using PackTypes = STIGForge.Core.Constants.PackTypes;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
using STIGForge.Verify.Adapters;

namespace STIGForge.Verify;

public sealed partial class VerificationWorkflowService : IVerificationWorkflowService
{
  private readonly EvaluateStigRunner _evaluateStigRunner;
  private readonly IScapRunner _scapRunner;
  private readonly DscScanRunner _dscScanRunner;
  private static readonly string OverflowErrorSignature = "Overflow range error (OVERFLOW)";
  private static readonly string OverflowFallbackLabel = "overflow-fallback";

  private sealed class ScapAttemptResult
  {
    public string Label { get; set; } = string.Empty;
    public VerifyRunResult RunResult { get; set; } = new VerifyRunResult();
    public string OutputDirectory { get; set; } = string.Empty;
    public string PatchedOutputDirectory { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool UsedFallbackProfile { get; set; }
  }

  public VerificationWorkflowService(EvaluateStigRunner evaluateStigRunner, IScapRunner scapRunner, DscScanRunner dscScanRunner)
  {
    _evaluateStigRunner = evaluateStigRunner;
    _scapRunner = scapRunner;
    _dscScanRunner = dscScanRunner;
  }

  public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
  {
    return Task.Run(() => RunCore(request, ct), ct);
  }

  private VerificationWorkflowResult RunCore(VerificationWorkflowRequest request, CancellationToken ct)
  {
    if (request == null)
      throw new ArgumentNullException(nameof(request));

    if (string.IsNullOrWhiteSpace(request.OutputRoot))
      throw new ArgumentException("OutputRoot is required.", nameof(request));

    ct.ThrowIfCancellationRequested();

    Directory.CreateDirectory(request.OutputRoot);

    var workflowStartedAt = DateTimeOffset.Now;
    var diagnostics = new List<string>();
    var toolRuns = new List<VerificationToolRunResult>(3);

    toolRuns.Add(RunEvaluateStigIfConfigured(request, diagnostics));
    toolRuns.Add(RunScapIfConfigured(request, diagnostics));
    toolRuns.Add(RunDscScanIfConfigured(request, diagnostics));

    var discoveredOutputPaths = DiscoverToolOutputPaths(request, toolRuns, workflowStartedAt, diagnostics);

    var artifacts = BuildArtifactPaths(request.OutputRoot);
    var toolLabel = ResolveConsolidatedToolLabel(request, toolRuns);

    var report = VerifyReportWriter.BuildFromCkls(request.OutputRoot, toolLabel);

    MergeAdapterResults(request, report, diagnostics, discoveredOutputPaths);
    MergeDscScanResults(request, report, diagnostics);

    report.StartedAt = ResolveReportStart(workflowStartedAt, toolRuns);
    report.FinishedAt = ResolveReportFinish(report.StartedAt, toolRuns);

    VerifyReportWriter.WriteJson(artifacts.ConsolidatedJsonPath, report);
    VerifyReportWriter.WriteCsv(artifacts.ConsolidatedCsvPath, report.Results);

    var coverage = VerifyReportWriter.BuildCoverageSummary(report.Results);
    VerifyReportWriter.WriteCoverageSummary(artifacts.CoverageSummaryCsvPath, artifacts.CoverageSummaryJsonPath, coverage);

    if (report.Results.Count == 0)
      diagnostics.Add("No verification results were found (CKL, SCAP XCCDF, Evaluate-STIG XML, or DSC scan) under output root.");

    var workflowFinishedAt = DateTimeOffset.Now;

    WriteDiagnosticsLog(request.OutputRoot, diagnostics, workflowStartedAt, workflowFinishedAt);

    return new VerificationWorkflowResult
    {
      StartedAt = report.StartedAt,
      FinishedAt = workflowFinishedAt > report.FinishedAt ? workflowFinishedAt : report.FinishedAt,
      ConsolidatedJsonPath = artifacts.ConsolidatedJsonPath,
      ConsolidatedCsvPath = artifacts.ConsolidatedCsvPath,
      CoverageSummaryJsonPath = artifacts.CoverageSummaryJsonPath,
      CoverageSummaryCsvPath = artifacts.CoverageSummaryCsvPath,
      ConsolidatedResultCount = report.Results.Count,
      ToolRuns = toolRuns,
      Diagnostics = diagnostics
    };
  }

  private VerificationToolRunResult RunEvaluateStigIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics)
  {
    var now = DateTimeOffset.Now;

    if (!request.EvaluateStig.Enabled)
    {
      return BuildSkippedRun("Evaluate-STIG", now, "Evaluate-STIG execution not enabled.");
    }

    if (string.IsNullOrWhiteSpace(request.EvaluateStig.ToolRoot))
    {
      diagnostics.Add("Evaluate-STIG enabled but ToolRoot was empty.");
      return BuildSkippedRun("Evaluate-STIG", now, "Missing tool root.");
    }

    var outputDir = ResolveEvaluateStigOutputDir(request.OutputRoot);
    Directory.CreateDirectory(outputDir);

    var fixedUserArgs = FixEvaluateStigArguments(request.EvaluateStig.Arguments, request.EvaluateStig.ToolRoot, diagnostics);
    var effectiveArgs = BuildEvaluateStigArguments(fixedUserArgs, outputDir);
    
    var workingDirectory = outputDir;

    diagnostics.Add($"[DIAG] Evaluate-STIG tool root: {request.EvaluateStig.ToolRoot}");
    diagnostics.Add($"[DIAG] Evaluate-STIG working directory: {workingDirectory}");
    diagnostics.Add($"[DIAG] Evaluate-STIG output directory: {outputDir}");
    diagnostics.Add($"[DIAG] Evaluate-STIG arguments: {effectiveArgs}");

    try
    {
      var runResult = _evaluateStigRunner.Run(
        request.EvaluateStig.ToolRoot,
        effectiveArgs,
        workingDirectory);

      if (runResult.ExitCode != 0)
        diagnostics.Add($"[DIAG] Evaluate-STIG exited with code {runResult.ExitCode}.");

      if (!string.IsNullOrWhiteSpace(runResult.Output))
        diagnostics.Add($"[DIAG] Evaluate-STIG stdout: {TruncateForLog(runResult.Output)}");
      if (!string.IsNullOrWhiteSpace(runResult.Error))
        diagnostics.Add($"[DIAG] Evaluate-STIG stderr: {TruncateForLog(runResult.Error)}");

      WriteToolProcessLogs(outputDir, "Evaluate-STIG", runResult.Output, runResult.Error, diagnostics);

      var generatedCkls = Directory.GetFiles(outputDir, "*.ckl", SearchOption.AllDirectories);
      var generatedXmls = Directory.GetFiles(outputDir, "*.xml", SearchOption.AllDirectories);
      diagnostics.Add($"[DIAG] Evaluate-STIG generated {generatedCkls.Length} CKL file(s), {generatedXmls.Length} XML file(s)");
      
      var allGenerated = generatedCkls.Concat(generatedXmls).ToList();
      if (allGenerated.Count > 0)
      {
        foreach (var file in allGenerated.Take(5))
          diagnostics.Add($"[DIAG]   - {Path.GetFileName(file)}");
        if (allGenerated.Count > 5)
          diagnostics.Add($"[DIAG]   - ... and {allGenerated.Count - 5} more");
      }
      else
      {
        diagnostics.Add($"[DIAG] WARNING: No output files generated. Check tool stderr for errors.");
      }

      return new VerificationToolRunResult
      {
        Tool = "Evaluate-STIG",
        Executed = true,
        ExitCode = runResult.ExitCode,
        StartedAt = runResult.StartedAt,
        FinishedAt = runResult.FinishedAt,
        Output = runResult.Output,
        Error = runResult.Error
      };
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] Evaluate-STIG execution failed: {ex.Message}");
      return new VerificationToolRunResult
      {
        Tool = "Evaluate-STIG",
        Executed = false,
        ExitCode = -1,
        StartedAt = now,
        FinishedAt = DateTimeOffset.Now,
        Output = string.Empty,
        Error = ex.Message
      };
    }
  }

  private static string ResolveEvaluateStigOutputDir(string outputRoot)
  {
    if (outputRoot.EndsWith("Evaluate-STIG", StringComparison.OrdinalIgnoreCase))
      return outputRoot;
    
    return Path.Combine(outputRoot, VerificationWorkflowDefaults.EvaluateStigOutputDir);
  }

  private static string FixEvaluateStigArguments(string userArgs, string toolRoot, List<string> diagnostics)
  {
    if (string.IsNullOrWhiteSpace(userArgs))
      return string.Empty;

    var fixedArgs = userArgs;
    
    if (fixedArgs.IndexOf("-AnswerFile", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      fixedArgs = System.Text.RegularExpressions.Regex.Replace(
        fixedArgs, 
        @"-AnswerFile\s*", 
        "-AFPath ", 
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
      fixedArgs = System.Text.RegularExpressions.Regex.Replace(
        fixedArgs, 
        @"-AnswerFile:", 
        "-AFPath:", 
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    var afPathMatch = System.Text.RegularExpressions.Regex.Match(
      fixedArgs,
      @"(?i)-AFPath(?::|\s+)(?:""([^""]+)""|(\S+))");

    if (afPathMatch.Success)
    {
      var rawPath = afPathMatch.Groups[1].Success ? afPathMatch.Groups[1].Value : afPathMatch.Groups[2].Value;
      var resolvedPath = ResolveEvaluateStigAnswerFileDirectory(rawPath, toolRoot);
      if (!string.Equals(rawPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
      {
        fixedArgs = System.Text.RegularExpressions.Regex.Replace(
          fixedArgs,
          @"(?i)-AFPath(?::|\s+)(?:""([^""]+)""|(\S+))",
          $"-AFPath \"{resolvedPath}\"");
        diagnostics.Add($"[DIAG] Evaluate-STIG AFPath resolved to directory: {resolvedPath}");
      }
    }

    return fixedArgs;
  }

  private static string ResolveEvaluateStigAnswerFileDirectory(string configuredPath, string toolRoot)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
      return configuredPath;

    if (Path.IsPathRooted(configuredPath))
    {
      if (Directory.Exists(configuredPath))
        return Path.GetFullPath(configuredPath);

      if (File.Exists(configuredPath))
      {
        var fileDir = Path.GetDirectoryName(Path.GetFullPath(configuredPath));
        return string.IsNullOrWhiteSpace(fileDir) ? configuredPath : fileDir;
      }

      return configuredPath;
    }

    var candidates = new List<string>();

    if (!string.IsNullOrWhiteSpace(toolRoot))
    {
      candidates.Add(Path.GetFullPath(Path.Combine(toolRoot, configuredPath)));

      var fileName = Path.GetFileName(configuredPath);
      if (!string.IsNullOrWhiteSpace(fileName))
      {
        candidates.Add(Path.GetFullPath(Path.Combine(toolRoot, "AnswerFiles", fileName)));
      }

      candidates.Add(Path.GetFullPath(Path.Combine(toolRoot, "AnswerFiles")));
    }

    foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      if (Directory.Exists(candidate))
        return candidate;

      if (File.Exists(candidate))
      {
        var fileDir = Path.GetDirectoryName(candidate);
        if (!string.IsNullOrWhiteSpace(fileDir))
          return fileDir;
      }
    }

    return configuredPath;
  }

  private static string BuildEvaluateStigArguments(string userArgs, string outputDir)
  {
    var hasOutput = !string.IsNullOrWhiteSpace(userArgs) && 
      (userArgs.IndexOf("-Output", StringComparison.OrdinalIgnoreCase) >= 0 ||
       userArgs.IndexOf("-OutputPath", StringComparison.OrdinalIgnoreCase) >= 0);

    if (hasOutput)
      return userArgs ?? string.Empty;

    var outputArgs = $"-Output CKL,XCCDF -OutputPath \"{outputDir}\"";
    return string.IsNullOrWhiteSpace(userArgs) 
      ? outputArgs 
      : $"{userArgs} {outputArgs}";
  }

  private VerificationToolRunResult RunScapIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics)
  {
    var toolName = string.IsNullOrWhiteSpace(request.Scap.ToolLabel) ? PackTypes.Scap : request.Scap.ToolLabel.Trim();
    var now = DateTimeOffset.Now;

    if (!request.Scap.Enabled)
    {
      return BuildSkippedRun(toolName, now, "SCAP execution not enabled.");
    }

    if (string.IsNullOrWhiteSpace(request.Scap.CommandPath))
    {
      diagnostics.Add("SCAP enabled but CommandPath was empty.");
      return BuildSkippedRun(toolName, now, "Missing command path.");
    }

    var commandPath = ResolveScapExecutablePath(request.Scap.CommandPath, diagnostics, toolName);
    if (!File.Exists(commandPath))
    {
      diagnostics.Add($"[{toolName}] SCAP command path not found: {commandPath}");
      return BuildSkippedRun(toolName, now, "Missing command path.");
    }

    var outputDir = ResolveScapOutputDir(request.OutputRoot);
    Directory.CreateDirectory(outputDir);
    var workingDirectory = !string.IsNullOrWhiteSpace(request.Scap.WorkingDirectory)
      ? request.Scap.WorkingDirectory!
      : (Path.GetDirectoryName(commandPath) ?? outputDir);

    diagnostics.Add($"[DIAG] {toolName} command path: {commandPath}");
    diagnostics.Add($"[DIAG] {toolName} working directory: {workingDirectory}");
    diagnostics.Add($"[DIAG] {toolName} output directory: {outputDir}");

    var canFallback = !HasScapSFlag(request.Scap.Arguments);
    if (!canFallback)
      diagnostics.Add($"[DIAG] {toolName} detected user-supplied -s; overflow fallback profile overrides are disabled.");

    try
    {
      var primaryAttempt = ExecuteScapScanAttempt(
        commandPath,
        request.Scap.Arguments,
        outputDir,
        workingDirectory,
        diagnostics,
        toolName,
        canFallback ? "primary" : "primary",
        useFallbackProfile: false);

      if (canFallback && IsScapOverflowFailure(primaryAttempt.RunResult))
      {
        diagnostics.Add($"[DIAG] {toolName} detected SCC overflow error. Retrying with focused fallback profile and alternate output.");

        var fallbackOutputDir = ResolveScapFallbackOutputDir(request.OutputRoot);
        Directory.CreateDirectory(fallbackOutputDir);

        var fallbackAttempt = ExecuteScapScanAttempt(
          commandPath,
          request.Scap.Arguments,
          fallbackOutputDir,
          workingDirectory,
          diagnostics,
          toolName,
          OverflowFallbackLabel,
          useFallbackProfile: true);

        return BuildScapRunResult(
          toolName,
          primaryAttempt,
          fallbackAttempt);
      }

      if (IsScapOverflowFailure(primaryAttempt.RunResult) && !canFallback)
      {
        diagnostics.Add($"[DIAG] {toolName} detected SCC overflow error. User supplied -s, so overflow fallback will not retry.");
      }

      if (!primaryAttempt.RunResult.ExitCode.Equals(0) && !IsScapOverflowFailure(primaryAttempt.RunResult))
      {
        diagnostics.Add($"[DIAG] {toolName} did not overflow and will not retry.");
      }

      return BuildScapRunResult(toolName, primaryAttempt);
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} execution failed: {ex.Message}");
      return new VerificationToolRunResult
      {
        Tool = toolName,
        Executed = false,
        ExitCode = -1,
        StartedAt = now,
        FinishedAt = DateTimeOffset.Now,
        Output = string.Empty,
        Error = ex.Message
      };
    }
  }

  private static string ResolveScapOutputDir(string outputRoot)
  {
    if (outputRoot.EndsWith(PackTypes.Scap, StringComparison.OrdinalIgnoreCase))
      return outputRoot;
    
    return Path.Combine(outputRoot, VerificationWorkflowDefaults.ScapOutputDir);
  }

  private static string BuildScapArguments(string userArgs, string commandPath, string outputDir, List<string> diagnostics, string toolName, out string patchedOutputDirectory)
  {
    patchedOutputDirectory = string.Empty;
    var effectiveArgs = (userArgs ?? string.Empty).Trim();

    if (HasScapSFlag(effectiveArgs))
    {
      diagnostics.Add($"[DIAG] {toolName} user supplied -s flag — using as-is: {effectiveArgs}");
      return effectiveArgs;
    }

    var sourceOptions = ResolveScapOptionsXml(commandPath);
    if (string.IsNullOrWhiteSpace(sourceOptions) || !File.Exists(sourceOptions))
    {
      diagnostics.Add($"[DIAG] {toolName} ERROR: cannot find options.xml near {commandPath}. SCC requires -s <options.xml> to scan.");
      return effectiveArgs;
    }

    var stagedPath = Path.Combine(outputDir, "scc-options-staged.xml");
    try
    {
      File.Copy(sourceOptions, stagedPath, overwrite: true);
      patchedOutputDirectory = PatchScapOptionsForScan(stagedPath, outputDir, diagnostics, toolName);
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} failed to stage options.xml: {ex.Message}");
      return effectiveArgs;
    }

    var quotedPath = stagedPath.IndexOf(' ') >= 0 ? $"\"{stagedPath}\"" : stagedPath;
    diagnostics.Add($"[DIAG] {toolName} staged options profile for scan: {stagedPath}");
    return $"-s {quotedPath}";
  }

  private ScapAttemptResult ExecuteScapScanAttempt(
    string commandPath,
    string userArgs,
    string outputDirectory,
    string workingDirectory,
    List<string> diagnostics,
    string toolName,
    string attemptLabel,
    bool useFallbackProfile)
  {
    var attemptStartedAt = DateTimeOffset.Now;
    Directory.CreateDirectory(outputDirectory);

    diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' started at {attemptStartedAt:HH:mm:ss.fff}");
    var effectiveArgs = BuildScapArguments(
      userArgs,
      commandPath,
      outputDirectory,
      diagnostics,
      toolName,
      out var patchedOutputDirectory);

    diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' output directory: {outputDirectory}");
    diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' arguments: {effectiveArgs}");
    if (useFallbackProfile)
      diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' is using fallback output/profile path.");

    try
    {
      var runResult = _scapRunner.Run(commandPath, effectiveArgs, workingDirectory);

      diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' exited with code {runResult.ExitCode}.");

      if (!string.IsNullOrWhiteSpace(runResult.Output))
        diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' stdout: {TruncateForLog(runResult.Output)}");

      if (!string.IsNullOrWhiteSpace(runResult.Error))
        diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' stderr: {TruncateForLog(runResult.Error)}");

      WriteToolProcessLogs(outputDirectory, $"{toolName}-{attemptLabel}", runResult.Output, runResult.Error, diagnostics);
      LogScapOutputDiagnostics(toolName, outputDirectory, patchedOutputDirectory, runResult.StartedAt, diagnostics);

      return new ScapAttemptResult
      {
        Label = attemptLabel,
        RunResult = runResult,
        OutputDirectory = outputDirectory,
        PatchedOutputDirectory = patchedOutputDirectory,
        Arguments = effectiveArgs,
        UsedFallbackProfile = useFallbackProfile
      };
    }
    catch (Exception ex)
    {
      diagnostics.Add($"[DIAG] {toolName} SCAP attempt '{attemptLabel}' failed: {ex.Message}");

      var failedRun = new VerifyRunResult
      {
        ExitCode = -1,
        Output = string.Empty,
        Error = ex.Message,
        StartedAt = attemptStartedAt,
        FinishedAt = DateTimeOffset.Now
      };

      return new ScapAttemptResult
      {
        Label = attemptLabel,
        RunResult = failedRun,
        OutputDirectory = outputDirectory,
        PatchedOutputDirectory = patchedOutputDirectory,
        Arguments = effectiveArgs,
        UsedFallbackProfile = useFallbackProfile
      };
    }
  }

  private static VerificationToolRunResult BuildScapRunResult(string toolName, ScapAttemptResult primaryAttempt, ScapAttemptResult? fallbackAttempt = null)
  {
    var attempts = fallbackAttempt == null
      ? new[] { primaryAttempt }
      : new[] { primaryAttempt, fallbackAttempt };

    var combinedRunOutput = new List<string>();
    var combinedRunError = new List<string>();
    var startedAt = attempts.Min(a => a.RunResult.StartedAt);
    var finishedAt = attempts.Max(a => a.RunResult.FinishedAt);
    var finalAttempt = attempts.Last();

    foreach (var attempt in attempts)
    {
      combinedRunOutput.Add($"[SCAP ATTEMPT: {attempt.Label}]");
      combinedRunOutput.Add($"Arguments: {attempt.Arguments}");
      combinedRunOutput.Add($"Output Directory: {attempt.OutputDirectory}");

      if (!string.IsNullOrWhiteSpace(attempt.PatchedOutputDirectory))
        combinedRunOutput.Add($"Patched Output Directory: {attempt.PatchedOutputDirectory}");

      combinedRunOutput.Add($"Exit Code: {attempt.RunResult.ExitCode}");

      if (!string.IsNullOrWhiteSpace(attempt.RunResult.Output))
        combinedRunOutput.Add($"STDOUT: {attempt.RunResult.Output.Trim()}");

      if (!string.IsNullOrWhiteSpace(attempt.RunResult.Error))
        combinedRunError.Add($"[{attempt.Label}] {attempt.RunResult.Error.Trim()}");

      foreach (var resultFile in ResolveScapResultFiles(attempt.OutputDirectory))
      {
        combinedRunOutput.Add(resultFile);
      }
    }

    return new VerificationToolRunResult
    {
      Tool = toolName,
      Executed = true,
      ExitCode = finalAttempt.RunResult.ExitCode,
      StartedAt = startedAt,
      FinishedAt = finishedAt,
      Output = string.Join(Environment.NewLine, combinedRunOutput),
      Error = string.Join(Environment.NewLine, combinedRunError)
    };
  }

  private static bool IsScapOverflowFailure(VerifyRunResult runResult)
  {
    if (runResult.ExitCode == 0)
      return false;

    var combinedOutput = string.Concat(runResult.Output ?? string.Empty, " ", runResult.Error ?? string.Empty);
    return combinedOutput.IndexOf(OverflowErrorSignature, StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static string ResolveScapFallbackOutputDir(string outputRoot)
  {
    if (outputRoot.EndsWith(VerificationWorkflowDefaults.ScapFallbackOutputDir, StringComparison.OrdinalIgnoreCase))
      return outputRoot;

    return Path.Combine(outputRoot, VerificationWorkflowDefaults.ScapFallbackOutputDir);
  }

  private static IEnumerable<string> ResolveScapResultFiles(string outputDirectory)
  {
    if (!Directory.Exists(outputDirectory))
      return Array.Empty<string>();

    var resultFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    try
    {
      foreach (var path in Directory.GetFiles(outputDirectory, "*.xml", SearchOption.AllDirectories))
        resultFiles.Add(path);

      foreach (var path in Directory.GetFiles(outputDirectory, "*.xccdf", SearchOption.AllDirectories))
        resultFiles.Add(path);

      foreach (var path in Adapters.ScapResultAdapter.EnumerateCandidateFiles(outputDirectory))
        resultFiles.Add(path);
    }
    catch
    {
    }

    return resultFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
  }

  private static bool HasScapSFlag(string args)
  {
    if (string.IsNullOrWhiteSpace(args))
      return false;

    return System.Text.RegularExpressions.Regex.IsMatch(args, @"(?i)(^|\s)-s\s+\S");
  }

  private static string ResolveScapOptionsXml(string commandPath)
  {
    if (string.IsNullOrWhiteSpace(commandPath))
      return string.Empty;

    var commandDir = Path.GetDirectoryName(commandPath);
    if (string.IsNullOrWhiteSpace(commandDir))
      return string.Empty;

    var candidate = Path.Combine(commandDir, "options.xml");
    if (File.Exists(candidate))
      return candidate;

    var parentDir = Path.GetDirectoryName(commandDir);
    if (!string.IsNullOrWhiteSpace(parentDir))
    {
      candidate = Path.Combine(parentDir, "options.xml");
      if (File.Exists(candidate))
        return candidate;
    }

    return string.Empty;
  }

  private static string PatchScapOptionsForScan(string optionsPath, string outputDir, List<string> diagnostics, string toolName)
  {
    var doc = System.Xml.Linq.XDocument.Load(optionsPath);
    var root = doc.Root;
    if (root == null)
      return string.Empty;

    SetXmlElement(root, "scan", "1");
    SetXmlElement(root, "load", "1");
    SetXmlElement(root, "customPathLocal", outputDir);

    // Point summary source/destination to our output dir so post-scan reports find results
    SetXmlElement(root, "summarySourceDirectory", outputDir);
    SetXmlElement(root, "summaryDestinationDirectory", outputDir);
    SetXmlElement(root, "detailSummarySourceDirectory", outputDir);
    SetXmlElement(root, "detailSummaryDestinationDirectory", outputDir);
    SetXmlElement(root, "detailSummaryOVALSourceDirectory", outputDir);
    SetXmlElement(root, "detailSummaryOVALDestinationDirectory", outputDir);

    SetXmlElement(root, "keepXCCDFXML", "1");
    SetXmlElement(root, "saveDISACKL", "1");
    SetXmlElement(root, "saveDISACKLB", "1");
    SetXmlElement(root, "keepARFXML", "0");
    SetXmlElement(root, "openSummaryDestinationDirectory", "0");
    SetXmlElement(root, "openDetailSummaryDestinationDirectory", "0");
    SetXmlElement(root, "openDetailSummaryOVALDestinationDirectory", "0");

    // Disable UI popups and auto-open behaviors
    SetXmlElement(root, "openWithSCC", "0");
    SetXmlElement(root, "enableSummaryViewer", "0");

    var customPathLocal = (root.Element("customPathLocal")?.Value ?? string.Empty).Trim();

    doc.Save(optionsPath);
    diagnostics.Add($"[DIAG] {toolName} patched staged options: scan=1, output={outputDir}");
    diagnostics.Add($"[DIAG] {toolName} patched options customPathLocal: {customPathLocal}");
    return customPathLocal;
  }

  private static void SetXmlElement(System.Xml.Linq.XElement root, string name, string value)
  {
    var el = root.Element(name);
    if (el != null)
      el.Value = value;
    else
      root.Add(new System.Xml.Linq.XElement(name, value));
  }





  private static string ResolveScapExecutablePath(string configuredPath, List<string> diagnostics, string toolName)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
      return configuredPath;

    if (!File.Exists(configuredPath))
      return configuredPath;

    var fileName = Path.GetFileName(configuredPath);
    if (!string.Equals(fileName, "scc.exe", StringComparison.OrdinalIgnoreCase))
      return configuredPath;

    var commandDir = Path.GetDirectoryName(configuredPath) ?? string.Empty;
    if (string.IsNullOrWhiteSpace(commandDir))
      return configuredPath;

    var csccPath = Path.Combine(commandDir, "cscc.exe");
    if (File.Exists(csccPath))
    {
      diagnostics.Add($"[DIAG] {toolName} resolved CLI executable: {csccPath}");
      return csccPath;
    }

    var cscc64Path = Path.Combine(commandDir, "lib64", "cscc64.exe");
    if (File.Exists(cscc64Path))
    {
      diagnostics.Add($"[DIAG] {toolName} resolved CLI executable: {cscc64Path}");
      return cscc64Path;
    }

    return configuredPath;
  }



  private static VerificationToolRunResult BuildSkippedRun(string tool, DateTimeOffset at, string message)
  {
    return new VerificationToolRunResult
    {
      Tool = tool,
      Executed = false,
      ExitCode = 0,
      StartedAt = at,
      FinishedAt = at,
      Output = string.Empty,
      Error = message
    };
  }

  private static VerificationWorkflowArtifacts BuildArtifactPaths(string outputRoot)
  {
    return new VerificationWorkflowArtifacts
    {
      ConsolidatedJsonPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.ConsolidatedJsonFile),
      ConsolidatedCsvPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.ConsolidatedCsvFile),
      CoverageSummaryJsonPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.CoverageSummaryJsonFile),
      CoverageSummaryCsvPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.CoverageSummaryCsvFile)
    };
  }

  private static string ResolveConsolidatedToolLabel(VerificationWorkflowRequest request, IReadOnlyList<VerificationToolRunResult> runs)
  {
    if (!string.IsNullOrWhiteSpace(request.ConsolidatedToolLabel))
      return request.ConsolidatedToolLabel.Trim();

    var executed = runs.Where(r => r.Executed).Select(r => r.Tool).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    if (executed.Count == 1)
      return executed[0];

    if (executed.Count > 1)
      return "Verification";

    return "Verification";
  }

  private static DateTimeOffset ResolveReportStart(DateTimeOffset fallback, IReadOnlyList<VerificationToolRunResult> runs)
  {
    var executedStarts = runs.Where(r => r.Executed).Select(r => r.StartedAt).ToList();
    return executedStarts.Count == 0 ? fallback : executedStarts.Min();
  }

  private static DateTimeOffset ResolveReportFinish(DateTimeOffset fallback, IReadOnlyList<VerificationToolRunResult> runs)
  {
    var executedFinishes = runs.Where(r => r.Executed).Select(r => r.FinishedAt).ToList();
    return executedFinishes.Count == 0 ? fallback : executedFinishes.Max();
  }

  private VerificationToolRunResult RunDscScanIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics)
  {
    var toolName = string.IsNullOrWhiteSpace(request.DscScan.ToolLabel)
      ? VerificationWorkflowDefaults.DscScanToolLabel
      : request.DscScan.ToolLabel.Trim();
    var now = DateTimeOffset.Now;

    if (!request.DscScan.Enabled)
      return BuildSkippedRun(toolName, now, "DSC scan not enabled.");

    if (string.IsNullOrWhiteSpace(request.DscScan.MofPath))
    {
      diagnostics.Add("DSC scan enabled but MofPath was empty.");
      return BuildSkippedRun(toolName, now, "Missing MOF path.");
    }

    var outputDir = Path.Combine(request.OutputRoot, VerificationWorkflowDefaults.DscScanOutputDir);

    try
    {
      var runResult = _dscScanRunner.Run(
        request.DscScan.MofPath,
        request.DscScan.Verbose,
        outputDir);

      if (runResult.ExitCode != 0)
        diagnostics.Add($"{toolName} exited with code {runResult.ExitCode}.");

      return new VerificationToolRunResult
      {
        Tool = toolName,
        Executed = true,
        ExitCode = runResult.ExitCode,
        StartedAt = runResult.StartedAt,
        FinishedAt = runResult.FinishedAt,
        Output = runResult.Output,
        Error = runResult.Error
      };
    }
    catch (Exception ex)
    {
      diagnostics.Add($"{toolName} execution failed: {ex.Message}");
      return new VerificationToolRunResult
      {
        Tool = toolName,
        Executed = false,
        ExitCode = -1,
        StartedAt = now,
        FinishedAt = DateTimeOffset.Now,
        Output = string.Empty,
        Error = ex.Message
      };
    }
  }

  private static void MergeAdapterResults(VerificationWorkflowRequest request, VerifyReport report, List<string> diagnostics, IReadOnlyList<string> discoveredOutputPaths)
  {
    var adapters = new IVerifyResultAdapter[]
    {
      new Adapters.EvaluateStigAdapter(),
      new Adapters.ScapResultAdapter()
    };

    var searchRoots = new List<string> { request.OutputRoot };

    var evalOutputDir = ResolveEvaluateStigOutputDir(request.OutputRoot);
    var scapOutputDir = ResolveScapOutputDir(request.OutputRoot);

    if (request.EvaluateStig.Enabled)
    {
      if (!string.IsNullOrWhiteSpace(request.EvaluateStig.WorkingDirectory))
        searchRoots.Add(request.EvaluateStig.WorkingDirectory!);
      searchRoots.Add(evalOutputDir);
    }

    if (request.Scap.Enabled)
    {
      if (!string.IsNullOrWhiteSpace(request.Scap.WorkingDirectory))
        searchRoots.Add(request.Scap.WorkingDirectory!);
      searchRoots.Add(scapOutputDir);
    }

    foreach (var discoveredPath in discoveredOutputPaths)
    {
      if (!searchRoots.Contains(discoveredPath, StringComparer.OrdinalIgnoreCase))
        searchRoots.Add(discoveredPath);
    }

    var merged = new List<ControlResult>(report.Results);
    var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var totalXmlFilesFound = 0;
    var totalResultsParsed = 0;

    diagnostics.Add($"[DIAG] Searching for verification results in {searchRoots.Count} locations...");

    var normalizedOutputRoot = Path.GetFullPath(request.OutputRoot);

    foreach (var root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      if (string.IsNullOrWhiteSpace(root))
      {
        diagnostics.Add($"[DIAG] Skipping empty search root");
        continue;
      }

      if (!Directory.Exists(root))
      {
        diagnostics.Add($"[DIAG] Search root does not exist: {root}");
        continue;
      }

      diagnostics.Add($"[DIAG] Searching for XML files in: {root}");

      string[] xmlFiles;
      string[] xccdfFiles;
      string[] cklFiles;
      var scapPatternFiles = Array.Empty<string>();
      try
      {
        xmlFiles = Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories);
        xccdfFiles = Directory.GetFiles(root, "*.xccdf", SearchOption.AllDirectories);
        cklFiles = Directory.GetFiles(root, "*.ckl", SearchOption.AllDirectories);
        scapPatternFiles = Adapters.ScapResultAdapter.EnumerateCandidateFiles(root).ToArray();
      }
      catch (Exception ex)
      {
        diagnostics.Add($"[DIAG] Failed to search directory '{root}': {ex.Message}");
        continue;
      }

      var cklStems = new HashSet<string>(
        cklFiles
          .Where(f => IsPathUnderRoot(f, normalizedOutputRoot))
          .Select(GetResultArtifactKey),
        StringComparer.OrdinalIgnoreCase);

      diagnostics.Add($"[DIAG] SCAP adapter pattern matches in {root}: {scapPatternFiles.Length}");

      var allResultFiles = xmlFiles
        .Concat(xccdfFiles)
        .Concat(scapPatternFiles)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
      diagnostics.Add($"[DIAG] Found {xmlFiles.Length} XML + {xccdfFiles.Length} XCCDF files in {root}");
      totalXmlFilesFound += allResultFiles.Length;

      foreach (var xmlFile in allResultFiles)
      {
        if (!processedFiles.Add(xmlFile))
          continue;

        var xmlStem = GetResultArtifactKey(xmlFile);
        if (!string.IsNullOrWhiteSpace(xmlStem) && cklStems.Contains(xmlStem))
        {
          diagnostics.Add($"[DIAG] Skipping XML '{Path.GetFileName(xmlFile)}' because matching CKL exists for the same scan artifact.");
          continue;
        }

        var handledByAdapter = false;
        foreach (var adapter in adapters)
        {
          try
          {
            if (!adapter.CanHandle(xmlFile))
            {
              LogAdapterRejectionReason(adapter.ToolName, xmlFile, diagnostics);
              continue;
            }

            diagnostics.Add($"[DIAG] {adapter.ToolName} adapter accepted '{Path.GetFileName(xmlFile)}' - parsing...");
            var parsed = adapter.ParseResults(xmlFile);

            if (parsed.Results.Count == 0)
            {
              diagnostics.Add($"[DIAG] {adapter.ToolName} adapter parsed 0 results from '{Path.GetFileName(xmlFile)}' - file may be empty or contain no check elements");
              if (parsed.DiagnosticMessages.Count > 0)
                diagnostics.AddRange(parsed.DiagnosticMessages.Select(d => $"[DIAG]   {d}"));
              continue;
            }

            diagnostics.Add($"[DIAG] {adapter.ToolName} adapter parsed {parsed.Results.Count} results from '{Path.GetFileName(xmlFile)}'");
            totalResultsParsed += parsed.Results.Count;
            diagnostics.AddRange(parsed.DiagnosticMessages);

            foreach (var result in parsed.Results)
            {
              merged.Add(new ControlResult
              {
                VulnId = result.VulnId,
                RuleId = result.RuleId,
                Title = result.Title,
                Severity = result.Severity,
                Status = MapVerifyStatus(result.Status),
                FindingDetails = result.FindingDetails,
                Comments = result.Comments,
                Tool = adapter.ToolName,
                SourceFile = result.SourceFile,
                VerifiedAt = result.VerifiedAt
              });
            }

            handledByAdapter = true;
            break;
          }
          catch (Exception ex)
          {
            diagnostics.Add($"[DIAG] Failed to parse '{Path.GetFileName(xmlFile)}' with {adapter.ToolName} adapter: {ex.Message}");
          }
        }

        if (!handledByAdapter)
        {
          LogUnhandledPotentialResultFile(xmlFile, diagnostics);
        }
      }
    }

    diagnostics.Add($"[DIAG] Total XML files found: {totalXmlFilesFound}, Total results parsed: {totalResultsParsed}");

    if (totalXmlFilesFound == 0)
    {
      diagnostics.Add("[DIAG] No XML files found. Tools may be outputting to a different location.");
      diagnostics.Add("[DIAG] Expected locations:");
      diagnostics.Add($"[DIAG]   - OutputRoot: {request.OutputRoot}");
      if (request.EvaluateStig.Enabled)
        diagnostics.Add($"[DIAG]   - Evaluate-STIG ToolRoot: {request.EvaluateStig.ToolRoot ?? "(not set)"}");
      if (request.Scap.Enabled)
        diagnostics.Add($"[DIAG]   - SCAP WorkingDirectory: {request.Scap.WorkingDirectory ?? "(not set)"}");
    }

    report.Results = merged;
  }

  private static void MergeDscScanResults(VerificationWorkflowRequest request, VerifyReport report, List<string> diagnostics)
  {
    if (!request.DscScan.Enabled)
      return;

    var scanDir = Path.Combine(request.OutputRoot, VerificationWorkflowDefaults.DscScanOutputDir);
    diagnostics.Add($"[DIAG] DSC scan output directory: {scanDir}");

    if (!Directory.Exists(scanDir))
    {
      diagnostics.Add("[DIAG] DSC scan output directory does not exist");
      return;
    }

    var dscFiles = Directory.GetFiles(scanDir, "*.dsc-test.json", SearchOption.AllDirectories);
    diagnostics.Add($"[DIAG] Found {dscFiles.Length} DSC test result files");

    if (dscFiles.Length == 0)
    {
      diagnostics.Add("DSC scan ran but no .dsc-test.json files found.");
      return;
    }

    var adapter = new Adapters.DscResultAdapter();
    var toolLabel = string.IsNullOrWhiteSpace(request.DscScan.ToolLabel)
      ? VerificationWorkflowDefaults.DscScanToolLabel
      : request.DscScan.ToolLabel.Trim();

    var merged = new List<ControlResult>(report.Results);
    var totalParsed = 0;
    foreach (var dscFile in dscFiles)
    {
      try
      {
        var parsed = adapter.ParseResults(dscFile);
        foreach (var result in parsed.Results)
        {
          merged.Add(new ControlResult
          {
            VulnId = result.VulnId,
            RuleId = result.RuleId,
            Title = result.Title,
            Severity = result.Severity,
            Status = MapVerifyStatus(result.Status),
            FindingDetails = result.FindingDetails,
            Comments = result.Comments,
            Tool = toolLabel,
            SourceFile = result.SourceFile,
            VerifiedAt = result.VerifiedAt
          });
        }

        totalParsed += parsed.Results.Count;
        if (parsed.Results.Count > 0)
          diagnostics.Add($"[DIAG] DSC adapter parsed {parsed.Results.Count} results from '{Path.GetFileName(dscFile)}'");
        diagnostics.AddRange(parsed.DiagnosticMessages);
      }
      catch (Exception ex)
      {
        diagnostics.Add($"Failed to parse DSC scan file '{dscFile}': {ex.Message}");
      }
    }

    diagnostics.Add($"[DIAG] DSC scan total results parsed: {totalParsed}");
    report.Results = merged;
  }

}
