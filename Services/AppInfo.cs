using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SchemaCompare.Services;

/// <summary>
/// Version and environment facts shown in the About window and copied into
/// diagnostics text for bug reports. Everything here is offline and cheap.
/// </summary>
public static class AppInfo
{
    public static Assembly EntryAssembly { get; } = typeof(AppInfo).Assembly;

    public static string ProductName =>
        EntryAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Schema Compare";

    /// <summary>Informational version, including the "simple" form without build metadata.</summary>
    public static string VersionText
    {
        get
        {
            var info = EntryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info)) info = EntryAssembly.GetName().Version?.ToString() ?? "unknown";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }

    public static string OsText => RuntimeInformation.OSDescription;

    public static string DotNetText => $".NET {Environment.Version}";

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string DataDirectory => AppLog.LogDirectory;

    /// <summary>Loaded dependency versions, e.g. Avalonia, SqlClient, DacFx.</summary>
    public static IEnumerable<(string Name, string Version)> Dependencies()
    {
        foreach (var name in new[] { "Avalonia", "AvaloniaEdit", "Microsoft.Data.SqlClient", "Microsoft.SqlServer.Dac", "CommunityToolkit.Mvvm" })
            yield return (name, LoadedVersion(name));
    }

    private static string LoadedVersion(string assemblyName)
    {
        try
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            var asm = loaded ?? Assembly.Load(new AssemblyName(assemblyName));
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info)) info = asm.GetName().Version?.ToString();
            var plus = info?.IndexOf('+') ?? -1;
            return plus > 0 ? info![..plus] : info ?? "not loaded";
        }
        catch
        {
            return "not loaded";
        }
    }

    /// <summary>Multi-line environment block for the About window and bug reports.</summary>
    public static string BuildDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{ProductName} {VersionText}");
        sb.AppendLine($"OS: {OsText} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime: {DotNetText}");
        sb.AppendLine($"64-bit process: {(Environment.Is64BitProcess ? "yes" : "no")}");
        foreach (var (name, version) in Dependencies())
            sb.AppendLine($"{name}: {version}");
        sb.AppendLine($"Data folder: {DataDirectory}");
        sb.AppendLine($"Log file: {AppLog.LogFilePath}");
        foreach (var file in AppLog.ExistingLogFiles())
        {
            try { sb.AppendLine($"  {Path.GetFileName(file)}: {new FileInfo(file).Length:N0} bytes"); }
            catch { sb.AppendLine($"  {Path.GetFileName(file)}: size unavailable"); }
        }
        if (AppLog.WriteFailure is { } failure)
            sb.AppendLine($"Log write failure: {failure}");
        sb.AppendLine($"Local time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }
}
