using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WifiQrScanner.Models;
using ZXing;
using ZXing.Common;

namespace WifiQrScanner.Services;

public static class QrGeneratorService
{
    public static WriteableBitmap Generate(string ssid, string password, WifiSecurityType secType, int size = 190)
    {
        var secStr = secType switch
        {
            WifiSecurityType.WPA2 => "WPA",
            WifiSecurityType.WEP  => "WEP",
            _                     => "nopass"
        };

        var content = string.IsNullOrEmpty(password)
            ? $"WIFI:T:nopass;S:{Escape(ssid)};;"
            : $"WIFI:T:{secStr};S:{Escape(ssid)};P:{Escape(password)};;";

        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 1 }
        };

        var pixelData = writer.Write(content);
        var bitmap = new WriteableBitmap(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, pixelData.Width, pixelData.Height), pixelData.Pixels, pixelData.Width * 4, 0);
        return bitmap;
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\"", "\\\"");
}
