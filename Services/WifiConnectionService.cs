using System.Diagnostics;
using System.IO;
using WifiQrScanner.Models;

namespace WifiQrScanner.Services;

public enum ConnectionStatus { Connecting, Connected, AuthFailed, NotFound, Error }

public record ConnectionResult(ConnectionStatus Status, string Message);

public class WifiConnectionService
{
    public async Task<ConnectionResult> ConnectAsync(WifiCredentials creds, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"wifi_{Guid.NewGuid():N}.xml");

        try
        {
            // 1. Write profile XML
            var xml = WifiProfileBuilder.BuildProfileXml(creds);
            await File.WriteAllTextAsync(tempFile, xml, ct);

            // 2. Delete existing profile if present (ignore errors)
            await RunNetshAsync($"wlan delete profile name=\"{creds.Ssid}\"");

            // 3. Add profile
            var addResult = await RunNetshAsync($"wlan add profile filename=\"{tempFile}\" user=all");
            if (addResult.ExitCode != 0)
                return new ConnectionResult(ConnectionStatus.Error, $"Failed to add profile: {addResult.Output}");

            // 4. Connect
            var connectResult = await RunNetshAsync($"wlan connect name=\"{creds.Ssid}\" ssid=\"{creds.Ssid}\"");
            if (connectResult.ExitCode != 0)
                return new ConnectionResult(ConnectionStatus.Error, $"Connect command failed: {connectResult.Output}");

            // 5. Poll for connection (15 seconds)
            return await PollConnectionAsync(creds.Ssid, ct);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    private async Task<ConnectionResult> PollConnectionAsync(string ssid, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct);

            var result = await RunNetshAsync("wlan show interfaces");
            var state = ParseInterfaceState(result.Output);

            if (state == "connected")
                return new ConnectionResult(ConnectionStatus.Connected, $"Connected to {ssid}");

            if (state == "disconnected")
                return new ConnectionResult(ConnectionStatus.AuthFailed, "Authentication failed. Check password.");
        }

        return new ConnectionResult(ConnectionStatus.AuthFailed, "Connection timed out. Wrong password or out of range?");
    }

    private static string ParseInterfaceState(string netshOutput)
    {
        foreach (var line in netshOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2)
                    return parts[1].Trim().ToLower();
            }
        }
        return "unknown";
    }

    private static async Task<(int ExitCode, string Output)> RunNetshAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
