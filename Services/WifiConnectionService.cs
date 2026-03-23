using Windows.Devices.WiFi;
using Windows.Security.Credentials;
using WifiQrScanner.Models;

namespace WifiQrScanner.Services;

public enum ConnectionStatus { Connecting, Connected, AuthFailed, NotFound, Error }

public record ConnectionResult(ConnectionStatus Status, string Message);

public class WifiConnectionService
{
    public async Task<ConnectionResult> ConnectAsync(WifiCredentials creds, CancellationToken ct = default)
    {
        var access = await WiFiAdapter.RequestAccessAsync();
        if (access != WiFiAccessStatus.Allowed)
            return new ConnectionResult(ConnectionStatus.Error, "WiFi access denied. Check app permissions in Windows Settings.");

        var adapters = await WiFiAdapter.FindAllAdaptersAsync();
        if (adapters.Count == 0)
            return new ConnectionResult(ConnectionStatus.Error, "No WiFi adapter found.");

        var adapter = adapters[0];

        ct.ThrowIfCancellationRequested();
        await adapter.ScanAsync();
        ct.ThrowIfCancellationRequested();

        var network = adapter.NetworkReport.AvailableNetworks
            .FirstOrDefault(n => n.Ssid == creds.Ssid);

        if (network == null)
            return new ConnectionResult(ConnectionStatus.NotFound, $"Network '{creds.Ssid}' not found. Move closer and try again.");

        WiFiConnectionResult result;

        if (creds.SecurityType == WifiSecurityType.Open || string.IsNullOrEmpty(creds.Password))
        {
            result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic);
        }
        else
        {
            var credential = new PasswordCredential { Password = creds.Password };
            result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic, credential);
        }

        return result.ConnectionStatus switch
        {
            WiFiConnectionStatus.Success
                => new ConnectionResult(ConnectionStatus.Connected, $"Connected to {creds.Ssid}"),
            WiFiConnectionStatus.InvalidCredential
                => new ConnectionResult(ConnectionStatus.AuthFailed, "Wrong password."),
            WiFiConnectionStatus.NetworkNotAvailable
                => new ConnectionResult(ConnectionStatus.NotFound, $"Network '{creds.Ssid}' not available."),
            WiFiConnectionStatus.Timeout
                => new ConnectionResult(ConnectionStatus.AuthFailed, "Connection timed out. Move closer and try again."),
            _ => new ConnectionResult(ConnectionStatus.Error, $"Connection failed: {result.ConnectionStatus}")
        };
    }
}
