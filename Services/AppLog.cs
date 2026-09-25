using System.Text;

namespace SchemaCompare.Services;

/// <summary>
/// Plain-text application log in the data folder, size-rotated so a long-lived
/// session cannot fill the disk. Every write is best-effort and never throws:
/// a logging failure must not take down the operation being logged.
/// </summary>
public static class AppLog
{
    public const string FolderName = "SchemaCompare";
    public const string FileName = "app_log.txt";

    private const long MaxBytes = 1024 * 1024;
    private const int KeepRotations = 2;
    private static readonly object Gate = new();

    /// <summary>Set when a write failed; surfaced in the About / diagnostics window.</summary>
    public static string? WriteFailure { get; private set; }

    /// <summary>
    /// Test hook: redirects the whole log folder (used by the verification harness
    /// so it can exercise rotation without touching the real log).
    /// </summary>
    public static string? RedirectDirectory { get; set; }

    public static string LogDirectory =>
        RedirectDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    public static string LogFilePath => Path.Combine(LogDirectory, FileName);

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message) => Write("WARN ", message, null);

    public static void Error(string area, Exception exception, string? detail = null) =>
        Write("ERROR", string.IsNullOrEmpty(detail) ? area : $"{area}: {detail}", exception);

    public static void Fatal(string area, Exception? exception, string? context = null) =>
        Write("FATAL", $"{area}{(context is null ? "" : ": " + context)}", exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {level} | {message}";
        if (exception != null) text += Environment.NewLine + exception;

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, text + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                WriteFailure = $"{ex.GetType().Name}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[AppLog] Write failed: {WriteFailure}");
            }
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < MaxBytes) return;

        for (var i = KeepRotations - 1; i >= 1; i--)
        {
            var from = RotationPath(i);
            if (File.Exists(from)) File.Move(from, RotationPath(i + 1), overwrite: true);
        }
        File.Move(LogFilePath, RotationPath(1), overwrite: true);
    }

    private static string RotationPath(int generation) =>
        Path.Combine(LogDirectory, $"{Path.GetFileNameWithoutExtension(FileName)}.{generation}.txt");

    public static IEnumerable<string> ExistingLogFiles()
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return Array.Empty<string>();
            return Directory.GetFiles(LogDirectory, "*.txt").OrderBy(f => f).ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
