using System.Globalization;
using System.Windows;
using System.Windows.Data;

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
