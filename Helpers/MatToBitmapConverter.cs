using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace WifiQrScanner.Helpers;

public static class MatToBitmapConverter
{
    private static WriteableBitmap? _bitmap;

    /// <summary>
    /// Writes an OpenCV BGR Mat into a reused WriteableBitmap.
    /// Must be called on the UI thread.
    /// </summary>
    public static WriteableBitmap ToBitmap(Mat frame)
    {
        var width = frame.Width;
        var height = frame.Height;

        if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);

        _bitmap.Lock();
        try
        {
            var stride = width * 3;
            var size = height * stride;
            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), frame.Data, size, stride);
        }
        finally
        {
            _bitmap.Unlock();
        }

        return _bitmap;
    }
}
