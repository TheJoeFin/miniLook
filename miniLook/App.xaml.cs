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
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.ViewManagement;

using Microsoft.UI.Xaml.Input;

using WinUIEx;

using Application = Microsoft.UI.Xaml.Application;

namespace miniLook;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private readonly UISettings _uiSettings = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _trayIconRestoreEvent;
    private DispatcherQueueTimer? _restoreEventMonitorTimer;

    private int _lastUnreadCount = 0;
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

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();

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

        // Initialize tray icon
        InitializeTrayIcon();

        base.OnLaunched(args);

        // Initialize services (graph auth, theme, etc.) via the activation service.
        // This activates MainWindow briefly for WinUI lifecycle, then we hide it.
        await App.GetService<IActivationService>().ActivateAsync(args);
        MainWindow.Hide();

        await UpdateTrayIconAsync();
        UpdateTrayTooltip();
        StartRestoreEventMonitor();

        GetService<IBackgroundSyncService>().Start();
    }

    #region Tray Icon

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon(0, "Assets/mouseIcon.ico", "miniLook");
        _trayIcon.Selected += TrayIcon_Selected;
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
    }

    private void TrayIcon_Selected(TrayIcon sender, TrayIconEventArgs args)
    {
        args.Flyout = CreateAppFlyout();
    }

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        args.Flyout = CreateContextMenu();
    }

    private Flyout CreateAppFlyout()
    {
        ShellPage shellPage = GetService<ShellPage>();

        Flyout flyout = new()
        {
            Content = shellPage,
            FlyoutPresenterStyle = CreateNoPaddingStyle(),
        };

        flyout.Opened += (s, e) =>
        {
            INavigationService navService = GetService<INavigationService>();
            IGraphService graphService = GetService<IGraphService>();
            if (graphService.IsAuthenticated)
                navService.NavigateTo(typeof(ListDetailsViewModel).FullName!);
            else
                navService.NavigateTo(typeof(WelcomeViewModel).FullName!);
        };

        flyout.Closing += (s, e) =>
        {
            if (s is Flyout f)
                f.Content = null;
        };

        return flyout;
    }

    private static Style CreateNoPaddingStyle()
    {
        Style style = new(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
        return style;
    }

    private MenuFlyout CreateContextMenu()
    {
        MenuFlyoutItem checkMailItem = new()
        {
            Command = FindCommand("CheckMailCommand"),
        };
        checkMailItem.Click += (_, _) =>
        {
            // Open the flyout to trigger a mail check via navigation
            // The tray icon Selected event will handle showing the UI
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
        exitItem.Click += (_, _) => ExitApplication();

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

    private static XamlUICommand? FindCommand(string key)
    {
        if (Current.Resources.TryGetValue(key, out object? resource) && resource is XamlUICommand cmd)
            return cmd;

        return null;
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        MainWindow.Close();
    }

    #endregion

    #region Tray Icon State

    private async Task UpdateTrayIconAsync()
    {
        if (_trayIcon is null)
            return;

        bool isDark = IsSystemInDarkMode();
        string iconUri;

        if (_lastUnreadCount > 0)
        {
            iconUri = "Assets/email.ico";
        }
        else if (HasImminentEvent())
        {
            iconUri = "Assets/Calendar.ico";
        }
        else
        {
            iconUri = "Assets/mouseIcon.ico";
        }

        try
        {
            _trayIcon.SetIcon(iconUri);
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

        if (_lastUnreadCount > 0)
            _trayIcon.Tooltip = $"miniLook - {_lastUnreadCount} unread";
        else
            _trayIcon.Tooltip = "miniLook - No new mail";
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            UISettings uiSettings = new();
            Color foregroundColor = uiSettings.GetColorValue(UIColorType.Foreground);
            return (foregroundColor.R + foregroundColor.G + foregroundColor.B) > 384;
        }
        catch
        {
            return true;
        }
    }

    private bool HasImminentEvent()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        return _upcomingEvents.Any(ev =>
        {
            if (ev.IsAllDay is true)
                return false;

            if (!DateTimeOffset.TryParse(ev.Start?.DateTime, out DateTimeOffset start)
                || !DateTimeOffset.TryParse(ev.End?.DateTime, out DateTimeOffset end))
                return false;

            // Skip multi-day events (spans more than 24 hours)
            if ((end - start).TotalHours >= 24)
                return false;

            TimeSpan untilStart = start - now;
            return untilStart > TimeSpan.Zero && untilStart <= TimeSpan.FromMinutes(10);
        });
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        _ = UpdateTrayIconAsync();
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
            InitializeTrayIcon();
            await UpdateTrayIconAsync();
            UpdateTrayTooltip();
        }
        catch
        {
        }
    }

    #endregion

    #region Badge & Tray Sync

    public static void SetTaskbarBadgeToNumber(int number)
    {
        // Update taskbar badge
        XmlDocument badgeXml =
            BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

        if (badgeXml.SelectSingleNode("/badge") is not XmlElement badgeElement)
            return;

        badgeElement.SetAttribute("value", number.ToString());

        BadgeNotification badge = new BadgeNotification(badgeXml);

        BadgeUpdater badgeUpdater =
            BadgeUpdateManager.CreateBadgeUpdaterForApplication();

        badgeUpdater.Update(badge);

        // Also update tray icon and tooltip
        if (Current is App app)
        {
            app._lastUnreadCount = number;
            _ = app.UpdateTrayIconAsync();
            app.UpdateTrayTooltip();
        }
    }

    public static void SetUpcomingEvents(IEnumerable<Event> events)
    {
        if (Current is App app)
        {
            app._upcomingEvents = events.ToList();
            _ = app.UpdateTrayIconAsync();
        }
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
