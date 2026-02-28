using Microsoft.UI;
using Microsoft.UI.Xaml;
using miniLook.Contracts.Services;
using miniLook.ViewModels;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace miniLook.Views;

public sealed partial class PopOutWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private readonly UISettings settings;
    private bool _initialized = false;

    public PopOutWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/mouseIcon.ico"));
        Title = "miniLook";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarPanel);

        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged;

        Activated += PopOutWindow_Activated;

        UpdateCaptionButtonColors();
    }

    private void PopOutWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            return;

        INavigationService navService = App.GetService<INavigationService>();
        navService.Frame = ContentFrame;

        if (!_initialized)
        {
            _initialized = true;
            navService.NavigateTo(typeof(ListDetailsViewModel).FullName!);
        }
    }

    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        dispatcherQueue.TryEnqueue(UpdateCaptionButtonColors);
    }

    private void UpdateCaptionButtonColors()
    {
        bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        Color foreground = isDark ? Colors.White : Colors.Black;
        Color hoverBackground = isDark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
        Color pressedBackground = isDark
            ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x66, 0x00, 0x00, 0x00);

        AppWindow.TitleBar.BackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBackground;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = pressedBackground;
    }
}
