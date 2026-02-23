using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class MainViewModel
{
  // Additional profile editing fields for full policy knob coverage
  [ObservableProperty] private string profileConfidenceThreshold = "High";
  [ObservableProperty] private string profileAutomationMode = "Standard";
  [ObservableProperty] private bool profileRequiresMapping = true;
  [ObservableProperty] private string profileReleaseDateSource = "ContentPack";

  // Review queue items populated after scope compilation during build
  public ObservableCollection<ReviewQueueItem> ReviewQueueItems { get; } = new();

  [ObservableProperty] private string reviewQueueSummary = "";

  private void LoadProfileFieldsExtended(Profile profile)
  {
    ProfileConfidenceThreshold = profile.NaPolicy.ConfidenceThreshold.ToString();
    ProfileAutomationMode = profile.AutomationPolicy.Mode.ToString();
    ProfileRequiresMapping = profile.AutomationPolicy.AutoApplyRequiresMapping;
    ProfileReleaseDateSource = profile.AutomationPolicy.ReleaseDateSource.ToString();
  }

  private Confidence ParseConfidence(string value)
  {
    return Enum.TryParse<Confidence>(value, true, out var c) ? c : Confidence.High;
  }

  private AutomationMode ParseAutomationMode(string value)
  {
    return Enum.TryParse<AutomationMode>(value, true, out var m) ? m : AutomationMode.Standard;
  }

  private ReleaseDateSource ParseReleaseDateSource(string value)
  {
    return Enum.TryParse<ReleaseDateSource>(value, true, out var r) ? r : ReleaseDateSource.ContentPack;
  }

  private void PopulateReviewQueue(IReadOnlyList<CompiledControl> reviewQueue)
  {
    ReviewQueueItems.Clear();
    foreach (var c in reviewQueue)
    {
      ReviewQueueItems.Add(new ReviewQueueItem
      {
        VulnId = c.Control.ExternalIds.VulnId ?? "",
        RuleId = c.Control.ExternalIds.RuleId ?? "",
        Title = c.Control.Title,
        ReviewReason = c.ReviewReason ?? "Manual review required"
      });
    }

    ReviewQueueSummary = reviewQueue.Count > 0
      ? $"{reviewQueue.Count} control(s) flagged for review."
      : "Review queue is empty -- all controls passed scope filtering.";
  }
}

public sealed class ReviewQueueItem
{
  public string VulnId { get; init; } = "";
  public string RuleId { get; init; } = "";
  public string Title { get; init; } = "";
  public string ReviewReason { get; init; } = "";
}
