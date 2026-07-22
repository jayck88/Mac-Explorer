using MacExplorer.Models;
using Microsoft.Extensions.Logging;

namespace MacExplorer.Services.Impl;

public class FileOperationHistoryService : IFileOperationHistoryService
{
    private readonly IFileService _fileService;
    private readonly IDirectoryChangeNotifier? _directoryChangeNotifier;
    private readonly IFileTagService? _fileTagService;
    private readonly ILogger<FileOperationHistoryService>? _logger;
    private readonly Stack<FileOperationRecord> _history = new();
    private readonly object _lock = new();

    private const string TrashPath = "/.Trash";

    public FileOperationHistoryService(
        IFileService fileService,
        IDirectoryChangeNotifier? directoryChangeNotifier = null,
        IFileTagService? fileTagService = null,
        ILogger<FileOperationHistoryService>? logger = null)
    {
        _fileService = fileService;
        _directoryChangeNotifier = directoryChangeNotifier;
        _fileTagService = fileTagService;
        _logger = logger;
    }

    public bool CanUndo
    {
        get { lock (_lock) return _history.Count > 0; }
    }

    public Task RecordRenameAsync(string oldPath, string newPath)
    {
        var record = new FileOperationRecord
        {
            Kind = FileOperationKind.Rename,
            OriginalPath = oldPath,
            CurrentPath = newPath
        };
        lock (_lock) _history.Push(record);
        return Task.CompletedTask;
    }

    public Task RecordTrashAsync(string originalPath, string trashedPath)
    {
        var record = new FileOperationRecord
        {
            Kind = FileOperationKind.MoveToTrash,
            OriginalPath = originalPath,
            CurrentPath = trashedPath
        };
        lock (_lock) _history.Push(record);
        return Task.CompletedTask;
    }

    public Task RecordMoveAsync(string oldPath, string newPath)
    {
        var record = new FileOperationRecord
        {
            Kind = FileOperationKind.Move,
            OriginalPath = oldPath,
            CurrentPath = newPath
        };
        lock (_lock) _history.Push(record);
        return Task.CompletedTask;
    }

    public async Task<bool> UndoLastAsync()
    {
        FileOperationRecord? record;
        lock (_lock)
        {
            if (_history.Count == 0) return false;
            record = _history.Pop();
        }

        try
        {
            var affectedDirs = new HashSet<string>(StringComparer.Ordinal);

            switch (record!.Kind)
            {
                case FileOperationKind.Rename:
                    // Rename back: CurrentPath -> OriginalPath
                    if (File.Exists(record.CurrentPath) || Directory.Exists(record.CurrentPath))
                    {
                        await _fileService.RenameAsync(record.CurrentPath, Path.GetFileName(record.OriginalPath));
                        if (_fileTagService != null)
                            await _fileTagService.UpdatePathAsync(record.CurrentPath, record.OriginalPath);
                        affectedDirs.Add(Path.GetDirectoryName(record.OriginalPath) ?? "");
                        affectedDirs.Add(Path.GetDirectoryName(record.CurrentPath) ?? "");
                    }
                    break;

                case FileOperationKind.Move:
                    // Move back: CurrentPath -> OriginalPath
                    if (File.Exists(record.CurrentPath) || Directory.Exists(record.CurrentPath))
                    {
                        var destDir = Path.GetDirectoryName(record.OriginalPath) ?? "";
                        if (Directory.Exists(destDir))
                        {
                            await _fileService.MoveAsync(record.CurrentPath, destDir, overwrite: false);
                            if (_fileTagService != null)
                                await _fileTagService.UpdatePathAsync(record.CurrentPath, record.OriginalPath);
                            affectedDirs.Add(destDir);
                            affectedDirs.Add(Path.GetDirectoryName(record.CurrentPath) ?? "");
                        }
                    }
                    break;

                case FileOperationKind.MoveToTrash:
                    // Try to restore from trash
                    var trashFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + TrashPath,
                        Path.GetFileName(record.OriginalPath));

                    // Also try the trashed path if it was recorded
                    var candidatePath = !string.IsNullOrEmpty(record.CurrentPath) && File.Exists(record.CurrentPath)
                        ? record.CurrentPath
                        : trashFile;

                    if (File.Exists(candidatePath) || Directory.Exists(candidatePath))
                    {
                        var destDir = Path.GetDirectoryName(record.OriginalPath) ?? "";
                        if (Directory.Exists(destDir))
                        {
                            await _fileService.MoveAsync(candidatePath, destDir, overwrite: false);
                            if (_fileTagService != null)
                                await _fileTagService.UpdatePathAsync(candidatePath, record.OriginalPath);
                            affectedDirs.Add(destDir);
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("Could not find trashed file to restore: {Path}", record.OriginalPath);
                        return false;
                    }
                    break;
            }

            if (affectedDirs.Count > 0)
                _directoryChangeNotifier?.NotifyChanged(affectedDirs.ToArray(), null);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to undo file operation: {Kind} {Path}", record!.Kind, record.OriginalPath);
            // Push the record back so it can be retried
            lock (_lock) _history.Push(record);
            return false;
        }
    }
}
