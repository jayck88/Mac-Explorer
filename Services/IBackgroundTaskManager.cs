using MacExplorer.Models;

namespace MacExplorer.Services;

public interface IBackgroundTaskManager
{
    IReadOnlyList<BackgroundTaskInfo> Tasks { get; }
    event Action? TasksChanged;

    BackgroundTaskInfo AddTask(string label, Func<Task>? onCompleted = null);
    BackgroundTaskInfo AddTask(string label, BackgroundTaskKind kind, Func<Task>? onCompleted = null, Func<Task>? retryAction = null);
    void UpdateProgress(string taskId, double progress, string currentFile, string? label = null);
    void CompleteTask(string taskId);
    void FailTask(string taskId, string error, string? errorDetail = null);
    void CancelTask(string taskId);
    void RetryTask(string taskId);
    void MinimizeTask(string taskId);
    void RemoveTask(string taskId);
}
