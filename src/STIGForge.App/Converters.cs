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
      ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // Green
      : new SolidColorBrush(Color.FromRgb(239, 68, 68));   // Red
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
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
