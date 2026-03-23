using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using WifiQrScanner.Helpers;
using WifiQrScanner.Models;
using WifiQrScanner.Services;

namespace WifiQrScanner.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CameraService _camera = new();
    private readonly QrDecoderService _decoder = new();
    private readonly WifiConnectionService _wifi = new();
    private CancellationTokenSource? _connectCts;
    private int _framePending; // 1 = UI thread already has a frame queued
    private readonly Windows.Networking.Connectivity.NetworkStatusChangedEventHandler _networkStatusChanged;

    // ── Bindable Properties ────────────────────────────────────────────────

    private WriteableBitmap? _frame;
    public WriteableBitmap? Frame { get => _frame; private set => Set(ref _frame, value); }

    private string _statusText = "Point camera at a WiFi QR code";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private string _detectedSsid = "";
    public string DetectedSsid { get => _detectedSsid; private set => Set(ref _detectedSsid, value); }

    private string _detectedPassword = "";
    public string DetectedPassword { get => _detectedPassword; private set => Set(ref _detectedPassword, value); }

    private bool _isConnecting;
    public bool IsConnecting { get => _isConnecting; private set => Set(ref _isConnecting, value); }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }

    private bool _isError;
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    private bool _noCameraFound;
    public bool NoCameraFound { get => _noCameraFound; private set => Set(ref _noCameraFound, value); }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    private bool _hasCurrentNetwork;
    public bool HasCurrentNetwork { get => _hasCurrentNetwork; private set => Set(ref _hasCurrentNetwork, value); }

    private string _currentNetworkSsid = "";
    public string CurrentNetworkSsid { get => _currentNetworkSsid; private set => Set(ref _currentNetworkSsid, value); }

    private string _sharePassword = "";
    public string SharePassword
    {
        get => _sharePassword;
        set
        {
            if (_sharePassword == value) return;
            _sharePassword = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SharePassword)));
            UpdateShareQr();
        }
    }

    private WriteableBitmap? _shareQrBitmap;
    public WriteableBitmap? ShareQrBitmap { get => _shareQrBitmap; private set => Set(ref _shareQrBitmap, value); }

    // ── Init ───────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _decoder.WifiQrDetected += OnWifiQrDetected;
        _camera.FrameReady += OnFrameReady;
        _camera.CameraReady += OnCameraReady;

        _camera.Start();

        _networkStatusChanged = _ => RefreshCurrentNetwork();
        Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += _networkStatusChanged;
        RefreshCurrentNetwork();
    }

    private void RefreshCurrentNetwork()
    {
        Task.Run(async () =>
        {
            var network = CurrentNetworkService.GetCurrentNetwork();

            // WinRT network APIs can briefly return null at startup — retry once
            if (network == null)
            {
                await Task.Delay(2000);
                network = CurrentNetworkService.GetCurrentNetwork();
            }

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (network != null)
                {
                    CurrentNetworkSsid = network.Ssid;
                    SharePassword = network.Password;
                    HasCurrentNetwork = true;
                }
                else
                {
                    HasCurrentNetwork = false;
                    CurrentNetworkSsid = "";
                    SharePassword = "";
                }
            });
        });
    }

    private void UpdateShareQr()
    {
        if (string.IsNullOrEmpty(CurrentNetworkSsid)) return;
        var secType = string.IsNullOrEmpty(_sharePassword)
            ? WifiSecurityType.Open
            : WifiSecurityType.WPA2;
        ShareQrBitmap = QrGeneratorService.Generate(CurrentNetworkSsid, _sharePassword, secType);
    }

    // ── Camera Ready ──────────────────────────────────────────────────────

    private void OnCameraReady(bool success)
    {
        if (!success)
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsLoading = false;
                NoCameraFound = true;
                StatusText = "No camera found. Check your device.";
            });
    }

    // ── Camera Frame → UI ──────────────────────────────────────────────────

    private void OnFrameReady(Mat frame)
    {
        if (IsLoading)
            Application.Current.Dispatcher.BeginInvoke(() => IsLoading = false);

        // Decode on camera thread using downscaled copy (faster ZXing)
        if (!IsConnecting && !IsConnected)
            _decoder.ProcessFrame(frame);

        // Drop frame if UI thread hasn't rendered the previous one yet
        if (System.Threading.Interlocked.CompareExchange(ref _framePending, 1, 0) == 1)
        {
            frame.Dispose();
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Frame = MatToBitmapConverter.ToBitmap(frame);
            frame.Dispose();
            System.Threading.Interlocked.Exchange(ref _framePending, 0);
        });
    }

    // ── QR Detected ───────────────────────────────────────────────────────

    private void OnWifiQrDetected(WifiCredentials creds)
    {
        if (IsConnecting || IsConnected) return;

        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            IsConnecting = true;
            IsError = false;
            IsConnected = false;
            DetectedSsid = creds.Ssid;
            DetectedPassword = creds.Password;
            StatusText = $"Connecting to \"{creds.Ssid}\"...";

            _connectCts = new CancellationTokenSource();

            var result = await _wifi.ConnectAsync(creds, _connectCts.Token);

            IsConnecting = false;

            switch (result.Status)
            {
                case ConnectionStatus.Connected:
                    IsConnected = true;
                    StatusText = $"Connected to \"{creds.Ssid}\"";
                    break;

                case ConnectionStatus.AuthFailed:
                    IsError = true;
                    StatusText = result.Message;
                    _decoder.ResetDebounce();
                    break;

                default:
                    IsError = true;
                    StatusText = result.Message;
                    _decoder.ResetDebounce();
                    break;
            }
        });
    }

    public void RetryCamera()
    {
        NoCameraFound = false;
        IsLoading = true;
        _camera.Start();
    }

    public void RetryScanning()
    {
        _connectCts?.Cancel();
        IsConnecting = false;
        IsConnected = false;
        IsError = false;
        DetectedSsid = "";
        DetectedPassword = "";
        StatusText = "Point camera at a WiFi QR code";
        _decoder.ResetDebounce();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= _networkStatusChanged;
        _connectCts?.Cancel();
        _camera.Dispose();
    }
}
