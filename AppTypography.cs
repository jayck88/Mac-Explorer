using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace MacExplorer;

/// <summary>
/// Binds controls created in code to the same live typography resources used by XAML.
/// </summary>
internal static class AppTypography
{
    public const string IconGlyph = "FontSizeIconGlyph";
    public const string Meta = "FontSizeMeta";
    public const string Caption = "FontSizeCaption";
    public const string Label = "FontSizeLabel";
    public const string Body = "FontSizeBody";
    public const string BodyLarge = "FontSizeBodyLarge";
    public const string Heading = "FontSizeHeading";
    public const string Title = "FontSizeTitle";
    public const string Display = "FontSizeDisplay";

    public static TextBlock BindFontSize(TextBlock control, string resourceKey)
    {
        control.Bind(TextBlock.FontSizeProperty, control.GetResourceObservable(resourceKey));
        return control;
    }

    public static T BindFontSize<T>(T control, string resourceKey) where T : TemplatedControl
    {
        control.Bind(TemplatedControl.FontSizeProperty, control.GetResourceObservable(resourceKey));
        return control;
    }
}
