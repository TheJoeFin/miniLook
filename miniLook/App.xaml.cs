using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

using miniLook.Activation;
using miniLook.Contracts.Services;
using miniLook.Core.Contracts.Services;
using miniLook.Core.Services;
using miniLook.Models;
using miniLook.Notifications;
using miniLook.Services;
using miniLook.ViewModels;
using miniLook.Views;

using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.UI.ViewManagement;

using Microsoft.UI.Xaml.Input;

using WinUIEx;

using Application = Microsoft.UI.Xaml.Application;

namespace miniLook;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private TrayIcon? _calendarTrayIcon;
    private TrayFlyoutWindow? _flyoutWindow;
    private TrayFlyoutWindow? _calendarFlyoutWindow;
    private CalendarPage? _calendarPage;
    private readonly UISettings _uiSettings = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _trayIconRestoreEvent;
    private DispatcherQueueTimer? _restoreEventMonitorTimer;
    private DispatcherQueueTimer? _idleMemoryTimer;
    private CancellationTokenSource? _idleMemoryCancellation;
    private string? _lastTrayIconPath;
    private int _lastBadgeCount = -1;

    private int _lastFocusedUnreadCount = 0;
    private List<Event> _upcomingEvents = [];

    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public static Views.PopOutWindow? PopOutWindow { get; set; }

    public static Dictionary<string, Views.MailItemWindow> MailItemWindows { get; } = [];

    public static Dictionary<string, Views.HtmlViewWindow> HtmlViewWindows { get; } = [];

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            // Other Activation Handlers
            services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();

            // Services
            services.AddTransient<IWebViewService, WebViewService>();
            services.AddSingleton<IAppNotificationService, AppNotificationService>();
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IMailCacheService, MailCacheService>();
            services.AddSingleton<IGraphService, GraphService>();
            services.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();

            // Core Services
            services.AddSingleton<ISampleDataService, SampleDataService>();
            services.AddSingleton<IFileService, FileService>();

            // Views and ViewModels
            services.AddTransient<MailDetailViewModel>();
            services.AddTransient<MailDetailPage>();
            services.AddTransient<RenderWebViewViewModel>();
            services.AddTransient<RenderWebViewPage>();
            services.AddTransient<GraphExplainerViewModel>();
            services.AddTransient<GraphExplainerPage>();
            services.AddTransient<SendMailViewModel>();
            services.AddTransient<SendMailPage>();
            services.AddTransient<WelcomeViewModel>();
            services.AddTransient<WelcomePage>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<CalendarViewModel>();
            services.AddTransient<CalendarPage>();
            services.AddSingleton<ListDetailsViewModel>();
            services.AddTransient<ListDetailsPage>();
            services.AddTransient<ShellViewModel>();
            services.AddTransient<ShellPage>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();

        App.GetService<IAppNotificationService>().Initialize();

        UnhandledException += App_UnhandledException;

        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single-instance enforcement
        const string mutexName = "Global\\miniLook_SingleInstance_Mutex";
        const string restoreEventName = "Global\\miniLook_RestoreTrayIcon_Event";

        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                try
                {
                    using EventWaitHandle restoreEvent = EventWaitHandle.OpenExisting(restoreEventName);
                    Windows.Win32.PInvoke.AllowSetForegroundWindow(uint.MaxValue);
                    restoreEvent.Set();
                }
                catch
                {
                }

                Exit();
                return;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            _trayIconRestoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, restoreEventName);
        }
        catch
        {
        }

        // Initialize tray icons.
        InitializeTrayIcons();

        base.OnLaunched(args);

        // Initialize services (graph auth, theme, etc.) via the activation service.
        // This activates MainWindow briefly for WinUI lifecycle, then we hide it.
        await App.GetService<IActivationService>().ActivateAsync(args);
        MainWindow.Hide();

        await UpdateTrayIconAsync();
        UpdateTrayTooltip();
        UpdateCalendarTrayTooltip();
        StartRestoreEventMonitor();

        await GetService<IBackgroundSyncService>().StartAsync();
    }

    #region Tray Icon

    private void InitializeTrayIcons()
    {
        InitializeMailTrayIcon();
        InitializeCalendarTrayIcon();
    }

    private void InitializeMailTrayIcon()
    {
        if (_trayIcon is not null)
            return;

        _trayIcon = new TrayIcon(0, "Assets/mouseIcon.ico", "miniLook");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
    }

    private void InitializeCalendarTrayIcon()
    {
        if (_calendarTrayIcon is not null)
            return;

        _calendarTrayIcon = new TrayIcon(1, "Assets/Calendar.ico", "miniLook Calendar");
        _calendarTrayIcon.Selected += CalendarTrayIcon_Selected;
        _calendarTrayIcon.ContextMenu += CalendarTrayIcon_ContextMenu;
        _calendarTrayIcon.IsVisible = true;
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
        EnsureFlyout().Toggle();
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        args.Flyout = CreateContextMenu();
    }

    private void CalendarTrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
        EnsureCalendarFlyout().Toggle();
    }

    private void CalendarTrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        args.Flyout = CreateCalendarContextMenu();
    }

    private TrayFlyoutWindow EnsureFlyout()
    {
        if (_flyoutWindow is null && _trayIcon is not null)
        {
            _flyoutWindow = new TrayFlyoutWindow(
                GetService<ShellPage>(),
                _trayIcon,
                "Assets/mouseIcon.ico",
                "miniLook");
            _flyoutWindow.Closed += (_, _) =>
            {
                _flyoutWindow = null;
                NotifyWindowVisibilityChanged();
            };
        }

        return _flyoutWindow ?? throw new InvalidOperationException("The mail tray icon is unavailable.");
    }

    private void ShowFlyout()
    {
        EnsureFlyout().ShowPopup();
    }

    private MenuFlyout CreateContextMenu()
    {
        MenuFlyoutItem checkMailItem = new()
        {
            Command = FindCommand("CheckMailCommand"),
        };
        checkMailItem.Click += (_, _) =>
        {
            ShowFlyout();
        };

        MenuFlyoutItem openOutlookItem = new()
        {
            Command = FindCommand("OpenOutlookCommand"),
        };
        openOutlookItem.Click += (_, _) =>
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://outlook.live.com/mail/0/"));
        };

        MenuFlyoutItem exitItem = new()
        {
            Command = FindCommand("ExitApplicationCommand"),
        };
        exitItem.Click += async (_, _) => await ExitApplicationAsync();

        MenuFlyout flyout = new()
        {
            Items =
            {
                checkMailItem,
                openOutlookItem,
                new MenuFlyoutSeparator(),
                exitItem,
            }
        };

        return flyout;
    }

    private TrayFlyoutWindow EnsureCalendarFlyout()
    {
        if (_calendarFlyoutWindow is null && _calendarTrayIcon is not null)
        {
            _calendarPage = GetService<CalendarPage>();
            _calendarFlyoutWindow = new TrayFlyoutWindow(
                _calendarPage,
                _calendarTrayIcon,
                "Assets/Calendar.ico",
                "miniLook Calendar");
            _calendarFlyoutWindow.Closed += (_, _) =>
            {
                _calendarFlyoutWindow = null;
                _calendarPage = null;
                NotifyWindowVisibilityChanged();
            };
        }

        return _calendarFlyoutWindow
            ?? throw new InvalidOperationException("The calendar tray icon is unavailable.");
    }

    private void ShowCalendarFlyout()
    {
        EnsureCalendarFlyout().ShowPopup();
    }

    private void CreateCalendarEvent()
    {
        EnsureCalendarFlyout();
        _calendarPage?.StartNewEvent();
        ShowCalendarFlyout();
    }

    private MenuFlyout CreateCalendarContextMenu()
    {
        MenuFlyoutItem openItem = new()
        {
            Text = "Open calendar",
            Icon = new FontIcon { Glyph = "\uE787" },
        };
        openItem.Click += (_, _) => ShowCalendarFlyout();

        MenuFlyoutItem createItem = new()
        {
            Text = "New event",
            Icon = new FontIcon { Glyph = "\uE710" },
        };
        createItem.Click += (_, _) => CreateCalendarEvent();

        MenuFlyoutItem outlookItem = new()
        {
            Text = "Open Outlook calendar",
            Icon = new FontIcon { Glyph = "\uE774" },
        };
        outlookItem.Click += (_, _) =>
        {
            _ = Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://outlook.live.com/calendar/0/view/month"));
        };

        MenuFlyoutItem exitItem = new()
        {
            Text = "Exit",
            Icon = new FontIcon { Glyph = "\uE8BB" },
        };
        exitItem.Click += async (_, _) => await ExitApplicationAsync();

        return new MenuFlyout
        {
            Items =
            {
                openItem,
                createItem,
                outlookItem,
                new MenuFlyoutSeparator(),
                exitItem,
            }
        };
    }

    private static XamlUICommand? FindCommand(string key)
    {
        if (Current.Resources.TryGetValue(key, out object? resource) && resource is XamlUICommand cmd)
            return cmd;

        return null;
    }

    private async Task ExitApplicationAsync()
    {
        await GetService<IBackgroundSyncService>().StopAsync();
        _idleMemoryTimer?.Stop();
        _idleMemoryCancellation?.Cancel();
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _restoreEventMonitorTimer?.Stop();
        _flyoutWindow?.CloseWindow();
        _calendarFlyoutWindow?.CloseWindow();
        PopOutWindow?.Close();
        foreach (MailItemWindow window in MailItemWindows.Values.ToArray())
            window.Close();
        foreach (HtmlViewWindow window in HtmlViewWindows.Values.ToArray())
            window.Close();
        _trayIcon?.Dispose();
        _trayIcon = null;
        _calendarTrayIcon?.Dispose();
        _calendarTrayIcon = null;
        _trayIconRestoreEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        MainWindow.Close();
        Exit();
    }

    public static bool HasVisibleMailWindows => Current is App app
        && (app._flyoutWindow?.IsPopupVisible == true || IsVisible(PopOutWindow)
            || MailItemWindows.Values.Any(IsVisible) || HtmlViewWindows.Values.Any(IsVisible));

    public static bool HasVisibleWindows => Current is App app
        && (HasVisibleMailWindows || app._calendarFlyoutWindow?.IsPopupVisible == true);

    private static bool IsVisible(Window? window) => window is not null && window.AppWindow.IsVisible
        && (window.AppWindow.Presenter is not OverlappedPresenter presenter
            || presenter.State != OverlappedPresenterState.Minimized);

    public static void NotifyWindowVisibilityChanged()
    {
        if (Current is not App app)
            return;

        GetService<IBackgroundSyncService>().SetForegroundActive(HasVisibleMailWindows);
        app._idleMemoryTimer?.Stop();
        app._idleMemoryCancellation?.Cancel();
        if (HasVisibleWindows
            || app._flyoutWindow is not null
            || app._calendarFlyoutWindow is not null)
            return;

        app._idleMemoryCancellation?.Dispose();
        app._idleMemoryCancellation = new();
        if (app._idleMemoryTimer is null)
        {
            app._idleMemoryTimer = MainWindow.DispatcherQueue.CreateTimer();
            app._idleMemoryTimer.IsRepeating = false;
            app._idleMemoryTimer.Interval = TimeSpan.FromSeconds(5);
            app._idleMemoryTimer.Tick += (_, _) =>
            {
                if (!HasVisibleWindows
                    && app._flyoutWindow is null
                    && app._calendarFlyoutWindow is null
                    && app._idleMemoryCancellation is not null)
                    MemoryService.ReleaseIdle(app._idleMemoryCancellation.Token);
            };
        }
        app._idleMemoryTimer.Start();
    }

    #endregion

    #region Tray Icon State

    private async Task UpdateTrayIconAsync()
    {
        if (_trayIcon is null)
            return;

        string iconUri;

        iconUri = _lastFocusedUnreadCount > 0
            ? "Assets/email.ico"
            : "Assets/mouseIcon.ico";

        try
        {
            if (_lastTrayIconPath != iconUri)
            {
                _trayIcon.SetIcon(iconUri);
                _lastTrayIconPath = iconUri;
            }
        }
        catch
        {
            _trayIcon.SetIcon("Assets/mouseIcon.ico");
        }

        await Task.CompletedTask;
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon is null)
            return;

        if (_lastFocusedUnreadCount > 0)
            _trayIcon.Tooltip = $"miniLook - {_lastFocusedUnreadCount} unread in Focused";
        else
            _trayIcon.Tooltip = "miniLook - No new mail in Focused";
    }

    private void UpdateCalendarTrayTooltip()
    {
        if (_calendarTrayIcon is null)
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        Event? next = _upcomingEvents
            .Where(item => TryGetEventTime(item, now, out _, out _))
            .OrderBy(item =>
            {
                TryGetEventTime(item, now, out DateTimeOffset start, out _);
                return start;
            })
            .FirstOrDefault();

        if (next is null
            || !TryGetEventTime(next, now, out DateTimeOffset nextStart, out DateTimeOffset nextEnd))
        {
            _calendarTrayIcon.Tooltip = "miniLook Calendar - no upcoming events";
            return;
        }

        string subject = string.IsNullOrWhiteSpace(next.Subject) ? "Untitled event" : next.Subject;
        if (subject.Length > 48)
            subject = $"{subject[..45]}...";

        if (now >= nextStart && now < nextEnd)
        {
            _calendarTrayIcon.Tooltip = $"miniLook Calendar - now: {subject}";
            return;
        }

        string when = next.IsAllDay is true
            ? nextStart.ToString("ddd, MMM d")
            : nextStart.Date == now.Date
                ? nextStart.ToString("h:mm tt")
                : nextStart.ToString("ddd h:mm tt");
        _calendarTrayIcon.Tooltip = $"miniLook Calendar - {when}: {subject}";
    }

    private static bool TryGetEventTime(
        Event calendarEvent,
        DateTimeOffset now,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        start = default;
        end = default;
        if (!DateTimeOffset.TryParse(calendarEvent.Start?.DateTime, out DateTimeOffset parsedStart)
            || !DateTimeOffset.TryParse(calendarEvent.End?.DateTime, out DateTimeOffset parsedEnd))
            return false;

        start = parsedStart.ToLocalTime();
        end = parsedEnd.ToLocalTime();
        return end > now || start > now;
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        if (!MainWindow.DispatcherQueue.TryEnqueue(() => _ = UpdateTrayIconAsync()))
            System.Diagnostics.Debug.WriteLine("[Tray] Could not dispatch a theme update.");
    }

    #endregion

    #region Single Instance Restore

    private void StartRestoreEventMonitor()
    {
        if (_trayIconRestoreEvent is null)
            return;

        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
            return;

        _restoreEventMonitorTimer = dispatcherQueue.CreateTimer();
        _restoreEventMonitorTimer.Interval = TimeSpan.FromSeconds(2);
        _restoreEventMonitorTimer.Tick += async (sender, args) =>
        {
            try
            {
                if (_trayIconRestoreEvent?.WaitOne(0) == true)
                {
                    await EnsureTrayIconVisibleAsync();
                }
            }
            catch
            {
            }
        };
        _restoreEventMonitorTimer.Start();
    }

    private async Task EnsureTrayIconVisibleAsync()
    {
        try
        {
            if (_trayIcon is null)
                InitializeMailTrayIcon();
            else
                _trayIcon.IsVisible = true;
            if (_calendarTrayIcon is null)
                InitializeCalendarTrayIcon();
            else
                _calendarTrayIcon.IsVisible = true;
            await UpdateTrayIconAsync();
            UpdateTrayTooltip();
            UpdateCalendarTrayTooltip();
            ShowFlyout();
        }
        catch
        {
        }
    }

    #endregion

    #region Badge & Tray Sync

    public static void SetUnreadMailCounts(int totalUnreadCount, int focusedUnreadCount)
    {
        if (Current is not App app)
            return;
        if (app._lastBadgeCount != totalUnreadCount)
        {
            // Update taskbar badge
            XmlDocument badgeXml =
                BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

            if (badgeXml.SelectSingleNode("/badge") is not XmlElement badgeElement)
                return;

            badgeElement.SetAttribute("value", totalUnreadCount.ToString());

            BadgeNotification badge = new BadgeNotification(badgeXml);

            BadgeUpdater badgeUpdater =
                BadgeUpdateManager.CreateBadgeUpdaterForApplication();

            badgeUpdater.Update(badge);
            app._lastBadgeCount = totalUnreadCount;
        }

        // Also update tray icon and tooltip
        if (app._lastFocusedUnreadCount != focusedUnreadCount)
        {
            app._lastFocusedUnreadCount = focusedUnreadCount;
            _ = app.UpdateTrayIconAsync();
            app.UpdateTrayTooltip();
        }
    }

    public static void SetUpcomingEvents(IEnumerable<Event> events)
    {
        if (Current is App app)
        {
            app._upcomingEvents = events.ToList();
            app.UpdateCalendarTrayTooltip();
        }
    }

    public static void RefreshTrayState()
    {
        if (Current is App app)
            _ = app.UpdateTrayIconAsync();
    }

    #endregion

    ~App()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _trayIconRestoreEvent?.Dispose();
        }
        catch
        {
        }
    }
}