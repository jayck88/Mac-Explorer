namespace MacExplorer.Services;

public sealed record ThumbnailResult(byte[] Bytes, string CachePath);

public interface IThumbnailService
{
    Task<ThumbnailResult?> GetThumbnailResultAsync(
        string filePath,
        int maxPixelSize,
        CancellationToken ct = default);
    Task<byte[]?> GetThumbnailAsync(string filePath, int maxPixelSize, CancellationToken ct = default);
    Task<byte[]?> GetFaceCropAsync(string filePath, float bx, float by, float bw, float bh, int maxPixelSize = 128, CancellationToken ct = default);
    bool IsImageFile(string extension);
    void EvictFromCache(string filePath);
}
