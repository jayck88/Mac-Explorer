using Avalonia.Controls;
using MacExplorer.Controls;
using Xunit;

namespace MacExplorer.Tests;

public class ResponsiveWindowLayoutTests
{
    [Theory]
    [InlineData(1000)]
    [InlineData(1179)]
    public void WidthBelowBreakpointUsesCompactOverlay(double width)
    {
        var layout = ResponsiveWindowLayout.Resolve(width);

        Assert.True(layout.IsCompact);
        Assert.Equal(220, layout.SidebarWidth);
        Assert.Equal(SplitViewDisplayMode.Overlay, layout.InfoPanelDisplayMode);
    }

    [Theory]
    [InlineData(1180)]
    [InlineData(1280)]
    [InlineData(1600)]
    public void WidthAtOrAboveBreakpointUsesWideInlineLayout(double width)
    {
        var layout = ResponsiveWindowLayout.Resolve(width);

        Assert.False(layout.IsCompact);
        Assert.Equal(260, layout.SidebarWidth);
        Assert.Equal(SplitViewDisplayMode.Inline, layout.InfoPanelDisplayMode);
    }
}
