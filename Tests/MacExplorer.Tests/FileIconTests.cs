using MacExplorer.Models;
using MacExplorer.Services.Impl;
using Xunit;

namespace MacExplorer.Tests;

public sealed class FileIconTests
{
    [Theory]
    [InlineData(".apk")]
    [InlineData(".APK")]
    public void ApkFilesResolveToAndroidPackageIcon(string extension)
    {
        Assert.Equal("file-android-package", FileIconResolver.ResolveIconKey(extension));
    }

    [Fact]
    public void AndroidPackageIconUsesAndroidBrandArtwork()
    {
        var svg = FileIconRenderer.Render("file-android-package", ".apk", 32);

        Assert.Contains("#3DDC84", svg, StringComparison.Ordinal);
        Assert.Contains("<circle cx=\"12\" cy=\"11.6\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("APK</text>", svg, StringComparison.Ordinal);
    }
}
