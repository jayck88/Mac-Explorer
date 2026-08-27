namespace MacExplorer.Services;

public enum FontSizePreset
{
    Small,
    Standard,
    Large
}

public interface ITypographyService
{
    FontSizePreset CurrentPreset { get; }
    event EventHandler<TypographyChangedEventArgs>? TypographyChanged;
    void Initialize();
    void SetPreset(FontSizePreset preset);
}

public sealed class TypographyChangedEventArgs : EventArgs
{
    public required FontSizePreset Preset { get; init; }
}
