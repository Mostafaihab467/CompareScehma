using Avalonia;
using SchemaCompare.Services;

namespace SchemaCompare;

sealed class Program
{
    // STAThread: the app uses the Windows clipboard (OLE), which requires a
    // single-threaded apartment. Without it clipboard calls can fail outright.
    [STAThread]
    public static void Main(string[] args)
    {
        // Leave a trail if the process ever dies from an unhandled exception
        // (e.g. out-of-memory while reading or rendering huge query results).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception, false);
            e.SetObserved();
        };
        AppLog.Info($"Startup | {AppInfo.VersionText} | {AppInfo.OsText}");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            AppLog.Info("Shutdown");
        }
    }

    private static void WriteCrashLog(string source, Exception? exception, bool isTerminating) =>
        // Never let logging crash the crash handler: AppLog swallows its own failures.
        AppLog.Fatal(source, exception,
            $"terminating={isTerminating} | memory={Environment.WorkingSet / (1024 * 1024)} MB");

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
