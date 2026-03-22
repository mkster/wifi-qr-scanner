using WifiQrScanner.Models;
using ZXing;
using ZXing.Common;

namespace WifiQrScanner.Services;

public class QrDecoderService
{
    private readonly BarcodeReaderGeneric _reader;
    private int _frameCounter;
    private string? _lastDecodedText;
    private DateTime _lastDecodeTime = DateTime.MinValue;
    private byte[]? _decodeBuffer;         // reused each decode — avoids per-frame alloc
    private const int DecodeEveryNFrames = 1;
    private const int DebounceSeconds = 5;

    public event Action<WifiCredentials>? WifiQrDetected;

    public QrDecoderService()
    {
        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true,
                TryInverted = true
            }
        };

    }

    /// <summary>Called on camera thread for each frame.</summary>
    public void ProcessFrame(OpenCvSharp.Mat frame)
    {
        if (++_frameCounter % DecodeEveryNFrames != 0)
            return;

        try
        {
            var width      = frame.Width;
            var height     = frame.Height;
            var bufferSize = width * height * 3;

            if (_decodeBuffer == null || _decodeBuffer.Length != bufferSize)
                _decodeBuffer = new byte[bufferSize];

            System.Runtime.InteropServices.Marshal.Copy(frame.Data, _decodeBuffer, 0, bufferSize);

            var result = _reader.Decode(_decodeBuffer, width, height, RGBLuminanceSource.BitmapFormat.BGR24);
            if (result?.Text == null) return;

            var text = result.Text;
            var now  = DateTime.UtcNow;

            if (text == _lastDecodedText && (now - _lastDecodeTime).TotalSeconds < DebounceSeconds)
                return;

            _lastDecodedText = text;
            _lastDecodeTime  = now;

            var creds = WifiCredentials.Parse(text);
            if (creds != null)
                WifiQrDetected?.Invoke(creds);
        }
        catch
        {
            // Swallow decode errors silently
        }
    }

    public void ResetDebounce()
    {
        _lastDecodedText = null;
        _lastDecodeTime  = DateTime.MinValue;
    }
}
