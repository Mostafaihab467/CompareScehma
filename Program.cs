using Avalonia;

namespace SchemaCompare;

sealed class Program
{
    // STAThread: the app uses the Windows clipboard (OLE), which requires a
    // single-threaded apartment. Without it clipboard calls can fail outright.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
