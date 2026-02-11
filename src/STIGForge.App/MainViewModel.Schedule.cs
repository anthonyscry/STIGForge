using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Infrastructure.System;

namespace STIGForge.App;

public partial class MainViewModel
{
  [ObservableProperty] private string schedTaskName = "";
  [ObservableProperty] private string schedFrequency = "DAILY";
  [ObservableProperty] private string schedTime = "06:00";
  [ObservableProperty] private string schedStatus = "";
  [ObservableProperty] private string schedListOutput = "";

  public IReadOnlyList<string> ScheduleFrequencies { get; } = new[]
  {
    "DAILY",
    "WEEKLY",
    "MONTHLY"
  };

  [RelayCommand]
  private async Task ScheduleRegister()
  {
    if (_scheduledTaskService == null) { StatusText = "Schedule service not available."; return; }
    try
    {
      if (string.IsNullOrWhiteSpace(SchedTaskName))
      {
        StatusText = "Task name is required.";
        return;
      }
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      IsBusy = true;
      StatusText = "Registering scheduled task...";

      var taskName = SchedTaskName;
      var bundleRoot = BundleRoot;
      var frequency = SchedFrequency;
      var time = SchedTime;
      var svc = _scheduledTaskService;

      var result = await Task.Run(() => svc.Register(new ScheduledTaskRequest
      {
        TaskName = taskName,
        BundleRoot = bundleRoot,
        Frequency = frequency,
        StartTime = time
      }), _cts.Token);

      SchedStatus = result.Success ? $"Task '{result.TaskName}' registered." : result.Message;
      StatusText = result.Success ? "Schedule registered." : "Schedule register failed.";
      ScheduleRefreshList();
    }
    catch (Exception ex)
    {
      SchedStatus = "Failed: " + ex.Message;
      StatusText = "Schedule register failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ScheduleRemove()
  {
    if (_scheduledTaskService == null) { StatusText = "Schedule service not available."; return; }
    try
    {
      if (string.IsNullOrWhiteSpace(SchedTaskName))
      {
        StatusText = "Task name is required.";
        return;
      }

      IsBusy = true;
      var taskName = SchedTaskName;
      var svc = _scheduledTaskService;

      var result = await Task.Run(() => svc.Unregister(taskName), _cts.Token);
      SchedStatus = result.Success ? $"Task '{taskName}' removed." : result.Message;
      StatusText = result.Success ? "Schedule removed." : "Schedule remove failed.";
      ScheduleRefreshList();
    }
    catch (Exception ex)
    {
      SchedStatus = "Failed: " + ex.Message;
      StatusText = "Schedule remove failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private void ScheduleRefresh()
  {
    ScheduleRefreshList();
  }

  private void ScheduleRefreshList()
  {
    if (_scheduledTaskService == null) return;
    try
    {
      var result = _scheduledTaskService.List();
      SchedListOutput = result.Message;
      StatusText = "Schedule list refreshed.";
    }
    catch (Exception ex)
    {
      StatusText = "Schedule list failed: " + ex.Message;
    }
  }
}
