using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WifiQrScanner.ViewModels;

namespace WifiQrScanner.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private bool _passwordVisible = false;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableDarkTitleBar();

        try
        {
            _vm = new MainViewModel();
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int value = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, Marshal.SizeOf(value));
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;

        switch (e.PropertyName)
        {
            case nameof(_vm.Frame):
                CameraImage.Source = _vm.Frame;
                break;

            case nameof(_vm.IsLoading):
                if (!_vm.IsLoading) FadeOutLoading();
                break;

            case nameof(_vm.NoCameraFound):
                NoCameraPanel.Visibility = _vm.NoCameraFound ? Visibility.Visible : Visibility.Collapsed;
                ScanGuide.Visibility     = _vm.NoCameraFound ? Visibility.Collapsed : Visibility.Visible;
                if (_vm.NoCameraFound) FadeOutLoading();
                break;

            case nameof(_vm.DetectedSsid):
                SsidText.Text = _vm.DetectedSsid;
                break;

            case nameof(_vm.DetectedPassword):
                // Reset password visibility state on each new connection
                _passwordVisible = false;
                TogglePasswordBtn.Content = "\uE7B3"; // eye icon
                UpdatePasswordDisplay();
                break;

            case nameof(_vm.StatusText):
                StatusLabel.Text = _vm.StatusText;
                break;

            case nameof(_vm.IsConnecting):
                ConnectingBar.Visibility = _vm.IsConnecting ? Visibility.Visible : Visibility.Collapsed;
                ScanGuide.Visibility     = (_vm.IsConnecting || _vm.NoCameraFound) ? Visibility.Collapsed : Visibility.Visible;
                UpdateRetryButton();
                break;

            case nameof(_vm.IsConnected):
                StatusLabel.Foreground = _vm.IsConnected
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                    : new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                UpdateNetworkInfoRow();
                UpdateRetryButton();
                break;

            case nameof(_vm.IsError):
                StatusLabel.Foreground = _vm.IsError
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                UpdateNetworkInfoRow();
                UpdateRetryButton();
                break;
        }
    }

    private void FadeOutLoading()
    {
        if (LoadingPanel.Visibility == Visibility.Collapsed) return;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) => LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateNetworkInfoRow()
    {
        if (_vm == null) return;

        var showRow = _vm.IsConnected || _vm.IsError || _vm.IsConnecting;
        NetworkInfoRow.Visibility = showRow ? Visibility.Visible : Visibility.Collapsed;

        // Only show password controls when connected and there's a password
        var hasPassword = !string.IsNullOrEmpty(_vm.DetectedPassword);
        PasswordBadge.Visibility      = (_vm.IsConnected && hasPassword) ? Visibility.Visible : Visibility.Collapsed;
        TogglePasswordBtn.Visibility  = (_vm.IsConnected && hasPassword) ? Visibility.Visible : Visibility.Collapsed;
        CopyPasswordBtn.Visibility    = (_vm.IsConnected && hasPassword) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePasswordDisplay()
    {
        if (_vm == null) return;
        var pwd = _vm.DetectedPassword;
        PasswordText.Text = _passwordVisible ? pwd : new string('•', pwd.Length);
    }

    private void UpdateRetryButton()
    {
        if (_vm == null) return;
        RetryButton.Visibility = (_vm.IsConnected || _vm.IsError)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        // Eye open: E7B3 — Eye with slash (hide): ED1A
        TogglePasswordBtn.Content = _passwordVisible ? "\uED1A" : "\uE7B3";
        ((System.Windows.Controls.ToolTip)TogglePasswordBtn.ToolTip).Content =
            _passwordVisible ? "Hide password" : "Show password";
        UpdatePasswordDisplay();
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null || string.IsNullOrEmpty(_vm.DetectedPassword)) return;
        Clipboard.SetText(_vm.DetectedPassword);

        // Flash checkmark icon then revert to copy icon
        CopyPasswordBtn.Content = "\uE73E"; // Checkmark glyph
        CopyPasswordBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        timer.Tick += (_, _) =>
        {
            CopyPasswordBtn.Content = "\uE8C8"; // Copy glyph
            CopyPasswordBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            timer.Stop();
        };
        timer.Start();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
        ScanGuide.Visibility = Visibility.Visible;
        NetworkInfoRow.Visibility = Visibility.Collapsed;
        _vm.RetryScanning();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _vm?.Dispose();
    }
}
