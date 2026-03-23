using OpenCvSharp;

namespace WifiQrScanner.Services;

public class CameraService : IDisposable
{
    private VideoCapture? _capture;
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    public event Action<Mat>? FrameReady;
    public event Action<bool>? CameraReady; // true = opened, false = failed

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
        var task = Task.Run(() => new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY));
        if (!task.Wait(TimeSpan.FromSeconds(8)) || !task.Result.IsOpened())
        {
            if (task.IsCompleted) task.Result.Dispose();
            _running = false;
            CameraReady?.Invoke(false);
            return;
        }

        _capture = task.Result;
        _capture.Set(VideoCaptureProperties.FrameWidth, 1280);
        _capture.Set(VideoCaptureProperties.FrameHeight, 720);
        _capture.Set(VideoCaptureProperties.Fps, 30);

        CameraReady?.Invoke(true);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop(waitForRelease: false);
    }
}
