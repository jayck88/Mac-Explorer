using MacExplorer.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MacExplorer.Services.Impl;

public sealed class FileTagService : IFileTagService, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IFinderTagQueryService? _finderTagQueryService;
    private readonly ILogger<FileTagService>? _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    public FileTagService(
        DatabaseConnectionFactory connectionFactory,
        IFinderTagQueryService? finderTagQueryService = null,
        ILogger<FileTagService>? logger = null)
    {
        _connection = connectionFactory.GetConnection();
        _finderTagQueryService = finderTagQueryService;
        _logger = logger;
        EnsureSchema();
    }

    public event EventHandler? TagsChanged;

    public async Task<IReadOnlyList<FileTag>> GetSidebarTagsAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var systemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var customTags = new List<FileTag>();

            using (var systemCommand = _connection.CreateCommand())
            {
                systemCommand.CommandText = """
                    SELECT tag, COUNT(*)
                    FROM file_tags
                    WHERE is_system = 1
                    GROUP BY tag
                    """;
                using var reader = await systemCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    systemCounts[FileTagCatalog.NormalizeName(reader.GetString(0))] = reader.GetInt32(1);
            }

            using (var customCommand = _connection.CreateCommand())
            {
                customCommand.CommandText = """
                    SELECT tag, COUNT(*) AS item_count, MAX(created_at) AS last_used
                    FROM file_tags
                    WHERE is_system = 0
                    GROUP BY tag COLLATE NOCASE
                    ORDER BY item_count DESC, last_used DESC, tag COLLATE NOCASE
                    """;
                using var reader = await customCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    customTags.Add(new FileTag(
                        reader.GetString(0),
                        FileTagCatalog.CustomTagColor,
                        FileTagKind.Custom,
                        reader.GetInt32(1)));
                }
            }

            return FileTagCatalog.FinderColors
                .Select(tag => tag with { ItemCount = systemCounts.GetValueOrDefault(tag.Name) })
                .Concat(customTags)
                .ToArray();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> FindFilePathsAsync(
        FileTag tag,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = FileTagCatalog.NormalizeName(tag.Name);
        var paths = new HashSet<string>(StringComparer.Ordinal);

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT file_path
                FROM file_tags
                WHERE tag = @tag COLLATE NOCASE
                  AND is_system = @isSystem
                ORDER BY created_at DESC
                """;
            command.Parameters.AddWithValue("@tag", normalizedName);
            command.Parameters.AddWithValue("@isSystem", tag.IsFinderColor ? 1 : 0);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                paths.Add(reader.GetString(0));
        }
        finally
        {
            _connectionLock.Release();
        }

        if (_finderTagQueryService != null)
        {
            try
            {
                var finderPaths = await _finderTagQueryService.FindFilePathsAsync(tag, cancellationToken);
                paths.UnionWith(finderPaths);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Finder tag query failed for {Tag}", normalizedName);
            }
        }

        return paths.ToArray();
    }

    public async Task ReplaceFileTagsAsync(
        string filePath,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || VirtualPath.IsRemotePath(filePath)) return;

        var normalizedTags = NormalizeTags(tags);
        var changed = false;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var existing = new HashSet<(string Name, bool IsSystem)>();
            using (var select = _connection.CreateCommand())
            {
                select.CommandText = "SELECT tag, is_system FROM file_tags WHERE file_path = @path";
                select.Parameters.AddWithValue("@path", filePath);
                using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    existing.Add((FileTagCatalog.NormalizeName(reader.GetString(0)), reader.GetInt32(1) != 0));
            }

            var desired = normalizedTags
                .Select(name => (Name: name, IsSystem: FileTagCatalog.TryGetFinderColor(name, out _)))
                .ToHashSet();
            changed = !existing.SetEquals(desired);
            if (!changed) return;

            using var transaction = _connection.BeginTransaction();
            using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM file_tags WHERE file_path = @path";
                delete.Parameters.AddWithValue("@path", filePath);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var insert = _connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO file_tags (file_path, tag, is_system, created_at)
                    VALUES (@path, @tag, @isSystem, @createdAt)
                    """;
                var pathParameter = insert.Parameters.Add("@path", SqliteType.Text);
                var tagParameter = insert.Parameters.Add("@tag", SqliteType.Text);
                var systemParameter = insert.Parameters.Add("@isSystem", SqliteType.Integer);
                var createdParameter = insert.Parameters.Add("@createdAt", SqliteType.Integer);
                pathParameter.Value = filePath;

                foreach (var (name, isSystem) in desired)
                {
                    tagParameter.Value = name;
                    systemParameter.Value = isSystem ? 1 : 0;
                    createdParameter.Value = DateTime.UtcNow.Ticks;
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            transaction.Commit();
        }
        finally
        {
            _connectionLock.Release();
        }

        if (changed)
            TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task UpdatePathAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) =>
        TransferPathAsync(oldPath, newPath, deleteSource: true, cancellationToken);

    public Task CopyPathAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) =>
        TransferPathAsync(sourcePath, destinationPath, deleteSource: false, cancellationToken);

    public async Task DeletePathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || VirtualPath.IsRemotePath(path)) return;

        var changed = false;
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                DELETE FROM file_tags
                WHERE file_path = @path
                   OR substr(file_path, 1, length(@prefix)) = @prefix
                """;
            command.Parameters.AddWithValue("@path", path);
            command.Parameters.AddWithValue("@prefix", EnsureDirectoryPrefix(path));
            changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally
        {
            _connectionLock.Release();
        }

        if (changed)
            TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task TransferPathAsync(
        string oldPath,
        string newPath,
        bool deleteSource,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(oldPath)
            || string.IsNullOrWhiteSpace(newPath)
            || VirtualPath.IsRemotePath(oldPath)
            || VirtualPath.IsRemotePath(newPath)
            || string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return;

        var changed = false;
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var oldPrefix = EnsureDirectoryPrefix(oldPath);
            var rows = new List<(string Path, string Tag, int IsSystem, long CreatedAt)>();
            using (var select = _connection.CreateCommand())
            {
                select.CommandText = """
                    SELECT file_path, tag, is_system, created_at
                    FROM file_tags
                    WHERE file_path = @path
                       OR substr(file_path, 1, length(@prefix)) = @prefix
                    """;
                select.Parameters.AddWithValue("@path", oldPath);
                select.Parameters.AddWithValue("@prefix", oldPrefix);
                using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3)));
            }

            if (rows.Count == 0) return;

            using var transaction = _connection.BeginTransaction();
            if (deleteSource)
            {
                using var delete = _connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = """
                    DELETE FROM file_tags
                    WHERE file_path = @path
                       OR substr(file_path, 1, length(@prefix)) = @prefix
                    """;
                delete.Parameters.AddWithValue("@path", oldPath);
                delete.Parameters.AddWithValue("@prefix", oldPrefix);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var insert = _connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT OR REPLACE INTO file_tags (file_path, tag, is_system, created_at)
                    VALUES (@path, @tag, @isSystem, @createdAt)
                    """;
                var pathParameter = insert.Parameters.Add("@path", SqliteType.Text);
                var tagParameter = insert.Parameters.Add("@tag", SqliteType.Text);
                var systemParameter = insert.Parameters.Add("@isSystem", SqliteType.Integer);
                var createdParameter = insert.Parameters.Add("@createdAt", SqliteType.Integer);

                foreach (var row in rows)
                {
                    var relativeSuffix = row.Path.Length == oldPath.Length ? string.Empty : row.Path[oldPath.Length..];
                    pathParameter.Value = newPath + relativeSuffix;
                    tagParameter.Value = row.Tag;
                    systemParameter.Value = row.IsSystem;
                    createdParameter.Value = row.CreatedAt;
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            transaction.Commit();
            changed = true;
        }
        finally
        {
            _connectionLock.Release();
        }

        if (changed)
            TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS file_tags (
                file_path TEXT NOT NULL,
                tag TEXT NOT NULL,
                is_system INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL,
                PRIMARY KEY (file_path, tag)
            );
            CREATE INDEX IF NOT EXISTS idx_file_tags_tag ON file_tags(tag, is_system);
            CREATE INDEX IF NOT EXISTS idx_file_tags_path ON file_tags(file_path);
            """;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(NormalizeTagForDisplay)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(FileTagCatalog.NormalizeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeTagForDisplay(string tag)
    {
        var normalized = tag.Trim().Replace("\\012", "\n", StringComparison.Ordinal);
        var suffixStart = normalized.LastIndexOf('\n');
        if (suffixStart >= 0 && int.TryParse(normalized[(suffixStart + 1)..], out _))
            normalized = normalized[..suffixStart];
        return normalized.Trim();
    }

    private static string EnsureDirectoryPrefix(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
        _connectionLock.Dispose();
    }
}
