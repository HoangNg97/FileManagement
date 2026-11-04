// Root launcher for the published folder: starts app\WorkSync.exe so the folder root stays tidy.
// Compiled by scripts/publish-windows.ps1 with the .NET Framework csc.exe that ships with Windows.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

static class Launcher
{
    [STAThread]
    static int Main()
    {
        string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
        string app = Path.Combine(appDir, "WorkSync.exe");
        if (!File.Exists(app))
        {
            MessageBox.Show(
                "Could not find " + app + "\n\nKeep the 'app' folder next to WorkSync.exe.",
                "WorkSync", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        Process.Start(new ProcessStartInfo(app) { UseShellExecute = false, WorkingDirectory = appDir });
        return 0;
    }
}
