namespace MacExplorer.Models;

/// <summary>Preview item showing old name → new name and conflict status.</summary>
public class BatchRenamePreviewItem
{
    public string OriginalPath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string NewName { get; set; } = "";
    public string NewPath { get; set; } = "";
    public bool HasConflict { get; set; }
    public bool HasError { get; set; }
    public string ErrorReason { get; set; } = "";
    public bool IsChanged => !string.Equals(OriginalName, NewName, StringComparison.Ordinal);
}

/// <summary>Result of a batch rename operation.</summary>
public class BatchRenameResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<BatchRenamePreviewItem> SuccessfulItems { get; set; } = [];
}

/// <summary>Progress update emitted while applying a batch rename.</summary>
public class BatchRenameProgress
{
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public string CurrentPath { get; set; } = "";
    public double Percent => TotalCount <= 0 ? 0 : CompletedCount * 100d / TotalCount;
}
