using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace LauncherCatalog.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static string LogDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "LauncherCatalog", "Logs");
        }
    }

    public static string LatestLogPath => Path.Combine(LogDirectory, "latest.log");

    public static string WriteCrash(Exception exception, string source, bool isTerminating = true)
    {
        var timestamp = DateTime.Now;
        var crashLogPath = Path.Combine(LogDirectory, $"crash-{timestamp:yyyyMMdd-HHmmss-fff}.log");
        var content = BuildLogContent(exception, source, timestamp, isTerminating);

        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(crashLogPath, content, Encoding.UTF8);
            File.WriteAllText(LatestLogPath, content, Encoding.UTF8);
        }

        return crashLogPath;
    }

    public static void WriteInfo(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LatestLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] INFO {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never become the reason the app fails to start.
        }
    }

    private static string BuildLogContent(Exception exception, string source, DateTime timestamp, bool isTerminating)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";

        var builder = new StringBuilder();
        builder.AppendLine("Launcher Catalog crash log");
        builder.AppendLine("==========================");
        builder.AppendLine($"Time: {timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Terminating: {isTerminating}");
        builder.AppendLine($"App version: {version}");
        builder.AppendLine($"Process: {Process.GetCurrentProcess().ProcessName}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Current directory: {Environment.CurrentDirectory}");
        builder.AppendLine($"Base directory: {AppContext.BaseDirectory}");
        builder.AppendLine($"User: {Environment.UserName}");
        builder.AppendLine();
        builder.AppendLine("Exception");
        builder.AppendLine("---------");
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }
}
