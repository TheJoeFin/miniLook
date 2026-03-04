using Microsoft.UI;
using Microsoft.UI.Xaml;
using miniLook.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace miniLook.Views;

public sealed partial class MailItemWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private readonly UISettings settings;

    public MailItemWindow(MailData mailData)
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/mouseIcon.ico"));
        Title = mailData.Subject ?? "miniLook";
        TitleText.Text = mailData.Subject ?? string.Empty;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarPanel);

        DetailControl.ListDetailsMenuItem = mailData;

        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged;

        UpdateCaptionButtonColors();
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
