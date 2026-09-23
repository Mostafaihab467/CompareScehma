using Avalonia;

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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void WriteCrashLog(string source, Exception? exception, bool isTerminating)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SchemaCompare");
            Directory.CreateDirectory(dir);
            var entry =
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {source} | terminating={isTerminating}" +
                $" | memory={Environment.WorkingSet / (1024 * 1024)} MB ===" +
                Environment.NewLine + exception + Environment.NewLine + Environment.NewLine;
            File.AppendAllText(Path.Combine(dir, "crash_log.txt"), entry);
        }
        catch { /* never let logging crash the crash handler */ }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
