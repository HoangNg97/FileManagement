using Microsoft.Win32;

namespace LauncherCatalog.Services;

public sealed class StartupLaunchService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunName = "LauncherCatalog";

    public void EnsureEnabled()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            AppLogger.WriteInfo("Startup registration skipped because the executable path could not be resolved.");
            return;
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (runKey is null)
            {
                AppLogger.WriteInfo("Startup registration skipped because the Windows Run key could not be opened.");
                return;
            }

            var expectedValue = $"\"{executablePath}\"";
            var currentValue = runKey.GetValue(AppRunName) as string;
            if (string.Equals(currentValue, expectedValue, StringComparison.Ordinal))
            {
                return;
            }

            runKey.SetValue(AppRunName, expectedValue, RegistryValueKind.String);
            AppLogger.WriteInfo($"Startup registration updated: {expectedValue}");
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"Startup registration failed: {exception.Message}");
        }
    }
}
