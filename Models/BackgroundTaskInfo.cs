namespace MacExplorer.Models;

public enum BackgroundTaskState { Running, Completed, Failed, Cancelled }

public enum BackgroundTaskKind
{
    Generic,
    Copy,
    Move,
    Delete,
    Compress,
    Extract,
    RemoteUpload,
    RemoteDownload,
    BatchRename
}

public class BackgroundTaskInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Label { get; set; } = "";
    public double Progress { get; set; }
    public string CurrentFile { get; set; } = "";
    public BackgroundTaskState State { get; set; } = BackgroundTaskState.Running;
    public BackgroundTaskKind Kind { get; set; } = BackgroundTaskKind.Generic;
    public string? ErrorMessage { get; set; }
    public string? ErrorDetail { get; set; }
    public bool CanCancel { get; set; } = true;
    public bool CanRetry { get; set; }
    public Func<Task>? RetryAction { get; set; }
    public bool IsDismissedByUser { get; set; }
    public CancellationTokenSource Cts { get; set; } = new();
    public Func<Task>? OnCompleted { get; set; }
}
