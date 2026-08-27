using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace MacExplorer.Tests;

public sealed class ScrollBarStyleTests
{
    [AvaloniaFact]
    public void LightThemeUsesSubtleScrollBarTokens()
    {
        AssertResourceBrush("ScrollBarPanningThumbBackground", "#D9D9D9");
        AssertResourceBrush("ScrollBarThumbFillPointerOver", "#C9C9C9");
        AssertResourceDouble("ScrollBarSize", 8);
    }

    private static void AssertResourceBrush(string key, string expected)
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        Assert.True(application.TryGetResource(key, application.ActualThemeVariant, out var value));
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Color.Parse(expected), brush.Color);
    }

    private static void AssertResourceDouble(string key, double expected)
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        Assert.True(application.TryGetResource(key, application.ActualThemeVariant, out var value));
        Assert.Equal(expected, Assert.IsType<double>(value));
    }
}
