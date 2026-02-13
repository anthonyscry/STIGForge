using System.Collections.Generic;
using System.ComponentModel;
using STIGForge.Core.Models;

namespace STIGForge.App.ViewModels;

public interface IMainSharedState : INotifyPropertyChanged
{
  string BundleRoot { get; set; }
  string StatusText { get; set; }
  bool IsBusy { get; set; }
  IList<ContentPack> ContentPacks { get; }
  ContentPack? SelectedPack { get; set; }
  Profile? SelectedProfile { get; set; }
  string LastOutputPath { get; set; }
}
