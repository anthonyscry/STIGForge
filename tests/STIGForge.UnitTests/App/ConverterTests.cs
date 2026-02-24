using System.Globalization;
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
}
