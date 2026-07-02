using MacExplorer.Models;
using Microsoft.Extensions.Logging;

namespace MacExplorer.Services.Impl;

public class BackgroundTaskManager : IBackgroundTaskManager
{
    private readonly List<BackgroundTaskInfo> _tasks = [];
    private readonly object _lock = new();
    private readonly ILogger<BackgroundTaskManager>? _logger;

    public IReadOnlyList<BackgroundTaskInfo> Tasks
    {
        get { lock (_lock) return _tasks.ToList(); }
    }

    public event Action? TasksChanged;

    public BackgroundTaskManager(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<BackgroundTaskManager>();
    }

    public BackgroundTaskInfo AddTask(string label, Func<Task>? onCompleted = null)
    {
        var task = new BackgroundTaskInfo { Label = label, OnCompleted = onCompleted };
        lock (_lock) _tasks.Add(task);
        RaiseTasksChanged();
        return task;
    }

    public BackgroundTaskInfo AddTask(string label, BackgroundTaskKind kind, Func<Task>? onCompleted = null, Func<Task>? retryAction = null)
    {
        var task = new BackgroundTaskInfo
        {
            Label = label,
            Kind = kind,
            OnCompleted = onCompleted,
            RetryAction = retryAction,
            CanRetry = retryAction != null
        };
        lock (_lock) _tasks.Add(task);
        RaiseTasksChanged();
        return task;
    }

    public void UpdateProgress(string taskId, double progress, string currentFile, string? label = null)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            task.Progress = progress;
            task.CurrentFile = currentFile;
            if (!string.IsNullOrWhiteSpace(label))
                task.Label = label;
        }
        RaiseTasksChanged();
    }

    public void CompleteTask(string taskId)
    {
        Func<Task>? callback;
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            task.State = BackgroundTaskState.Completed;
            task.Progress = 100;
            callback = task.OnCompleted;
        }
        RaiseTasksChanged();

        // 执行完成回调
        if (callback != null)
            _ = Task.Run(async () => { try { await callback(); } catch (Exception ex) { _logger?.LogWarning(ex, "Background task callback failed for task {TaskId}", taskId); } });

    }

    public void FailTask(string taskId, string error)
    {
        FailTask(taskId, error, null);
    }

    public void FailTask(string taskId, string error, string? errorDetail)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            task.State = BackgroundTaskState.Failed;
            task.ErrorMessage = error;
            task.ErrorDetail = errorDetail;
            task.CanRetry = task.RetryAction != null;
        }
        RaiseTasksChanged();
    }

    public void CancelTask(string taskId)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            task.Cts.Cancel();
            task.State = BackgroundTaskState.Cancelled;
        }
        RaiseTasksChanged();
    }

    public void RetryTask(string taskId)
    {
        Func<Task>? retryAction = null;
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null || task.RetryAction == null) return;
            task.State = BackgroundTaskState.Running;
            task.Progress = 0;
            task.ErrorMessage = null;
            task.ErrorDetail = null;
            task.Cts = new CancellationTokenSource();
            retryAction = task.RetryAction;
        }
        RaiseTasksChanged();

        if (retryAction != null)
            _ = Task.Run(async () => { try { await retryAction(); } catch (Exception ex) { _logger?.LogWarning(ex, "Background task retry failed for task {TaskId}", taskId); } });
    }

    public void MinimizeTask(string taskId)
    {
        // No-op: task lifecycle is now managed by TaskPanel component
    }

    public void RemoveTask(string taskId)
    {
        List<BackgroundTaskInfo> removed;
        lock (_lock)
        {
            removed = _tasks.Where(t => t.Id == taskId).ToList();
            _tasks.RemoveAll(t => t.Id == taskId);
        }

        foreach (var task in removed)
            task.Cts.Dispose();

        RaiseTasksChanged();
    }

    private void RaiseTasksChanged()
    {
        var handlers = TasksChanged?.GetInvocationList();
        if (handlers == null) return;

        foreach (var handler in handlers)
        {
            try { ((Action)handler)(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Background task subscriber failed"); }
        }
    }
}
