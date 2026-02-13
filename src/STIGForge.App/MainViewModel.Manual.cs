using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
using STIGForge.App.Helpers;
using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
using PackTypes = STIGForge.Core.Constants.PackTypes;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Evidence;

namespace STIGForge.App;

public partial class MainViewModel
{
  private int _totalPackControls;
  [RelayCommand]
  private void BrowseEvidenceFile()
  {
    var ofd = new Microsoft.Win32.OpenFileDialog
    {
      Filter = "All Files (*.*)|*.*",
      Title = "Select evidence file"
    };

    if (ofd.ShowDialog() != true) return;
    EvidenceFilePath = ofd.FileName;
  }

  [RelayCommand]
  private void SaveEvidence()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (string.IsNullOrWhiteSpace(EvidenceRuleId))
      {
        EvidenceStatus = "RuleId is required.";
        return;
      }

      var type = ParseEvidenceType(EvidenceType);
      var request = new EvidenceWriteRequest
      {
        BundleRoot = BundleRoot,
        RuleId = EvidenceRuleId,
        Type = type,
        ContentText = string.IsNullOrWhiteSpace(EvidenceText) ? null : EvidenceText,
        SourceFilePath = string.IsNullOrWhiteSpace(EvidenceFilePath) ? null : EvidenceFilePath
      };

      var result = _evidence.WriteEvidence(request);
      EvidenceStatus = "Saved: " + result.EvidencePath;
      LastOutputPath = result.EvidencePath;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Evidence save failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void SaveManualAnswer()
  {
    SaveManualAnswerInternal(isAutoSave: false);
  }

  [RelayCommand]
  private void ImportManualCsv()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot))
    {
      StatusText = "Load a bundle first.";
      return;
    }

    var ofd = new Microsoft.Win32.OpenFileDialog
    {
      Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
      Title = "Import Manual Answers from CSV"
    };

    if (ofd.ShowDialog() != true)
      return;

    try
    {
      var (imported, skipped, errors) = ParseAndImportCsv(ofd.FileName);
      StatusText = $"Imported {imported} answers, skipped {skipped}, errors {errors}.";
      LoadManualControls();
    }
    catch (Exception ex)
    {
      StatusText = $"Import failed: {ex.Message}";
    }
  }

  private (int imported, int skipped, int errors) ParseAndImportCsv(string filePath)
  {
    var imported = 0;
    var skipped = 0;
    var errors = 0;

    using var reader = new StreamReader(filePath);
    string? headerLine = null;
    while (!reader.EndOfStream)
    {
      var candidate = reader.ReadLine();
      if (!string.IsNullOrWhiteSpace(candidate))
      {
        headerLine = candidate;
        break;
      }
    }

    if (string.IsNullOrWhiteSpace(headerLine))
      throw new InvalidOperationException("CSV file is empty.");

    var headers = ParseCsvLine(headerLine);
    var indexByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < headers.Count; i++)
    {
      var name = headers[i].Trim().TrimStart('\uFEFF');
      if (!indexByHeader.ContainsKey(name))
        indexByHeader[name] = i;
    }

    var hasRuleId = indexByHeader.TryGetValue("RuleId", out var ruleIdIndex);
    var hasVulnId = indexByHeader.TryGetValue("VulnId", out var vulnIdIndex);
    var hasStatus = indexByHeader.TryGetValue("Status", out var statusIndex);
    indexByHeader.TryGetValue("Reason", out var reasonIndex);
    indexByHeader.TryGetValue("Comment", out var commentIndex);

    if ((!hasRuleId && !hasVulnId) || !hasStatus)
      throw new InvalidOperationException("CSV header must include Status and either RuleId or VulnId.");

    static string? ReadField(List<string> fields, int index)
    {
      if (index < 0 || index >= fields.Count)
        return null;

      var value = fields[index].Trim();
      return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    var processed = 0;
    while (!reader.EndOfStream)
    {
      var line = reader.ReadLine();
      if (string.IsNullOrWhiteSpace(line))
        continue;

      processed++;

      var parts = ParseCsvLine(line);
      var ruleId = hasRuleId ? ReadField(parts, ruleIdIndex) : null;
      var vulnId = hasVulnId ? ReadField(parts, vulnIdIndex) : null;
      if (string.IsNullOrWhiteSpace(ruleId) && string.IsNullOrWhiteSpace(vulnId))
      {
        skipped++;
        continue;
      }

      var answer = new ManualAnswer
      {
        RuleId = ruleId,
        VulnId = vulnId,
        Status = ReadField(parts, statusIndex) ?? string.Empty,
        Reason = ReadField(parts, reasonIndex),
        Comment = ReadField(parts, commentIndex)
      };

      try
      {
        _manualAnswerService.SaveAnswer(BundleRoot, answer);
        imported++;
      }
      catch (Exception ex)
      {
        errors++;
        _logger?.LogWarning(ex, "Failed to import manual answer from CSV row {Row}", processed + 1);
      }

      if (processed % 25 == 0)
        StatusText = $"Importing manual answers... processed {processed} rows (imported {imported}, skipped {skipped}, errors {errors}).";
    }

    return (imported, skipped, errors);
  }

  private bool SaveManualAnswerInternal(bool isAutoSave)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        if (!isAutoSave)
          EvidenceStatus = "Select a bundle first.";
        return false;
      }

      if (SelectedManualControl == null)
      {
        if (!isAutoSave)
          EvidenceStatus = "Select a manual control.";
        return false;
      }

      var normalizedStatus = _manualAnswerService.NormalizeStatus(ManualStatus);
      _manualAnswerService.ValidateReasonRequirement(normalizedStatus, ManualReason);

      var previousAnswer = new ManualAnswer
      {
        RuleId = SelectedManualControl.RuleId,
        VulnId = SelectedManualControl.VulnId,
        Status = SelectedManualControl.Status,
        Reason = SelectedManualControl.Reason,
        Comment = SelectedManualControl.Comment
      };
      _answerUndo.Push(previousAnswer);

      var answer = new ManualAnswer
      {
        RuleId = SelectedManualControl.Control.ExternalIds.RuleId,
        VulnId = SelectedManualControl.Control.ExternalIds.VulnId,
        Status = normalizedStatus,
        Reason = string.IsNullOrWhiteSpace(ManualReason) ? null : ManualReason,
        Comment = string.IsNullOrWhiteSpace(ManualComment) ? null : ManualComment
      };

      _manualAnswerService.SaveAnswer(
        BundleRoot,
        answer,
        requireReasonForDecision: true,
        profileId: SelectedProfile?.ProfileId,
        packId: SelectedPack?.PackId);

      var path = Path.Combine(BundleRoot, BundlePaths.ManualDirectory, BundlePaths.AnswersFileName);
      _suppressManualAutoSave = true;
      try
      {
        if (!string.Equals(SelectedManualControl.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
          SelectedManualControl.PreviousStatus = SelectedManualControl.Status;

        SelectedManualControl.Status = normalizedStatus;
        SelectedManualControl.Reason = ManualReason;
        SelectedManualControl.Comment = ManualComment;
        ManualStatus = normalizedStatus;
      }
      finally
      {
        _suppressManualAutoSave = false;
      }

      OnPropertyChanged(nameof(ManualControls));
      UpdateManualSummary();
      CanUndoAnswer = _answerUndo.CanUndo;

      if (isAutoSave)
      {
        AutoSaveStatus = "Auto-saved";
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
          System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => AutoSaveStatus = string.Empty),
          TaskScheduler.Default);
      }
      else
      {
        EvidenceStatus = "Answer saved.";
        _notifications.Success("Answer saved.");
      }

      LastOutputPath = path;
      return true;
    }
    catch (Exception ex)
    {
      if (!isAutoSave)
        EvidenceStatus = "Save failed: " + ex.Message;
      return false;
    }
  }

  private void ScheduleAutoSave()
  {
    if (_suppressManualAutoSave || SelectedManualControl == null || string.IsNullOrWhiteSpace(BundleRoot))
      return;

    _autoSaveTimer?.Dispose();
    _autoSaveTimer = new System.Threading.Timer(
      _ => AutoSaveAnswer(),
      null,
      dueTime: 3000,
      period: System.Threading.Timeout.Infinite);
  }

  private void AutoSaveAnswer()
  {
    _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
    {
      if (SelectedManualControl == null || string.IsNullOrWhiteSpace(BundleRoot))
        return;

      SaveManualAnswerInternal(isAutoSave: true);
    });
  }

  [RelayCommand]
  private void UndoAnswer()
  {
    var prev = _answerUndo.Pop();
    if (prev == null || string.IsNullOrWhiteSpace(BundleRoot))
      return;

    _manualAnswerService.SaveAnswer(BundleRoot, prev, requireReasonForDecision: false);

    var control = ManualControls.FirstOrDefault(c =>
      string.Equals(c.RuleId, prev.RuleId, StringComparison.OrdinalIgnoreCase));
    if (control != null)
    {
      if (!string.Equals(control.Status, prev.Status, StringComparison.OrdinalIgnoreCase))
        control.PreviousStatus = control.Status;

      control.Status = prev.Status;
      control.Reason = prev.Reason;
      control.Comment = prev.Comment;
    }

    if (SelectedManualControl?.RuleId == prev.RuleId)
    {
      _suppressManualAutoSave = true;
      try
      {
        ManualStatus = prev.Status;
        ManualReason = prev.Reason ?? string.Empty;
        ManualComment = prev.Comment ?? string.Empty;
      }
      finally
      {
        _suppressManualAutoSave = false;
      }
    }

    AutoSaveStatus = string.Empty;
    CanUndoAnswer = _answerUndo.CanUndo;
    StatusText = "Undid last answer change.";
    RefreshManualView();
    UpdateManualSummary();
  }

  [RelayCommand]
  private void UseSelectedControlForEvidence()
  {
    if (SelectedManualControl == null)
    {
      EvidenceStatus = "Select a manual control first.";
      return;
    }

    var rule = SelectedManualControl.Control.ExternalIds.RuleId;
    var vuln = SelectedManualControl.Control.ExternalIds.VulnId;
    EvidenceRuleId = !string.IsNullOrWhiteSpace(rule) ? rule : (vuln ?? string.Empty);
    EvidenceStatus = string.IsNullOrWhiteSpace(EvidenceRuleId)
      ? "Selected control has no RuleId/VulnId."
      : "Evidence target set: " + EvidenceRuleId;
  }

  [RelayCommand]
  private async Task CollectSelectedControlEvidence()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (SelectedManualControl == null)
      {
        EvidenceStatus = "Select a manual control first.";
        return;
      }

      var autopilot = new EvidenceAutopilot(Path.Combine(BundleRoot, BundlePaths.EvidenceDirectory));
      var result = await autopilot.CollectEvidenceAsync(SelectedManualControl.Control, CancellationToken.None);
      var folder = GetSelectedManualEvidenceFolder(SelectedManualControl.Control, BundleRoot);

      var outcome = "Collected " + result.EvidenceFiles.Count + " evidence file(s).";
      if (result.Errors.Count > 0)
      {
        outcome += " Errors: " + result.Errors.Count + ".";
        if (result.EvidenceFiles.Count == 0)
          outcome += " " + string.Join("; ", result.Errors.Take(3));
      }

      EvidenceStatus = outcome + " Folder: " + folder;
      LastOutputPath = folder;
      if (result.EvidenceFiles.Count > 0 && result.Errors.Count == 0)
        _notifications.Success("Evidence collection complete.");
      else if (result.EvidenceFiles.Count > 0)
        _notifications.Warn("Evidence collection completed with warnings.");
      else
        _notifications.Error("Evidence collection failed.");
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Auto-collection failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void OpenSelectedControlEvidenceFolder()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot) || SelectedManualControl == null)
    {
      EvidenceStatus = "Select a bundle and manual control first.";
      return;
    }

    var folder = GetSelectedManualEvidenceFolder(SelectedManualControl.Control, BundleRoot);
    if (!Directory.Exists(folder))
    {
      EvidenceStatus = "No evidence folder yet for selected control.";
      return;
    }

    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = folder,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Failed to open evidence folder: " + ex.Message;
    }
  }

  [RelayCommand]
  private void LaunchManualWizard()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      var manualControls = ManualControls.Select(m => m.Control).ToList();
      var viewModel = new ViewModels.ManualCheckWizardViewModel(
        BundleRoot,
        manualControls,
        _manualAnswerService,
        _annotationService);
      var wizard = new Views.ManualCheckWizard(viewModel);
      
      wizard.Closed += (s, e) =>
      {
        // Refresh manual controls after wizard closes
        LoadManualControls();
      };
      
      wizard.ShowDialog();
    }
    catch (Exception ex)
    {
      StatusText = "Failed to launch wizard: " + ex.Message;
    }
  }

  [RelayCommand]
  private void LaunchBulkDisposition()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot) || ManualControls.Count == 0)
    {
      StatusText = "No manual controls loaded.";
      return;
    }

    var viewModel = new ViewModels.BulkDispositionViewModel(BundleRoot, _manualAnswerService, ManualControls);
    var dialog = new Views.BulkDispositionDialog
    {
      DataContext = viewModel,
      Owner = System.Windows.Application.Current.MainWindow
    };
    dialog.ShowDialog();

    if (viewModel.DialogResult)
    {
      RefreshManualView();
      UpdateManualSummary();
      StatusText = $"Bulk disposition: {viewModel.AppliedCount} control(s) updated.";
    }
  }

  [RelayCommand]
  private void ExportManualCsv()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      var dialog = new Microsoft.Win32.SaveFileDialog
      {
        Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
        Title = "Export manual controls to CSV",
        FileName = "manual-controls-report.csv"
      };

      if (dialog.ShowDialog() != true)
        return;

      var rows = ManualControls.Select(item =>
      {
        var answer = _manualAnswerService.GetAnswer(BundleRoot, item.Control);
        var status = _manualAnswerService.NormalizeStatus(answer?.Status ?? item.Status);
        var reason = answer?.Reason ?? item.Reason;
        var comment = answer?.Comment ?? item.Comment;
        var updatedAt = answer?.UpdatedAt;

        return new
        {
          item.VulnId,
          item.RuleId,
          Title = item.Control.Title,
          item.StigGroup,
          Severity = item.Control.Severity,
          Status = status,
          Reason = reason,
          Comment = comment,
          UpdatedAt = updatedAt
        };
      }).ToList();

      using (var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8))
      {
        writer.WriteLine("VulnId,RuleId,Title,StigGroup,Severity,Status,Reason,Comment,UpdatedAt");
        foreach (var row in rows)
        {
          var fields = new[]
          {
            EscapeCsv(row.VulnId),
            EscapeCsv(row.RuleId),
            EscapeCsv(row.Title),
            EscapeCsv(row.StigGroup),
            EscapeCsv(row.Severity),
            EscapeCsv(row.Status),
            EscapeCsv(row.Reason),
            EscapeCsv(row.Comment),
            EscapeCsv(row.UpdatedAt?.ToString("o") ?? string.Empty)
          };
          writer.WriteLine(string.Join(",", fields));
        }
      }

      StatusText = $"Exported {rows.Count} controls to {dialog.FileName}";
    }
    catch (Exception ex)
    {
      StatusText = "Manual CSV export failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void ExportManualHtml()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      var dialog = new Microsoft.Win32.SaveFileDialog
      {
        Filter = "HTML Files (*.html)|*.html|All Files (*.*)|*.*",
        Title = "Export manual controls to HTML",
        FileName = "manual-controls-report.html"
      };

      if (dialog.ShowDialog() != true)
        return;

      var generatedAt = DateTimeOffset.Now;
      var rows = ManualControls.Select(item =>
      {
        var answer = _manualAnswerService.GetAnswer(BundleRoot, item.Control);
        var status = _manualAnswerService.NormalizeStatus(answer?.Status ?? item.Status);
        var reason = answer?.Reason ?? item.Reason;
        var comment = answer?.Comment ?? item.Comment;
        var updatedAt = answer?.UpdatedAt;

        return new
        {
          item.VulnId,
          item.RuleId,
          Title = item.Control.Title,
          item.StigGroup,
          Severity = item.Control.Severity,
          Status = status,
          Reason = reason,
          Comment = comment,
          UpdatedAt = updatedAt
        };
      }).ToList();

      var html = new StringBuilder(16 * 1024);
      html.AppendLine("<!DOCTYPE html>");
      html.AppendLine("<html lang=\"en\">");
      html.AppendLine("<head>");
      html.AppendLine("  <meta charset=\"utf-8\" />");
      html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
      html.AppendLine("  <title>STIGForge Manual Controls Report</title>");
      html.AppendLine("  <style>");
      html.AppendLine("    :root { color-scheme: light dark; --bg:#f6f8fb; --fg:#1f2937; --muted:#4b5563; --surface:#ffffff; --border:#d1d5db; --header:#e5e7eb; --pass:#1b8f47; --fail:#c62828; --open:#b45309; --na:#6b7280; --sev-high:#c62828; --sev-medium:#b45309; --sev-low:#1b8f47; }");
      html.AppendLine("    @media (prefers-color-scheme: dark) { :root { --bg:#111827; --fg:#f3f4f6; --muted:#9ca3af; --surface:#1f2937; --border:#374151; --header:#111827; --pass:#4ade80; --fail:#f87171; --open:#f59e0b; --na:#9ca3af; --sev-high:#f87171; --sev-medium:#f59e0b; --sev-low:#4ade80; } }");
      html.AppendLine("    body { margin:0; padding:24px; background:var(--bg); color:var(--fg); font-family:Segoe UI, Tahoma, sans-serif; }");
      html.AppendLine("    h1 { margin:0 0 8px 0; font-size:24px; }");
      html.AppendLine("    .meta { margin:0 0 16px 0; color:var(--muted); font-size:13px; }");
      html.AppendLine("    .table-wrap { overflow:auto; background:var(--surface); border:1px solid var(--border); border-radius:8px; }");
      html.AppendLine("    table { width:100%; border-collapse:collapse; min-width:1040px; }");
      html.AppendLine("    th, td { padding:10px 12px; border-bottom:1px solid var(--border); text-align:left; vertical-align:top; font-size:13px; }");
      html.AppendLine("    th { background:var(--header); position:sticky; top:0; z-index:1; }");
      html.AppendLine("    tr:nth-child(even) td { background:color-mix(in srgb, var(--surface) 94%, var(--header) 6%); }");
      html.AppendLine("    .status-pass { color:var(--pass); font-weight:600; }");
      html.AppendLine("    .status-fail { color:var(--fail); font-weight:600; }");
      html.AppendLine("    .status-open { color:var(--open); font-weight:600; }");
      html.AppendLine("    .status-na { color:var(--na); font-weight:600; }");
      html.AppendLine("    .sev-high { color:var(--sev-high); font-weight:600; }");
      html.AppendLine("    .sev-medium { color:var(--sev-medium); font-weight:600; }");
      html.AppendLine("    .sev-low { color:var(--sev-low); font-weight:600; }");
      html.AppendLine("    .mono { font-family:Consolas, 'Courier New', monospace; }");
      html.AppendLine("  </style>");
      html.AppendLine("</head>");
      html.AppendLine("<body>");
      html.AppendLine("  <h1>STIGForge Manual Controls Report</h1>");
      html.AppendLine($"  <p class=\"meta\">Generated: {WebUtility.HtmlEncode(generatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))} | Controls: {rows.Count}</p>");
      html.AppendLine("  <div class=\"table-wrap\">");
      html.AppendLine("    <table>");
      html.AppendLine("      <thead>");
      html.AppendLine("        <tr>");
      html.AppendLine("          <th>VulnId</th>");
      html.AppendLine("          <th>RuleId</th>");
      html.AppendLine("          <th>Title</th>");
      html.AppendLine("          <th>StigGroup</th>");
      html.AppendLine("          <th>Severity</th>");
      html.AppendLine("          <th>Status</th>");
      html.AppendLine("          <th>Reason</th>");
      html.AppendLine("          <th>Comment</th>");
      html.AppendLine("          <th>UpdatedAt</th>");
      html.AppendLine("        </tr>");
      html.AppendLine("      </thead>");
      html.AppendLine("      <tbody>");

      foreach (var row in rows)
      {
        var statusClass = GetStatusCssClass(row.Status);
        var severityClass = GetSeverityCssClass(row.Severity);
        var updatedAt = row.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? string.Empty;

        html.AppendLine("        <tr>");
        html.AppendLine($"          <td class=\"mono\">{Html(row.VulnId)}</td>");
        html.AppendLine($"          <td class=\"mono\">{Html(row.RuleId)}</td>");
        html.AppendLine($"          <td>{Html(row.Title)}</td>");
        html.AppendLine($"          <td>{Html(row.StigGroup)}</td>");
        html.AppendLine($"          <td class=\"{severityClass}\">{Html(row.Severity)}</td>");
        html.AppendLine($"          <td class=\"{statusClass}\">{Html(row.Status)}</td>");
        html.AppendLine($"          <td>{Html(row.Reason)}</td>");
        html.AppendLine($"          <td>{Html(row.Comment)}</td>");
        html.AppendLine($"          <td class=\"mono\">{Html(updatedAt)}</td>");
        html.AppendLine("        </tr>");
      }

      html.AppendLine("      </tbody>");
      html.AppendLine("    </table>");
      html.AppendLine("  </div>");
      html.AppendLine("</body>");
      html.AppendLine("</html>");

      File.WriteAllText(dialog.FileName, html.ToString(), Encoding.UTF8);
      StatusText = $"Exported {rows.Count} controls to {dialog.FileName}";
    }
    catch (Exception ex)
    {
      StatusText = "Manual HTML export failed: " + ex.Message;
    }
  }

  private static string EscapeCsv(string? value)
  {
    if (string.IsNullOrEmpty(value))
      return string.Empty;

    if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
      return value;

    return "\"" + value.Replace("\"", "\"\"") + "\"";
  }

  private static string Html(string? value)
  {
    return WebUtility.HtmlEncode(value ?? string.Empty);
  }

  private static string GetStatusCssClass(string? status)
  {
    if (string.Equals(status, ControlStatusStrings.Pass, StringComparison.OrdinalIgnoreCase))
      return "status-pass";
    if (string.Equals(status, ControlStatusStrings.Fail, StringComparison.OrdinalIgnoreCase))
      return "status-fail";
    if (string.Equals(status, ControlStatusStrings.NotApplicable, StringComparison.OrdinalIgnoreCase))
      return "status-na";
    return "status-open";
  }

  private static string GetSeverityCssClass(string? severity)
  {
    if (string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase))
      return "sev-high";
    if (string.Equals(severity, "medium", StringComparison.OrdinalIgnoreCase))
      return "sev-medium";
    if (string.Equals(severity, "low", StringComparison.OrdinalIgnoreCase))
      return "sev-low";
    return string.Empty;
  }

  partial void OnSelectedManualControlChanged(ManualControlItem? value)
  {
    if (value == null) return;

    _autoSaveTimer?.Dispose();
    _autoSaveTimer = null;
    AutoSaveStatus = string.Empty;

    _suppressManualAutoSave = true;
    try
    {
      ManualStatus = _manualAnswerService.NormalizeStatus(value.Status);
      ManualReason = value.Reason ?? string.Empty;
      ManualComment = value.Comment ?? string.Empty;
    }
    finally
    {
      _suppressManualAutoSave = false;
    }

    var rule = value.Control.ExternalIds.RuleId;
    var vuln = value.Control.ExternalIds.VulnId;
    EvidenceRuleId = !string.IsNullOrWhiteSpace(rule) ? rule : (vuln ?? string.Empty);

    var annotationRuleId = GetAnnotationRuleId(value);
    if (!string.IsNullOrWhiteSpace(BundleRoot) && !string.IsNullOrWhiteSpace(annotationRuleId))
    {
      ControlNotes = _annotationService.GetNotes(BundleRoot, annotationRuleId) ?? string.Empty;
      value.Notes = ControlNotes;
    }
    else
    {
      ControlNotes = string.Empty;
      value.Notes = null;
    }
  }

  [RelayCommand]
  private void SaveControlNotes()
  {
    if (SelectedManualControl == null || string.IsNullOrWhiteSpace(BundleRoot))
      return;

    var annotationRuleId = GetAnnotationRuleId(SelectedManualControl);
    if (string.IsNullOrWhiteSpace(annotationRuleId))
      return;

    _annotationService.SaveNotes(BundleRoot, annotationRuleId,
      SelectedManualControl.VulnId, ControlNotes);
    SelectedManualControl.Notes = ControlNotes;
    StatusText = "Notes saved.";
  }

  private static string GetAnnotationRuleId(ManualControlItem item)
  {
    if (!string.IsNullOrWhiteSpace(item.RuleId))
      return item.RuleId;

    return item.VulnId;
  }

  partial void OnManualFilterTextChanged(string value)
  {
    RefreshManualView();
  }

  partial void OnManualStatusChanged(string value) { ScheduleAutoSave(); }
  partial void OnManualReasonChanged(string value) { ScheduleAutoSave(); }
  partial void OnManualCommentChanged(string value) { ScheduleAutoSave(); }

  partial void OnManualStatusFilterChanged(string value)
  {
    RefreshManualView();
  }

  partial void OnManualReviewStatusFilterChanged(string value) { RefreshManualView(); }
  partial void OnManualCatFilterChanged(string value) { RefreshManualView(); }
  partial void OnManualStigGroupFilterChanged(string value) { RefreshManualView(); }

  private void LoadManualControls()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;

    var allControls = new Dictionary<string, ControlRecord>(StringComparer.OrdinalIgnoreCase);
    var bundlePaths = new List<string> { BundleRoot };
    foreach (var recent in RecentBundles)
    {
      if (!string.IsNullOrWhiteSpace(recent)
          && !string.Equals(recent, BundleRoot, StringComparison.OrdinalIgnoreCase)
          && Directory.Exists(recent))
        bundlePaths.Add(recent);
    }

    foreach (var bundlePath in bundlePaths)
    {
      IReadOnlyList<ControlRecord> controls;
      try
      {
        controls = PackControlsReader.Load(bundlePath);
      }
      catch (Exception ex)
      {
        _logger?.LogWarning(ex, "Failed to parse pack_controls.json");
        continue;
      }

      foreach (var c in controls)
      {
        var key = GetControlIdentityKey(c.ExternalIds.RuleId, c.ExternalIds.VulnId, c.ControlId);
        if (!string.IsNullOrWhiteSpace(key) && !allControls.ContainsKey(key))
          allControls[key] = c;
      }
    }

    if (allControls.Count == 0) return;

    _totalPackControls = allControls.Count;
    var answers = LoadAnswerFile();

    var manualScope = BuildManualScopeControls(allControls.Values.ToList(), BundleRoot);
    var verifyStatuses = LoadVerificationStatuses(BundleRoot);

    var manualItems = new List<ManualControlItem>();
    foreach (var c in manualScope)
    {
      var item = new ManualControlItem(c);

      var verifyKey = GetControlIdentityKey(c.ExternalIds.RuleId, c.ExternalIds.VulnId, c.ControlId);
      if (!string.IsNullOrWhiteSpace(verifyKey) && verifyStatuses.TryGetValue(verifyKey, out var scanResult))
      {
        item.Status = _manualAnswerService.NormalizeStatus(scanResult.Status);
        var disposition = string.Join("\n",
          new[] { scanResult.FindingDetails, scanResult.Comments }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(disposition))
          item.Comment = disposition;
      }
      else
      {
        item.Status = ControlStatusStrings.NotReviewed;
      }

      var ans = FindAnswer(answers, c);
      if (ans != null)
      {
        item.Status = _manualAnswerService.NormalizeStatus(ans.Status);
        item.PreviousStatus = string.IsNullOrWhiteSpace(ans.PreviousStatus)
          ? null
          : _manualAnswerService.NormalizeStatus(ans.PreviousStatus);
        item.Reason = ans.Reason;
        item.Comment = ans.Comment;
      }
      manualItems.Add(item);
    }

    _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
    {
      ManualControls.ReplaceAll(manualItems);
      OnPropertyChanged(nameof(HasManualControls));
    });

    _ = System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
    {
      var groups = ManualControls
        .Select(m => m.StigGroup)
        .Where(IsStigGroupName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g, StringComparer.OrdinalIgnoreCase);
      var allGroups = new List<string> { "All" };
      allGroups.AddRange(groups);
      ManualStigGroupFilters.ReplaceAll(allGroups);
      if (!ManualStigGroupFilters.Contains(ManualStigGroupFilter))
        ManualStigGroupFilter = "All";
      OnPropertyChanged(nameof(ManualStigGroupFilters));
    });

    UpdateManualSummary();
    _manualReviewView = null;
    OnPropertyChanged(nameof(ManualReviewView));
    ManualControlsView.Refresh();
  }

  private void ConfigureManualView()
  {
    var view = ManualControlsView;
    if (view is ListCollectionView listCollectionView)
    {
      listCollectionView.GroupDescriptions.Clear();
      listCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ManualControlItem.StigGroup)));
    }

    view.Filter = o =>
    {
      if (o is not ManualControlItem item) return false;

      var statusFilter = ManualStatusFilter ?? "All";
      if (!string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(item.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
        return false;

      var catFilter = ManualCatFilter ?? "All";
      if (!string.Equals(catFilter, "All", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(item.CatLevel, catFilter, StringComparison.OrdinalIgnoreCase))
        return false;

      var stigFilter = ManualStigGroupFilter ?? "All";
      if (!string.Equals(stigFilter, "All", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(item.StigGroup, stigFilter, StringComparison.OrdinalIgnoreCase))
        return false;

      var text = ManualFilterText?.Trim();
      if (string.IsNullOrWhiteSpace(text)) return true;

      return Contains(item.Control.ExternalIds.RuleId, text)
        || Contains(item.Control.ExternalIds.VulnId, text)
        || Contains(item.Control.Title, text)
        || Contains(item.Control.CheckText, text)
        || Contains(item.Control.FixText, text)
        || Contains(item.StigGroup, text)
        || Contains(item.Reason, text)
        || Contains(item.Comment, text);
    };

    RefreshManualView();
  }

  private void RefreshManualView()
  {
    ManualControlsView.Refresh();
    _manualReviewView?.Refresh();
    UpdateManualSummary();
  }

  private ICollectionView? _manualReviewView;
  public ICollectionView ManualReviewView => _manualReviewView ??= CreateManualReviewView();

  private ICollectionView CreateManualReviewView()
  {
    var view = new CollectionViewSource { Source = ManualControls }.View;
    if (view is ListCollectionView listCollectionView)
    {
      listCollectionView.GroupDescriptions.Clear();
      listCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ManualControlItem.StigGroup)));
    }

    view.Filter = o =>
    {
      if (o is not ManualControlItem item) return false;

      var reviewFilter = ManualReviewStatusFilter ?? ControlStatusStrings.NotReviewed;
      var statusMatches = true;
      if (string.Equals(reviewFilter, ControlStatusStrings.NotReviewed, StringComparison.OrdinalIgnoreCase))
      {
        var s = item.Status ?? string.Empty;
        statusMatches = string.Equals(s, ControlStatusStrings.Open, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, ControlStatusStrings.NotReviewed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, nameof(ControlStatusStrings.NotReviewed), StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(s);
      }
      else if (!string.Equals(reviewFilter, "All", StringComparison.OrdinalIgnoreCase))
      {
        statusMatches = string.Equals(item.Status, reviewFilter, StringComparison.OrdinalIgnoreCase);
      }

      if (!statusMatches)
        return false;

      var text = ManualFilterText?.Trim();
      if (string.IsNullOrWhiteSpace(text))
        return true;

      return Contains(item.Control.ExternalIds.RuleId, text)
        || Contains(item.Control.ExternalIds.VulnId, text)
        || Contains(item.Control.Title, text)
        || Contains(item.Control.CheckText, text)
        || Contains(item.Control.FixText, text)
        || Contains(item.StigGroup, text)
        || Contains(item.Reason, text)
        || Contains(item.Comment, text);

    };
    return view;
  }

  private void UpdateManualSummary()
  {
    var total = ManualControls.Count;
    var pass = ManualControls.Count(x => string.Equals(x.Status, ControlStatusStrings.Pass, StringComparison.OrdinalIgnoreCase));
    var fail = ManualControls.Count(x => string.Equals(x.Status, ControlStatusStrings.Fail, StringComparison.OrdinalIgnoreCase));
    var na = ManualControls.Count(x => string.Equals(x.Status, ControlStatusStrings.NotApplicable, StringComparison.OrdinalIgnoreCase));
    var open = ManualControls.Count(x => string.Equals(x.Status, ControlStatusStrings.Open, StringComparison.OrdinalIgnoreCase));
    var notReviewed = ManualControls.Count(x => string.Equals(x.Status, ControlStatusStrings.NotReviewed, StringComparison.OrdinalIgnoreCase));

    if (total == 0)
    {
      ManualSummary = "No controls loaded. Select a bundle with pack_controls.json.";
      return;
    }

    var excluded = _totalPackControls - total;
    var summary = $"Manual Scope: {total} | Pass: {pass} | Fail: {fail} | NA: {na} | Open: {open} | Not Reviewed: {notReviewed}";
    if (excluded > 0)
      summary += $" | ({excluded} ADMX/GPO/Template controls excluded)";
    ManualSummary = summary;
  }

  private static int CountReviewQueueItems(string bundleRoot)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot))
      return 0;

    var reviewPath = Path.Combine(bundleRoot, BundlePaths.ReportsDirectory, "review_required.csv");
    if (!File.Exists(reviewPath))
      return 0;

    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadLines(reviewPath).Skip(1))
    {
      if (string.IsNullOrWhiteSpace(line))
        continue;

      var parts = ParseCsvLine(line);
      var vulnId = parts.Count > 0 ? parts[0]?.Trim() : string.Empty;
      var ruleId = parts.Count > 1 ? parts[1]?.Trim() : string.Empty;

      if (!string.IsNullOrWhiteSpace(ruleId))
        keys.Add("RULE:" + ruleId);
      else if (!string.IsNullOrWhiteSpace(vulnId))
        keys.Add("VULN:" + vulnId);
    }

    return keys.Count;
  }

  private static IReadOnlyList<ControlRecord> BuildManualScopeControls(IReadOnlyList<ControlRecord> controls, string bundleRoot)
  {
    return controls
      .Where(c =>
      {
        var packName = (c.Revision.PackName ?? string.Empty).Trim();
        return packName.IndexOf(PackTypes.Admx, StringComparison.OrdinalIgnoreCase) < 0
          && packName.IndexOf(PackTypes.Gpo, StringComparison.OrdinalIgnoreCase) < 0
          && packName.IndexOf(PackTypes.Template, StringComparison.OrdinalIgnoreCase) < 0;
      })
      .ToList();
  }

  private sealed record VerifyScanResult(string Status, string? FindingDetails, string? Comments);

  private Dictionary<string, VerifyScanResult> LoadVerificationStatuses(string bundleRoot)
  {
    var statuses = new Dictionary<string, VerifyScanResult>(StringComparer.OrdinalIgnoreCase);
    var bundlePaths = new List<string> { bundleRoot };
    foreach (var recent in RecentBundles)
    {
      if (!string.IsNullOrWhiteSpace(recent)
          && !string.Equals(recent, bundleRoot, StringComparison.OrdinalIgnoreCase)
          && Directory.Exists(recent))
        bundlePaths.Add(recent);
    }

    foreach (var bp in bundlePaths)
    {
      var verifyRoot = Path.Combine(bp, BundlePaths.VerifyDirectory);
      if (!Directory.Exists(verifyRoot)) continue;

      string[] jsonFiles;
      try { jsonFiles = Directory.GetFiles(verifyRoot, BundlePaths.ConsolidatedResultsFileName, SearchOption.AllDirectories); }
      catch { continue; }

      foreach (var file in jsonFiles)
      {
        try
        {
          var json = File.ReadAllText(file);
          using var doc = JsonDocument.Parse(json);
          if (!doc.RootElement.TryGetProperty("Results", out var results)) continue;
          foreach (var r in results.EnumerateArray())
          {
            var vulnId = r.TryGetProperty("VulnId", out var v) ? v.GetString() : null;
            var ruleId = r.TryGetProperty("RuleId", out var rid) ? rid.GetString() : null;
            var status = r.TryGetProperty("Status", out var s) ? s.GetString() : null;
            if (status == null) continue;

            var findingDetails = r.TryGetProperty("FindingDetails", out var fd) ? fd.GetString() : null;
            var comments = r.TryGetProperty("Comments", out var cm) ? cm.GetString() : null;
            var entry = new VerifyScanResult(status, findingDetails, comments);

            if (!string.IsNullOrWhiteSpace(vulnId))
              statuses["VULN:" + vulnId.Trim()] = entry;
            if (!string.IsNullOrWhiteSpace(ruleId))
              statuses["RULE:" + ruleId.Trim()] = entry;
          }
        }
        catch (Exception ex)
        {
          _logger?.LogDebug(ex, "Failed to parse verification results");
        }
      }
    }
    return statuses;
  }

  private static string GetControlIdentityKey(string? ruleId, string? vulnId, string? fallback)
  {
    if (!string.IsNullOrWhiteSpace(ruleId))
      return "RULE:" + ruleId.Trim();
    if (!string.IsNullOrWhiteSpace(vulnId))
      return "VULN:" + vulnId.Trim();
    if (!string.IsNullOrWhiteSpace(fallback))
      return "CTRL:" + fallback.Trim();
    return string.Empty;
  }

  private AnswerFile LoadAnswerFile()
  {
    return _manualAnswerService.LoadAnswerFile(BundleRoot);
  }

  private static ManualAnswer? FindAnswer(AnswerFile file, ControlRecord control)
  {
    return file.Answers.FirstOrDefault(a =>
      (!string.IsNullOrWhiteSpace(a.RuleId) && string.Equals(a.RuleId, control.ExternalIds.RuleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(a.VulnId) && string.Equals(a.VulnId, control.ExternalIds.VulnId, StringComparison.OrdinalIgnoreCase)));
  }

  private static EvidenceArtifactType ParseEvidenceType(string value)
  {
    return Enum.TryParse<EvidenceArtifactType>(value, true, out var t) ? t : EvidenceArtifactType.Other;
  }

  private static bool Contains(string? source, string value)
  {
    return !string.IsNullOrWhiteSpace(source)
      && source!.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsStigGroupName(string? value)
  {
    return !string.IsNullOrWhiteSpace(value)
      && value!.IndexOf(PackTypes.Stig, StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static List<string> ParseCsvLine(string line)
  {
    var fields = new List<string>();
    var current = new System.Text.StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          current.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }
      }
      else if (ch == ',' && !inQuotes)
      {
        fields.Add(current.ToString().Trim());
        current.Clear();
      }
      else
      {
        current.Append(ch);
      }
    }

    fields.Add(current.ToString().Trim());
    return fields;
  }

  private static string GetSelectedManualEvidenceFolder(ControlRecord control, string bundleRoot)
  {
    var controlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? control.ControlId;
    var safeId = string.Join("_", controlId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(bundleRoot, BundlePaths.EvidenceDirectory, "by_control", safeId);
  }
}
