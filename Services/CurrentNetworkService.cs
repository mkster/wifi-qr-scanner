using System.Diagnostics;
using WifiQrScanner.Models;

namespace WifiQrScanner.Services;

public record CurrentNetwork(string Ssid, string Password, WifiSecurityType SecurityType);

public static class CurrentNetworkService
{
    public static CurrentNetwork? GetCurrentNetwork()
    {
        var ssid = TryGetConnectedSsid();
        if (string.IsNullOrEmpty(ssid)) return null;

        var password = TryGetPassword(ssid);
        var secType = string.IsNullOrEmpty(password) ? WifiSecurityType.Open : WifiSecurityType.WPA2;

        return new CurrentNetwork(ssid, password ?? "", secType);
    }

    private static string? TryGetConnectedSsid()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                // "SSID" line but not "BSSID"
                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var ssid = parts[1].Trim();
                        return string.IsNullOrEmpty(ssid) ? null : ssid;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string? TryGetPassword(string ssid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"wlan show profile name=\"{ssid}\" key=clear",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Key Content", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var pwd = parts[1].Trim();
                        return string.IsNullOrEmpty(pwd) ? null : pwd;
                    }
                }
            }
        }
        catch { }
        return null;
    }
}
