using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Core.Abstractions;

namespace STIGForge.App;

public partial class MainViewModel
{
  [ObservableProperty] private string auditActionFilter = "";
  [ObservableProperty] private string auditTargetFilter = "";
  [ObservableProperty] private int auditLimit = 100;
  [ObservableProperty] private string auditVerifyResult = "";

  public ObservableCollection<AuditEntry> AuditEntries { get; } = new();

  [RelayCommand]
  private async Task AuditQuery()
  {
    if (_audit == null) { StatusText = "Audit trail service not available."; return; }
    try
    {
      IsBusy = true;
      StatusText = "Querying audit log...";

      var query = new AuditQuery
      {
        Action = string.IsNullOrWhiteSpace(AuditActionFilter) ? null : AuditActionFilter.Trim(),
        Target = string.IsNullOrWhiteSpace(AuditTargetFilter) ? null : AuditTargetFilter.Trim(),
        Limit = AuditLimit > 0 ? AuditLimit : 100
      };

      var entries = await _audit.QueryAsync(query, CancellationToken.None);
      AuditEntries.Clear();
      foreach (var e in entries) AuditEntries.Add(e);

      StatusText = $"Loaded {entries.Count} audit entries.";
    }
    catch (Exception ex)
    {
      StatusText = "Audit query failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task AuditVerify()
  {
    if (_audit == null) { StatusText = "Audit trail service not available."; return; }
    try
    {
      IsBusy = true;
      StatusText = "Verifying audit trail integrity...";
      var ok = await _audit.VerifyIntegrityAsync(CancellationToken.None);
      AuditVerifyResult = ok ? "PASS — Chain integrity verified." : "FAIL — Chain integrity broken!";
      StatusText = "Audit verify complete.";
    }
    catch (Exception ex)
    {
      AuditVerifyResult = "ERROR — " + ex.Message;
      StatusText = "Audit verify failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }
}
