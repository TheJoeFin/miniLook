using System.Diagnostics;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

using miniLook.Controls;
using miniLook.Services;

using Windows.Graphics;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

using WinRT.Interop;

using WinUIEx;

namespace miniLook.Views;

public sealed partial class TrayFlyoutWindow : WindowEx
{
    private const int PopupWidth = 400;
    private const int PopupHeight = 600;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int ShowSlideDistance = 24;
    private const int HideSlideDistance = 12;
    private const double ShowDurationMs = 250;
    private const double HideDurationMs = 180;
    private static readonly TimeSpan RecentDismissWindow = TimeSpan.FromMilliseconds(300);

    private readonly HWND _hwnd;
    private readonly ShellPage _page;
    private readonly ShellBackdrop _backdrop = new();
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _closeTimer;
    private readonly DispatcherQueueTimer _animationTimer;
    private readonly Stopwatch _animationClock = new();

    private bool _isShowing;
    private bool _isPopupVisible;
    private bool _allowClose;
    private bool _isClosed;
    private bool _isHiding;
    private DateTime _lastDismissedAtUtc = DateTime.MinValue;
    private int _baseX;
    private int _baseY;
    private int _fromOffset;
    private int _toOffset;
    private double _fromOpacity;
    private double _toOpacity;
    private double _durationMs = ShowDurationMs;
    private int _currentOffset;
    private double _currentOpacity;

    public TrayFlyoutWindow(ShellPage page)
    {
        _page = page;
        InitializeComponent();
        _hwnd = (HWND)WindowNative.GetWindowHandle(this);
        PageHost.Content = page;
        SystemBackdrop = _backdrop;
        ApplyShellTheme();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "mouseIcon.ico"));
        ConfigureWindowChrome();

        Activated += OnWindowActivated;
        AppWindow.Closing += OnAppWindowClosing;

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(150);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideIfFocusLeftWindow();

        _closeTimer = DispatcherQueue.CreateTimer();
        _closeTimer.Interval = TimeSpan.FromMinutes(1);
        _closeTimer.IsRepeating = false;
        _closeTimer.Tick += (_, _) =>
        {
            if (!_isPopupVisible)
                CloseWindow();
        };

        _animationTimer = DispatcherQueue.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(15);
        _animationTimer.IsRepeating = true;
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    public bool IsPopupVisible => _isPopupVisible;

    public void Toggle()
    {
        if (_isPopupVisible)
        {
            HidePopup();
            return;
        }

        // The taskbar click that light-dismissed us must not immediately reopen us.
        if (DateTime.UtcNow - _lastDismissedAtUtc < RecentDismissWindow)
            return;

        ShowPopup();
    }

    public void ShowPopup()
    {
        WindowPlacementService.PositionNearTray(this, PopupWidth, PopupHeight);
        _baseX = AppWindow.Position.X;
        _baseY = AppWindow.Position.Y;
        _hideTimer.Stop();
        _closeTimer.Stop();
        _isShowing = true;
        if (!_isPopupVisible)
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        _isPopupVisible = true;

        ApplyShellTheme();
        PlayShowAnimation();
        this.Show();
        Activate();
        if (!PInvoke.SetForegroundWindow(_hwnd))
            Debug.WriteLine("Windows did not grant foreground activation to the tray flyout.");
        _page.OnShown();
    }

    public void HidePopup()
    {
        _hideTimer.Stop();
        _isShowing = false;
        if (!_isPopupVisible)
            return;

        _isPopupVisible = false;
        _lastDismissedAtUtc = DateTime.UtcNow;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _page.OnHidden();
        PlayHideAnimation();
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    public void CloseWindow()
    {
        if (_isClosed)
            return;

        _isClosed = true;
        _hideTimer.Stop();
        _closeTimer.Stop();
        _animationTimer.Stop();
        if (_isPopupVisible)
            _page.OnHidden();
        _isPopupVisible = false;
        _page.OnClosed();
        PageHost.Content = null;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        Activated -= OnWindowActivated;
        AppWindow.Closing -= OnAppWindowClosing;
        _allowClose = true;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
            HidePopup();
        }
    }

    private void OnColorValuesChanged(UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed && _isPopupVisible)
                ApplyShellTheme();
        });

    private void ApplyShellTheme()
    {
        bool isLight = SystemThemeService.IsLight;
        PopupRoot.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;
        _backdrop.Update(isLight, SystemThemeService.ShowsAccentColor
            ? _uiSettings.GetColorValue(UIColorType.AccentDark2) : null);
    }

    private void PlayShowAnimation()
    {
        _isHiding = false;
        _durationMs = ShowDurationMs;
        _fromOffset = ScaleForDpi(ShowSlideDistance);
        _toOffset = 0;
        _fromOpacity = 0;
        _toOpacity = 1;
        ApplyFrame(_fromOffset, 0);
        _animationClock.Restart();
        _animationTimer.Start();
    }

    private void PlayHideAnimation()
    {
        _isHiding = true;
        _durationMs = HideDurationMs;
        _fromOffset = _currentOffset;
        _toOffset = ScaleForDpi(HideSlideDistance);
        _fromOpacity = _currentOpacity;
        _toOpacity = 0;
        _animationClock.Restart();
        _animationTimer.Start();
    }

    private void AnimationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        double progress = Math.Min(1, _animationClock.Elapsed.TotalMilliseconds / _durationMs);
        double eased = _isHiding ? progress * progress * progress : 1 - Math.Pow(1 - progress, 3);
        int offset = _fromOffset + (int)Math.Round((_toOffset - _fromOffset) * eased);
        double opacity = _fromOpacity + ((_toOpacity - _fromOpacity) * progress);
        ApplyFrame(offset, opacity);
        if (progress < 1)
            return;

        _animationTimer.Stop();
        _animationClock.Reset();
        if (_isHiding)
            this.Hide();
    }

    private void ApplyFrame(int offset, double opacity)
    {
        _currentOffset = offset;
        _currentOpacity = opacity;
        byte alpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        if (!PInvoke.SetLayeredWindowAttributes(_hwnd, new COLORREF(0), alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA))
            Debug.WriteLine("Could not update the tray flyout animation opacity.");
        AppWindow.Move(new PointInt32(_baseX, _baseY + offset));
    }

    private int ScaleForDpi(int logical)
    {
        uint dpi = PInvoke.GetDpiForWindow(_hwnd);
        return (int)Math.Round(logical * (dpi == 0 ? 96 : dpi) / 96.0);
    }

    private void ConfigureWindowChrome()
    {
        OverlappedPresenter presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        unsafe
        {
            DWM_WINDOW_CORNER_PREFERENCE corner = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            if (PInvoke.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                &corner, sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Failed)
                Debug.WriteLine("Rounded tray flyout corners are unavailable on this Windows version.");
        }

        int exStyle = PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _isShowing = false;
            _hideTimer.Stop();
            return;
        }

        if (_isShowing || !_isPopupVisible)
            return;

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideIfFocusLeftWindow()
    {
        _hideTimer.Stop();
        HWND foreground = PInvoke.GetForegroundWindow();
        if (_isPopupVisible && foreground != _hwnd
            && (foreground == HWND.Null
                || PInvoke.GetAncestor(foreground, GET_ANCESTOR_FLAGS.GA_ROOTOWNER) != _hwnd))
            HidePopup();
    }

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        HidePopup();
    }
}