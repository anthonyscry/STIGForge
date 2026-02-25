using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace STIGForge.App;

/// <summary>
/// Converts a string to Visibility: Collapsed when the string is null or empty, Visible otherwise.
/// Used for showing empty-state messages in timeline and other list panels.
/// </summary>
public sealed class NullOrEmptyToCollapsedConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts a boolean to PASS/FAIL text for submission readiness checklist.
/// </summary>
public sealed class BoolToPassFailConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value is true ? "PASS" : "FAIL";
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts a boolean to READY/NOT READY verdict text.
/// </summary>
public sealed class BoolToReadyVerdictConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value is true ? "READY FOR SUBMISSION" : "NOT READY FOR SUBMISSION";
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts a boolean to green (true) or red (false) SolidColorBrush for readiness display.
/// </summary>
public sealed class BoolToReadyColorConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value is true
      ? ConverterBrushes.ResolveThemeBrush("SuccessBrush", Color.FromRgb(34, 197, 94))
      : ConverterBrushes.ResolveThemeBrush("DangerBrush", Color.FromRgb(239, 68, 68));
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

internal static class ConverterBrushes
{
  public static SolidColorBrush ResolveThemeBrush(string key, Color fallback)
  {
    return Application.Current?.Resources[key] is SolidColorBrush brush
      ? brush
      : CreateFrozenBrush(fallback);
  }

  private static SolidColorBrush CreateFrozenBrush(Color color)
  {
    var brush = new SolidColorBrush(color);
    brush.Freeze();
    return brush;
  }
}

/// <summary>
/// Converts the current WorkflowStep to FontWeight: Bold if it matches the step parameter, Normal otherwise.
/// Used for highlighting the current step in the step indicator.
/// </summary>
public sealed class StepFontWeightConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is WorkflowStep current && parameter is string stepName)
    {
      if (Enum.TryParse<WorkflowStep>(stepName, out var step))
        return current == step ? FontWeights.Bold : FontWeights.Normal;
    }
    return FontWeights.Normal;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts StepState to a border brush color for dashboard step panels.
/// Ready=Blue, Running=Amber, Complete=Green, Locked=Gray, Error=Red
/// </summary>
public sealed class StepStateToBorderBrushConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value switch
    {
      StepState.Ready => ConverterBrushes.ResolveThemeBrush("WorkflowStepReadyBrush", Color.FromRgb(59, 130, 246)),
      StepState.Running => ConverterBrushes.ResolveThemeBrush("WorkflowStepRunningBrush", Color.FromRgb(245, 158, 11)),
      StepState.Complete => ConverterBrushes.ResolveThemeBrush("WorkflowStepCompleteBrush", Color.FromRgb(34, 197, 94)),
      StepState.Locked => ConverterBrushes.ResolveThemeBrush("WorkflowStepLockedBrush", Color.FromRgb(107, 114, 128)),
      StepState.Error => ConverterBrushes.ResolveThemeBrush("WorkflowStepErrorBrush", Color.FromRgb(239, 68, 68)),
      _ => ConverterBrushes.ResolveThemeBrush("WorkflowStepLockedBrush", Color.FromRgb(107, 114, 128))
    };
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts StepState to Visibility for the running indicator.
/// </summary>
public sealed class StepStateToRunningVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value is StepState.Running ? Visibility.Visible : Visibility.Collapsed;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts StepState to Visibility for the complete indicator.
/// </summary>
public sealed class StepStateToCompleteVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value is StepState.Complete ? Visibility.Visible : Visibility.Collapsed;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}

/// <summary>
/// Converts StepState to Visibility for the error indicator.
/// </summary>
public sealed class StepStateToErrorVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value is StepState.Error ? Visibility.Visible : Visibility.Collapsed;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}
