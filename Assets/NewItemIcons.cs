using Avalonia.Media.Imaging;
using MacExplorer.Services.Impl;

namespace MacExplorer.Assets;

internal static class NewItemIcons
{
    private const int RenderSize = 24;

    public static Bitmap Folder { get; } = SvgIconCache.GetFolderIcon(RenderSize);
    public static Bitmap Text { get; } = GetFileIcon(".txt");
    public static Bitmap Markdown { get; } = GetFileIcon(".md");
    public static Bitmap Json { get; } = GetFileIcon(".json");
    public static Bitmap Word { get; } = GetFileIcon(".docx");
    public static Bitmap Excel { get; } = GetFileIcon(".xlsx");
    public static Bitmap PowerPoint { get; } = GetFileIcon(".pptx");
    public static Bitmap Pages { get; } = GetFileIcon(".pages");
    public static Bitmap Numbers { get; } = GetFileIcon(".numbers");
    public static Bitmap Keynote { get; } = GetFileIcon(".key");

    private static Bitmap GetFileIcon(string extension) => SvgIconCache.GetFileIcon(
        FileIconResolver.ResolveIconKey(extension),
        extension,
        RenderSize);
}
