namespace WifiQrScanner.Models;

public enum WifiSecurityType { WPA2, WEP, Open }

public record WifiCredentials(
    string Ssid,
    string Password,
    WifiSecurityType SecurityType,
    bool IsHidden)
{
    /// <summary>
    /// Parses a WIFI QR code string: WIFI:T:WPA;S:MyNet;P:pass;H:false;;
    /// Returns null if the string is not a valid WIFI QR code.
    /// </summary>
    public static WifiCredentials? Parse(string qrText)
    {
        if (string.IsNullOrWhiteSpace(qrText) || !qrText.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
            return null;

        var data = qrText[5..]; // strip "WIFI:"
        var fields = SplitFields(data);

        var ssid = fields.GetValueOrDefault("S", "");
        var password = fields.GetValueOrDefault("P", "");
        var typeStr = fields.GetValueOrDefault("T", "").ToUpperInvariant();
        var hiddenStr = fields.GetValueOrDefault("H", "false").ToLowerInvariant();

        if (string.IsNullOrEmpty(ssid))
            return null;

        var securityType = typeStr switch
        {
            "WPA" or "WPA2" => WifiSecurityType.WPA2,
            "WEP"           => WifiSecurityType.WEP,
            _               => WifiSecurityType.Open
        };

        var isHidden = hiddenStr == "true";

        return new WifiCredentials(ssid, password, securityType, isHidden);
    }

    /// <summary>
    /// Splits WIFI QR field string into key-value pairs, respecting backslash escapes.
    /// </summary>
    private static Dictionary<string, string> SplitFields(string data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i < data.Length)
        {
            // Read key up to ':'
            var key = ReadToken(data, ref i, ':');
            if (string.IsNullOrEmpty(key)) break;

            // Read value up to ';'
            var value = ReadToken(data, ref i, ';');
            result[key] = value;
        }

        return result;
    }

    private static string ReadToken(string data, ref int i, char delimiter)
    {
        var sb = new System.Text.StringBuilder();
        while (i < data.Length)
        {
            var c = data[i];
            if (c == '\\' && i + 1 < data.Length)
            {
                // Escaped character
                sb.Append(data[i + 1]);
                i += 2;
            }
            else if (c == delimiter)
            {
                i++; // consume delimiter
                break;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }
}
