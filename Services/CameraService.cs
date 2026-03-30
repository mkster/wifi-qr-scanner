using System.IO;
using System.Text;
using OpenCvSharp;
using Windows.Devices.Enumeration;

namespace WifiQrScanner.Services;

public class CameraService : IDisposable
{
    private VideoCapture? _capture;
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    public event Action<Mat>? FrameReady;
    /// <summary>Fired when camera open attempt completes. errorMessage=null means success.</summary>
    public event Action<string?, string>? CameraReady; // (errorMessage, diagnostics)

    public bool IsRunning => _running;

    /// <summary>Returns immediately. Camera opens on the background thread.</summary>
    public void Start(int cameraIndex = 0)
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(() => CaptureLoop(cameraIndex))
        {
            IsBackground = true,
            Name = "CameraCapture"
        };
        _thread.Start();
    }

    public void Stop(bool waitForRelease = false)
    {
        _running = false;
        if (waitForRelease)
        {
            _thread?.Join(2000);
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }
        // On app exit: skip join/release — OS cleans up the camera handle instantly
    }

    private void CaptureLoop(int cameraIndex)
    {
        var diag = new StringBuilder();
        diag.AppendLine("=== WiFi QR Scanner Diagnostics ===");
        diag.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        diag.AppendLine($"OS: {Environment.OSVersion}");
        diag.AppendLine();

        // Enumerate cameras
        try
        {
            var devices = DeviceInformation
                .FindAllAsync(DeviceClass.VideoCapture)
                .AsTask().GetAwaiter().GetResult();

            diag.AppendLine($"Detected cameras ({devices.Count}):");
            if (devices.Count == 0)
            {
                diag.AppendLine("  (none)");
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                    diag.AppendLine($"  [{i}] {devices[i].Name}");
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"Camera enumeration failed: {ex.Message}");
        }

        diag.AppendLine();
        diag.AppendLine("Open attempts:");

        var backends = new[] { VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW };

        // Try all detected cameras, falling back to indices 0–2 if enumeration failed
        int[] indices;
        try
        {
            var devices = DeviceInformation
                .FindAllAsync(DeviceClass.VideoCapture)
                .AsTask().GetAwaiter().GetResult();
            indices = devices.Count > 0
                ? Enumerable.Range(0, devices.Count).ToArray()
                : new[] { 0, 1, 2 };
        }
        catch
        {
            indices = new[] { 0, 1, 2 };
        }
        const int maxAttempts = 2;
        const int retryDelayMs = 2000;

        VideoCapture? opened = null;

        for (int attempt = 0; attempt < maxAttempts && opened == null; attempt++)
        {
            if (attempt > 0)
            {
                diag.AppendLine($"  [retrying after {retryDelayMs}ms delay]");
                Thread.Sleep(retryDelayMs);

                // MediaCapture pre-flight on retry only — adds latency so skip on first attempt.
                // If this succeeds it confirms Windows can access the camera at all, and may
                // also prime the camera stack so the subsequent MSMF attempt succeeds.
                diag.AppendLine("  [MediaCapture pre-flight]");
                try
                {
                    var mc = new Windows.Media.Capture.MediaCapture();
                    mc.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
                    {
                        StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video
                    }).AsTask().GetAwaiter().GetResult();
                    mc.Dispose();
                    diag.AppendLine("  MediaCapture → OK (camera accessible via WinRT)");
                }
                catch (Exception ex)
                {
                    diag.AppendLine($"  MediaCapture → Failed: {ex.Message}");
                }
            }

            foreach (var idx in indices)
            {
                foreach (var backend in backends)
                {
                    var b = backend; // capture for lambda
                    var i = idx;
                    var task = Task.Run(() => new VideoCapture(i, b));
                    bool timedOut = !task.Wait(TimeSpan.FromSeconds(8));

                    if (timedOut)
                    {
                        diag.AppendLine($"  Index {idx}, {backend} → Timed out");
                        if (task.IsCompleted) task.Result.Dispose();
                    }
                    else if (task.Result.IsOpened())
                    {
                        diag.AppendLine($"  Index {idx}, {backend} → Opened");
                        opened = task.Result;
                        break;
                    }
                    else
                    {
                        diag.AppendLine($"  Index {idx}, {backend} → Failed");
                        task.Result.Dispose();
                    }
                }
                if (opened != null) break;
            }
        }

        diag.AppendLine();

        if (opened == null)
        {
            diag.AppendLine("Result: No camera could be opened.");
            _running = false;
            PersistDiagnostics(diag.ToString());
            CameraReady?.Invoke(BuildErrorMessage(diag), diag.ToString());
            return;
        }

        diag.AppendLine("Result: Camera opened successfully.");
        PersistDiagnostics(diag.ToString());
        _capture = opened;
        _capture.Set(VideoCaptureProperties.FrameWidth, 1280);
        _capture.Set(VideoCaptureProperties.FrameHeight, 720);
        _capture.Set(VideoCaptureProperties.Fps, 30);

        CameraReady?.Invoke(null, diag.ToString());

        using var frame = new Mat();
        while (_running)
        {
            if (_capture == null || !_capture.Read(frame) || frame.Empty())
            {
                Thread.Sleep(33); // only sleep on failure
                continue;
            }

            // Read() already blocks until next frame — no sleep needed
            FrameReady?.Invoke(frame.Clone());
        }
    }

    private static void PersistDiagnostics(string content)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WifiQrScanner");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "diagnostics.txt"), content);
        }
        catch { /* best-effort */ }
    }

    private static string BuildErrorMessage(StringBuilder diag)
    {
        // Check if any cameras were detected from the diag output
        bool noCameras = diag.ToString().Contains("(none)");
        if (noCameras)
            return "No webcam was detected on this device.";

        return "Camera found but could not be opened. Close any other app using the camera and try again.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop(waitForRelease: false);
    }
}
