using MacExplorer.Models;

namespace MacExplorer.Services;

public static class TagPathHelper
{
    public const string Prefix = "__tag:";

    public static bool IsTagPath(string? path) =>
        path?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string Build(FileTag tag) => Build(tag.Name, tag.Kind);

    public static string Build(string name, FileTagKind kind)
    {
        var kindValue = kind == FileTagKind.FinderColor ? "finder" : "custom";
        return $"{Prefix}{kindValue}:{Uri.EscapeDataString(FileTagCatalog.NormalizeName(name))}";
    }

    public static bool TryParse(string? path, out FileTag tag)
    {
        tag = null!;
        if (!IsTagPath(path)) return false;

        var payload = path![Prefix.Length..];
        var separator = payload.IndexOf(':');
        if (separator <= 0 || separator == payload.Length - 1) return false;

        var kindValue = payload[..separator];
        var name = FileTagCatalog.NormalizeName(Uri.UnescapeDataString(payload[(separator + 1)..]));
        if (string.IsNullOrWhiteSpace(name)) return false;

        if (string.Equals(kindValue, "finder", StringComparison.Ordinal))
        {
            if (!FileTagCatalog.TryGetFinderColor(name, out tag)) return false;
            return true;
        }

        if (!string.Equals(kindValue, "custom", StringComparison.Ordinal)) return false;
        tag = new FileTag(name, FileTagCatalog.CustomTagColor, FileTagKind.Custom);
        return true;
    }
}
