using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

public partial class AboutWindow : Window
{
    private const int TailLines = 40;

    public AboutWindow()
    {
        InitializeComponent();

        TitleText.Text = $"{AppInfo.ProductName} {AppInfo.VersionText}";
        SubtitleText.Text = AppInfo.IsWindows ? "Windows desktop tool" : "Runs best on Windows (credential encryption is Windows-only)";
        DiagnosticsBox.Text = AppInfo.BuildDiagnostics();
        LogTailBox.Text = ReadLogTail();

        CopyButton.Click += Copy_Click;
        OpenFolderButton.Click += OpenFolder_Click;
        CloseButton.Click += (_, _) => Close();
    }

    public string DiagnosticsText => DiagnosticsBox.Text ?? string.Empty;

    public string LogTailText => LogTailBox.Text ?? string.Empty;

    /// <summary>Last lines of the log file, newest last; never throws.</summary>
    public static string ReadLogTail()
    {
        try
        {
            var path = AppLog.LogFilePath;
            if (!File.Exists(path)) return "No log entries yet.";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(-Math.Min(fs.Length, 32 * 1024), SeekOrigin.End);
            using var reader = new StreamReader(fs);
            reader.ReadLine(); // the first partial line is not a whole entry
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
                if (lines.Count > TailLines) lines.RemoveAt(0);
            }
            return lines.Count == 0 ? "No log entries yet." : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            AppLog.Error("AboutWindow", ex, "Reading the log tail failed");
            return "The log file could not be read.";
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                await ClipboardExtensions.SetTextAsync(cb, DiagnosticsText);
                FeedbackText.Text = "Diagnostics copied to the clipboard.";
            }
            else FeedbackText.Text = "Clipboard is unavailable.";
        }
        catch (Exception ex)
        {
            AppLog.Error("AboutWindow", ex, "Copy diagnostics failed");
            FeedbackText.Text = "Copy failed: the system clipboard is currently unavailable.";
        }
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            StartExplorer(AppLog.LogDirectory);
            FeedbackText.Text = $"Opened {AppLog.LogDirectory}";
        }
        catch (Exception ex)
        {
            AppLog.Error("AboutWindow", ex, "Opening the data folder failed");
            FeedbackText.Text = "Could not open the data folder. Its path is listed above.";
        }
    }

    private static void StartExplorer(string path)
    {
        if (!AppInfo.IsWindows)
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
    }
}
