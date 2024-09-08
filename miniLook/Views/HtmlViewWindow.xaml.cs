using miniLook.Helpers;
using Windows.UI.ViewManagement;

namespace miniLook.Views;

public sealed partial class HtmlViewWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

    private readonly UISettings settings;

    public HtmlViewWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/mouseIcon.ico"));

        // Theme change code picked from https://github.com/microsoft/WinUI-Gallery/pull/1239
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event
    }

    // this handles updating the caption button colors correctly when windows system theme is changed
    // while the app is open
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        // This calls comes off-thread, hence we will need to dispatch it to current app's thread
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        });
    }

    public async Task SetContent(string content)
    {
        await WebView.EnsureCoreWebView2Async();
        WebView.NavigateToString(content);
    }
}
