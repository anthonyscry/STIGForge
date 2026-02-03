using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class MainViewModel : ObservableObject
{
  private readonly ContentPackImporter _importer;
  private readonly IContentPackRepository _packs;

  [ObservableProperty] private string statusText = "Ready.";
  [ObservableProperty] private string importHint = "Import the quarterly DISA zip(s). v1: parses XCCDF lightly.";

  public IList<ContentPack> ContentPacks { get; } = new List<ContentPack>();

  public MainViewModel(ContentPackImporter importer, IContentPackRepository packs)
  {
    _importer = importer;
    _packs = packs;
    _ = LoadAsync();
  }

  private async Task LoadAsync()
  {
    try
    {
      var list = await _packs.ListAsync(CancellationToken.None);
      ContentPacks.Clear();
      foreach (var p in list) ContentPacks.Add(p);
      OnPropertyChanged(nameof(ContentPacks));
    }
    catch (Exception ex)
    {
      StatusText = "Load failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private async Task ImportContentPackAsync()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "Zip Files (*.zip)|*.zip|All Files (*.*)|*.*",
      Title = "Select DISA Content Pack ZIP"
    };

    if (ofd.ShowDialog() != true) return;

    try
    {
      StatusText = "Importing...";
      var packName = "Imported_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm");
      var pack = await _importer.ImportZipAsync(ofd.FileName, packName, "manual_import", CancellationToken.None);
      ContentPacks.Insert(0, pack);
      OnPropertyChanged(nameof(ContentPacks));
      StatusText = "Imported: " + pack.Name;
    }
    catch (Exception ex)
    {
      StatusText = "Import failed: " + ex.Message;
    }
  }
}
