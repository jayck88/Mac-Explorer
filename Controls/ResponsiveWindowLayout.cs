using Avalonia.Controls;

namespace MacExplorer.Controls;

internal readonly record struct ResponsiveWindowLayout(
    bool IsCompact,
    double SidebarWidth,
    SplitViewDisplayMode InfoPanelDisplayMode)
{
    internal const double Breakpoint = 1180;

    internal static ResponsiveWindowLayout Resolve(double width)
        => width < Breakpoint
            ? new ResponsiveWindowLayout(true, 220, SplitViewDisplayMode.Overlay)
            : new ResponsiveWindowLayout(false, 260, SplitViewDisplayMode.Inline);
}
