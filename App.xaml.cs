using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace WifiQrScanner;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Relaunch as admin if not already elevated
        if (!IsElevated())
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName,
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                Process.Start(psi);
            }
            catch
            {
                MessageBox.Show(
                    "Administrator rights are required to connect to WiFi networks.\nPlease run the app as administrator.",
                    "Elevation Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static bool IsElevated()
    {
        using var identity  = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
