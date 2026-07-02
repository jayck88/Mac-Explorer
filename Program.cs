using Avalonia;
using System;
using System.IO;

namespace MacExplorer;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var logPath = Path.Combine(AppContext.BaseDirectory, "macexplorer_crash.log");
        try { File.AppendAllText(logPath,
            $"[{DateTime.Now:HH:mm:ss.fff}] UnhandledException: {ex}\n\n"); } catch { }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "macexplorer_crash.log");
        try { File.AppendAllText(logPath,
            $"[{DateTime.Now:HH:mm:ss.fff}] UnobservedTaskException: {e.Exception}\n\n"); } catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
