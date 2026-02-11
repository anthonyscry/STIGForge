using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Export;

namespace STIGForge.App;

public partial class MainViewModel
{
  [ObservableProperty] private string poamSystemName = "";
  [ObservableProperty] private string poamStatus = "";
  [ObservableProperty] private string cklHostName = "";
  [ObservableProperty] private string cklStigId = "";
  [ObservableProperty] private string cklStatus = "";

  [RelayCommand]
  private async Task ExportPoam()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      IsBusy = true;
      StatusText = "Exporting POA&M...";

      await Task.Run(() =>
      {
        var request = new PoamExportRequest
        {
          BundleRoot = BundleRoot,
          SystemName = string.IsNullOrWhiteSpace(PoamSystemName) ? "STIGForge" : PoamSystemName
        };

        var result = StandalonePoamExporter.ExportPoam(request);
        PoamStatus = result.Message;
      }, _cts.Token);

      StatusText = "POA&M export complete.";
    }
    catch (Exception ex)
    {
      PoamStatus = "Failed: " + ex.Message;
      StatusText = "POA&M export failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ExportCkl()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      IsBusy = true;
      StatusText = "Exporting CKL...";

      await Task.Run(() =>
      {
        var request = new CklExportRequest
        {
          BundleRoot = BundleRoot,
          HostName = string.IsNullOrWhiteSpace(CklHostName) ? null : CklHostName,
          StigId = string.IsNullOrWhiteSpace(CklStigId) ? null : CklStigId
        };

        var result = CklExporter.ExportCkl(request);
        CklStatus = result.Message + (string.IsNullOrWhiteSpace(result.OutputPath) ? "" : $" ({result.OutputPath})");
      }, _cts.Token);

      StatusText = "CKL export complete.";
    }
    catch (Exception ex)
    {
      CklStatus = "Failed: " + ex.Message;
      StatusText = "CKL export failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }
}
