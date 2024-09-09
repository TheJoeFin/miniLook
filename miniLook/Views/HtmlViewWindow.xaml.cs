using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using miniLook.Helpers;
using Windows.UI.ViewManagement;

namespace miniLook.Views;

public sealed partial class HtmlViewWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

    private readonly UISettings settings;
    private readonly bool IsDarkTheme;

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

    private void WebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (App.Current.RequestedTheme is ApplicationTheme.Dark)
        {
            WebView.DefaultBackgroundColor = Colors.DimGray;
            WebView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
        }
        else
        {
            WebView.DefaultBackgroundColor = Colors.LightGray;
            WebView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
        }

        WebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
    }

    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        // No need to wait for the launcher to finish sending the URI to the browser
        // before we allow the WebView2 in our app to continue.
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
        // LaunchUriAsync is the WinRT API for launching a URI.
        // Another option not involving WinRT might be System.Diagnostics.Process.Start(args.Uri);
    }
}
