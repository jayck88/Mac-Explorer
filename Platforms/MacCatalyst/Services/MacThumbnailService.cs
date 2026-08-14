using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MacExplorer.Services;

namespace MacExplorer.Platforms.MacCatalyst.Services;

public class MacThumbnailService : IThumbnailService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp",
        ".heic", ".heif", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf",
        ".rw2", ".pef", ".raw"
    };
    private static readonly HashSet<string> QuickLookDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".pages", ".numbers", ".key", ".rtf", ".odt", ".ods", ".odp"
    };

    private const int MaxMemoryEntries = 300;
    private const long MaxMemoryBytes = 64L * 1024 * 1024;
    private const long DefaultMaxDiskBytes = 256L * 1024 * 1024;
    private const double DefaultDiskTargetRatio = 0.8;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private readonly ConcurrentQueue<string> _cacheOrder = new();
    private readonly SemaphoreSlim _generationGate = new(1);
    private readonly string _diskCacheDirectory;
    private readonly long _maxDiskBytes;
    private readonly long _targetDiskBytes;
    private long _memoryBytes;

    public MacThumbnailService() : this(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MacExplorer",
            "thumbnail-cache"),
        DefaultMaxDiskBytes,
        DefaultDiskTargetRatio)
    {
    }

    internal MacThumbnailService(string diskCacheDirectory, long maxDiskBytes, double targetRatio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskCacheDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDiskBytes);
        if (targetRatio is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(targetRatio));

        _diskCacheDirectory = diskCacheDirectory;
        _maxDiskBytes = maxDiskBytes;
        _targetDiskBytes = Math.Max(1, (long)(maxDiskBytes * targetRatio));
        Directory.CreateDirectory(_diskCacheDirectory);
    }

    public bool IsImageFile(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && ImageExtensions.Contains(extension);

    public async Task<byte[]?> GetThumbnailAsync(
        string filePath,
        int maxPixelSize,
        CancellationToken ct = default)
        => (await GetThumbnailResultAsync(filePath, maxPixelSize, ct))?.Bytes;

    public async Task<ThumbnailResult?> GetThumbnailResultAsync(
        string filePath,
        int maxPixelSize,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(filePath);
        if (!File.Exists(filePath) || (!IsImageFile(extension) && !QuickLookDocumentExtensions.Contains(extension)))
            return null;

        var cacheKey = $"{filePath}:{File.GetLastWriteTimeUtc(filePath).Ticks}:{maxPixelSize}";
        var cachePath = GetCachePath(cacheKey);
        if (_memoryCache.TryGetValue(cacheKey, out var memoryBytes))
        {
            if (File.Exists(cachePath))
            {
                TouchCacheFile(cachePath);
                return new ThumbnailResult(memoryBytes, cachePath);
            }

            await _generationGate.WaitAsync(ct);
            try
            {
                if (!File.Exists(cachePath))
                {
                    await WriteCacheFileAtomicallyAsync(cachePath, memoryBytes, ct);
                    TrimDiskCache(cachePath);
                }
                else
                {
                    TouchCacheFile(cachePath);
                }
                return new ThumbnailResult(memoryBytes, cachePath);
            }
            finally
            {
                _generationGate.Release();
            }
        }

        var diskBytes = await TryReadCacheFileAsync(cachePath, ct);
        if (diskBytes != null)
        {
            AddToMemory(cacheKey, diskBytes);
            return new ThumbnailResult(diskBytes, cachePath);
        }

        await _generationGate.WaitAsync(ct);
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out memoryBytes))
            {
                if (!File.Exists(cachePath))
                {
                    await WriteCacheFileAtomicallyAsync(cachePath, memoryBytes, ct);
                    TrimDiskCache(cachePath);
                }
                else
                {
                    TouchCacheFile(cachePath);
                }
                return new ThumbnailResult(memoryBytes, cachePath);
            }

            var cached = await TryReadCacheFileAsync(cachePath, ct);
            if (cached != null)
            {
                AddToMemory(cacheKey, cached);
                return new ThumbnailResult(cached, cachePath);
            }

            var generated = await GenerateThumbnailAsync(filePath, cachePath, maxPixelSize, ct);
            if (generated == null) return null;
            AddToMemory(cacheKey, generated);
            TrimDiskCache(cachePath);
            return new ThumbnailResult(generated, cachePath);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public async Task<byte[]?> GetFaceCropAsync(
        string filePath,
        float bx,
        float by,
        float bw,
        float bh,
        int maxPixelSize = 128,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath) || bw <= 0 || bh <= 0)
            return null;

        var cacheKey = $"face-v2:{filePath}:{File.GetLastWriteTimeUtc(filePath).Ticks}:{bx:F4}:{by:F4}:{bw:F4}:{bh:F4}:{maxPixelSize}";
        if (_memoryCache.TryGetValue(cacheKey, out var memoryBytes))
            return memoryBytes;

        var cachePath = GetCachePath(cacheKey);
        if (File.Exists(cachePath))
        {
            try
            {
                var diskBytes = await File.ReadAllBytesAsync(cachePath, ct);
                TouchCacheFile(cachePath);
                AddToMemory(cacheKey, diskBytes);
                return diskBytes;
            }
            catch
            {
                TryDelete(cachePath);
            }
        }

        await _generationGate.WaitAsync(ct);
        try
        {
            if (File.Exists(cachePath))
            {
                var cached = await File.ReadAllBytesAsync(cachePath, ct);
                TouchCacheFile(cachePath);
                AddToMemory(cacheKey, cached);
                return cached;
            }

            var dimensions = await GetDimensionsAsync(filePath, ct);
            if (dimensions == null) return null;

            var (width, height) = dimensions.Value;
            var cropWidth = Math.Clamp((int)Math.Round(bw * width * 1.6), 1, width);
            var cropHeight = Math.Clamp((int)Math.Round(bh * height * 1.6), 1, height);
            var centerX = (bx + bw / 2f) * width;
            var centerY = (1f - by - bh / 2f) * height;
            var offsetX = Math.Clamp((int)Math.Round(centerX - cropWidth / 2f), 0, Math.Max(0, width - cropWidth));
            var offsetY = Math.Clamp((int)Math.Round(centerY - cropHeight / 2f), 0, Math.Max(0, height - cropHeight));

            var croppedPath = CreateTemporaryPath("face-crop");
            var generatedPath = CreateTemporaryPath("face-result");
            try
            {
                var cropArguments = new[]
                {
                    "-c", cropHeight.ToString(), cropWidth.ToString(),
                    "--cropOffset", offsetY.ToString(), offsetX.ToString(),
                    "--setProperty", "format", "png",
                    filePath, "--out", croppedPath
                };
                if (!await RunSipsAsync(cropArguments, ct) || !File.Exists(croppedPath))
                    return null;

                var resizeArguments = new[]
                {
                    "-Z", Math.Max(1, maxPixelSize).ToString(),
                    "--setProperty", "format", "png",
                    croppedPath, "--out", generatedPath
                };
                if (!await RunSipsAsync(resizeArguments, ct) || !File.Exists(generatedPath))
                {
                    return null;
                }

                PromoteTemporaryFile(generatedPath, cachePath);
                var bytes = await File.ReadAllBytesAsync(cachePath, ct);
                TouchCacheFile(cachePath);
                AddToMemory(cacheKey, bytes);
                TrimDiskCache(cachePath);
                return bytes;
            }
            finally
            {
                TryDelete(croppedPath);
                TryDelete(generatedPath);
            }
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public void EvictFromCache(string filePath)
    {
        foreach (var key in _memoryCache.Keys.Where(key => key.Contains(filePath, StringComparison.Ordinal)))
            RemoveFromMemory(key);
    }

    public void ClearCache()
    {
        _memoryCache.Clear();
        Interlocked.Exchange(ref _memoryBytes, 0);
        while (_cacheOrder.TryDequeue(out _)) { }
    }

    private async Task<byte[]?> GenerateThumbnailAsync(
        string sourcePath,
        string cachePath,
        int maxPixelSize,
        CancellationToken ct)
    {
        if (!IsImageFile(Path.GetExtension(sourcePath)))
            return await GenerateQuickLookThumbnailAsync(sourcePath, cachePath, maxPixelSize, ct);

        var generatedPath = CreateTemporaryPath("thumbnail");
        try
        {
            var arguments = new[]
            {
                "-Z", Math.Max(32, maxPixelSize).ToString(),
                "--setProperty", "format", "png",
                sourcePath, "--out", generatedPath
            };
            if (await RunSipsAsync(arguments, ct) && File.Exists(generatedPath))
            {
                PromoteTemporaryFile(generatedPath, cachePath);
                var generated = await File.ReadAllBytesAsync(cachePath, ct);
                TouchCacheFile(cachePath);
                return generated;
            }
        }
        finally
        {
            TryDelete(generatedPath);
        }

        var info = new FileInfo(sourcePath);
        if (info.Length > 10 * 1024 * 1024) return null;

        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, ct);
        await WriteCacheFileAtomicallyAsync(cachePath, sourceBytes, ct);
        return sourceBytes;
    }

    private async Task<byte[]?> GenerateQuickLookThumbnailAsync(
        string sourcePath,
        string cachePath,
        int maxPixelSize,
        CancellationToken ct)
    {
        var outputDirectory = Path.Combine(_diskCacheDirectory, ".quicklook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/qlmanage",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-t", "-s", Math.Max(128, maxPixelSize).ToString(), "-o", outputDirectory, sourcePath })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process == null) return null;
            TrySetBelowNormalPriority(process);
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                throw;
            }

            if (process.ExitCode != 0) return null;
            var generatedPath = Directory.EnumerateFiles(outputDirectory, "*.png").FirstOrDefault();
            if (generatedPath == null) return null;
            var temporaryPath = CreateTemporaryPath("quicklook");
            try
            {
                File.Move(generatedPath, temporaryPath, overwrite: true);
                PromoteTemporaryFile(temporaryPath, cachePath);
                var generated = await File.ReadAllBytesAsync(cachePath, ct);
                TouchCacheFile(cachePath);
                return generated;
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); }
            catch { }
        }
    }

    private static async Task<(int Width, int Height)?> GetDimensionsAsync(
        string filePath,
        CancellationToken ct)
    {
        var startInfo = CreateSipsStartInfo(["-g", "pixelWidth", "-g", "pixelHeight", filePath]);
        using var process = Process.Start(startInfo);
        if (process == null) return null;
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        var output = await outputTask;
        if (process.ExitCode != 0) return null;

        int width = 0, height = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            if (parts[0] == "pixelWidth") int.TryParse(parts[1], out width);
            if (parts[0] == "pixelHeight") int.TryParse(parts[1], out height);
        }
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static async Task<bool> RunSipsAsync(IEnumerable<string> arguments, CancellationToken ct)
    {
        Process? process = null;
        try
        {
            process = Process.Start(CreateSipsStartInfo(arguments));
            if (process == null) return false;
            TrySetBelowNormalPriority(process);
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            if (process != null)
                TryKillProcess(process);
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static ProcessStartInfo CreateSipsStartInfo(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/sips",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static void TrySetBelowNormalPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
        }
    }

    private string GetCachePath(string cacheKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        return Path.Combine(_diskCacheDirectory, hash + ".png");
    }

    private string CreateTemporaryPath(string purpose)
        => Path.Combine(_diskCacheDirectory, $".tmp-{purpose}-{Guid.NewGuid():N}");

    private async Task<byte[]?> TryReadCacheFileAsync(string cachePath, CancellationToken ct)
    {
        if (!File.Exists(cachePath)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(cachePath, ct);
            TouchCacheFile(cachePath);
            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDelete(cachePath);
            return null;
        }
    }

    private async Task WriteCacheFileAtomicallyAsync(
        string cachePath,
        byte[] bytes,
        CancellationToken ct)
    {
        var temporaryPath = CreateTemporaryPath("write");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, ct);
            PromoteTemporaryFile(temporaryPath, cachePath);
            TouchCacheFile(cachePath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void PromoteTemporaryFile(string temporaryPath, string cachePath)
        => File.Move(temporaryPath, cachePath, overwrite: true);

    private void TrimDiskCache(string protectedPath)
    {
        try
        {
            var files = Directory.EnumerateFiles(_diskCacheDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith(".tmp-", StringComparison.Ordinal))
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .ToList();
            var totalBytes = files.Sum(info => info.Length);
            if (totalBytes <= _maxDiskBytes) return;

            foreach (var file in files.OrderBy(info => info.LastAccessTimeUtc))
            {
                if (totalBytes <= _targetDiskBytes) break;
                if (string.Equals(file.FullName, protectedPath, StringComparison.Ordinal)) continue;
                var length = file.Length;
                try
                {
                    file.Delete();
                    totalBytes -= length;
                }
                catch
                {
                    // Locked and concurrently removed files are skipped; the next write retries trimming.
                }
            }
        }
        catch
        {
            // Cache cleanup is best-effort and must not fail thumbnail delivery.
        }
    }

    private static void TouchCacheFile(string cachePath)
    {
        try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); }
        catch { }
    }

    private void AddToMemory(string key, byte[] bytes)
    {
        if (!_memoryCache.TryAdd(key, bytes)) return;
        _cacheOrder.Enqueue(key);
        Interlocked.Add(ref _memoryBytes, bytes.LongLength);

        while (_memoryCache.Count > MaxMemoryEntries || Interlocked.Read(ref _memoryBytes) > MaxMemoryBytes)
        {
            if (!_cacheOrder.TryDequeue(out var oldest)) break;
            RemoveFromMemory(oldest);
        }
    }

    private void RemoveFromMemory(string key)
    {
        if (_memoryCache.TryRemove(key, out var removed))
            Interlocked.Add(ref _memoryBytes, -removed.LongLength);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
