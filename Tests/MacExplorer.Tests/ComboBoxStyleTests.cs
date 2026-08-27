using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace MacExplorer.Tests;

public sealed class ComboBoxStyleTests
{
    [AvaloniaFact]
    public void LightThemeUsesTheSidebarActionMenuSurfaceForComboBoxes()
    {
        AssertResourceBrush("ComboBoxDropDownBackground", "#F7FFFFFF");
        AssertResourceBrush("ComboBoxDropDownBorderBrush", "#E6E8ED");
        AssertResourceBrush("ComboBoxItemBackgroundSelected", "Transparent");
        AssertResourceBrush("ComboBoxItemBackgroundPointerOver", "#F0F1F3");
    }

    private static void AssertResourceBrush(string key, string expected)
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        Assert.True(application.TryGetResource(key, application.ActualThemeVariant, out var value));
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Equal(Color.Parse(expected), brush.Color);
    }
}
