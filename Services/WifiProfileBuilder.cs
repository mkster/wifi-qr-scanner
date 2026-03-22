using System.Xml.Linq;
using WifiQrScanner.Models;

namespace WifiQrScanner.Services;

public static class WifiProfileBuilder
{
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

    public static string BuildProfileXml(WifiCredentials creds)
    {
        var profile = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "WLANProfile",
                new XElement(Ns + "name", creds.Ssid),
                new XElement(Ns + "SSIDConfig",
                    new XElement(Ns + "SSID",
                        new XElement(Ns + "name", creds.Ssid)
                    ),
                    new XElement(Ns + "nonBroadcast", creds.IsHidden.ToString().ToLower())
                ),
                new XElement(Ns + "connectionType", "ESS"),
                new XElement(Ns + "connectionMode", "auto"),
                new XElement(Ns + "MSM",
                    new XElement(Ns + "security",
                        BuildAuthEncryption(creds),
                        BuildSharedKey(creds)
                    )
                )
            )
        );

        return profile.Declaration + Environment.NewLine + profile.ToString();
    }

    private static XElement BuildAuthEncryption(WifiCredentials creds)
    {
        var (auth, enc) = creds.SecurityType switch
        {
            WifiSecurityType.WPA2 => ("WPA2PSK", "AES"),
            WifiSecurityType.WEP  => ("open", "WEP"),
            _                     => ("open", "none")
        };

        return new XElement(Ns + "authEncryption",
            new XElement(Ns + "authentication", auth),
            new XElement(Ns + "encryption", enc),
            new XElement(Ns + "useOneX", "false")
        );
    }

    private static XElement? BuildSharedKey(WifiCredentials creds)
    {
        if (creds.SecurityType == WifiSecurityType.Open || string.IsNullOrEmpty(creds.Password))
            return null;

        var keyType = creds.SecurityType == WifiSecurityType.WEP ? "networkKey" : "passPhrase";

        return new XElement(Ns + "sharedKey",
            new XElement(Ns + "keyType", keyType),
            new XElement(Ns + "protected", "false"),
            new XElement(Ns + "keyMaterial", creds.Password)
        );
    }
}
