using System.Globalization;
using System.Windows;
using System.Windows.Media;
using STIGForge.App;

namespace STIGForge.UnitTests.App;

public class ConverterTests
{
    [Theory]
    [InlineData(StepState.Ready, "#3B82F6")]    // Blue
    [InlineData(StepState.Running, "#F59E0B")]  // Amber
    [InlineData(StepState.Complete, "#22C55E")] // Green
    [InlineData(StepState.Locked, "#6B7280")]   // Gray
    [InlineData(StepState.Error, "#EF4444")]    // Red
    public void StepStateToBorderBrushConverter_ReturnsCorrectColor(StepState state, string expectedHex)
    {
        var converter = new StepStateToBorderBrushConverter();
        var result = converter.Convert(state, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        
        Assert.NotNull(result);
        var expected = (Color)ColorConverter.ConvertFromString(expectedHex);
        Assert.Equal(expected, result.Color);
    }

    [Fact]
    public void StepStateToBorderBrushConverter_ReturnsGrayForNullInput()
    {
        var converter = new StepStateToBorderBrushConverter();
        var result = converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;

        Assert.NotNull(result);
        var expected = (Color)ColorConverter.ConvertFromString("#6B7280");
        Assert.Equal(expected, result.Color);
    }

    [Fact]
    public void StepStateToBorderBrushConverter_ReturnsGrayForNonStepStateInput()
    {
        var converter = new StepStateToBorderBrushConverter();
        var result = converter.Convert("invalid", typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;

        Assert.NotNull(result);
        var expected = (Color)ColorConverter.ConvertFromString("#6B7280");
        Assert.Equal(expected, result.Color);
    }

    [Fact]
    public void StepStateToBorderBrushConverter_ReturnsFrozenBrushes()
    {
        var converter = new StepStateToBorderBrushConverter();
        var result = converter.Convert(StepState.Ready, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;

        Assert.NotNull(result);
        Assert.True(result.IsFrozen);
    }

    [Theory]
    [InlineData(StepState.Ready, Visibility.Collapsed)]
    [InlineData(StepState.Running, Visibility.Visible)]
    [InlineData(StepState.Complete, Visibility.Collapsed)]
    [InlineData(StepState.Locked, Visibility.Collapsed)]
    [InlineData(StepState.Error, Visibility.Collapsed)]
    public void StepStateToRunningVisibilityConverter_ReturnsCorrect(StepState state, Visibility expected)
    {
        var converter = new StepStateToRunningVisibilityConverter();
        var result = converter.Convert(state, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StepState.Ready, Visibility.Collapsed)]
    [InlineData(StepState.Running, Visibility.Collapsed)]
    [InlineData(StepState.Complete, Visibility.Visible)]
    [InlineData(StepState.Locked, Visibility.Collapsed)]
    [InlineData(StepState.Error, Visibility.Collapsed)]
    public void StepStateToCompleteVisibilityConverter_ReturnsCorrect(StepState state, Visibility expected)
    {
        var converter = new StepStateToCompleteVisibilityConverter();
        var result = converter.Convert(state, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StepState.Ready, Visibility.Collapsed)]
    [InlineData(StepState.Running, Visibility.Collapsed)]
    [InlineData(StepState.Complete, Visibility.Collapsed)]
    [InlineData(StepState.Locked, Visibility.Collapsed)]
    [InlineData(StepState.Error, Visibility.Visible)]
    public void StepStateToErrorVisibilityConverter_ReturnsCorrect(StepState state, Visibility expected)
    {
        var converter = new StepStateToErrorVisibilityConverter();
        var result = converter.Convert(state, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }
}
