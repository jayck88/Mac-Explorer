using MacExplorer.Models;

namespace MacExplorer.Services;

/// <summary>
/// Records and undoes reversible local file operations.
/// </summary>
public interface IFileOperationHistoryService
{
    /// <summary>Record a rename operation for potential undo.</summary>
    Task RecordRenameAsync(string oldPath, string newPath);

    /// <summary>Record a move-to-trash operation for potential undo.</summary>
    Task RecordTrashAsync(string originalPath, string trashedPath);

    /// <summary>Record a move operation for potential undo.</summary>
    Task RecordMoveAsync(string oldPath, string newPath);

    /// <summary>Undo the most recent reversible operation. Returns true if an operation was undone.</summary>
    Task<bool> UndoLastAsync();

    /// <summary>Check if there are any undoable operations.</summary>
    bool CanUndo { get; }
}

/// <summary>Represents a single undoable file operation.</summary>
public enum FileOperationKind
{
    Rename,
    MoveToTrash,
    Move
}

/// <summary>Represents a recorded file operation that can be undone.</summary>
public class FileOperationRecord
{
    public FileOperationKind Kind { get; set; }
    public string OriginalPath { get; set; } = "";
    public string CurrentPath { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
